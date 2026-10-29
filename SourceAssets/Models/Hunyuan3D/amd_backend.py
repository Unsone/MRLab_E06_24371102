"""Memory-bounded adapter around unmodified Hunyuan3D-2.1 shape modules."""
import gc
import importlib
import os
import sys
import types
from pathlib import Path

ROOT = Path(__file__).resolve().parent
SOURCE_COMMIT = '82920d643c0dc2f7bfd7255f45f62d386edfe60c'


class BoundedVolumeDecoder:
    """Official dense grid/order, with synchronized CPU collection per chunk."""
    def __call__(self, latents, geo_decoder, bounds=1.01, num_chunks=1024,
                 octree_resolution=64, enable_pbar=True, **kwargs):
        import numpy as np
        import torch
        from tqdm import tqdm
        from hy3dshape.models.autoencoders.volume_decoders import generate_dense_grid_points
        if isinstance(bounds, float):
            bounds = [-bounds, -bounds, -bounds, bounds, bounds, bounds]
        points, grid_size, _ = generate_dense_grid_points(np.array(bounds[:3]),
            np.array(bounds[3:]), octree_resolution=octree_resolution, indexing='ij')
        points = torch.from_numpy(points).reshape(-1, 3)
        chunks = []
        for start in tqdm(range(0, len(points), num_chunks), desc='Bounded volume decoding', disable=not enable_pbar):
            query = points[start:start + num_chunks].to(device=latents.device, dtype=latents.dtype)
            query = query.unsqueeze(0).repeat(latents.shape[0], 1, 1)
            logits = geo_decoder(queries=query, latents=latents).cpu()
            if not torch.isfinite(logits).all():
                raise RuntimeError(f'Non-finite voxel values at query chunk starting {start}.')
            chunks.append(logits)
        field = torch.cat(chunks, dim=1).view(latents.shape[0], *grid_size).float()
        print(f'Voxel field: min={field.min().item():.6g}, max={field.max().item():.6g}', flush=True)
        return field


def register_source():
    # Only bypass upstream's optional postprocessor imports, never neural modules.
    if 'hy3dshape' not in sys.modules:
        package = types.ModuleType('hy3dshape')
        package.__path__ = [str(ROOT / 'vendor' / 'hy3dshape')]
        sys.modules['hy3dshape'] = package
    os.environ['HY3DGEN_DEBUG'] = '0'
    os.environ['HF_HUB_OFFLINE'] = '1'
    os.environ['TRANSFORMERS_OFFLINE'] = '1'


def install_chunked_attention(chunk_size=256):
    """Exact query slicing (not token reduction); preserves attention semantics."""
    import torch
    import torch.nn.functional as functional
    original = getattr(functional.scaled_dot_product_attention, '_hy3d_original',
                       functional.scaled_dot_product_attention)

    def attention(query, key, value, attn_mask=None, dropout_p=0.0,
                  is_causal=False, scale=None, **kwargs):
        if (query.shape[-2] <= chunk_size or is_causal or dropout_p or
                kwargs.get('enable_gqa', False)):
            return original(query, key, value, attn_mask=attn_mask,
                dropout_p=dropout_p, is_causal=is_causal, scale=scale, **kwargs)
        pieces = []
        for start in range(0, query.shape[-2], chunk_size):
            end = min(start + chunk_size, query.shape[-2])
            mask = attn_mask
            if mask is not None and mask.ndim >= 2 and mask.shape[-2] != 1:
                mask = mask[..., start:end, :]
            pieces.append(original(query[..., start:end, :].contiguous(), key, value,
                attn_mask=mask, dropout_p=0.0, is_causal=False, scale=scale, **kwargs))
        # DirectML strided slice assignment can lose writes; concatenate instead.
        return torch.cat(pieces, dim=-2)

    attention._hy3d_original = original
    functional.scaled_dot_product_attention = attention
    return original


def check_memory():
    import psutil
    if psutil.virtual_memory().available < 1024 ** 3:
        raise MemoryError('Less than 1 GiB system RAM available; close Unity/Blender and retry.')


def make_component(spec, state, device, dtype):
    import torch
    from accelerate import init_empty_weights
    module_name, class_name = spec['target'].rsplit('.', 1)
    cls = getattr(importlib.import_module(module_name), class_name)
    check_memory()
    with init_empty_weights(include_buffers=False):
        component = cls(**spec.get('params', {}))
    # Strictly require all trained parameters. Upstream VAE allows extra keys.
    result = component.load_state_dict(state, strict=False, assign=True)
    missing_parameters = [name for name, p in component.named_parameters() if p.is_meta]
    if missing_parameters:
        raise RuntimeError('Missing trained parameters: ' + ', '.join(missing_parameters[:8]))
    if result.missing_keys:
        print('State missing buffers: ' + str(result.missing_keys), flush=True)
    return component.eval().requires_grad_(False).to(device=device, dtype=dtype)


