import bpy,sys,json
from mathutils import Vector
bpy.ops.wm.open_mainfile(filepath=sys.argv[sys.argv.index('--')+1])
o=bpy.data.objects['Beaker_Repaired_10cm']
data=[]
for f in o.data.polygons:
    c=f.center
    if c.x*c.x+c.y*c.y<.035**2 and f.normal.z>.3:
        data.append([f.index,round(c.z,6),round(c.x,5),round(c.y,5),round(f.normal.z,3),len(f.vertices)])
print('FLOOR_FACES',json.dumps(data[:60]))
for z in [.02,.05,.07,.075,.08,.085,.09]:
    vv=[v.co for v in o.data.vertices if abs(v.co.z-z)<.002]
    print('RING',z, len(vv),sorted(round((v.x*v.x+v.y*v.y)**.5,4) for v in vv)[::max(1,len(vv)//12)])
for x in [0,.01,.02,.03]:
    print('RAY',x,o.ray_cast(Vector((x,0,.2)),Vector((0,0,-1)))[:3])
