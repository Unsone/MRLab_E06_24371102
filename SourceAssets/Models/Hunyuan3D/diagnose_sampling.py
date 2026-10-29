"""Real trained-network CPU/DirectML comparison, using identical inputs."""
import argparse
import json
from pathlib import Path
import time

import torch
import yaml

import amd_backend
from download_model import MODEL_DIR


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('run', type=Path)
    parser.add_argument('--backend', choices=['cpu', 'directml'], required=True)
    args = parser.parse_args()
    out = args.run / 'diagnostics'
    out.mkdir(exist_ok=True)
    torch.set_num_threads(8)
    amd_backend.register_source()
    amd_backend.install_chunked_attention(256)
    from hy3dshape.pipelines import instantiate_from_config
    config = yaml.safe_load((MODEL_DIR / 'config.yaml').read_text(encoding='utf-8'))
    weights = torch.load(MODEL_DIR / 'model.fp16.ckpt', map_location='cpu', mmap=True, weights_only=True)
    if args.backend == 'directml':
        import torch_directml
        torch_directml.disable_tiled_resources(True)
        device, dtype = torch_directml.device(), torch.float16
    else:
        device, dtype = torch.device('cpu'), torch.float32
    report = {'backend': args.backend, 'blocks': []}
    with torch.inference_mode():
        if args.backend == 'cpu':
            print('CPU trained image encoder', flush=True)
            encoder = amd_backend.make_component(config['conditioner'], weights['conditioner'], 'cpu', torch.float32)
            processor = instantiate_from_config(config['image_processor'])
            condition = encoder(**processor(str(args.run / 'input.png')))
            torch.save(condition, out / 'cpu_condition.pt')
            del encoder
        else:
            condition = torch.load(out / 'cpu_condition.pt', weights_only=True)
        condition = {k: v.to(device=device, dtype=dtype) for k, v in condition.items()}
        model = amd_backend.make_component(config['model'], weights['model'], 'cpu', torch.float16)
        for name, child in model.named_children():
            if name != 'blocks': child.to(device=device, dtype=dtype)
        for index, block in enumerate(model.blocks):
            state = block.state_dict()
            def before(module, inputs, i=index):
                print('block', i, flush=True)
                module.to(device=device, dtype=dtype)
            def after(module, inputs, output, i=index, saved=state):
                actual = output.cpu().float()
                row = dict(index=i, max=float(actual.abs().max()), std=float(actual.std()))
                path = out / f'cpu_block_{i}.pt'
                if args.backend == 'cpu':
                    torch.save(actual, path)
                else:
                    expected = torch.load(path, weights_only=True)
                    row.update(max_error=float((actual-expected).abs().max()),
                               mean_error=float((actual-expected).abs().mean()))
                report['blocks'].append(row)
                print(json.dumps(row), flush=True)
                module.load_state_dict(saved, strict=True, assign=True)
                return output
            block.register_forward_pre_hook(before)
            block.register_forward_hook(after)
        generator = torch.Generator().manual_seed(1234)
        noise = torch.randn((1,4096,64), generator=generator, dtype=torch.float16).to(device=device,dtype=dtype)
        began = time.monotonic()
        prediction = model(noise, torch.zeros(1,device=device,dtype=dtype), condition).cpu()
        torch.save(prediction, out / (args.backend + '_prediction.pt'))
        report['seconds'] = round(time.monotonic()-began,2)
        (out / (args.backend + '_sampling.json')).write_text(json.dumps(report,indent=2),encoding='utf-8')


if __name__ == '__main__': main()
