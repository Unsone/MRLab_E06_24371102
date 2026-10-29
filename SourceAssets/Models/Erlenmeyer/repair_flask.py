import bpy,bmesh,json,math,hashlib
from pathlib import Path
from mathutils import Vector
from mathutils.bvhtree import BVHTree
root=Path(__file__).parent; source=root/'Raw/attempt02/model.obj'; out=root/'Final';out.mkdir(exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.wm.obj_import(filepath=str(source),forward_axis='NEGATIVE_Z',up_axis='Y')
o=bpy.context.object;o.name='Erlenmeyer_Empty_13cm';bpy.ops.object.transform_apply(location=False,rotation=True,scale=True)
vs=o.data.vertices;low=min(v.co.z for v in vs);high=max(v.co.z for v in vs);h=high-low
cx=(min(v.co.x for v in vs)+max(v.co.x for v in vs))/2;cy=(min(v.co.y for v in vs)+max(v.co.y for v in vs))/2
for v in vs:v.co=(v.co-Vector((cx,cy,low)))*(.13/h)
bm=bmesh.new();bm.from_mesh(o.data);cut=.112
bmesh.ops.bisect_plane(bm,geom=list(bm.verts)+list(bm.edges)+list(bm.faces),plane_co=(0,0,cut),plane_no=(0,0,1),dist=1e-8)
bm.normal_update();seeds=[f for f in bm.faces if .1<f.calc_center_median().z<cut and f.calc_center_median().xy.length<.006 and f.normal.z>.8]
assert seeds,'False partition not found'
removed={seeds[0]};stack=list(removed)
while stack:
 f=stack.pop()
 for e in f.edges:
  for n in e.link_faces:
   if n not in removed and all(v.co.z<=cut+1e-7 for v in n.verts):removed.add(n);stack.append(n)
assert min(f.calc_center_median().z for f in removed)>.095,'Removal reached exterior'
removed_count=len(removed);bmesh.ops.delete(bm,geom=list(removed),context='FACES')
bmesh.ops.delete(bm,geom=[v for v in bm.verts if not v.link_faces],context='VERTS')
edges=[e for e in bm.edges if e.is_boundary];adj={}
for e in edges:
 a,b=e.verts;adj.setdefault(a,[]).append(b);adj.setdefault(b,[]).append(a)
assert all(len(x)==2 for x in adj.values()) and all(abs(v.co.z-cut)<1e-6 for v in adj)
start=next(iter(adj));ring=[start];prev=None;cur=start
while True:
 nxt=next(v for v in adj[cur] if v!=prev)
 if nxt==start:break
 ring.append(nxt);prev,cur=cur,nxt
assert len(ring)==len(adj)
# The surviving exterior supplies the actual conical profile for the new inner wall.
tree=BVHTree.FromBMesh(bm);template=[v.co.copy() for v in ring];old=ring
for z in [.109,.106,.102,.096,.088,.080,.072,.064,.056,.048,.040,.032,.024,.016,.009,.005,.0035]:
 new=[]
 for p in template:
  d=Vector((p.x,p.y,0)).normalized();hit,_,_,_=tree.ray_cast(Vector((0,0,z)),d,.2)
  assert hit is not None,(z,list(d))
  target=hit.xy.length-.0025
  blend=min(1,(cut-z)/.012);radius=p.xy.length*(1-blend)+target*blend
  new.append(bm.verts.new((d.x*radius,d.y*radius,z)))
 for i in range(len(old)):
  j=(i+1)%len(old);bm.faces.new((old[i],old[j],new[j],new[i]))
 old=new
bm.faces.new(tuple(reversed(old)))
# Level the entire lip band; maintain a gradual transition down the neck.
for v in bm.verts:
 if v.co.z>.124:v.co.z=.128
 elif v.co.z>.119:v.co.z=.119+(v.co.z-.119)*(.009/.005)
bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces));assert all(e.is_manifold for e in bm.edges)
bm.to_mesh(o.data);bm.free()
g=o.vertex_groups.new(name='Wall_smoothing_keep_rim_and_base')
for v in o.data.vertices:g.add([v.index],max(0,min(1,(v.co.z-.005)/.009,(.127-v.co.z)/.009)),'REPLACE')
m=o.modifiers.new('Relax_generated_surface','SMOOTH');m.factor=.45;m.iterations=12;m.vertex_group=g.name
rim=o.vertex_groups.new(name='Rim_horizontal_polish')
for v in o.data.vertices:rim.add([v.index],max(0,min(1,(v.co.z-.119)/.006)),'REPLACE')
m=o.modifiers.new('Polish_rim_contour','SMOOTH');m.factor=.5;m.iterations=15;m.use_z=False;m.vertex_group=rim.name
m=o.modifiers.new('Surface_refinement','SUBSURF');m.levels=2
for m in list(o.modifiers):bpy.ops.object.modifier_apply(modifier=m.name)
lo=min(v.co.z for v in o.data.vertices);hi=max(v.co.z for v in o.data.vertices)
for v in o.data.vertices:v.co=(v.co-Vector((0,0,lo)))*(.13/(hi-lo))
for f in o.data.polygons:f.use_smooth=True
bm=bmesh.new();bm.from_mesh(o.data);tree=BVHTree.FromBMesh(bm)
probes=[]
for x,y in [(0,0),(.006,0),(-.006,0),(0,.006),(0,-.006)]:
 p,n,idx,d=tree.ray_cast(Vector((x,y,.15)),Vector((0,0,-1)),.2);assert p is not None and .001<p.z<.008,(x,y,p)
 probes.append({'xy':[x,y],'floor_z_m':p.z})
