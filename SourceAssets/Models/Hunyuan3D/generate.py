"""Project-local, real pretrained image-to-shape generation CLI."""
import argparse
from datetime import datetime
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import sys
import time
import traceback

from download_model import MODEL_DIR, REVISION, CHECKPOINT_SHA256, CHECKPOINT_BYTES

ROOT = Path(__file__).resolve().parent


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--input', required=True, type=Path)
    parser.add_argument('--output', type=Path)
    parser.add_argument('--decode-from', type=Path, help='Resume real latents saved by a previous run.')
    parser.add_argument('--backend', choices=['directml', 'cpu'], default='directml')
    parser.add_argument('--steps', type=int, default=20, choices=range(2, 61), metavar='2..60')
    parser.add_argument('--resolution', type=int, default=128, choices=[64, 96, 128, 192, 256])
    parser.add_argument('--seed', type=int, default=1234)
    parser.add_argument('--guidance', type=float, default=5.0)
    parser.add_argument('--attention-chunk', type=int, default=256, choices=[64, 128, 256, 512])
    parser.add_argument('--volume-chunk', type=int, default=1024, choices=[256, 512, 1024, 2048])
    args = parser.parse_args()
    if not args.input.is_file():
        parser.error('Input image does not exist.')
    output = args.output or ROOT / 'Outputs' / datetime.now().strftime('%Y%m%d_%H%M%S_%f')
    output.mkdir(parents=True, exist_ok=True)
    if (output / 'run.json').exists():
        parser.error('Output already contains a run. Choose a new folder to preserve evidence.')
    started = time.perf_counter()
    record = {'status': 'running', 'started': datetime.now().isoformat(),
              'model': 'tencent/Hunyuan3D-2.1/hunyuan3d-dit-v2-1',
              'model_revision': REVISION, 'model_sha256': CHECKPOINT_SHA256,
              'settings': {k: str(v) if isinstance(v, Path) else v for k, v in vars(args).items()}}

    def save():
        temp = output / 'run.json.tmp'
        temp.write_text(json.dumps(record, ensure_ascii=False, indent=2), encoding='utf-8')
        temp.replace(output / 'run.json')

    def event(stage, detail):
        import psutil
        memory = psutil.Process().memory_info()
        record['memory'] = {'available_ram_mib': round(psutil.virtual_memory().available / 1024 ** 2),
                            'process_rss_mib': round(memory.rss / 1024 ** 2),
                            'process_peak_wset_mib': round(getattr(memory, 'peak_wset', memory.rss) / 1024 ** 2)}
        record.update(stage=stage, detail=detail, elapsed_seconds=round(time.perf_counter() - started, 2))
        save()
        print(json.dumps({'stage': stage, **detail}, ensure_ascii=False), flush=True)

    save()
    try:
        checkpoint = MODEL_DIR / 'model.fp16.ckpt'
        marker = MODEL_DIR / 'verified.json'
        if not checkpoint.is_file() or not marker.is_file():
            raise RuntimeError('Weights are not ready. Run setup.ps1/download_model.py first.')
        verified = json.loads(marker.read_text())
        if (checkpoint.stat().st_size != CHECKPOINT_BYTES or verified.get('sha256') != CHECKPOINT_SHA256
                or verified.get('mtime_ns') != checkpoint.stat().st_mtime_ns):
            raise RuntimeError('Weights changed since verification. Rerun download_model.py.')
        from PIL import Image
        with Image.open(args.input) as image:
            image.load()
            if image.width * image.height > 24_000_000:
                raise ValueError('Reference image exceeds 24 megapixels.')
        copied_input = output / ('input' + args.input.suffix.lower())
        shutil.copy2(args.input, copied_input)
        record['input_sha256'] = hashlib.sha256(copied_input.read_bytes()).hexdigest()
        record['prepared_input'] = str(copied_input.resolve())
        record['execution'] = 'isolated_sampling_and_decoding_processes'
        stages = ['sample', 'decode']
        if not args.decode_from:
            event('prepare_reference', {})
            from prepare_reference import prepare_reference
            prepared, preprocessing = prepare_reference(copied_input, output)
            record.update(prepared_input=str(prepared.resolve()), preprocessing=preprocessing)
        if args.decode_from:
            original = json.loads((args.decode_from / 'run.json').read_text(encoding='utf-8'))
            if original['model_sha256'] != CHECKPOINT_SHA256 or original.get('input_sha256') != record['input_sha256']:
                raise ValueError('Saved latents belong to a different input or checkpoint.')
            latent = args.decode_from / 'latents.pt'
            latent_hash = hashlib.sha256(latent.read_bytes()).hexdigest()
            if original.get('latent_sha256', latent_hash) != latent_hash:
                raise ValueError('Saved latents changed since generation.')
            shutil.copy2(latent, output / 'latents.pt')
            for key in ('steps', 'seed', 'guidance'):
                record['settings'][key] = original['settings'][key]
            record.update(resumed_from=str(args.decode_from.resolve()), latent_sha256=latent_hash,
                          original_sampling_elapsed_seconds=original.get('elapsed_seconds'),
                          metadata=original.get('metadata', {'sampling_metadata':
                              'not returned; see original sampling run',
                              'backend_requested': original['settings']['backend']}))
            stages = ['decode']
        save()
        for stage in stages:
            subprocess.run([sys.executable, '-u', str(ROOT / 'stage_worker.py'), str(output.resolve()),
                            '--stage', stage], check=True)
            record = json.loads((output / 'run.json').read_text(encoding='utf-8'))
        record.update(status='completed', stage='completed',
            elapsed_seconds=round(time.perf_counter() - started, 2),
            scope='pretrained shape only; no texture generation or Blender optimization')
        save()
        print('COMPLETED: ' + str(output / 'model.obj'), flush=True)
    except Exception as error:
        record = json.loads((output / 'run.json').read_text(encoding='utf-8'))
        record.update(status='failed', error=record.get('error', f'{type(error).__name__}: {error}'),
                      elapsed_seconds=round(time.perf_counter() - started, 2))
        save()
        with (output / 'error.txt').open('a', encoding='utf-8') as stream:
            stream.write(traceback.format_exc())
        raise


if __name__ == '__main__':
    main()
