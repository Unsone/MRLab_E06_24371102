"""Detect an extracted sampling-box shell; this is not semantic shape grading."""
import numpy as np


def assess_mesh(mesh, bounds=1.01, resolution=128):
    vertices = np.asarray(mesh.vertices)
    tolerance = max(2.5 * (2 * bounds) / resolution, .025)
    near_boundary = np.max(np.abs(vertices), axis=1) >= bounds - tolerance
    boundary_fraction = float(near_boundary.mean())
    extents = np.sort(np.ptp(vertices, axis=0))
    spans_box = bool(np.all(extents > 2 * bounds * .94))
    box_shell = spans_box and boundary_fraction > .98
    flat_sheet = bool(np.all(extents[1:] > 2 * bounds * .94) and extents[0] < .02 * extents[-1])
    reason = ('Mesh is almost entirely the sampling-box boundary; reconstruction failed.' if box_shell else
              'Mesh collapsed to a thin sheet spanning two sampling-box axes; reconstruction failed.' if flat_sheet else
              'No sampling-box collapse detected; visual inspection still required.')
    return {'passed': not (box_shell or flat_sheet), 'sampling_boundary_fraction': boundary_fraction,
            'flat_sampling_sheet': flat_sheet,
            'spans_sampling_box': spans_box, 'resolution': resolution,
            'reason': reason}
