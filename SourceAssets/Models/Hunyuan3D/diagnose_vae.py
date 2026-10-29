"""Compare the trained VAE on identical saved diffusion latents."""
import argparse
import json
from pathlib import Path
import time

import numpy as np
import torch
import yaml

import amd_backend
from download_model import MODEL_DIR


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('run', type=Path)
    parser.add_argument('--backend', choices=['cpu', 'directml'], required=True)
    parser.add_argument('--fp32', action='store_true')
    parser.add_argument('--resolution', type=int, default=24)
    args = parser.parse_args()
    destination = args.run / 'diagnostics'
    destination.mkdir(exist_ok=True)
    tag = args.backend + ('_fp32' if args.fp32 or args.backend == 'cpu' else '_fp16')
    began = time.monotonic()
    torch.set_num_threads(8)
    amd_backend.register_source()
    amd_backend.install_chunked_attention(256)
    if args.backend == 'directml':
        import torch_directml
        torch_directml.disable_tiled_resources(True)
        device = torch_directml.device()
    else:
        device = torch.device('cpu')
    dtype = torch.float32 if args.fp32 or args.backend == 'cpu' else torch.float16
    config = yaml.safe_load((MODEL_DIR / 'config.yaml').read_text(encoding='utf-8'))
    checkpoint = torch.load(MODEL_DIR / 'model.fp16.ckpt', map_location='cpu', mmap=True, weights_only=True)
    latents = torch.load(args.run / 'latents.pt', map_location='cpu', weights_only=True)['latents']
    print('Loading VAE', tag, flush=True)
    with torch.inference_mode():
        vae = amd_backend.make_component(config['vae'], checkpoint['vae'], device, dtype)
        decoded = vae(latents.to(device=device, dtype=dtype) / vae.scale_factor)
        cpu_decoded = decoded.cpu()
        torch.save(cpu_decoded, destination / (tag + '_decoded.pt'))
        print('Decoded', tuple(cpu_decoded.shape), float(cpu_decoded.min()), float(cpu_decoded.max()), flush=True)
        field = amd_backend.BoundedVolumeDecoder()(decoded, vae.geo_decoder,
            octree_resolution=args.resolution, num_chunks=256, enable_pbar=False)
        np.save(destination / (tag + '_field.npy'), field.numpy())
        mesh = vae.surface_extractor(field, bounds=1.01, mc_level=0., octree_resolution=args.resolution)[0]
        report = dict(backend=args.backend, dtype=str(dtype), resolution=args.resolution,
            elapsed_seconds=round(time.monotonic() - began, 2),
            decoded_min=float(cpu_decoded.min()), decoded_max=float(cpu_decoded.max()),
            field_min=float(field.min()), field_max=float(field.max()),
            field_positive_fraction=float((field > 0).float().mean()),
            center_value=float(field[0, args.resolution//2, args.resolution//2, args.resolution//2]))
        if mesh is not None:
            import trimesh
            raw = trimesh.Trimesh(mesh.mesh_v, mesh.mesh_f, process=False)
            raw.export(destination / (tag + '.obj'))
            report.update(vertices=len(raw.vertices), faces=len(raw.faces), bounds=raw.bounds.tolist())
        (destination / (tag + '.json')).write_text(json.dumps(report, indent=2), encoding='utf-8')
        print(json.dumps(report), flush=True)


if __name__ == '__main__':
    main()