def offload_dit_blocks(model, device):
    """Keep mapped CPU weights; transfer one trained block per forward call.

    Restore the original CPU tensors after execution rather than copying GPU
    weights back. This bounds resident GPU weights without altering the network.
    """
    handles = []
    for name, child in model.named_children():
        if name != 'blocks':
            child.to(device)
    for block_index, block in enumerate(model.blocks):
        cpu_state = block.state_dict()

        def before(module, inputs, index=block_index):
            check_memory()
            print(f'DiT block {index + 1}/{len(model.blocks)} -> DirectML', flush=True)
            try:
                module.to(device)
            except Exception as error:
                raise RuntimeError(f'DirectML transfer failed at DiT block {index + 1}: {error}') from error

        def after(module, inputs, output, state=cpu_state):
            # DirectML work is asynchronous. Complete the current block before
            # releasing its GPU parameter tensors and loading the next block.
            output.reshape(-1)[:1].cpu()
            module.load_state_dict(state, strict=True, assign=True)
            return output

        handles.append(block.register_forward_pre_hook(before))
        handles.append(block.register_forward_hook(after))
    return handles


def generate(image_path, weights_dir, *, backend='directml', steps=20,
             resolution=128, seed=1234, guidance=5.0, attention_chunk=256,
             volume_chunk=1024, latent_path=None, sample_only=False,
             event=lambda stage, detail: print(stage, detail)):
    import numpy as np
    import torch
    import yaml
    register_source()
    install_chunked_attention(attention_chunk)
    # Import after attention patch: upstream VAE captures SDPA on import.
    from hy3dshape.pipelines import instantiate_from_config, export_to_trimesh

    torch.set_num_threads(min(8, os.cpu_count() or 4))
    if backend == 'directml':
        import torch_directml
        # Process-local setting; no driver or OS configuration is changed.
        torch_directml.disable_tiled_resources(True)
        if os.name == 'nt':
            import ctypes
            ctypes.windll.kernel32.SetThreadUILanguage(0x0409)
        device = torch_directml.device()
        device_name = torch_directml.device_name(0).rstrip('\0')
        dtype = torch.float16
    else:
        device, device_name, dtype = torch.device('cpu'), 'CPU', torch.float32
    event('device', {'name': device_name, 'dtype': str(dtype)})
    config = yaml.safe_load((weights_dir / 'config.yaml').read_text(encoding='utf-8'))
    event('load_checkpoint', {})
    checkpoint = torch.load(weights_dir / 'model.fp16.ckpt', mmap=True,
                            map_location='cpu', weights_only=True)

    with torch.inference_mode():
        event('encode_image', {})
        conditioner = make_component(config['conditioner'], checkpoint['conditioner'], device, dtype)
        processor = instantiate_from_config(config['image_processor'])
        image_inputs = processor(str(image_path))
        condition = conditioner(**image_inputs)
        unconditional = conditioner.unconditional_embedding(1)
        del conditioner, image_inputs
        gc.collect()

        event('load_dit', {})
        # Full DiT residency exceeds this iGPU's practical DirectML allocation
        # budget. Sequential block offload is also used in the real smoke run.
        model = make_component(config['model'], checkpoint['model'], 'cpu', dtype)
        if backend == 'directml':
            offload_handles = offload_dit_blocks(model, device)
            event('dit_offload', {'mode': 'sequential_blocks', 'blocks': len(model.blocks)})
        scheduler = instantiate_from_config(config['scheduler'])
        # Exact official flow-matching schedule, including official terminal sigma.
        scheduler.set_timesteps(sigmas=np.linspace(0, 1, steps), device='cpu')
        latent_shape = (1, config['vae']['params']['num_latents'], config['vae']['params']['embed_dim'])
        generator = torch.Generator(device='cpu').manual_seed(seed)
        latents = torch.randn(latent_shape, generator=generator, dtype=dtype).to(device)
        latents *= getattr(scheduler, 'init_noise_sigma', 1.0)
        for index, timestep in enumerate(scheduler.timesteps):
            check_memory()
            event('sampling', {'step': index, 'total': steps})
            t = timestep.expand(1).to(device=device, dtype=dtype) / scheduler.config.num_train_timesteps
            prediction_cond = model(latents, t, condition)
            # Sequential CFG is equivalent to upstream concatenated batches at eval,
            # while avoiding two full sets of 4096-token activations on shared RAM.
            if guidance >= 0 and guidance != 1:
                prediction_uncond = model(latents, t, unconditional)
                prediction = prediction_uncond + guidance * (prediction_cond - prediction_uncond)
            else:
                prediction = prediction_cond
            latents = scheduler.step(prediction, timestep, latents).prev_sample
            if not torch.isfinite(latents).all().cpu().item():
                raise RuntimeError('Non-finite latent values. Try CPU backend; no fake model will be exported.')
        latents = latents.cpu()
        if latent_path is not None:
            torch.save({'latents': latents, 'seed': seed, 'steps': steps,
                        'guidance': guidance, 'source_commit': SOURCE_COMMIT}, latent_path)
        if sample_only:
            return None, {'device': device_name, 'dtype': str(dtype), 'source_commit': SOURCE_COMMIT,
                          'dit_offload': 'sequential_blocks' if backend == 'directml' else 'none'}
        del model, condition, unconditional, prediction_cond, prediction
        if backend == 'directml':
            for handle in offload_handles:
                handle.remove()
            del offload_handles
        if 'prediction_uncond' in locals():
            del prediction_uncond
        gc.collect()

        event('decode_mesh', {'resolution': resolution})
        vae = make_component(config['vae'], checkpoint['vae'], device, dtype)
        vae.volume_decoder = BoundedVolumeDecoder()
        decoded = vae(latents.to(device=device, dtype=dtype) / vae.scale_factor)
        decoded_cpu = decoded.cpu()
        if not torch.isfinite(decoded_cpu).all():
            raise RuntimeError('Non-finite values in pretrained VAE decoder output.')
        print(f'Decoded latents: min={decoded_cpu.min().item():.6g}, max={decoded_cpu.max().item():.6g}', flush=True)
        del decoded_cpu
        meshes = vae.latents2mesh(decoded, bounds=1.01, mc_level=0.0,
            num_chunks=volume_chunk, octree_resolution=resolution, enable_pbar=True)
        mesh = export_to_trimesh(meshes)[0]
        if mesh is None or len(mesh.vertices) < 4 or len(mesh.faces) < 4:
            raise RuntimeError('No valid surface extracted; increase sampling steps or check reference image.')
        if not np.isfinite(mesh.vertices).all():
            raise RuntimeError('Mesh contains invalid coordinates.')
        return mesh, {'device': device_name, 'dtype': str(dtype), 'source_commit': SOURCE_COMMIT,
                      'dit_offload': 'sequential_blocks' if backend == 'directml' else 'none',
                      'parameters': sum(t.numel() for part in checkpoint.values()
                                        if isinstance(part, dict) for t in part.values()
                                        if isinstance(t, torch.Tensor))}


