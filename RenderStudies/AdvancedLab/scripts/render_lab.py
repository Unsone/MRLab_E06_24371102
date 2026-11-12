"""Independent still study: raster CSM sunlight + Cycles transport, no Unity edits.

Blender 4.2: blender -b -t 8 --python render_lab.py -- --preview
Four orthographic EEVEE depth maps are sampled by explicit shader comparisons.
The resulting diffuse sunlight is added as outgoing radiance; Cycles transports
that radiance and evaluates area-light illumination, reflection and refraction.
"""
import bpy, math, json, sys, time, hashlib, argparse
import numpy as np
from mathutils.bvhtree import BVHTree
from pathlib import Path
from mathutils import Vector

ROOT = Path(__file__).resolve().parents[3]
OUT = Path(__file__).resolve().parents[1]
parser = argparse.ArgumentParser()
parser.add_argument('--preview', action='store_true')
parser.add_argument('--view', type=int, default=0)
args = parser.parse_args(sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else [])
PREVIEW = args.preview
SIZE = (960, 640) if PREVIEW else (1800, 1200)
SHADOW_SIZE = 1024 if PREVIEW else 2048
SAMPLES = 24 if PREVIEW else 32
for d in ('images','evidence','scene'): (OUT/d).mkdir(exist_ok=True)
IMAGE_DIR=OUT/'evidence/previews' if PREVIEW else OUT/'images'
IMAGE_DIR.mkdir(parents=True,exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
s=bpy.context.scene
s.render.engine='CYCLES';s.cycles.device='CPU';s.cycles.samples=SAMPLES
s.cycles.use_denoising=True;s.cycles.max_bounces=10;s.cycles.transmission_bounces=8
s.cycles.glossy_bounces=4;s.cycles.diffuse_bounces=4
s.cycles.use_adaptive_sampling=True;s.cycles.adaptive_threshold=.035
s.render.threads_mode='FIXED';s.render.threads=8
s.render.resolution_x,s.render.resolution_y=SIZE;s.render.resolution_percentage=100
s.render.image_settings.file_format='PNG';s.render.image_settings.color_mode='RGB'
s.view_settings.view_transform='AgX';s.view_settings.look='AgX - Medium High Contrast'
s.world=bpy.data.worlds.new('Cool ambient environment');s.world.use_nodes=True
bg=s.world.node_tree.nodes.get('Background');bg.inputs[0].default_value=(.60,.73,1,1);bg.inputs[1].default_value=.22
MATS={};OPAQUE=[];TRANSPARENT=[];VESSEL_PROTOTYPES={}
def mat(name,color,rough=.45,metal=0,trans=0):
 m=bpy.data.materials.new(name);m.use_nodes=True;m.diffuse_color=(*color,1)
 p=m.node_tree.nodes.get('Principled BSDF');p.inputs['Base Color'].default_value=(*color,1)
 p.inputs['Roughness'].default_value=rough;p.inputs['Metallic'].default_value=metal
 p.inputs['Transmission Weight'].default_value=trans;p.inputs['IOR'].default_value=1.46 if trans else 1.5
 m['csm_color']=list(color);m['csm_gain']=0 if trans else 1-metal
 MATS[name]=m;return m
ivory=mat('Warm porcelain',(.72,.76,.73),.32)
stone=mat('Ivory terrazzo worktop',(.65,.69,.66),.25)
navy=mat('Deep petrol enamel',(.025,.11,.13),.3,.15)
wood=mat('Natural oak',(.33,.18,.075),.4)
floor=mat('Limestone floor',(.36,.39,.37),.52)
wall=mat('Plaster',(.61,.64,.60),.75)
dark=mat('Graphite',(.018,.026,.032),.38)
metal=mat('Brushed stainless steel',(.52,.59,.62),.22,.85)
brass=mat('Brass',(.55,.34,.12),.24,.75)
paper=mat('Paper',(.8,.77,.65),.7)
teal=mat('Teal accents',(.02,.35,.33),.35)
glass=mat('Optical glass',(.93,.98,1),.055,trans=1)
blue=mat('Cyan solution',(.035,.48,.65),.13,trans=.78)
amber=mat('Amber solution',(.70,.24,.025),.13,trans=.72)
red=mat('Rose solution',(.53,.025,.065),.16,trans=.68)
green=mat('Green solution',(.11,.48,.22),.13,trans=.7)
# Subtle physical bump, kept out of the neutral white-model render.
for m,scale,strength in [(stone,95,.055),(floor,32,.07),(wood,8,.07),(wall,45,.025)]:
 n=m.node_tree.nodes;l=m.node_tree.links;p=n.get('Principled BSDF')
 noise=n.new('ShaderNodeTexNoise');noise.inputs['Scale'].default_value=scale
 bump=n.new('ShaderNodeBump');bump.inputs['Strength'].default_value=strength;bump.inputs['Distance'].default_value=.015
 l.new(noise.outputs['Fac'],bump.inputs['Height']);l.new(bump.outputs['Normal'],p.inputs['Normal'])

def assign(o,m):
 o.data.materials.clear();o.data.materials.append(m)
 (TRANSPARENT if m.node_tree.nodes.get('Principled BSDF').inputs['Transmission Weight'].default_value>0 else OPAQUE).append(o)
 return o
def cube(name,loc,dim,m,bevel=.015):
 bpy.ops.mesh.primitive_cube_add(size=1,location=loc);o=bpy.context.object;o.name=name;o.dimensions=dim
 bpy.ops.object.transform_apply(location=False,rotation=False,scale=True);assign(o,m)
 if bevel:
  mod=o.modifiers.new('Machined edges','BEVEL');mod.width=bevel;mod.segments=3
  mod=o.modifiers.new('Corner normals','WEIGHTED_NORMAL')
 return o
def cyl(name,loc,r,depth,m):
 bpy.ops.mesh.primitive_cylinder_add(vertices=48,radius=r,depth=depth,location=loc);o=bpy.context.object;o.name=name;assign(o,m)
 for p in o.data.polygons:p.use_smooth=True
 mod=o.modifiers.new('Soft edges','BEVEL');mod.width=min(.008,r*.12);mod.segments=3
 return o
def sphere(name,loc,scale,m):
 bpy.ops.mesh.primitive_uv_sphere_add(segments=24,ring_count=12,location=loc);o=bpy.context.object;o.name=name;o.scale=scale;assign(o,m)
 for p in o.data.polygons:p.use_smooth=True
 return o
def rod(name,a,b,r,m):
 a,b=Vector(a),Vector(b);o=cyl(name,(a+b)/2,r,(b-a).length,m);o.rotation_euler=(b-a).to_track_quat('Z','Y').to_euler();return o
def text(label,loc,size,m,rotation=(math.pi/2,0,0)):
 bpy.ops.object.text_add(location=loc,rotation=rotation);o=bpy.context.object;o.name='Lettering '+label
 o.data.body=label;o.data.size=size;o.data.extrude=.0005;o.data.align_x='CENTER';o.data.materials.append(m);OPAQUE.append(o);return o
def area(name,loc,target,power,size,color):
 d=bpy.data.lights.new(name,'AREA');d.energy=power;d.shape='DISK';d.size=size;d.color=color
 o=bpy.data.objects.new(name,d);s.collection.objects.link(o);o.location=loc;o.rotation_euler=(Vector(target)-o.location).to_track_quat('-Z','Y').to_euler();return o
def vessel(kind,name,loc,height,m=glass):
 rel='SourceAssets/Models/Beaker_Empty_10cm/Beaker_Empty_10cm.blend' if kind=='beaker' else 'SourceAssets/Models/Erlenmeyer/Final/Erlenmeyer_Empty_13cm.blend'
 wanted='Beaker_Empty_10cm' if kind=='beaker' else 'Erlenmeyer_Empty_13cm'
 if kind in VESSEL_PROTOTYPES:
  o=VESSEL_PROTOTYPES[kind].copy();s.collection.objects.link(o)
 else:
  with bpy.data.libraries.load(str(ROOT/rel),link=False) as (src,dst):dst.objects=[wanted]
  o=dst.objects[0];s.collection.objects.link(o);o.hide_render=False;o.hide_set(False)
  # Evaluate the retained Blender modifiers once on our local copy. Shared
  # geometry avoids reevaluating four identical repaired cups at every pass.
  bpy.context.view_layer.update()
  if o.modifiers:
   mesh=bpy.data.meshes.new_from_object(o.evaluated_get(bpy.context.evaluated_depsgraph_get()))
   o.modifiers.clear();o.data=mesh
  VESSEL_PROTOTYPES[kind]=o
 o.name=name;o.hide_render=False;o.hide_set(False)
 o.location=(0,0,0);o.rotation_euler=(0,0,0);o.scale=(1,1,1)
 bpy.context.view_layer.update();h=o.dimensions.z;o.scale=(height/h,)*3
 bpy.context.view_layer.update();bottom=min((o.matrix_world@Vector(v)).z for v in o.bound_box)
 o.location=Vector(loc)-Vector((0,0,bottom));assign(o,m);return o

# A deep, furnished laboratory, with foreground, midground and background occluders.
cube('Floor',(0,1,-.10),(11,15,.20),floor)
for x in range(-5,6):cube('Floor joint X',(x,1,.001),(.009,15,.003),dark,0)
for y in range(-6,9):cube('Floor joint Y',(0,y,.002),(11,.009,.003),dark,0)
cube('Back wall',(0,6,1.9),(11,.18,3.8),wall)
cube('Back skirting',(0,5.88,.12),(11,.05,.24),wood)
cube('Right wall',(5.1,2,1.9),(.15,8,3.8),wall)
# Left window is deliberately open to the sky; mullions create readable sun shadows.
for y in [-2,-.5,1,2.5,4,5.5]:cube('Window upright',(-4.2,y,1.9),(.10,.07,3.8),dark)
for z in [.15,1.05,2.6,3.65]:cube('Window rail',(-4.2,1.7,z),(.1,7.8,.065),dark)
for y in [-1.65,-.9,-.15,.6,1.35,2.1,2.85,3.6,4.35,5.1]:
 o=cube('Solar screen oak fin',(-4.12,y,2.45),(.3,.045,2.3),wood);o.rotation_euler.z=-.35
cube('Window sill',(-4.08,1.6,.8),(.4,8,.07),stone)
# Main island: 3.6m, dark enamel, inset fronts, fine brass handles.
cube('Island plinth',(0,0,.12),(3.35,1.34,.24),dark)
cube('Island body',(0,0,.54),(3.5,1.45,.86),navy)
cube('Stone top',(0,0,1.015),(3.75,1.64,.11),stone,.025)
for x in [-1.30,-.43,.43,1.30]:
 cube('Front cabinet',(x,-.738,.59),(.82,.045,.71),navy,.008)
 cube('Brass pull',(x,-.78,.84),(.31,.026,.025),brass,.009)
 cube('Drawer inset',(x,-.77,.94),(.82,.016,.07),dark,.003)
# Tall background cabinets and counter.
cube('Rear counter',(0,5,1.02),(8.5,1.1,.12),stone)
for x in [-3.6,-2.4,-1.2,0,1.2,2.4,3.6]:
 cube('Rear cabinet',(x,5,.53),(1.15,1,.94),ivory)
 cube('Rear handle',(x,4.46,.83),(.4,.035,.025),metal)
for x in [-3.1,0,3.1]:
 for z in [1.65,2.35,3.05]:cube('Floating oak shelf',(x,5.48,z),(2.5,.62,.055),wood)
 for xx in [x-1.1,x+1.1]:rod('Shelf upright',(xx,5.75,1.4),(xx,5.75,3.15),.018,dark)
 for j in range(6):
  bx=x-1+.4*j
  for k,z in enumerate([1.69,2.39]):
   cyl('Reagent bottle',(bx,5.40,z+.12),.065,.24,amber if (j+k)%3==0 else ivory)
   cyl('Bottle cap',(bx,5.40,z+.255),.046,.035,dark)
   cube('Bottle label',(bx,5.332,z+.12),(.08,.006,.09),paper,.002)
text('M R   /   MATERIALS LAB',(0,5.88,3.42),.24,dark)
text('LIGHT   /   TRANSPORT   /   MATTER',(0,5.88,3.18),.075,teal)
# Glassware hero arrangement using the actual Hunyuan-derived repaired meshes.
z=1.074
vessel('flask','Hunyuan / repaired empty flask',(.18,.08,z),.51)
cyl('Cyan fluid in flask',(.18,.08,z+.08),.13,.12,blue)
for i,(x,y,m) in enumerate([(-.57,-.18,amber),(.85,-.2,red),(-.15,-.46,blue),(1.22,.23,green)]):
 vessel('beaker','Hunyuan / repaired beaker '+str(i),(x,y,z),.28)
 cyl('Solution '+str(i),(x,y,z+.055),.087,.085,m)
# Flask volume is a deliberate liquid object, not a false opaque mesh partition.
cube('Magnetic stirrer',(-1.24,.23,z+.065),(.5,.48,.13),ivory)
cyl('Stirrer top',(-1.24,.23,z+.145),.19,.025,metal)
cyl('Control knob',(-1.24,-.025,z+.065),.026,.025,dark).rotation_euler.x=math.pi/2
vessel('flask','Small flask on stirrer',(-1.24,.23,z+.16),.32)
cube('Display',(-1.12,-.019,z+.07),(.1,.008,.035),teal,.003)
# Clamp stand and thin rods make fine shadow edges visible.
cube('Retort base',(.8,.48,z+.025),(.44,.27,.05),metal)
rod('Retort vertical',(.8,.5,z+.05),(.8,.5,2.02),.015,metal)
rod('Retort clamp',(.78,.5,1.85),(.20,.5,1.85),.012,metal)
cyl('Clamp joint',(.8,.5,1.85),.03,.055,dark)
rod('Thermometer',(.2,.5,1.85),(.2,.5,1.34),.009,ivory)
for h in range(12):cube('Thermometer mark',(.2,.489,1.43+h*.025),(.014,.004,.002),dark,0)
cube('Tube rack',(-1.24,-.42,z+.13),(.51,.21,.045),wood)
for x in [-1.46,-1.24,-1.02]:
 rod('Rack feet',(x,-.42,z),(x,-.42,z+.14),.014,wood)
 cyl('Test tube',(x,-.42,z+.19),.025,.28,glass)
 sphere('Round tube bottom',(x,-.42,z+.05),(.025,.025,.025),glass)
 cyl('Tube solution',(x,-.42,z+.105),.020,.09,blue)
cube('Laboratory notes',(.55,-.54,z+.015),(.34,.23,.03),paper,.005)
rod('Pencil',(.38,-.62,z+.04),(.72,-.48,z+.04),.006,wood)
text('LAB  /  06',(-1.1,-.766,.48),.085,paper)
# Additional benches, task stools and equipment establish depth.
for yy in [2.6,4.1]:
 for xx in [-3.1,3.0]:
  cyl('Stool seat',(xx,yy,.65),.27,.075,wood)
  for dx,dy in [(-.16,-.16),(.16,-.16),(-.16,.16),(.16,.16)]:rod('Stool leg',(xx+dx,yy+dy,.63),(xx+dx*1.5,yy+dy*1.5,.04),.018,metal)
cube('Instrument housing',(2.9,4.9,1.38),(1,.65,.65),ivory)
cube('Instrument dark screen',(2.9,4.56,1.5),(.6,.015,.26),dark)
cube('Instrument display',(2.9,4.547,1.5),(.53,.004,.18),teal)
text('SPECTRA',(2.9,4.533,1.24),.06,dark)
for x in [-2.8,0,2.8]:
 rod('Pendant cable',(x,1.6,3.65),(x,1.6,3.1),.006,dark)
 cyl('Pendant lamp',(x,1.6,3.03),.28,.12,navy)
 cyl('Pendant diffuser',(x,1.6,2.965),.25,.012,ivory)
area('Window soft illumination',(-3.7,-1,3.1),(0,0,1),550,3.2,(.77,.87,1))
area('Warm reflected fill',(1,3,3.3),(0,0,1),380,2.4,(1,.84,.64))
area('Glass rim strip',(2.5,.3,2.6),(0,0,1.3),160,1.4,(.78,.9,1))

def camera(name,location,target,lens):
 d=bpy.data.cameras.new(name);d.lens=lens;d.sensor_width=36;d.clip_start=.1;d.clip_end=40
 o=bpy.data.objects.new(name,d);s.collection.objects.link(o);o.location=location
 o.rotation_euler=(Vector(target)-o.location).to_track_quat('-Z','Y').to_euler();return o
cams=[camera('01 / laboratory overview',(6,-8,4.7),(0,1.45,1.2),43),
      camera('02 / glassware study',(-3.4,-4.6,2.8),(.05,.35,1.25),48)]
# Sun rays travel down and toward the back-right of the room.
SUN=Vector((.82,.36,-.42)).normalized();SUN_RGB=(1.0,.83,.61);SUN_STRENGTH=1.25
splits=[.1,4,7,12,24]
all_bounds=[o.matrix_world@Vector(c) for o in s.objects if o.type=='MESH' for c in o.bound_box]

def vecdot(nodes,links,socket,v):
 n=nodes.new('ShaderNodeVectorMath');n.operation='DOT_PRODUCT';links.new(socket,n.inputs[0]);n.inputs[1].default_value=v;return n.outputs['Value']
def mathnode(nodes,links,op,a,b=None):
 n=nodes.new('ShaderNodeMath');n.operation=op
 for i,x in enumerate([a,b]):
  if x is None:continue
  if isinstance(x,(float,int)):n.inputs[i].default_value=x
  else:links.new(x,n.inputs[i])
 return n.outputs[0]

def make_depth_material(origin,forward,far):
 m=bpy.data.materials.new('CSM linear light-space depth');m.use_nodes=True;n=m.node_tree.nodes;l=m.node_tree.links;n.clear()
 geo=n.new('ShaderNodeNewGeometry');d=vecdot(n,l,geo.outputs['Position'],forward)
 d=mathnode(n,l,'SUBTRACT',d,origin.dot(forward));d=mathnode(n,l,'DIVIDE',d,far)
 e=n.new('ShaderNodeEmission');l.new(d,e.inputs['Color']);o=n.new('ShaderNodeOutputMaterial');l.new(e.outputs[0],o.inputs['Surface']);return m

def create_maps(cam,view):
 """Fit orthographic depth cameras to four main-camera frustum slices."""
 bpy.context.view_layer.update();q=SUN.to_track_quat('-Z','Y');basis=q.to_matrix()
 right=basis@Vector((1,0,0));up=basis@Vector((0,1,0))
 result=[];frame=cam.data.view_frame(scene=s)
 original=[(o,list(o.data.materials)) for o in OPAQUE]
 hide=[(o,o.hide_render) for o in TRANSPARENT]
 for o,_ in hide:o.hide_render=True
 s.render.engine='BLENDER_EEVEE_NEXT';s.render.resolution_x=s.render.resolution_y=SHADOW_SIZE
 s.render.image_settings.file_format='OPEN_EXR';s.render.image_settings.color_mode='RGB';s.render.image_settings.color_depth='32'
 s.view_settings.view_transform='Standard';bg.inputs[0].default_value=(1,1,1,1);bg.inputs[1].default_value=1
 for i,(near,far) in enumerate(zip(splits[:-1],splits[1:])):
  corners=[cam.matrix_world@(v*(d/abs(v.z))) for d in [near*.92,far*1.03] for v in frame]
  xs=[p.dot(right) for p in corners];ys=[p.dot(up) for p in corners]
  scale=max(max(xs)-min(xs),max(ys)-min(ys))*1.06
  texel=scale/SHADOW_SIZE
  cx=round(((min(xs)+max(xs))/2)/texel)*texel;cy=round(((min(ys)+max(ys))/2)/texel)*texel
  z0=min(p.dot(SUN) for p in all_bounds)-2;z1=max(p.dot(SUN) for p in all_bounds)+2;depth=z1-z0
  origin=right*cx+up*cy+SUN*z0
  c=camera('CSM depth camera '+str(i),origin,origin+SUN,50);c.rotation_euler=q.to_euler();c.data.type='ORTHO';c.data.ortho_scale=scale;c.data.clip_start=.01;c.data.clip_end=depth+1;s.camera=c
  dm=make_depth_material(origin,SUN,depth)
  for o,_ in original:o.data.materials.clear();o.data.materials.append(dm)
  path=OUT/'evidence'/f'view{view:02d}_cascade{i+1}.exr';s.render.filepath=str(path)
  bpy.ops.render.render(write_still=True)
  im=bpy.data.images.load(str(path),check_existing=False);im.colorspace_settings.name='Non-Color';im.pack()
  pixels=np.empty(SHADOW_SIZE*SHADOW_SIZE*4,dtype=np.float32);im.pixels.foreach_get(pixels)
  values=pixels.reshape(SHADOW_SIZE,SHADOW_SIZE,4)[:,:,0]
  result.append(dict(image=im,origin=origin,forward=SUN,right=right,up=up,scale=scale,depth=depth,near=near,far=far,texel=texel,depth_min=float(values.min()),depth_max=float(values.max()),occupied_fraction=float(np.mean(values<.9999))))
  bpy.data.objects.remove(c,do_unlink=True)
 for o,materials in original:
  o.data.materials.clear()
  for m in materials:o.data.materials.append(m)
 for o,h in hide:o.hide_render=h
 s.camera=cam;s.render.engine='CYCLES';s.render.resolution_x,s.render.resolution_y=SIZE
 s.render.image_settings.file_format='PNG';s.render.image_settings.color_mode='RGB';s.render.image_settings.color_depth='8'
 s.view_settings.view_transform='AgX';s.view_settings.look='AgX - Medium High Contrast'
 bg.inputs[0].default_value=(.60,.73,1,1);bg.inputs[1].default_value=.22
 return result

def csm_group(maps,cam):
 group=bpy.data.node_groups.new('CSM / 4 cascades / PCF / world-space','ShaderNodeTree')
 group.interface.new_socket(name='Sun visibility cosine',in_out='OUTPUT',socket_type='NodeSocketFloat')
 n=group.nodes;l=group.links;geo=n.new('ShaderNodeNewGeometry');pos=geo.outputs['Position']
 direction=cam.rotation_euler.to_matrix()@Vector((0,0,-1))
 eye=mathnode(n,l,'SUBTRACT',vecdot(n,l,pos,direction),cam.location.dot(direction))
 visibility=[]
 for i,m in enumerate(maps):
  uv=[]
  for axis in ['right','up']:
   v=mathnode(n,l,'SUBTRACT',vecdot(n,l,pos,m[axis]),m['origin'].dot(m[axis]))
   v=mathnode(n,l,'ADD',mathnode(n,l,'DIVIDE',v,m['scale']),.5);uv.append(v)
  depth=mathnode(n,l,'SUBTRACT',vecdot(n,l,pos,m['forward']),m['origin'].dot(m['forward']))
  depth=mathnode(n,l,'DIVIDE',depth,m['depth'])
  # Slope-scale receiver bias covers nearest-texel and PCF depth variation.
  # A constant tiny bias causes alternating self-shadow stripes on oblique planes.
  cos=mathnode(n,l,'ABSOLUTE',vecdot(n,l,geo.outputs['Normal'],-SUN))
  sin=mathnode(n,l,'SQRT',mathnode(n,l,'MAXIMUM',mathnode(n,l,'SUBTRACT',1,mathnode(n,l,'MULTIPLY',cos,cos)),0))
  slope=mathnode(n,l,'DIVIDE',sin,mathnode(n,l,'MAXIMUM',cos,.12))
  bias=mathnode(n,l,'ADD',mathnode(n,l,'MULTIPLY',slope,m['texel']*1.4),.025)
  bias=mathnode(n,l,'MINIMUM',bias,.10)
  depth=mathnode(n,l,'SUBTRACT',depth,mathnode(n,l,'DIVIDE',bias,m['depth']))
  taps=[]
  for dx,dy in [(-.5,-.5),(.5,-.5),(-.5,.5),(.5,.5)]:
   combine=n.new('ShaderNodeCombineXYZ')
   l.new(mathnode(n,l,'ADD',uv[0],dx/SHADOW_SIZE),combine.inputs[0]);l.new(mathnode(n,l,'ADD',uv[1],dy/SHADOW_SIZE),combine.inputs[1])
   tex=n.new('ShaderNodeTexImage');tex.image=m['image'];tex.interpolation='Closest';tex.extension='EXTEND';l.new(combine.outputs[0],tex.inputs['Vector'])
   taps.append(mathnode(n,l,'LESS_THAN',depth,tex.outputs['Color']))
  value=mathnode(n,l,'MULTIPLY',mathnode(n,l,'ADD',mathnode(n,l,'ADD',taps[0],taps[1]),mathnode(n,l,'ADD',taps[2],taps[3])),.25)
  visibility.append(value)
 # Blend out each near cascade over the last 10% of its depth interval.
 value=visibility[-1]
 for i in reversed(range(3)):
  edge=maps[i]['far'];width=(edge-maps[i]['near'])*.1
  blend=mathnode(n,l,'DIVIDE',mathnode(n,l,'SUBTRACT',edge,eye),width)
  blend=mathnode(n,l,'MINIMUM',mathnode(n,l,'MAXIMUM',blend,0),1)
  value=mathnode(n,l,'ADD',mathnode(n,l,'MULTIPLY',visibility[i],blend),mathnode(n,l,'MULTIPLY',value,mathnode(n,l,'SUBTRACT',1,blend)))
 cosine=mathnode(n,l,'MAXIMUM',vecdot(n,l,geo.outputs['Normal'],-SUN),0)
 value=mathnode(n,l,'MULTIPLY',value,cosine)
 output=n.new('NodeGroupOutput');l.new(value,output.inputs[0]);return group

def verify_maps(maps,cam,view):
 """Compare the raster depth lookup to independent geometry ray intersections.

 The checks use opaque shadow casters and points on the floor/worktop. Points
 close to cascade blends are omitted, since this test checks a single map.
 """
 dg=bpy.context.evaluated_depsgraph_get();vertices=[];faces=[]
 for obj in OPAQUE:
  if obj.type!='MESH':continue
  ev=obj.evaluated_get(dg);mesh=ev.to_mesh();offset=len(vertices)
  vertices.extend(ev.matrix_world@v.co for v in mesh.vertices)
  faces.extend(tuple(offset+i for i in p.vertices) for p in mesh.polygons)
  ev.to_mesh_clear()
 tree=BVHTree.FromPolygons(vertices,faces)
 buffers=[]
 for m in maps:
  data=np.empty(SHADOW_SIZE*SHADOW_SIZE*4,dtype=np.float32);m['image'].pixels.foreach_get(data)
  buffers.append(data.reshape(SHADOW_SIZE,SHADOW_SIZE,4)[:,:,0])
 points=[Vector((x,y,.012)) for x in np.linspace(-3.8,4.4,35) for y in np.linspace(-3,5.6,35)]
 points += [Vector((x,y,1.078)) for x in np.linspace(-1.75,1.75,25) for y in np.linspace(-.73,.73,13)]
 forward=cam.rotation_euler.to_matrix()@Vector((0,0,-1));tested=agree=shadowed=lit=0;by_cascade=[0]*4
 cos=abs(SUN.z);slope=math.sqrt(1-cos*cos)/cos
 for p in points:
  distance=(p-cam.location).dot(forward)
  idx=next((i for i,m in enumerate(maps) if m['near']<=distance<m['far']),None)
  if idx is None:continue
  m=maps[idx]
  if distance>m['far']-(m['far']-m['near'])*.12:continue
  u=(p-m['origin']).dot(m['right'])/m['scale']+.5;v=(p-m['origin']).dot(m['up'])/m['scale']+.5
  if not(.01<u<.99 and .01<v<.99):continue
  depth=((p-m['origin']).dot(SUN)-min(.10,slope*m['texel']*1.4+.025))/m['depth']
  count=0
  for dx,dy in [(-.5,-.5),(.5,-.5),(-.5,.5),(.5,.5)]:
   ix=min(SHADOW_SIZE-1,max(0,int(u*SHADOW_SIZE+dx)));iy=min(SHADOW_SIZE-1,max(0,int(v*SHADOW_SIZE+dy)))
   count+=int(depth<float(buffers[idx][iy,ix]))
  if count not in (0,4):continue # PCF edge pixels are deliberately fractional.
  expected_lit=tree.ray_cast(p,-SUN,80)[0] is None
  observed_lit=count==4;tested+=1;agree+=int(expected_lit==observed_lit);by_cascade[idx]+=1
  lit+=int(expected_lit);shadowed+=int(not expected_lit)
 result={'tested_points':tested,'matches':agree,'agreement':agree/max(1,tested),'ray_lit':lit,'ray_shadowed':shadowed,'cascade_counts':by_cascade,'method':'Independent BVH sun-ray intersection vs raster map depth comparisons; floor/worktop, exclude PCF edges/split blends; small bias-induced differences expected.'}
 (OUT/'evidence'/f'view{view:02d}_shadow_validation.json').write_text(json.dumps(result,indent=2),encoding='utf-8')
 print('SHADOW_VALIDATION',json.dumps(result),flush=True)
 if tested<100 or lit<10 or shadowed<10 or result['agreement']<.90:
  raise RuntimeError('CSM depth-map validation failed; inspect before final rendering.')
 return result

def install_sun(group):
 for m in MATS.values():
  n=m.node_tree.nodes;l=m.node_tree.links;p=n.get('Principled BSDF')
  # Reuse the material node between views; avoid accumulation.
  for old in list(n):
   if old.name.startswith('CSM_'):n.remove(old)
  out=n.get('Material Output')
  if m['csm_gain']<=0:l.new(p.outputs['BSDF'],out.inputs['Surface']);continue
  g=n.new('ShaderNodeGroup');g.node_tree=group;g.name='CSM_visibility'
  mul=n.new('ShaderNodeMath');mul.operation='MULTIPLY';mul.name='CSM_strength';mul.inputs[1].default_value=SUN_STRENGTH*m['csm_gain'];l.new(g.outputs[0],mul.inputs[0])
  e=n.new('ShaderNodeEmission');e.name='CSM_diffuse_sun_radiance';e.inputs['Color'].default_value=tuple(a*b for a,b in zip(m['csm_color'],SUN_RGB))+(1,);l.new(mul.outputs[0],e.inputs['Strength'])
  add=n.new('ShaderNodeAddShader');add.name='CSM_plus_path_traced_BSDF';l.new(p.outputs[0],add.inputs[0]);l.new(e.outputs[0],add.inputs[1]);l.new(add.outputs[0],out.inputs['Surface'])
  # The cache is evaluated along traced paths as well: bounced sunlight is
  # transported by Cycles, instead of compositing a painted shadow over a photo.

def white_model(cam,path):
 # Unshadowed raster white-model shader; no path tracing, material texture,
 # shadow map, reflection or transparency. Normal shading still reveals shape.
 s.camera=cam;s.render.engine='BLENDER_EEVEE_NEXT'
 m=bpy.data.materials.new('Neutral white / basic normal shading only');m.use_nodes=True
 n=m.node_tree.nodes;l=m.node_tree.links;n.clear();geo=n.new('ShaderNodeNewGeometry')
 cosine=mathnode(n,l,'MAXIMUM',vecdot(n,l,geo.outputs['Normal'],Vector((-.4,-.6,.7)).normalized()),0)
 value=mathnode(n,l,'ADD',mathnode(n,l,'MULTIPLY',cosine,.55),.25)
 e=n.new('ShaderNodeEmission');l.new(value,e.inputs['Color']);o=n.new('ShaderNodeOutputMaterial');l.new(e.outputs[0],o.inputs['Surface'])
 original=[(o,list(o.data.materials)) for o in OPAQUE+TRANSPARENT]
 for obj,_ in original:obj.data.materials.clear();obj.data.materials.append(m)
 bg.inputs[0].default_value=(.3,.3,.3,1);bg.inputs[1].default_value=1
 s.render.film_transparent=False;s.view_settings.view_transform='Standard';s.view_settings.look='None'
 s.render.filepath=str(path);bpy.ops.render.render(write_still=True)
 for obj,mats in original:
  obj.data.materials.clear()
  for material in mats:obj.data.materials.append(material)
 bg.inputs[0].default_value=(.60,.73,1,1);bg.inputs[1].default_value=.22
 s.render.engine='CYCLES';s.view_settings.view_transform='AgX';s.view_settings.look='AgX - Medium High Contrast'

records=[]
for index,cam in enumerate(cams,1):
 if args.view and index!=args.view:continue
 start=time.time();s.camera=cam;s.cycles.seed=24371102+index
 print('START_VIEW',index,'preview',PREVIEW,flush=True)
 maps=create_maps(cam,index)
 validation=verify_maps(maps,cam,index) if not PREVIEW else None
 group=csm_group(maps,cam);install_sun(group)
 suffix='_preview' if PREVIEW else ''
 hybrid=IMAGE_DIR/f'view{index:02d}_hybrid{suffix}.png'
 white=IMAGE_DIR/f'view{index:02d}_white{suffix}.png'
 s.render.filepath=str(hybrid)
 # Save the editable, self-contained view including its packed CSM images.
 if not PREVIEW:
  bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'scene'/f'view{index:02d}_hybrid.blend'))
 bpy.ops.render.render(write_still=True)
 white_model(cam,white)
 record={'view':index,'camera_world_matrix':[list(row) for row in cam.matrix_world],'lens_mm':cam.data.lens,'resolution':SIZE,'samples':SAMPLES,'seconds':round(time.time()-start,2),'hybrid':hybrid.name,'white':white.name,'shadow_validation':validation,'cascades':[{k:list(v) if isinstance(v,Vector) else v for k,v in m.items() if k!='image'} for m in maps]}
 records.append(record)
 (OUT/'evidence'/f'view{index:02d}{suffix}.json').write_text(json.dumps(record,indent=2),encoding='utf-8')
 print('VIEW_COMPLETE',index,record['seconds'],flush=True)
report={'blender':bpy.app.version_string,'method':'EEVEE rasterized 4-cascade sunlight visibility + Cycles CPU path transport','shadow_resolution':SHADOW_SIZE,'shadow_filter':'4-tap PCF, slope-scale bias, 10% split blending, texel snapping','sun_direction':list(SUN),'mesh_objects':sum(o.type=='MESH' for o in s.objects),'white_model':'EEVEE basic normal shading, one neutral color, no shadows/reflection/transmission/path tracing','views':records,'limitations':['Sun specular disk and transmissive sun casters are not included in CSM; glass and fluid use traced area/world illumination.','Indirect sunlight uses the active camera CSM cache, not a full scene-space radiance solution.','This is an offline hybrid study, not a Unity runtime integration.']}
(OUT/'evidence'/('preview_report.json' if PREVIEW else 'render_report.json')).write_text(json.dumps(report,indent=2),encoding='utf-8')
print('RENDER_STUDY_COMPLETE',flush=True)
