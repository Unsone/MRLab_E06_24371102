"""Download pinned official weights with resume and SHA-256 verification."""
import argparse
import hashlib
import json
import shutil
import time
from concurrent.futures import ThreadPoolExecutor, as_completed
from pathlib import Path

import requests

ROOT = Path(__file__).resolve().parent
MODEL_DIR = ROOT / '.runtime' / 'weights'
REVISION = '0b94677654c57bb9a6b6845cd7b704ccf551d327'
CHECKPOINT_BYTES = 7366389768
CHECKPOINT_SHA256 = '6b519fc7242f78e9b5f47ea4d55668fe3d944a2d27332f4ca68d29a6ff603f5e'
BASE_URL = f'https://huggingface.co/tencent/Hunyuan3D-2.1/resolve/{REVISION}/hunyuan3d-dit-v2-1'


def digest(path):
    h = hashlib.sha256()
    with path.open('rb') as stream:
        for block in iter(lambda: stream.read(8 * 1024 * 1024), b''):
            h.update(block)
    return h.hexdigest()


def save_marker(target, marker):
    marker.write_text(json.dumps({'repository': 'tencent/Hunyuan3D-2.1',
        'revision': REVISION, 'sha256': CHECKPOINT_SHA256,
        'bytes': target.stat().st_size, 'mtime_ns': target.stat().st_mtime_ns}, indent=2), encoding='utf-8')


def parallel_resume(partial, workers=6):
    journal = partial.with_suffix('.ranges.json')
    if journal.exists():
        state = json.loads(journal.read_text())
        if state.get('sha256') != CHECKPOINT_SHA256:
            raise RuntimeError('Range journal belongs to another checkpoint.')
    else:
        prefix = partial.stat().st_size if partial.exists() else 0
        if prefix > CHECKPOINT_BYTES:
            raise RuntimeError('Partial checkpoint is larger than expected.')
        state = {'sha256': CHECKPOINT_SHA256, 'prefix': prefix, 'completed': []}
    prefix = state['prefix']
    block_size = 8 * 1024 ** 2
    if 'block_size' not in state:
        old_completed = state['completed']
        state['completed'] = [point for start in old_completed
            for point in range(start, min(start + 64 * 1024 ** 2, CHECKPOINT_BYTES), block_size)]
    state['block_size'] = block_size
    ranges = [(start, min(start + block_size, CHECKPOINT_BYTES) - 1)
              for start in range(prefix, CHECKPOINT_BYTES, block_size)]

    def commit():
        temp = journal.with_suffix('.tmp')
        temp.write_text(json.dumps(state), encoding='utf-8')
        temp.replace(journal)

    commit()  # Record the contiguous prefix before the file becomes sparse.
    if not partial.exists():
        partial.touch()

    def fetch(bounds):
        start, end = bounds
        for attempt in range(5):
            try:
                # Some intermediary caches key by URL, ignoring the Range header.
                with requests.get(BASE_URL + f'/model.fp16.ckpt?download=true&range={start}-{end}',
                        headers={'Range': f'bytes={start}-{end}'}, stream=True,
                        timeout=(30, 60)) as response:
                    response.raise_for_status()
                    if response.status_code != 206 or not response.headers.get(
                            'Content-Range', '').startswith(f'bytes {start}-{end}/'):
                        raise RuntimeError('Server did not honor bounded range: ' +
                                           response.headers.get('Content-Range', 'missing'))
                    received = 0
                    with partial.open('r+b') as stream:
                        stream.seek(start)
                        for block in response.iter_content(1024 * 1024):
                            received += len(block)
                            if received > end - start + 1:
                                raise RuntimeError('Range response exceeds expected size.')
                            stream.write(block)
                    if received == end - start + 1:
                        return start
            except requests.RequestException as error:
                print(f'Range retry: {type(error).__name__}', flush=True)
                time.sleep(2)
        raise RuntimeError('Range incomplete. Rerun to resume.')

    pending = [bounds for bounds in ranges if bounds[0] not in state['completed']]
    began = time.monotonic()
    initial = prefix + sum(end - start + 1 for start, end in ranges if start in state['completed'])
    print(f'Resuming {initial / 1e9:.2f}/7.37 GB, {workers} connections.', flush=True)
    with ThreadPoolExecutor(max_workers=workers) as pool:
        futures = [pool.submit(fetch, bounds) for bounds in pending]
        try:
            for future in as_completed(futures):
                state['completed'].append(future.result())
                commit()
                current = prefix + sum(end - start + 1 for start, end in ranges if start in state['completed'])
                print(f'DOWNLOAD {current / CHECKPOINT_BYTES:.1%} {current / 1e9:.2f}/7.37 GB '
                      f'{(current - initial) / max(time.monotonic() - began, 1) / 1e6:.2f} MB/s', flush=True)
        except BaseException:
            for future in futures:
                future.cancel()
            raise


def download(directory=MODEL_DIR, workers=6):
    directory.mkdir(parents=True, exist_ok=True)
    target = directory / 'model.fp16.ckpt'
    partial = directory / 'model.fp16.ckpt.part'
    marker = directory / 'verified.json'
    if target.exists():
        stat = target.stat()
        saved = json.loads(marker.read_text()) if marker.exists() else {}
        if (stat.st_size == CHECKPOINT_BYTES and saved.get('sha256') == CHECKPOINT_SHA256
                and saved.get('mtime_ns') == stat.st_mtime_ns):
            print('Weights already verified.', flush=True)
        elif stat.st_size != CHECKPOINT_BYTES or digest(target) != CHECKPOINT_SHA256:
            raise RuntimeError('Existing checkpoint failed verification; preserve it and inspect manually.')
        else:
            save_marker(target, marker)
    else:
        offset = partial.stat().st_size if partial.exists() else 0
        if offset > CHECKPOINT_BYTES:
            raise RuntimeError('Partial checkpoint is larger than expected.')
        needed = 1024 ** 3 if partial.with_suffix('.ranges.json').exists() else CHECKPOINT_BYTES - offset + 1024 ** 3
        if shutil.disk_usage(directory).free < needed:
            raise RuntimeError('Need checkpoint remaining size plus 1 GiB free disk space.')
        parallel_resume(partial, workers)
        if not partial.exists() or partial.stat().st_size != CHECKPOINT_BYTES:
            raise RuntimeError('Download incomplete; rerun to resume the preserved .part file.')
        print('Verifying SHA-256...', flush=True)
        if digest(partial) != CHECKPOINT_SHA256:
            raise RuntimeError('SHA-256 mismatch; partial file preserved, inference is disabled.')
        partial.replace(target)
        save_marker(target, marker)
    config = directory / 'config.yaml'
    response = requests.get(BASE_URL + '/config.yaml', timeout=(30, 60))
    response.raise_for_status()
    temp = config.with_suffix('.yaml.tmp')
    temp.write_bytes(response.content)
    temp.replace(config)
    print('Official checkpoint and config ready: ' + str(directory), flush=True)


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--directory', type=Path, default=MODEL_DIR)
    parser.add_argument('--workers', type=int, choices=range(1, 13), default=6)
    args = parser.parse_args()
    download(args.directory, args.workers)