def decode_latents(latent_path, weights_dir, *, backend='directml', resolution=64,
                   volume_chunk=1024, event=lambda stage, detail: print(stage, detail)):
    """Decode real saved diffusion latents in a fresh DirectML process."""
    import numpy as np
    import torch
    import yaml
    register_source()
    install_chunked_attention(256)
    from hy3dshape.pipelines import export_to_trimesh
    torch.set_num_threads(min(8, os.cpu_count() or 4))
    if backend == 'directml':
        import torch_directml
        torch_directml.disable_tiled_resources(True)
        device, dtype = torch_directml.device(), torch.float16
        device_name = torch_directml.device_name(0).rstrip('\0')
    else:
        device, dtype = torch.device('cpu'), torch.float32
        device_name = 'CPU'
    event('device', {'name': device_name, 'dtype': str(dtype), 'stage_process': 'decode'})
    config = yaml.safe_load((weights_dir / 'config.yaml').read_text(encoding='utf-8'))
    saved = torch.load(latent_path, map_location='cpu', weights_only=True)
    latents = saved['latents']
    expected = (1, config['vae']['params']['num_latents'], config['vae']['params']['embed_dim'])
    if tuple(latents.shape) != expected or not torch.isfinite(latents).all():
        raise ValueError('Invalid saved diffusion latents.')
    checkpoint = torch.load(weights_dir / 'model.fp16.ckpt', mmap=True,
                            map_location='cpu', weights_only=True)
    with torch.inference_mode():
        event('decode_mesh', {'resolution': resolution, 'fresh_process': True})
        vae = make_component(config['vae'], checkpoint['vae'], device, dtype)
        vae.volume_decoder = BoundedVolumeDecoder()
        decoded = vae(latents.to(device=device, dtype=dtype) / vae.scale_factor)
        decoded_cpu = decoded.cpu()
        if not torch.isfinite(decoded_cpu).all():
            raise RuntimeError('Non-finite values in pretrained VAE decoder output.')
        print(f'Decoded latents: min={decoded_cpu.min().item():.6g}, max={decoded_cpu.max().item():.6g}', flush=True)
        del decoded_cpu
        meshes = vae.latents2mesh(decoded, bounds=1.01, mc_level=0.0,
            num_chunks=volume_chunk, octree_resolution=resolution, enable_pbar=True)
        mesh = export_to_trimesh(meshes)[0]
        if mesh is None or len(mesh.vertices) < 4 or len(mesh.faces) < 4 or not np.isfinite(mesh.vertices).all():
            raise RuntimeError('No valid finite mesh extracted from pretrained latents.')
        return mesh
