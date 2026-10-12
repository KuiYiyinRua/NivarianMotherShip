import sys,json,re,hashlib,xml.etree.ElementTree as ET
from pathlib import Path
sys.stdout.reconfigure(encoding='utf-8')
root=Path(__file__).resolve().parent
inv=json.loads((root/'inventory.json').read_text(encoding='utf-8'))
translations=json.loads((root/'translations.json').read_text(encoding='utf-8'))
defs=json.loads((root/'def-entries.json').read_text(encoding='utf-8'))
mod=root.parent.parent
locales=['ChineseSimplified','English','Russian','Japanese','French']
# Update the old ability paragraph to reflect the 0.9.6 research unlock.
old='购买“精确友军识别”后'
new='完成“精确友军识别”研究后'
p=mod/'1.6/Defs/CommanderAbilities.xml'
s=p.read_text(encoding='utf-8-sig')
assert old in s or new in s
p.write_text(s.replace(old,new),encoding='utf-8')
inv[488]['zh']=inv[488]['zh'].replace(old,new)
(root/'inventory.json').write_text(json.dumps(inv,ensure_ascii=False,indent=2),encoding='utf-8')
active=set()
for file in (mod/'Source').glob('*.cs'):
 active.update(re.findall(r'"(NMC_(?:Text\d+|ResearchTask[A-Za-z]+|CarrierSteel[A-Za-z]+|Settings[A-Za-z]+))"',file.read_text(encoding='utf-8-sig')))
rows={r['id']:r for r in inv}
def save(path,items):
 path.parent.mkdir(parents=True,exist_ok=True)
 node=ET.Element('LanguageData')
 for key,val in items:
  assert val and '\ufffd' not in val
  el=ET.SubElement(node,key);el.text=val
 ET.indent(node,space='  ')
 ET.ElementTree(node).write(path,encoding='utf-8',xml_declaration=True)
for ix,locale in enumerate(locales):
 def val(r):return r['zh'] if ix==0 else translations[str(r['id'])][ix-1]
 keyed=[(r['key'],val(r)) for r in inv if r['key'] in active]
 save(mod/'1.6/Languages'/locale/'Keyed/NMC.xml',keyed)
 groups={}
 for entry in defs:
  groups.setdefault((entry['folder'],entry['type']),[]).append((entry['path'],val(rows[entry['id']])))
 for (folder,typ),items in groups.items():
  save(mod/folder/'Languages'/locale/'DefInjected'/typ/('NMC_'+folder.replace('.','_')+'.xml'),items)
 # Localized metadata for in-game tooling that reads standard keyed labels.
 save(mod/'1.6/Languages'/locale/'Keyed/ModInfo.xml',[
  ('NMC_ModName','Nivarian Race: MotherShip'),
  ('NMC_ModDescription',[
   '扩展涅瓦莲母舰的战术支援、生产和运输能力。包含19项支援、连续体任务、母舰商店、舰船与米莉拉星舰电池联动。精确友军识别通过独立研究解锁。',
   'Expands Nivarian mothership support, production and transport. Includes 19 supports, the Continuum campaign, points shop, ships and Milira battery upgrades. Precision ally recognition is unlocked by research.',
   'Расширяет поддержку, производство и транспорт корабля-матки нивариан. 19 видов поддержки, кампания Континуума, магазин, корабли и батареи милира. Точное распознавание союзников открывается исследованием.',
   '母艦の支援、生産、輸送を拡張。19種の支援、連続体任務、ショップ、艦船、ミリラバッテリー改良を追加。精密友軍識別は研究で解放。',
   'Étend soutien, production et transport du vaisseau-mère. 19 soutiens, campagne du Continuum, boutique, vaisseaux et améliorations Milira. Identification précise des alliés débloquée par recherche.'
  ][ix])])
# About metadata has no per-language description field; keep the portable English description.
p=mod/'About/About.xml'
tree=ET.parse(p)
tree.find('description').text='Nivarian mothership expansion for RimWorld 1.6. Includes 19 tactical supports, a Continuum campaign, a shared points shop, carrier and gravity ship, and optional Milira starship battery upgrades. Precision ally recognition unlocks through research. Languages: 简体中文 / English / Русский / 日本語 / Français.'
ET.indent(tree,space='  ')
tree.write(p,encoding='utf-8',xml_declaration=True)
print(json.dumps({'languages':locales,'runtime_keys':len(active),'def_injections':len(defs),'unique_active_strings':len(active|{rows[d['id']]['key'] for d in defs})},ensure_ascii=False))
