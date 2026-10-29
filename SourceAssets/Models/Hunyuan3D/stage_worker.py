"""Isolated pretrained sampling or mesh decoding worker (called by generate.py)."""
import argparse
import hashlib
import json
from pathlib import Path
import time
import traceback

from download_model import MODEL_DIR


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('directory', type=Path)
    parser.add_argument('--stage', choices=['sample', 'decode'], required=True)
    args = parser.parse_args()
    record_path = args.directory / 'run.json'
    record = json.loads(record_path.read_text(encoding='utf-8'))
    settings = record['settings']
    began = time.monotonic()
    previous_elapsed = record.get('elapsed_seconds', 0)

    def save():
        record['elapsed_seconds'] = round(previous_elapsed + time.monotonic() - began, 2)
        temp = record_path.with_suffix('.worker.tmp')
        temp.write_text(json.dumps(record, ensure_ascii=False, indent=2), encoding='utf-8')
        temp.replace(record_path)

    def event(stage, detail):
        import psutil
        memory = psutil.Process().memory_info()
        record['memory'] = {'available_ram_mib': round(psutil.virtual_memory().available / 1024 ** 2),
                            'process_rss_mib': round(memory.rss / 1024 ** 2),
                            'process_peak_wset_mib': round(getattr(memory, 'peak_wset', memory.rss) / 1024 ** 2)}
        record.update(stage=stage, detail=detail)
        if args.stage == 'decode' and stage == 'device':
            record.setdefault('metadata', {})['decode_device'] = detail['name']
        save()
        print(json.dumps({'stage': stage, **detail}), flush=True)

    try:
        if args.stage == 'sample':
            from amd_backend import generate
            _, metadata = generate(Path(record['prepared_input']), MODEL_DIR,
                backend=settings['backend'], steps=settings['steps'], resolution=settings['resolution'],
                seed=settings['seed'], guidance=settings['guidance'], attention_chunk=settings['attention_chunk'],
                volume_chunk=settings['volume_chunk'], latent_path=args.directory / 'latents.pt',
                sample_only=True, event=event)
            record['metadata'] = metadata
            record['latent_sha256'] = hashlib.sha256((args.directory / 'latents.pt').read_bytes()).hexdigest()
            record['sampling_seconds'] = round(time.monotonic() - began, 2)
            save()
        else:
            from amd_backend import decode_latents
            mesh = decode_latents(args.directory / 'latents.pt', MODEL_DIR,
                backend=settings['backend'], resolution=settings['resolution'],
                volume_chunk=settings['volume_chunk'], event=event)
            event('export', {})
            mesh.export(args.directory / 'model.obj')
            mesh.export(args.directory / 'model.glb')
            from mesh_quality import assess_mesh
            quality = assess_mesh(mesh, resolution=settings['resolution'])
            (args.directory / 'mesh_quality.json').write_text(json.dumps(quality, indent=2), encoding='utf-8')
            record.update(vertices=len(mesh.vertices), faces=len(mesh.faces), bounds=mesh.bounds.tolist(),
                quality=quality,
                decoding_seconds=round(time.monotonic() - began, 2),
                output_sha256={name: hashlib.sha256((args.directory / name).read_bytes()).hexdigest()
                    for name in ('model.obj', 'model.glb')})
            save()
            if not quality['passed']:
                raise RuntimeError(quality['reason'] + ' Raw output retained for diagnosis.')
    except Exception as error:
        record.update(status='failed', error=f'{type(error).__name__}: {error}')
        save()
        (args.directory / 'error.txt').write_text(traceback.format_exc(), encoding='utf-8')
        raise


if __name__ == '__main__':
    main()
