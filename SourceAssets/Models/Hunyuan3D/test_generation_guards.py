"""Regression checks for opaque-photo preparation and sampling-box rejection."""
import hashlib
import json
from pathlib import Path
import tempfile
import unittest

import numpy as np
from PIL import Image
import trimesh

from mesh_quality import assess_mesh
from prepare_reference import prepare_reference
from app import apply_quality_status


class GenerationGuards(unittest.TestCase):
    def test_sampling_sheet_rejected(self):
        self.assertFalse(assess_mesh(trimesh.creation.box(extents=(1.99,.012,1.99)))['passed'])

    def test_contained_thin_object_not_rejected(self):
        self.assertTrue(assess_mesh(trimesh.creation.box(extents=(.8,.012,1.3)))['passed'])

    def test_sampling_box_rejected(self):
        quality = assess_mesh(trimesh.creation.box(extents=(1.98, 1.98, 1.98)))
        self.assertFalse(quality['passed'])
        self.assertEqual(quality['sampling_boundary_fraction'], 1.)

    def test_contained_cylinder_not_rejected(self):
        cylinder = trimesh.creation.cylinder(radius=.65, height=1.7, sections=64)
        self.assertTrue(assess_mesh(cylinder)['passed'])

    def test_existing_alpha_preserved_and_original_unchanged(self):
        root = Path(__file__).parent / '.runtime' / 'test-temp'
        root.mkdir(exist_ok=True)
        with tempfile.TemporaryDirectory(dir=root) as temp:
            folder = Path(temp)
            rgba = np.zeros((64,64,4),dtype=np.uint8)
            rgba[8:56,16:48] = [80,100,160,255]
            source = folder / 'source.png'
            Image.fromarray(rgba).save(source)
            original_hash = hashlib.sha256(source.read_bytes()).hexdigest()
            prepared, metadata = prepare_reference(source,folder / 'out')
            self.assertEqual(metadata['method'],'existing_alpha')
            np.testing.assert_array_equal(np.array(Image.open(prepared))[:,:,3],rgba[:,:,3])
            self.assertEqual(hashlib.sha256(source.read_bytes()).hexdigest(),original_hash)

    def test_bad_old_result_is_not_shown_as_completed(self):
        root = Path(__file__).parent / '.runtime' / 'test-temp'
        root.mkdir(exist_ok=True)
        with tempfile.TemporaryDirectory(dir=root) as temp:
            folder = Path(temp)
            (folder / 'mesh_quality.json').write_text(json.dumps({'passed':False,'reason':'sampling box'}))
            result = apply_quality_status({'status':'completed'},folder)
            self.assertEqual(result['status'],'failed')
            self.assertEqual(result['error'],'sampling box')


if __name__ == '__main__': unittest.main(verbosity=2)
