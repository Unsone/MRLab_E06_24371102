"""Blender background import/render verification; never changes a user scene."""
import json
from pathlib import Path
import sys

import bpy
from mathutils import Vector

arguments = sys.argv[sys.argv.index('--') + 1:]
source, destination = Path(arguments[0]).resolve(), Path(arguments[1]).resolve()
destination.mkdir(parents=True, exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.wm.obj_import(filepath=str(source), forward_axis='NEGATIVE_Z', up_axis='Y')
objects = [o for o in bpy.context.scene.objects if o.type == 'MESH']
if not objects: raise RuntimeError('No mesh imported from actual OBJ.')
points = [o.matrix_world @ Vector(corner) for o in objects for corner in o.bound_box]
minimum = Vector([min(v[i] for v in points) for i in range(3)])
maximum = Vector([max(v[i] for v in points) for i in range(3)])
center = (minimum + maximum) / 2
radius = max((maximum - minimum).length / 2, .01)
material = bpy.data.materials.new('DiagnosticNeutralMaterial')
material.use_nodes = True
node = material.node_tree.nodes.get('Principled BSDF')
node.inputs['Base Color'].default_value = (.52,.65,.72,1)
node.inputs['Roughness'].default_value = .4
for obj in objects:
    obj.data.materials.clear()
    obj.data.materials.append(material)
    for polygon in obj.data.polygons: polygon.use_smooth = True
scene = bpy.context.scene
scene.render.engine = 'CYCLES'
scene.cycles.device = 'CPU'
scene.cycles.samples = 24
scene.cycles.use_denoising = True
scene.render.resolution_x = scene.render.resolution_y = 768
scene.render.resolution_percentage = 100
scene.render.image_settings.file_format = 'PNG'
scene.world = bpy.data.worlds.new('DiagnosticWorld')
scene.world.use_nodes = True
scene.world.node_tree.nodes['Background'].inputs[0].default_value = (.8,.8,.8,1)
scene.world.node_tree.nodes['Background'].inputs[1].default_value = .5
for index, offset in enumerate(((3,-4,5),(-4,-2,3),(0,4,4))):
    data = bpy.data.lights.new('CheckLight'+str(index), 'AREA')
    data.energy = 200 * radius * radius
    data.shape = 'DISK'
    data.size = radius * 3
    obj = bpy.data.objects.new(data.name,data)
    scene.collection.objects.link(obj)
    obj.location = center + Vector(offset) * radius
    obj.rotation_euler = (center - obj.location).to_track_quat('-Z','Y').to_euler()
camera_data = bpy.data.cameras.new('CheckCamera')
camera = bpy.data.objects.new('CheckCamera',camera_data)
scene.collection.objects.link(camera)
scene.camera = camera
camera_data.type = 'ORTHO'
camera_data.ortho_scale = radius * 2.5
for name, offset in [('preview',(3,-4,2.7)),('preview_top',(0,-.02,5))]:
    camera.location = center + Vector(offset) * radius
    camera.rotation_euler = (center-camera.location).to_track_quat('-Z','Y').to_euler()
    scene.render.filepath = str(destination/(name+'.png'))
    bpy.ops.render.render(write_still=True)
report = {'blender':bpy.app.version_string,'source':str(source),
          'vertices':sum(len(o.data.vertices) for o in objects),
          'polygons':sum(len(o.data.polygons) for o in objects),
          'world_bounds':[list(minimum),list(maximum)],'geometry_modified':False,
          'display_material':'neutral diagnostic material; no generated texture'}
(destination/'blender_import_check.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
bpy.ops.wm.save_as_mainfile(filepath=str(destination/'blender_import_check.blend'))
print(json.dumps(report))
