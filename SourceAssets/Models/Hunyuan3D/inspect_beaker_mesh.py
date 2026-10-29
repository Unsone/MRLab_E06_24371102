import bpy, json, sys
from collections import Counter
bpy.ops.wm.open_mainfile(filepath=sys.argv[sys.argv.index('--')+1])
o=next(o for o in bpy.context.scene.objects if o.type=='MESH')
bpy.context.view_layer.objects.active=o
o.select_set(True)
bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
bins={}
for v in o.data.vertices:
    z=round(v.co.z/0.01)*0.01
    bins.setdefault(round(z,3),[]).append(v.co)
print('HEIGHT_BINS',json.dumps({z:{'n':len(v),'x':[min(p.x for p in v),max(p.x for p in v)],'y':[min(p.y for p in v),max(p.y for p in v)]} for z,v in sorted(bins.items())}))
