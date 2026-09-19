#!/usr/bin/env python3
"""Checks an external Scarlet Beat chart against actual viewer output.
The user song is never modified or bundled with this script."""
import argparse,json,tempfile,subprocess,hashlib
from pathlib import Path
p=argparse.ArgumentParser();p.add_argument('assembly',type=Path);p.add_argument('song',type=Path);p.add_argument('--dotnet',default='dotnet');a=p.parse_args()
song=a.song.resolve();assembly=a.assembly.resolve();checks=0

def check(ok,name):
 global checks
 if not ok:raise AssertionError(name)
 checks+=1;print('PASS',name,flush=True)

def invoke(args):
 r=subprocess.run([a.dotnet,str(assembly),*args],capture_output=True,text=True)
 if r.returncode:raise RuntimeError(r.stdout+r.stderr)
 return r.stdout

vsm=song/'ENCORE.vsm';before=hashlib.sha256(vsm.read_bytes()).hexdigest()
with tempfile.TemporaryDirectory(prefix='kuroaki-scarlet-') as tmp:
 root=Path(tmp);project=root/'fixture.sgv.json'
 base={'Chart':str(song/'ENCORE.vsc'),'Gimmick':str(vsm),'Audio':str(song/'music.ogg'),'Bpm':110,'GameUiFont':'Monaco'}
 def setup(changes=None):project.write_text(json.dumps(base|(changes or {})))
 def report(changes=None):setup(changes);return json.loads(invoke(['--inspect',str(project)]))
 def render(changes=None,time=26):
  setup(changes);frame=root/'frame.ppm';invoke(['--snapshot',str(project),'--time',str(time),'--scene','--out',str(frame)]);return frame.read_bytes()
 r=report()
 check(r['modCount']==13238 and r['notes']=={'3':1,'0':84,'1':52,'2':73},'original chart and all 13,238 effects are retained')
 check(r['texts']=={'count':1,'cues':85},'lowercase encore_text.txt loads all 85 original text cues')
 check(r['checker']['available'] and r['checker']['refreshes']==16,'all three original checker frames load with 16 refreshes')
 tail=r['eventTimeline']['pastAudioTail']
 check(len(tail)==1 and tail[0]['Name']=='xoffset' and tail[0]['SourceLine']==19 and tail[0]['Beat']==110 and abs(tail[0]['end']-60.54545454545)<1e-6,'only the final range-expanded xoffset crosses the audio tail')
 check(abs(r['duration']-60.02501133786848)<1e-6 and not any(d['Source']=='timeline' for d in r['diagnostics']),'normal post-song event stays recorded without changing song playback duration or reporting an incompatibility')
 check(len(r['sourceNoOps'])==1 and r['sourceNoOps'][0]['Name']=='fx_colorise' and r['sourceNoOps'][0]['Line']==6,'unregistered naked colorise follows the original skip rule and remains auditable')
 check(r['fx']['dynamicDefinitions'] and any(l['Name']=='LBG' for l in r['fx']['layers']),'dynamic LBG loads exact embedded heat-haze defaults')
 check(r['diagnostics']==[],'this exact external Scarlet Beat chart has zero compatibility diagnostics')
 check(not r['fx']['autoFallback'] and r['fx']['room']=='scene_gameplay' and r['fx']['association']=='start_song: Scarlet Death(DouBaoAI Edit)','room follows exact original song name dispatch, not a partial name or jacket mode')
 check({'glow','FX_glow','FX_underwater','Effect_1'}.issubset(l['Name'] for l in r['fx']['layers']),'both original glow layers, background contrast and runtime underwater are bound')
 original=render()
 edited=root/'without-noop.vsm';edited.write_text('\n'.join(l for l in vsm.read_text().splitlines() if ',fx_colorise,' not in l))
 check(render({'Gimmick':str(edited)})==original,'naked colorise has no pixels; the two real tint commands still drive rendering')
 edited.write_text(vsm.read_text()+'\n0,0,linear,1,1,unverified_future_mod,-1\n')
 check(any('unverified_future_mod' in d['Message'] for d in report({'Gimmick':str(edited)})['diagnostics']),'unrelated unknown gimmicks are still reported')
 enabled=render(time=10);empty=root/'no-extras';empty.mkdir()
 disabled=render({'GimmickAssets':str(empty)},time=10)
 check(enabled!=disabled,'the real LBG filter changes rendered background pixels')
 # An explicit external room remains authoritative over bundled runtime settings.
 defs=json.loads((assembly.parent/'Assets/GimmickExtras/filter-definitions.json').read_text())
 water=next(d for s in defs if s.lstrip().startswith('{') and (d:=json.loads(s)).get('name')=='_filter_underwater')
 params={q['name']:q.get('defaults',q.get('default')) for q in water['parameters']}
 profile=root/'water.fx.json';profile.write_text(json.dumps({'room':'explicit-test-room','layers':[{'name':'FX_underwater','filter':'_filter_underwater','depth':-2200,'visible':True,'parameters':params}]}))
 supplied=report({'FxProfile':str(profile)})
 check(not any('Underwater uses' in d['Message'] for d in supplied['diagnostics']),'explicit FX_underwater loads through the original layer binding')
 visible=render({'FxProfile':str(profile)},time=10)
 data=json.loads(profile.read_text());data['layers'][0]['visible']=False;profile.write_text(json.dumps(data))
 hidden=render({'FxProfile':str(profile)},time=10)
 check(visible!=hidden,'original underwater layer visibility affects actual pixels and does not invoke the fallback')
 # Removing a required layer must reintroduce a precise warning (no report suppression).
 runtime=json.loads((assembly.parent/'Assets/GimmickExtras/scene_gameplay.runtime.fx.json').read_text())
 for layer in ('glow','FX_glow','FX_underwater'):
  cut=root/(layer+'.fx.json');data=json.loads(json.dumps(runtime));data['layers']=[l for l in data['layers'] if l['name']!=layer];cut.write_text(json.dumps(data))
  missing=report({'FxProfile':str(cut)})
  phrase={'glow':'Background particle glow','FX_glow':'Scene glow','FX_underwater':'Underwater uses'}[layer]
  check(any(phrase in d['Message'] for d in missing['diagnostics']),layer+' missing still produces a compatibility diagnostic')
  at=4.5 if layer=='FX_glow' else 2.5
  check(render({'FxProfile':str(cut)},time=at)!=render(time=at),layer+' changes actual scene pixels')
check(hashlib.sha256(vsm.read_bytes()).hexdigest()==before,'input VSM bytes remain unchanged')
print(f'{checks} Scarlet Beat checks passed.')
