"""Extract only visual scene dependencies from the user-supplied Unity package."""
import json,re,tarfile,pathlib,collections,argparse,zipfile,tempfile
root=pathlib.Path(__file__).resolve().parents[1]
parser=argparse.ArgumentParser(description=__doc__)
parser.add_argument('archive',nargs='?',default=str(next(root.glob('Open Concept Home*.zip'),root/'Open Concept Home.zip')))
args=parser.parse_args(); archive=pathlib.Path(args.archive)
work=pathlib.Path(tempfile.mkdtemp(prefix='movie-night-home-'))
if archive.suffix.lower()=='.zip':
 with zipfile.ZipFile(archive) as z:
  name=next(n for n in z.namelist() if n.endswith('.unitypackage'))
  package=work/'home.unitypackage'
  with z.open(name) as source, package.open('wb') as output:
   import shutil; shutil.copyfileobj(source,output)
else: package=archive
paths={}
with tarfile.open(package,'r:gz') as t:
 for m in t:
  if m.name.endswith('/pathname'): paths[m.name.split('/')[0]]=t.extractfile(m).read().decode().strip()
allowed={'.unity','.prefab','.mat','.fbx','.png','.jpg','.jpeg','.tga','.psd','.exr','.hdr'}
texts={}; metas={}
with tarfile.open(package,'r:gz') as t:
 for m in t:
  guid=m.name.split('/')[0]; p=paths.get(guid,''); suffix=pathlib.Path(p).suffix.lower()
  if m.name.endswith('/asset.meta'): metas[guid]=t.extractfile(m).read()
  elif m.name.endswith('/asset') and suffix in {'.unity','.prefab','.mat'}: texts[guid]=t.extractfile(m).read()
start=next(k for k,v in paths.items() if v=='Assets/Home Ulkan/Customizable Home.unity')
selected=set(); pending=[start]
while pending:
 guid=pending.pop()
 if guid in selected: continue
 p=paths.get(guid,''); suffix=pathlib.Path(p).suffix.lower()
 if suffix not in allowed or '/VRC Essentials/' in p or '/TextMesh Pro/' in p: continue
 selected.add(guid)
 for dep in re.findall(rb'guid: ([0-9a-f]{32})',texts.get(guid,b'')+metas.get(guid,b'')): pending.append(dep.decode())
folder=root/'unity/Assets/PrivateHome';folder.mkdir(exist_ok=True)
with tarfile.open(package,'r:gz') as t:
 for m in t:
  guid=m.name.split('/')[0]
  if guid not in selected or m.name.split('/')[-1] not in ('asset','asset.meta'):continue
  relative=pathlib.PurePosixPath(paths[guid])
  if relative.is_absolute() or '..' in relative.parts:raise ValueError('Unsafe package path')
  dest=folder/pathlib.Path(*relative.parts[1:])
  if m.name.endswith('/asset.meta'):dest=pathlib.Path(str(dest)+'.meta')
  dest.parent.mkdir(parents=True,exist_ok=True);dest.write_bytes(t.extractfile(m).read())
print('Selected visual assets:',len(selected),collections.Counter(pathlib.Path(paths[g]).suffix.lower() for g in selected))
