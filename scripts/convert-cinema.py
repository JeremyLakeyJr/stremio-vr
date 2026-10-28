import bpy,json
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
from mathutils import Vector
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=str(ROOT/'assets/worlds/cinema/source.glb'))
objects=[]
for o in bpy.context.scene.objects:
 if o.type!='MESH': continue
 vs=[o.matrix_world@Vector(v) for v in o.bound_box]
 objects.append({'name':o.name,'min':[round(min(v[i] for v in vs),3) for i in range(3)],'max':[round(max(v[i] for v in vs),3) for i in range(3)]})
with open('/tmp/cinema-objects.json','w') as f:json.dump(objects,f)
for o in bpy.context.scene.objects:
 if o.type=='MESH': o.data=o.data.copy()
bpy.ops.export_scene.fbx(filepath=str(ROOT/'unity/Assets/WorldSources/Cinema.fbx'),use_selection=False,object_types={'MESH','EMPTY'},add_leaf_bones=False,bake_anim=False,path_mode='COPY',embed_textures=True,axis_forward='-Z',axis_up='Y')
print('EXPORTED',len(objects))
