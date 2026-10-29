"""Local foreground extraction before Hunyuan image conditioning."""
import argparse
import hashlib
import json
import os
from pathlib import Path

import numpy as np
from PIL import Image

ROOT = Path(__file__).resolve().parent


def prepare_reference(source, directory, mask_model='u2netp'):
    directory.mkdir(parents=True, exist_ok=True)
    image = Image.open(source).convert('RGBA')
    alpha = np.asarray(image.getchannel('A'))
    existing_alpha = float((alpha < 250).mean()) > .01
    metadata = {'source_sha256': hashlib.sha256(Path(source).read_bytes()).hexdigest(),
                'method': 'existing_alpha' if existing_alpha else f'rembg_{mask_model}_cpu'}
    if not existing_alpha:
        os.environ['U2NET_HOME'] = str(ROOT / '.runtime' / 'rembg')
        os.environ.setdefault('OMP_NUM_THREADS', '4')
        from rembg import new_session, remove
        session = new_session(mask_model, providers=['CPUExecutionProvider'])
        image = remove(image, session=session)
        model = ROOT / '.runtime' / 'rembg' / (mask_model + '.onnx')
        if model.is_file():
            metadata['mask_model_sha256'] = hashlib.sha256(model.read_bytes()).hexdigest()
    alpha = np.asarray(image.getchannel('A'))
    foreground = alpha > 127
    fraction = float(foreground.mean())
    metadata.update(foreground_fraction=fraction, alpha_min=int(alpha.min()), alpha_max=int(alpha.max()))
    image.save(directory / 'prepared.png')
    image.getchannel('A').save(directory / 'foreground_mask.png')
    white = Image.new('RGBA', image.size, (255,255,255,255))
    Image.alpha_composite(white, image).convert('RGB').save(directory / 'prepared_preview.png')
    metadata['prepared_sha256'] = hashlib.sha256((directory / 'prepared.png').read_bytes()).hexdigest()
    (directory / 'preprocessing.json').write_text(json.dumps(metadata,indent=2),encoding='utf-8')
    if fraction < .01 or fraction > .97:
        raise ValueError('Foreground extraction is empty or covers nearly the entire photo. Check the saved mask.')
    return directory / 'prepared.png', metadata


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('input',type=Path)
    parser.add_argument('output',type=Path)
    args=parser.parse_args()
    print(prepare_reference(args.input,args.output)[1])
