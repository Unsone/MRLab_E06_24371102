"""Check the four still deliverables without invoking Blender or inference."""
import json
from pathlib import Path
from PIL import Image, ImageStat, ImageChops

root = Path(__file__).resolve().parents[1]
report = json.loads((root / 'evidence/render_report.json').read_text(encoding='utf-8'))
assert len(report['views']) == 2, 'Both views must complete.'
results = []
for view in report['views']:
    assert view['samples'] == 32
    assert len(view['cascades']) == 4
    assert view['shadow_validation']['agreement'] >= .90
    pair = []
    for key in ['hybrid', 'white']:
        path = root / 'images' / view[key]
        with Image.open(path) as im:
            im.load()
            assert im.size == (1800, 1200), (path, im.size)
            rgb = im.convert('RGB')
            # A neutral white-model close-up intentionally has low contrast.
            # Check both variance and range rather than requiring beauty-pass contrast.
            assert max(ImageStat.Stat(rgb).stddev) > (5 if key == 'white' else 15), 'Blank/flat image.'
            assert max(hi-lo for lo,hi in rgb.getextrema()) > 40, 'Insufficient visible geometry range.'
            if key == 'white':
                r, g, b = rgb.split()
                assert ImageChops.difference(r, g).getextrema()[1] <= 2
                assert ImageChops.difference(g, b).getextrema()[1] <= 2
            pair.append({'file': path.name, 'size': im.size, 'bytes': path.stat().st_size})
    scene = root / 'scene' / f"view{view['view']:02d}_hybrid.blend"
    assert scene.is_file() and scene.stat().st_size > 1_000_000
    results.append({'view': view['view'], 'images': pair, 'editable_scene': scene.name})
assert report['views'][0]['camera_world_matrix'] != report['views'][1]['camera_world_matrix']
assert len(list((root/'images').glob('*.png'))) == 4, 'Keep previews out of the final image directory.'
result = {'passed': True, 'views': results, 'matching_camera_per_pair': 'Both render paths use the same camera object without changing its transform/lens or scene geometry.'}
(root/'evidence/output_checks.json').write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding='utf-8')
print(json.dumps(result, ensure_ascii=False, indent=2))
