from pathlib import Path
import numpy as np
base=Path(__file__).resolve().parent
source=(base.parent/'AdvancedLab/scripts/render_lab.py').read_text(encoding='utf8')
# Reuse the original lab geometry, glass and hybrid lighting implementation.
prefix,body=source.split('records=[]\n',1)
env={'__file__':str(base.parent/'AdvancedLab/scripts/render_lab.py')}
exec(prefix,env)
globals().update({k:v for k,v in env.items() if not k.startswith('__')})
OUT=base; IMAGE_DIR=base/'images'; SIZE=(1920,1200); SAMPLES=12
s.cycles.samples=SAMPLES;s.render.resolution_x,s.render.resolution_y=SIZE
objects=[o for o in s.objects if o.type in ('MESH','FONT') and not o.hide_render]
for i,o in enumerate(objects,1):
 o.color=((i%16+1)/20,((i//16)%16+1)/20,(i//256+1)/20,1)
def coverage(cam):
 s.camera=cam;s.render.engine='BLENDER_WORKBENCH'
 sh=s.display.shading;sh.light='FLAT';sh.color_type='OBJECT';sh.show_shadows=False;sh.show_cavity=False;sh.show_specular_highlight=False;sh.background_type='WORLD';s.world.color=(0,0,0)
 s.display.render_aa='OFF';s.view_settings.view_transform='Raw';s.view_settings.look='None'
 path=OUT/'evidence'/(cam.name[:2]+'_ids.exr')
 s.render.image_settings.file_format='OPEN_EXR';s.render.image_settings.color_depth='32';s.render.filepath=str(path);bpy.ops.render.render(write_still=True)
 im=bpy.data.images.load(str(path),check_existing=False);arr=np.empty(im.size[0]*im.size[1]*4,np.float32);im.pixels.foreach_get(arr)
 rgb=np.rint(arr.reshape(-1,4)[:,:3]*20).astype(int);codes=rgb[:,0]+32*rgb[:,1]+1024*rgb[:,2]; un,counts=np.unique(codes,return_counts=True);seen=dict(zip(un,counts))
 count=sum(seen.get(i%16+1+32*((i//16)%16+1)+1024*(i//256+1),0)>=4 for i in range(1,len(objects)+1));bpy.data.images.remove(im)
 return int(count)
results=[]
for i,cam in enumerate(cams):
 for k,(dist,lens) in enumerate([(9,36),(11,34),(13,32)]):
  cam.location=((4.3 if i==0 else -2.8),-dist,5.0+k*.55);cam.rotation_euler=(Vector((0,1.95,1.25))-cam.location).to_track_quat('-Z','Y').to_euler();cam.data.lens=lens;bpy.context.view_layer.update()
  n=coverage(cam);print('COVERAGE',i,k,n,len(objects),flush=True)
  if n/len(objects)>=.75:break
 else:raise RuntimeError('Insufficient coverage')
 results.append([n,len(objects)])
(OUT/'evidence/coverage.json').write_text(json.dumps(results))
s.render.engine='CYCLES';s.render.image_settings.file_format='PNG';s.render.image_settings.color_depth='8';s.view_settings.view_transform='AgX';s.view_settings.look='AgX - Medium High Contrast'
env.update(globals());exec('records=[]\n'+body,env)
