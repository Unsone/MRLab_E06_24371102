"""Blender-only audit of a real generated flask, including a diagnostic cross section."""
import bpy,bmesh,json,sys
from pathlib import Path
from mathutils import Vector
from mathutils.bvhtree import BVHTree

source,out=map(lambda p:Path(p).resolve(),sys.argv[sys.argv.index('--')+1:])
out.mkdir(parents=True,exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.wm.obj_import(filepath=str(source),forward_axis='NEGATIVE_Z',up_axis='Y')
o=next(o for o in bpy.context.scene.objects if o.type=='MESH')
bpy.context.view_layer.objects.active=o
bpy.ops.object.transform_apply(location=False,rotation=True,scale=True)
vs=[v.co for v in o.data.vertices]
lo=Vector([min(v[i] for v in vs) for i in range(3)])
hi=Vector([max(v[i] for v in vs) for i in range(3)])
center=(lo+hi)/2; height=hi.z-lo.z; span=max(hi-lo)
bm=bmesh.new();bm.from_mesh(o.data);tree=BVHTree.FromBMesh(bm)
profile=[]
for f in [.05,.15,.25,.4,.55,.7,.8,.9,.97]:
    z=lo.z+height*f
    near=[v for v in vs if abs(v.z-z)<height*.015]
    profile.append({'height_fraction':f,'count':len(near),'x_span':max(v.x for v in near)-min(v.x for v in near) if near else None,
                    'y_span':max(v.y for v in near)-min(v.y for v in near) if near else None})
rayhits=[]; origin=Vector((center.x,center.y,hi.z+height*.1))
for _ in range(8):
    p,n,idx,dist=tree.ray_cast(origin,Vector((0,0,-1)),height*2)
    if p is None:break
    rayhits.append({'z':p.z,'fraction':(p.z-lo.z)/height,'normal':list(n)})
    origin=p-Vector((0,0,height*1e-5))
report={'source':str(source),'bounds':[list(lo),list(hi)],'vertices':len(vs),'faces':len(o.data.polygons),
        'boundary_edges':sum(e.is_boundary for e in bm.edges),'profile':profile,'center_vertical_hits':rayhits,
        'note':'Profile and ray data are diagnostic, not automatic semantic acceptance.'}
bm.free();(out/'inspection.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
mat=bpy.data.materials.new('Clay');mat.use_nodes=True
mat.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value=(.24,.43,.53,1)
mat.node_tree.nodes['Principled BSDF'].inputs['Roughness'].default_value=.6
o.data.materials.clear();o.data.materials.append(mat)
for f in o.data.polygons:f.use_smooth=True
s=bpy.context.scene;s.render.engine='CYCLES';s.cycles.device='CPU';s.cycles.samples=20;s.cycles.use_denoising=True
s.render.resolution_x=s.render.resolution_y=800;s.render.resolution_percentage=100
s.world=bpy.data.worlds.new('World');s.world.use_nodes=True
s.world.node_tree.nodes['Background'].inputs[0].default_value=(.65,.7,.8,1)
s.world.node_tree.nodes['Background'].inputs[1].default_value=.4
for i,offset in enumerate([(2,-3,4),(-3,-1,2),(0,3,3)]):
    d=bpy.data.lights.new('Light'+str(i),'AREA');d.energy=50*span*span;d.size=span*2
    l=bpy.data.objects.new(d.name,d);s.collection.objects.link(l);l.location=center+Vector(offset)*span
    l.rotation_euler=(center-l.location).to_track_quat('-Z','Y').to_euler()
d=bpy.data.cameras.new('Camera');c=bpy.data.objects.new('Camera',d);s.collection.objects.link(c);s.camera=c
d.type='ORTHO';d.ortho_scale=span*1.35
for name,offset in [('perspective',(2,-3,1.6)),('front',(0,-4,.1)),('top',(0,-.01,4))]:
    c.location=center+Vector(offset)*span;c.rotation_euler=(center-c.location).to_track_quat('-Z','Y').to_euler()
    s.render.filepath=str(out/(name+'.png'));bpy.ops.render.render(write_still=True)
section=o.copy();section.data=o.data.copy();s.collection.objects.link(section)
bm=bmesh.new();bm.from_mesh(section.data)
bmesh.ops.bisect_plane(bm,geom=list(bm.verts)+list(bm.edges)+list(bm.faces),plane_co=center,
    plane_no=(0,-1,0),clear_outer=True,clear_inner=False,dist=span*1e-6)
bm.to_mesh(section.data);bm.free();o.hide_render=True
c.location=center+Vector((1,-4,1.2))*span;c.rotation_euler=(center-c.location).to_track_quat('-Z','Y').to_euler()
s.render.filepath=str(out/'section.png');bpy.ops.render.render(write_still=True)
o.hide_render=False;section.hide_render=True;section.hide_set(True)
bpy.ops.wm.save_as_mainfile(filepath=str(out/'raw_inspection.blend'))
print(json.dumps(report))
