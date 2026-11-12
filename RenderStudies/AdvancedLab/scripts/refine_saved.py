"""Refine the receiver bias on the saved scenes, without rebuilding geometry."""
import bpy,sys,json,time,argparse,ast,math
import numpy as np
from mathutils import Vector
from mathutils.bvhtree import BVHTree
from pathlib import Path
P=Path(__file__).resolve().parents[1]
ap=argparse.ArgumentParser();ap.add_argument('--test',action='store_true');a=ap.parse_args(sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else [])
records=[]
for i in ([1] if a.test else [1,2]):
 start=time.time()
 bpy.ops.wm.open_mainfile(filepath=str(P/'scene'/f'view{i:02d}_hybrid.blend'))
 scene=bpy.context.scene;scene.render.threads_mode='FIXED';scene.render.threads=8
 changed=0
 for group in bpy.data.node_groups:
  if not group.name.startswith('CSM /'):continue
  for n in group.nodes:
   if n.bl_idname=='ShaderNodeMath' and n.operation=='ADD' and abs(n.inputs[1].default_value-.002)<1e-6:
    n.inputs[1].default_value=.025;changed+=1
 print('BIAS_NODES_UPDATED',changed,flush=True)
 scene.cycles.samples=8 if a.test else 32
 scene.render.resolution_x=900 if a.test else 1800;scene.render.resolution_y=600 if a.test else 1200
 if not a.test:
  # Reuse the actual generation/validation functions without executing its
  # scene construction. All geometry and packed depth images come from blend.
  global_ns=globals();global_ns.update(s=scene,OUT=P,SHADOW_SIZE=2048,SUN=Vector((.82,.36,-.42)).normalized(),bg=scene.world.node_tree.nodes['Background'])
  opaque=[];transparent=[]
  for ob in scene.objects:
   if ob.type not in ('MESH','FONT'):continue
   mats=list(ob.data.materials);p=mats[0].node_tree.nodes.get('Principled BSDF') if mats else None
   (transparent if p and p.inputs['Transmission Weight'].default_value>0 else opaque).append(ob)
  global_ns.update(OPAQUE=opaque,TRANSPARENT=transparent)
  source=ast.parse((P/'scripts/render_lab.py').read_text(encoding='utf-8'))
  functions=[n for n in source.body if isinstance(n,ast.FunctionDef) and n.name in ['vecdot','mathnode','white_model','verify_maps']]
  exec(compile(ast.Module(body=functions,type_ignores=[]),'shared_render_functions','exec'),global_ns)
  cam=scene.camera;sun=global_ns['SUN'];basis=sun.to_track_quat('-Z','Y').to_matrix();right=basis@Vector((1,0,0));up=basis@Vector((0,1,0))
  bounds=[ob.matrix_world@Vector(c) for ob in scene.objects if ob.type=='MESH' for c in ob.bound_box]
  z0=min(p.dot(sun) for p in bounds)-2;z1=max(p.dot(sun) for p in bounds)+2
  frame=cam.data.view_frame(scene=scene);splits=[.1,4,7,12,24];maps=[]
  for j,(near,far) in enumerate(zip(splits[:-1],splits[1:])):
   corners=[cam.matrix_world@(v*(d/abs(v.z))) for d in [near*.92,far*1.03] for v in frame]
   xs=[p.dot(right) for p in corners];ys=[p.dot(up) for p in corners]
   scale=max(max(xs)-min(xs),max(ys)-min(ys))*1.06;texel=scale/2048
   cx=round(((min(xs)+max(xs))/2)/texel)*texel;cy=round(((min(ys)+max(ys))/2)/texel)*texel
   origin=right*cx+up*cy+sun*z0
   image=next(im for im in bpy.data.images if Path(im.filepath).name==f'view{i:02d}_cascade{j+1}.exr')
   maps.append(dict(image=image,origin=origin,forward=sun,right=right,up=up,scale=scale,depth=z1-z0,near=near,far=far,texel=texel))
  validation=verify_maps(maps,cam,i)
  scene.render.filepath=str(P/'images'/f'view{i:02d}_hybrid.png')
  bpy.ops.wm.save_as_mainfile(filepath=str(P/'scene'/f'view{i:02d}_hybrid.blend'))
 else:scene.render.filepath=str(P/'evidence/previews'/f'view{i:02d}_bias_test.png')
 bpy.ops.render.render(write_still=True)
 if not a.test:
  white_model(cam,P/'images'/f'view{i:02d}_white.png')
  record={'view':i,'camera_world_matrix':[list(row) for row in cam.matrix_world],'lens_mm':cam.data.lens,'resolution':[1800,1200],'samples':32,'seconds':round(time.time()-start,2),'hybrid':f'view{i:02d}_hybrid.png','white':f'view{i:02d}_white.png','shadow_validation':validation,'cascades':[{k:list(v) if isinstance(v,Vector) else v for k,v in m.items() if k!='image'} for m in maps]}
  records.append(record);(P/'evidence'/f'view{i:02d}.json').write_text(json.dumps(record,indent=2),encoding='utf-8')
  print('VIEW_COMPLETE',i,record['seconds'],flush=True)
if not a.test:
 report={'blender':bpy.app.version_string,'method':'EEVEE rasterized 4-cascade sunlight visibility + Cycles CPU path transport','shadow_resolution':2048,'shadow_filter':'4-tap PCF, slope-scale bias plus 0.025 m offset, 10% split blending, texel snapping','sun_direction':list(SUN),'mesh_objects':sum(o.type=='MESH' for o in scene.objects),'white_model':'EEVEE basic normal shading, one neutral color, no shadows/reflection/transmission/path tracing','views':records,'limitations':['CSM excludes transmissive casters and sun specular disk; glass uses traced area/world illumination.','Camera-dependent indirect sun cache; no complete sun caustics.','Receiver bias trades reduced self-shadow artifacts for some loss of contact-shadow precision; area-light contact shadows remain path traced.','Offline study only.']}
 (P/'evidence/render_report.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
 print('RENDER_STUDY_COMPLETE',flush=True)
