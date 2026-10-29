"""Remove the reconstructed liquid surface and extend the cavity to the real bottom."""
import bpy, bmesh, json, sys, hashlib
from pathlib import Path
from mathutils import Vector
from mathutils.bvhtree import BVHTree

args=sys.argv[sys.argv.index('--')+1:]
source,out=Path(args[0]).resolve(),Path(args[1]).resolve()
out.mkdir(parents=True,exist_ok=True)
bpy.ops.wm.open_mainfile(filepath=str(source))
scene=bpy.context.scene
o=bpy.data.objects['Beaker_Repaired_10cm']
o.name='Beaker_Empty_10cm'
staging=bpy.data.collections['02_Render_Staging_NOT_FOR_EXPORT']; staging.hide_viewport=False
before=o.copy(); before.data=o.data.copy()
bpy.data.collections['01_Original_Hunyuan_Reference'].objects.link(before)
before.name='Previous_shallow_version_REFERENCE_ONLY'; before.hide_set(True); before.hide_render=True

bm=bmesh.new(); bm.from_mesh(o.data)
cut=.081
bmesh.ops.bisect_plane(bm,geom=list(bm.verts)+list(bm.edges)+list(bm.faces),
    plane_co=(0,0,cut),plane_no=(0,0,1),dist=1e-7,clear_inner=False,clear_outer=False)
bm.normal_update()
seeds=[f for f in bm.faces if .073<f.calc_center_median().z<.079
       and f.calc_center_median().xy.length<.02 and f.normal.z>.8]
if not seeds: raise RuntimeError('Could not find the false liquid surface.')
seed=min(seeds,key=lambda f:f.calc_center_median().xy.length)
removed={seed}; stack=[seed]
while stack:
    f=stack.pop()
    for e in f.edges:
        for other in e.link_faces:
            if other not in removed and all(v.co.z<=cut+1e-6 for v in other.verts):
                removed.add(other); stack.append(other)
if any(f.calc_center_median().z<.06 for f in removed):
    raise RuntimeError('Removal reached the actual vessel base; aborting.')
removed_count=len(removed)
bmesh.ops.delete(bm,geom=list(removed),context='FACES')
loose=[v for v in bm.verts if not v.link_faces]
if loose: bmesh.ops.delete(bm,geom=loose,context='VERTS')
edges=[e for e in bm.edges if e.is_boundary]
if not edges or any(abs(v.co.z-cut)>1e-5 for e in edges for v in e.verts):
    raise RuntimeError('Expected one inner-wall boundary at the liquid-surface cut.')
adj={}
for e in edges:
    a,b=e.verts; adj.setdefault(a,[]).append(b); adj.setdefault(b,[]).append(a)
if any(len(v)!=2 for v in adj.values()): raise RuntimeError('Inner boundary is not a simple loop.')
start=next(iter(adj)); ring=[start]; previous=None; current=start
while True:
    following=next(v for v in adj[current] if v!=previous)
    if following==start: break
    ring.append(following); previous,current=current,following
    if len(ring)>len(adj): raise RuntimeError('Boundary traversal failed.')
if len(ring)!=len(adj): raise RuntimeError('Multiple openings found; inspect before repairing.')
ring_center=sum((v.co for v in ring),Vector())/len(ring)
template=[v.co.copy() for v in ring]
previous_ring=ring
# Retain the generated upper inner wall and rim, extend its actual contour downwards.
for z in [.077,.07,.055,.04,.025,.012,.006,.0038,.003]:
    taper=1-.06*(cut-z)/(cut-.003)
    next_ring=[bm.verts.new((ring_center.x+(p.x-ring_center.x)*taper,
                          ring_center.y+(p.y-ring_center.y)*taper,z)) for p in template]
    for i in range(len(ring)):
        j=(i+1)%len(ring)
        bm.faces.new((previous_ring[i],previous_ring[j],next_ring[j],next_ring[i]))
    previous_ring=next_ring
bottom=bm.faces.new(tuple(reversed(previous_ring)))
bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces))
if any(not e.is_manifold for e in bm.edges): raise RuntimeError('Repaired cavity is not manifold.')
bm.to_mesh(o.data); bm.free(); o.data.update()
for f in o.data.polygons: f.use_smooth=True
weights=o.vertex_groups.new(name='Smooth_walls_keep_bottom_flat')
for v in o.data.vertices:
    weight=max(0,min(1,(v.co.z-.005)/.008))
    weights.add([v.index],weight,'REPLACE')
for mod in o.modifiers:
    if mod.type=='SMOOTH': mod.vertex_group=weights.name

def evaluated():
    deps=bpy.context.evaluated_depsgraph_get(); deps.update()
    ev=o.evaluated_get(deps)
    return ev,ev.to_mesh()
ev,mesh=evaluated()
lo=Vector([min(v.co[i] for v in mesh.vertices) for i in range(3)])
hi=Vector([max(v.co[i] for v in mesh.vertices) for i in range(3)])
factor=.1/(hi.z-lo.z)
ev.to_mesh_clear()
for v in o.data.vertices: v.co=(v.co-Vector((0,0,lo.z)))*factor
o.data.update()
ev,mesh=evaluated(); check=bmesh.new(); check.from_mesh(mesh)
tree=BVHTree.FromBMesh(check)
low=min(v.co.z for v in mesh.vertices); high=max(v.co.z for v in mesh.vertices)
probes=[]
for x,y in [(0,0),(.015,0),(-.015,0),(0,.015),(0,-.015),(.022,.01),(-.022,-.01)]:
    p,n,index,distance=tree.ray_cast(Vector((x,y,.12)),Vector((0,0,-1)),.2)
    if p is None or p.z>.007 or p.z<.001:
        raise RuntimeError('Cavity is obstructed or base missing at '+str((x,y,p)))
    probes.append({'xy_m':[x,y],'inner_bottom_z_m':p.z,'open_depth_from_top_m':high-p.z})
