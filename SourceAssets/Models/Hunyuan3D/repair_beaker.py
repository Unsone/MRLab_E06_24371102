"""Reproducible Blender repair of the actual Hunyuan mesh; preserves the raw OBJ."""
import bpy, bmesh, json, sys, hashlib
from pathlib import Path
from mathutils import Vector

args=sys.argv[sys.argv.index('--')+1:]
source, out=Path(args[0]).resolve(), Path(args[1]).resolve()
out.mkdir(parents=True,exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
scene=bpy.context.scene
scene.unit_settings.system='METRIC'
scene.unit_settings.length_unit='CENTIMETERS'
bpy.ops.wm.obj_import(filepath=str(source),forward_axis='NEGATIVE_Z',up_axis='Y')
obj=next(o for o in scene.objects if o.type=='MESH')
bpy.context.view_layer.objects.active=obj
bpy.ops.object.transform_apply(location=False,rotation=True,scale=True)
original=obj.copy(); original.data=obj.data.copy()
archive=bpy.data.collections.new('01_Original_Hunyuan_Reference')
scene.collection.children.link(archive); archive.objects.link(original)
original.name='Original_Hunyuan_UNEDITED'; original.hide_render=True; original.hide_set(True)
original['source_obj']=str(source)
obj.name='Beaker_Repaired_10cm'
raw_counts=[len(obj.data.vertices),len(obj.data.polygons)]

# The attached background sheet occupies z=-0.23..-0.22. Cut just above it.
cut_z=-0.205
bm=bmesh.new(); bm.from_mesh(obj.data)
bmesh.ops.bisect_plane(bm,geom=list(bm.verts)+list(bm.edges)+list(bm.faces),
    plane_co=(0,0,cut_z),plane_no=(0,0,1),dist=1e-6,clear_inner=True,clear_outer=False)
loose=[v for v in bm.verts if not v.link_faces]
if loose: bmesh.ops.delete(bm,geom=loose,context='VERTS')
boundary=[e for e in bm.edges if e.is_boundary]
if any(abs(v.co.z-cut_z)>1e-4 for e in boundary for v in e.verts):
    raise RuntimeError('Unexpected boundary outside the bottom cut; inspect before filling.')
bmesh.ops.holes_fill(bm,edges=boundary,sides=0)
# Keep the vessel, discard any disconnected reconstruction flecks or bubbles.
unseen=set(bm.verts); components=[]
while unseen:
    seed=unseen.pop(); component={seed}; stack=[seed]
    while stack:
        v=stack.pop()
        for edge in v.link_edges:
            other=edge.other_vert(v)
            if other in unseen:
                unseen.remove(other); component.add(other); stack.append(other)
    components.append(component)
components.sort(key=len,reverse=True)
removed_components=len(components)-1
for component in components[1:]: bmesh.ops.delete(bm,geom=list(component),context='VERTS')
bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces))
if any(not e.is_manifold for e in bm.edges):
    raise RuntimeError('Cut result is not a closed manifold.')
bm.to_mesh(obj.data); bm.free(); obj.data.update()
for f in obj.data.polygons: f.use_smooth=True
smooth=obj.modifiers.new('Surface smoothing - adjustable','SMOOTH')
smooth.factor=.5; smooth.iterations=25
sub=obj.modifiers.new('Gentle subdivision - keep rim','SUBSURF')
sub.levels=1; sub.render_levels=1

def evaluated_bounds(o):
    deps=bpy.context.evaluated_depsgraph_get(); deps.update()
    ev=o.evaluated_get(deps)
    return [Vector([min(v[i] for v in ev.bound_box) for i in range(3)]),
            Vector([max(v[i] for v in ev.bound_box) for i in range(3)])]
low,high=evaluated_bounds(obj)
scale=.1/(high.z-low.z)
center=(low+high)/2
for v in obj.data.vertices:
    v.co=(v.co-Vector((center.x,center.y,low.z)))*scale
obj.data.update()
low,high=evaluated_bounds(obj)
obj['target_height_m']=.1
obj['repair']='Bottom sheet cut at source Z=-0.205, bottom capped, normals recalculated; smoothing modifiers remain editable.'
obj['source_run']=source.parent.name

glass=bpy.data.materials.new('Beaker_Glass_IOR1.47')
glass.use_nodes=True
bs=glass.node_tree.nodes.get('Principled BSDF')
bs.inputs['Base Color'].default_value=(.97,.995,1,1)
bs.inputs['Roughness'].default_value=.08
bs.inputs['IOR'].default_value=1.47
bs.inputs['Transmission Weight'].default_value=1
glass.diffuse_color=(.75,.92,1,.22)
obj.data.materials.clear(); obj.data.materials.append(glass)

bpy.ops.object.select_all(action='DESELECT'); obj.hide_set(False); obj.select_set(True)
bpy.context.view_layer.objects.active=obj
bpy.ops.export_scene.fbx(filepath=str(out/'Beaker_Repaired_10cm.fbx'),use_selection=True,
    object_types={'MESH'},axis_forward='-Z',axis_up='Y',apply_unit_scale=True,
    use_mesh_modifiers=True,add_leaf_bones=False)
bpy.ops.export_scene.gltf(filepath=str(out/'Beaker_Repaired_10cm.glb'),export_format='GLB',
    use_selection=True,export_apply=True)