wall=[]
for z in [.012,.03,.05,.07,.09,.105,.117]:
 for angle in range(0,360,15):
  d=Vector((math.cos(math.radians(angle)),math.sin(math.radians(angle)),0));p,_,_,_=tree.ray_cast(Vector((0,0,z)),d,.2);assert p is not None
  q,_,_,_=tree.ray_cast(p+d*1e-6,d,.2);assert q is not None,(z,angle)
  gap=(q-p).length;assert gap>.0005,(z,angle,gap);wall.append(gap)
report={'raw_obj_sha256':hashlib.sha256(source.read_bytes()).hexdigest(),'removed_partition_faces':removed_count,'height_m':.13,'boundary_edges':sum(e.is_boundary for e in bm.edges),'nonmanifold_edges':sum(not e.is_manifold for e in bm.edges),'downward_cavity_probes':probes,'wall_probe_count':len(wall),'minimum_sampled_wall_m':min(wall),'vertices':len(o.data.vertices),'faces':len(o.data.polygons),'repair':'Original exterior retained, false liquid cap removed, inner wall follows generated exterior; lip leveled and surface relaxed. Height chosen as editable 13 cm, not measured from photo.'}
assert report['nonmanifold_edges']==0;bm.free()
glass=bpy.data.materials.new('Glass_IOR_1.47');glass.use_nodes=True;bs=glass.node_tree.nodes['Principled BSDF'];bs.inputs['Transmission Weight'].default_value=1;bs.inputs['IOR'].default_value=1.47;bs.inputs['Roughness'].default_value=.08;o.data.materials.append(glass)
s=bpy.context.scene;s.unit_settings.system='METRIC';s.unit_settings.length_unit='CENTIMETERS'
bpy.ops.export_scene.gltf(filepath=str(out/'Erlenmeyer_Empty_13cm.glb'),export_format='GLB',use_selection=True)
bpy.ops.export_scene.fbx(filepath=str(out/'Erlenmeyer_Empty_13cm.fbx'),use_selection=True,object_types={'MESH'},axis_forward='-Z',axis_up='Y',add_leaf_bones=False)
bpy.ops.wm.obj_export(filepath=str(out/'Erlenmeyer_Empty_13cm.obj'),export_selected_objects=True,forward_axis='NEGATIVE_Z',up_axis='Y')
(out/'repair_report.json').write_text(json.dumps(report,indent=2),encoding='utf8')
s.render.engine='CYCLES';s.cycles.samples=24;s.cycles.use_denoising=True;s.render.resolution_x=s.render.resolution_y=800;s.render.resolution_percentage=100
s.world=bpy.data.worlds.new('Studio');s.world.use_nodes=True;s.world.node_tree.nodes['Background'].inputs[0].default_value=(.65,.7,.8,1);s.world.node_tree.nodes['Background'].inputs[1].default_value=.5
center=Vector((0,0,.065))
for i,pos in enumerate([(.2,-.3,.4),(-.3,-.1,.2),(0,.3,.3)]):
 d=bpy.data.lights.new('Softbox'+str(i),'AREA');d.energy=8;d.size=.25;l=bpy.data.objects.new(d.name,d);s.collection.objects.link(l);l.location=pos;l.rotation_euler=(center-l.location).to_track_quat('-Z','Y').to_euler()
d=bpy.data.cameras.new('Camera');c=bpy.data.objects.new('Camera',d);s.collection.objects.link(c);s.camera=c;d.type='ORTHO';d.ortho_scale=.175
c.location=(.19,-.3,.20);c.rotation_euler=(center-c.location).to_track_quat('-Z','Y').to_euler()
clay=bpy.data.materials.new('Inspection_Clay');clay.diffuse_color=(.18,.4,.5,1);clay.use_nodes=True;clay.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value=(.18,.4,.5,1)
o.data.materials[0]=clay;s.render.filepath=str(out/'final_clay.png');bpy.ops.render.render(write_still=True)
section=o.copy();section.data=o.data.copy();s.collection.objects.link(section);section.name='Inspection_section_NOT_EXPORT'
bm=bmesh.new();bm.from_mesh(section.data);bmesh.ops.bisect_plane(bm,geom=list(bm.verts)+list(bm.edges)+list(bm.faces),plane_co=(0,0,0),plane_no=(0,-1,0),clear_outer=True,dist=1e-7);bm.to_mesh(section.data);bm.free()
o.hide_render=True;c.location=(.06,-.35,.17);c.rotation_euler=(center-c.location).to_track_quat('-Z','Y').to_euler();s.render.filepath=str(out/'final_section.png');bpy.ops.render.render(write_still=True)
o.hide_render=False;section.hide_render=True;section.hide_set(True);o.data.materials[0]=glass
c.location=(.19,-.3,.20);c.rotation_euler=(center-c.location).to_track_quat('-Z','Y').to_euler();s.render.filepath=str(out/'final_glass.png');bpy.ops.render.render(write_still=True)
bpy.ops.object.select_all(action='DESELECT');o.select_set(True);bpy.context.view_layer.objects.active=o
for screen in bpy.data.screens:
 for area in screen.areas:
  if area.type=='VIEW_3D':area.spaces.active.region_3d.view_distance=.24;area.spaces.active.region_3d.view_location=center
bpy.ops.wm.save_as_mainfile(filepath=str(out/'Erlenmeyer_Empty_13cm.blend'))
print('REPAIR_COMPLETE',json.dumps(report))

