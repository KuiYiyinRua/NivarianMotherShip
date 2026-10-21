import sys,json,re,collections,xml.etree.ElementTree as ET
from pathlib import Path
sys.stdout.reconfigure(encoding='utf-8')
tools=Path(__file__).resolve().parent
mod=tools.parent.parent
inv=json.loads((tools/'inventory.json').read_text(encoding='utf-8'))
translations=json.loads((tools/'translations.json').read_text(encoding='utf-8'))
defs=json.loads((tools/'def-entries.json').read_text(encoding='utf-8'))
locales=['ChineseSimplified','English','Russian','Japanese','French']
byid={r['id']:r for r in inv}
assert len(byid)==len(inv),'duplicate source ID'
assert len({r['key'] for r in inv})==len(inv),'duplicate source key'
assert set(translations)=={str(r['id']) for r in inv},'translation IDs differ'
keys=set()
for p in (mod/'Source').glob('*.cs'):
 keys.update(re.findall(r'"(NMC_(?:Text\d+|ResearchTask[A-Za-z]+|CarrierSteel[A-Za-z]+|Settings[A-Za-z]+|Warp[A-Za-z]+|Storm[A-Za-z]+|Sage[A-Za-z]+))"',p.read_text(encoding='utf-8-sig')))
keys.difference_update({'NMC_StormShip', 'NMC_StormOrb', 'NMC_StormOrbLaunch', 'NMC_StormImpactDamage', 'NMC_StormBlastDamage', 'NMC_SageShip', 'NMC_SageBeamDamage', 'NMC_SageShipFlying'})
keys.discard('NMC_WarpPrism')  # ThingDef identifier, not a translated UI string.
assert keys<={r['key'] for r in inv},('source calls missing from catalog',sorted(keys-{r['key'] for r in inv}))
def placeholders(s):return collections.Counter(re.findall(r'(?<!\{)\{(\d+)(?:,[^{}:]+)?(?::[^{}]+)?\}(?!\})',s))
def marks(s):return collections.Counter(re.findall(r'</?(?:color|b|i|size)[^>]*>|\[[A-Z][A-Za-z0-9_]*\]',s))
for r in inv:
 values=translations[str(r['id'])]
 assert len(values)==4,('four translations required',r['id'])
 for s in values:
  assert s and '\ufffd' not in s and 'TODO' not in s,('invalid value',r['id'])
  assert placeholders(s)==placeholders(r['zh']),(r['id'],'placeholders')
  assert marks(s)==marks(r['zh']),(r['id'],'tags')
for ix,locale in enumerate(locales):
 def value(r):return r['zh'] if ix==0 else translations[str(r['id'])][ix-1]
 actual={}
 for e in ET.parse(mod/'1.6/Languages'/locale/'Keyed/NMC.xml').getroot():
  assert e.tag not in actual,('duplicate key',e.tag)
  actual[e.tag]=e.text or ''
 assert set(actual)==keys,(locale,'key coverage')
 for r in inv:
  if r['key'] in keys:assert actual[r['key']]==value(r),(locale,r['key'])
 relative_paths=set()
 for folder in sorted({d['folder'] for d in defs}):
  paths={}
  for p in (mod/folder/'Languages'/locale/'DefInjected').rglob('*.xml'):
   rel=p.relative_to(mod/folder/'Languages'/locale)
   assert rel not in relative_paths,('load-folder file collision',locale,str(rel))
   relative_paths.add(rel)
   for e in ET.parse(p).getroot():
    k=(p.parent.name,e.tag)
    assert k not in paths,('duplicate Def key',k)
    paths[k]=e.text
  expected={(d['type'],d['path']) for d in defs if d['folder']==folder}
  assert len(expected)==sum(d['folder']==folder for d in defs),('duplicate catalog Def path',folder)
  assert set(paths)==expected,(locale,folder,'Def coverage')
  for d in (d for d in defs if d['folder']==folder):
   assert paths[d['type'],d['path']]==value(byid[d['id']]),(locale,d['path'])
version=ET.parse(mod/'About/About.xml').findtext('modVersion')
for proj in [mod/'Source/NivarianCommander.csproj',mod/'Source/NivarianCommander.RimWorld17.csproj']:
 assert ET.parse(proj).findtext('PropertyGroup/Version')==version,('version mismatch',str(proj))
# Optional adapter identities stay at their tested 0.9.8 build for MP owner compatibility.
for proj in (mod/'Multiplayer').glob('*Source/*.csproj'):
 assert ET.parse(proj).findtext('PropertyGroup/Version')=='0.9.8',('adapter identity version mismatch',str(proj))
for folder in ['1.6','1.7','Royalty','NoRoyalty','About','Multiplayer']:
 for p in (mod/folder).rglob('*.xml'):
  if 'obj' not in p.parts:ET.parse(p)
print(json.dumps({'status':'PASS','version':version,'languages':locales,'runtime_keys':len(keys),'def_fields':len(defs)},ensure_ascii=False))
