import argparse,hashlib,json,zipfile,xml.etree.ElementTree as ET
from pathlib import Path
mod=Path(__file__).resolve().parent.parent
parser=argparse.ArgumentParser(description='Package the loadable mod, full expansion source and Workshop materials.')
parser.add_argument('--output',type=Path,default=mod/'dist')
parser.add_argument('--revision', default='', help='Release revision suffix, e.g. r1; assembly versions remain unchanged.')
args=parser.parse_args()
assert not args.revision or all(c.isalnum() or c in '-_' for c in args.revision)

out=args.output.resolve()
out.mkdir(parents=True,exist_ok=True)
version=ET.parse(mod/'About/About.xml').findtext('modVersion')
files=[]
for folder in ['1.6','About','Royalty','NoRoyalty','Multiplayer']:
 for p in (mod/folder).rglob('*'):
  if p.is_file() and not {'Source','ShipUpgradeSource','WarpPrismSource','StormShipSource','SageShipSource','bin','obj'}.intersection(p.relative_to(mod).parts) and p.suffix.lower() not in ['.pdb','.log']:
   files.append(p)
files.extend([mod/'LoadFolders.xml',mod/'README.md',mod/'CHANGELOG.md'])
files.extend(p for p in (mod/'Docs').glob('*.md') if not p.name.startswith('DESYNC_'))
files.append(mod/'Directory.Build.props')
for folder in ['Source','Multiplayer/Source','Multiplayer/ShipUpgradeSource','Multiplayer/WarpPrismSource','Multiplayer/StormShipSource','Multiplayer/SageShipSource']:
 files.extend(p for p in (mod/folder).glob('*') if p.is_file() and p.suffix in ['.cs','.csproj'])
files.append(mod/'Tools/package.py')
for folder in ['Tests/ContinuumEntry','Tests/NativeConstruction','Tests/ShadowFootprint']:
 files.extend(p for p in (mod/folder).glob('*') if p.is_file() and p.suffix in ['.cs','.csproj','.ps1'])
files.extend(p for p in (mod/'Tools/Localization').glob('*') if p.is_file() and p.suffix in ['.py','.json'])
files.extend(p for p in (mod/'Workshop').glob('*') if p.is_file() and p.suffix.lower() in ['.txt','.json','.png','.jpg'])
for folder in ['Workshop/Gallery','Workshop/Screenshots']:
 files.extend(p for p in (mod/folder).glob('*') if p.is_file() and p.suffix.lower() in ['.png','.jpg'])
files=sorted(set(files))
manifest={str(p.relative_to(mod)).replace('\\','/'):hashlib.sha256(p.read_bytes()).hexdigest() for p in sorted(files)}
release_version=version+('-'+args.revision if args.revision else '')
archive=out/f'NivarianMotherShip-v{release_version}.zip'
with zipfile.ZipFile(archive,'w',compression=zipfile.ZIP_DEFLATED) as z:
 for p in sorted(files):z.write(p,'NivarianMotherShip/'+str(p.relative_to(mod)).replace('\\','/'))
 z.writestr('NivarianMotherShip/package-manifest.json',json.dumps(manifest,ensure_ascii=False,indent=2))
with zipfile.ZipFile(archive) as z:
 assert z.testzip() is None
 assert len(z.namelist())==len(manifest)+1
 for rel,digest in manifest.items():
  assert hashlib.sha256(z.read('NivarianMotherShip/'+rel)).hexdigest()==digest
digest=hashlib.sha256(archive.read_bytes()).hexdigest()
(out/'SHA256SUMS.txt').write_text(digest+'  '+archive.name+'\n',encoding='utf-8')
(out/'package-manifest.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2),encoding='utf-8')
print(json.dumps({'archive':archive.name,'files':len(manifest),'sha256':digest}))