# Render staging is kept separate from the exportable model.
staging=bpy.data.collections.new('02_Render_Staging_NOT_FOR_EXPORT')
scene.collection.children.link(staging)
def stage(o):
    for c in list(o.users_collection): c.objects.unlink(o)
    staging.objects.link(o)
bpy.ops.mesh.primitive_plane_add(size=1,location=(0,0,-.001))
floor=bpy.context.object; floor.name='Render_floor'; stage(floor)
mat=bpy.data.materials.new('Render_checker'); mat.use_nodes=True
nodes=mat.node_tree.nodes; links=mat.node_tree.links
check=nodes.new('ShaderNodeTexChecker'); check.inputs['Scale'].default_value=32
check.inputs['Color1'].default_value=(.07,.12,.16,1)
check.inputs['Color2'].default_value=(.55,.62,.68,1)
links.new(check.outputs['Color'],nodes.get('Principled BSDF').inputs['Base Color'])
nodes.get('Principled BSDF').inputs['Roughness'].default_value=.75
floor.data.materials.append(mat)
scene.world=bpy.data.worlds.new('Studio'); scene.world.use_nodes=True
scene.world.node_tree.nodes['Background'].inputs[0].default_value=(.4,.5,.65,1)
scene.world.node_tree.nodes['Background'].inputs[1].default_value=.5
for i,(position,power,size) in enumerate([((.15,-.15,.25),12,.15),((-.16,-.03,.15),8,.12),((0,.2,.2),16,.1)]):
    data=bpy.data.lights.new('Softbox_'+str(i),'AREA'); data.energy=power; data.size=size
    light=bpy.data.objects.new(data.name,data); staging.objects.link(light)
    light.location=position; light.rotation_euler=(Vector((0,0,.05))-light.location).to_track_quat('-Z','Y').to_euler()
data=bpy.data.cameras.new('Preview_camera'); camera=bpy.data.objects.new(data.name,data)
staging.objects.link(camera); scene.camera=camera
data.type='ORTHO'; data.ortho_scale=.17
scene.render.engine='CYCLES'; scene.cycles.device='CPU'
scene.cycles.samples=48; scene.cycles.use_denoising=True
scene.cycles.transmission_bounces=12; scene.cycles.max_bounces=16
scene.render.resolution_x=scene.render.resolution_y=800
scene.render.resolution_percentage=100
scene.render.image_settings.file_format='PNG'
scene.view_settings.exposure=-1.5
clay=bpy.data.materials.new('Inspection_clay'); clay.diffuse_color=(.25,.46,.6,1)
clay.use_nodes=True
clay.node_tree.nodes.get('Principled BSDF').inputs['Base Color'].default_value=(.25,.46,.6,1)
for name,position,use_glass in [('after_clay',(.18,-.25,.19),False),('after_glass',(.18,-.25,.19),True),('after_top',(0,-.001,.35),False)]:
    camera.location=position; camera.rotation_euler=(Vector((0,0,.05))-camera.location).to_track_quat('-Z','Y').to_euler()
    obj.data.materials[0]=glass if use_glass else clay
    scene.render.filepath=str(out/(name+'.png')); bpy.ops.render.render(write_still=True)
obj.data.materials[0]=glass
staging.hide_viewport=True
camera.location=(.18,-.25,.19); camera.rotation_euler=(Vector((0,0,.05))-camera.location).to_track_quat('-Z','Y').to_euler()
bpy.ops.object.select_all(action='DESELECT'); obj.select_set(True); bpy.context.view_layer.objects.active=obj
for screen in bpy.data.screens:
    for area in screen.areas:
        if area.type=='VIEW_3D':
            area.spaces.active.region_3d.view_distance=.28
            area.spaces.active.region_3d.view_location=(0,0,.05)
            area.spaces.active.clip_start=.001
            area.spaces.active.shading.type='MATERIAL'
low,high=evaluated_bounds(obj)
deps=bpy.context.evaluated_depsgraph_get(); ev=obj.evaluated_get(deps); mesh=ev.to_mesh()
bm=bmesh.new(); bm.from_mesh(mesh)
report={'source':str(source),'source_sha256':hashlib.sha256(source.read_bytes()).hexdigest(),
    'raw_vertices_faces':raw_counts,'trimmed_vertices_faces':[len(obj.data.vertices),len(obj.data.polygons)],
    'evaluated_vertices_faces':[len(mesh.vertices),len(mesh.polygons)],
    'dimensions_m':list(high-low),'bottom_z_m':low.z,
    'boundary_edges':sum(e.is_boundary for e in bm.edges),
    'nonmanifold_edges':sum(not e.is_manifold for e in bm.edges),
    'cut_source_z':cut_z,'removed_disconnected_components':removed_components,
    'glass':{'transmission':1,'IOR':1.47,'roughness':.08},
    'notes':'Original Hunyuan cavity depth and irregular rim retained. Glass is a Blender/Cycles material; FBX materials need a Unity URP transparent shader.'}
bm.free(); ev.to_mesh_clear()
if abs(report['dimensions_m'][2]-.1)>1e-6 or report['nonmanifold_edges']:
    raise RuntimeError('Repair validation failed: '+str(report))
(out/'repair_report.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
bpy.ops.wm.save_as_mainfile(filepath=str(out/'Beaker_Repaired_10cm.blend'))
print('REPAIR_REPORT',json.dumps(report))