# Check wall clearance at many angles/heights, using radial rays from inside outward.
import math
wall_samples=[]
for z in [.015,.035,.055,.075]:
    for angle in range(0,360,15):
        direction=Vector((math.cos(math.radians(angle)),math.sin(math.radians(angle)),0))
        p,n,idx,d=tree.ray_cast(Vector((ring_center.x,ring_center.y,z)),direction,.1)
        if p is None: raise RuntimeError('Missing inner wall.')
        q,nn,ii,dd=tree.ray_cast(p+direction*1e-5,direction,.1)
        if q is None or dd<.0005: raise RuntimeError('Intersecting or too-thin wall at '+str((z,angle,dd)))
        wall_samples.append(dd+1e-5)
report={'source_blend':str(source),'source_sha256':hashlib.sha256(source.read_bytes()).hexdigest(),
        'removed_false_surface_faces':removed_count,'inner_wall_join_height_m':cut,
        'base_vertices_faces':[len(o.data.vertices),len(o.data.polygons)],
        'evaluated_vertices_faces':[len(mesh.vertices),len(mesh.polygons)],
        'height_m':high-low,'boundary_edges':sum(e.is_boundary for e in check.edges),
        'nonmanifold_edges':sum(not e.is_manifold for e in check.edges),
        'cavity_downward_probes':probes,'radial_wall_probe_count':len(wall_samples),
        'minimum_sampled_wall_thickness_m':min(wall_samples),
        'note':'False liquid-surface cap removed; missing lower inner wall extended from existing contour; actual outer base retained. Not a fluid simulation collider.'}
if report['nonmanifold_edges'] or abs(report['height_m']-.1)>1e-6: raise RuntimeError(str(report))
check.free(); ev.to_mesh_clear()
o['repair']='False liquid surface removed; cavity extended to 3 mm above real base; upper generated rim and exterior retained.'
bpy.ops.object.select_all(action='DESELECT'); o.hide_set(False); o.select_set(True)
bpy.context.view_layer.objects.active=o
bpy.ops.export_scene.fbx(filepath=str(out/'Beaker_Empty_10cm.fbx'),use_selection=True,
    object_types={'MESH'},axis_forward='-Z',axis_up='Y',apply_unit_scale=True,use_mesh_modifiers=True,add_leaf_bones=False)
bpy.ops.export_scene.gltf(filepath=str(out/'Beaker_Empty_10cm.glb'),export_format='GLB',use_selection=True,export_apply=True)
glass=bpy.data.materials['Beaker_Glass_IOR1.47']
clay=bpy.data.materials.get('Inspection_clay') or bpy.data.materials.new('Inspection_clay')
clay.use_nodes=True
clay.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value=(.12,.3,.42,1)
clay.node_tree.nodes['Principled BSDF'].inputs['Roughness'].default_value=.55
(out/'empty_repair_report.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
camera=scene.camera; scene.cycles.samples=32
for name,position,mat in [('empty_clay',(.14,-.20,.23),clay),('empty_glass',(.18,-.25,.19),glass)]:
    o.data.materials[0]=mat
    camera.location=position; camera.rotation_euler=(Vector((0,0,.045))-camera.location).to_track_quat('-Z','Y').to_euler()
    scene.render.filepath=str(out/(name+'.png')); bpy.ops.render.render(write_still=True)
# Inspection-only section: hides front half so the entire depth and true base are visible.
section=o.copy(); section.data=o.data.copy(); staging.objects.link(section)
section.name='Inspection_half_section_NOT_EXPORT'; section.data.materials[0]=clay
bpy.ops.object.select_all(action='DESELECT'); section.select_set(True); bpy.context.view_layer.objects.active=section
for mod in list(section.modifiers): bpy.ops.object.modifier_apply(modifier=mod.name)
bm=bmesh.new(); bm.from_mesh(section.data)
bmesh.ops.bisect_plane(bm,geom=list(bm.verts)+list(bm.edges)+list(bm.faces),plane_co=(0,0,0),plane_no=(0,-1,0),clear_outer=True,clear_inner=False,dist=1e-6)
bm.to_mesh(section.data); bm.free()
o.hide_render=True
camera.location=(.12,-.26,.15); camera.rotation_euler=(Vector((0,0,.045))-camera.location).to_track_quat('-Z','Y').to_euler()
scene.render.filepath=str(out/'empty_section.png'); bpy.ops.render.render(write_still=True)
o.hide_render=False; section.hide_render=True; section.hide_set(True)
o.data.materials[0]=glass
staging.hide_viewport=True
bpy.ops.object.select_all(action='DESELECT'); o.select_set(True); bpy.context.view_layer.objects.active=o
camera.location=(.18,-.25,.19); camera.rotation_euler=(Vector((0,0,.045))-camera.location).to_track_quat('-Z','Y').to_euler()
(out/'empty_repair_report.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
bpy.ops.wm.save_as_mainfile(filepath=str(out/'Beaker_Empty_10cm.blend'))
print('EMPTY_REPAIR_REPORT',json.dumps(report))
