#!/usr/bin/env python3
"""CJK tests use the original shipped font assets; checker tests use synthetic
10x10 tiles to verify source state/layers, not to claim original checker art."""
import argparse,json,subprocess,tempfile,struct,zlib
from pathlib import Path
p=argparse.ArgumentParser();p.add_argument('assembly',type=Path);p.add_argument('--dotnet',default='dotnet');a=p.parse_args();assembly=a.assembly.resolve();checks=0

def check(ok,name):
 global checks
 if not ok:raise AssertionError(name)
 checks+=1;print('PASS',name,flush=True)

def png(path,w,h,color):
 def part(k,d):return struct.pack('>I',len(d))+k+d+struct.pack('>I',zlib.crc32(k+d))
 raw=b''.join(b'\0'+bytes(v for x in range(w) for v in color(x,y)) for y in range(h))
 path.write_bytes(b'\x89PNG\r\n\x1a\n'+part(b'IHDR',struct.pack('>IIBBBBB',w,h,8,6,0,0,0))+part(b'IDAT',zlib.compress(raw))+part(b'IEND',b''))

def ppm(path):
 _,size,_,data=path.read_bytes().split(b'\n',3);return data,*map(int,size.split())
def px(frame,x,y):data,w,h=frame;at=(y*w+x)*3;return tuple(data[at:at+3])
def crop(frame,x,y,w,h):return b''.join(bytes(px(frame,i,j)) for j in range(y,y+h) for i in range(x,x+w))

with tempfile.TemporaryDirectory(prefix='kuroaki-font-checker-') as temp:
 root=Path(temp);extras=root/'extras';extras.mkdir();text='中文下死亡猩红我你歌曲测试'
 (root/'ENCORE.vsc').write_text('10000,0,0')
 (root/'ENCORE_text.txt').write_text('0,'+text,encoding='utf-8')
 (root/'info.json').write_text(json.dumps({'name':text,'artist':'中文作者','difficulty_display_4':'16'}))
 (root/'ENCORE_cgmk_config.json').write_text(json.dumps({'ENABLE_TEXT':True,'ENABLE_ANGELSTAR_CHECKER':True}))
 base={'Chart':'ENCORE.vsc','Gimmick':'ENCORE.vsm','Notes':False,'GameUiEnabled':False,'PostProcessing':False,'GimmickAssets':str(extras),'ScrollSpeed':1}
 def run(args):
  r=subprocess.run([a.dotnet,str(assembly),*args],capture_output=True,text=True)
  if r.returncode:raise RuntimeError(r.stdout+r.stderr)
  return r
 def render(mods=(),changes=None,time=1):
  (root/'fixture.sgv.json').write_text(json.dumps(base|(changes or {})))
  (root/'ENCORE.vsm').write_text('!obj:obj_custom_gimmick\n0,0,linear,0,0,particle_alpha,-1\n'+'\n'.join(mods))
  run(['--snapshot',str(root/'fixture.sgv.json'),'--time',str(time),'--scene','--out',str(root/'frame.ppm')]);return ppm(root/'frame.ppm')
 default=render(changes={'GameUiFont':'Default'});monaco=render(changes={'GameUiFont':'Monaco'})
 check(default==monaco,'Monaco complete CJK glyphs match the same original CJK artwork in Default')
 check(any(px(monaco,x,98)!=(0,0,0) for x in range(320)),'CJK text retains its bottom stroke row')
 ui_default=render(changes={'GameUiFont':'Default','GameUiEnabled':True});ui_monaco=render(changes={'GameUiFont':'Monaco','GameUiEnabled':True})
 check(crop(ui_default,3,168,len(text)*10,12)==crop(ui_monaco,3,168,len(text)*10,12),'Chinese song title is complete with either font')
 report=json.loads(run(['--inspect',str(root/'fixture.sgv.json')]).stdout)
 check(not any('Fusion Pixel' in d['Message'] for d in report['diagnostics']),'original fonts no longer produce an obsolete fallback diagnostic')
 (root/'ENCORE_text.txt').unlink()
 pulse='2,0,linear,1,1,angelstar_checker_set,-1';alpha='0,0,linear,1,1,angelstar_checker_alpha,-1'
 missing=render([alpha,pulse]);report=json.loads(run(['--inspect',str(root/'fixture.sgv.json')]).stdout)
 check(not any(missing[0]) and any('tiles are missing' in d['Message'] for d in report['diagnostics']),'missing original checker art is reported and not fabricated')
 # Opaque test tiles make mode/tint/state differences observable.
 for i,c in enumerate([85,170,255]):png(extras/f'sp_angelstar_checker_{i}.png',10,10,lambda x,y,c=c:(c,c,c,255))
 png(root/'jacket.png',320,180,lambda x,y:(128,255,255,255))
 initial=render([alpha,pulse],time=0)
 check(px(initial,100,90)==(128,0,0),'mode 0 checker is tinted red then multiplied by the current jacket')
 plain=render([alpha,pulse,'0,0,linear,1,1,angelstar_checker_mode,-1'],time=0)
 check(px(plain,100,90)==(255,0,0),'mode 1 checker stays above the jacket multiplication')
 refreshed=render([alpha,pulse,'0,0,linear,1,1,angelstar_checker_mode,-1'],time=1.1)
 later=render([alpha,pulse,'0,0,linear,1,1,angelstar_checker_mode,-1'],time=3)
 check(refreshed==later and refreshed!=plain,'one _set event refreshes once and preserves the board afterwards')
 check(len({px(refreshed,x*10+4,y*10+4) for x in range(32) for y in range(18)})==3,'refresh chooses all three source frame indices across 32x18 cells')
 check(render([alpha,pulse,'0,0,linear,1,1,angelstar_checker_mode,-1'],time=0)==plain,'backward seek restores the initial all-frame-2 board')
 below=render([alpha,'0,0,linear,1,1,angelstar_checker_mode,-1'],{'Notes':True},time=0)
 above=render([alpha,'0,0,linear,2,2,angelstar_checker_mode,-1'],{'Notes':True},time=0)
 check(px(below,126,150)==(39,39,39) and px(above,126,150)==(255,0,0),'mode 2 covers the rail and judgment overlay; mode 1 stays behind them')
 (root/'ENCORE.vsc').write_text('1500,0,0')
 note=render([alpha,'0,0,linear,2,2,angelstar_checker_mode,-1'],{'Notes':True},time=1)
 check(px(note,126,97)==(238,238,238),'notes remain above checker mode 2')
 disabled={'ENABLE_TEXT':False,'ENABLE_ANGELSTAR_CHECKER':False};(root/'ENCORE_cgmk_config.json').write_text(json.dumps(disabled))
 check(not any(render([alpha,pulse])[0]),'ENABLE_ANGELSTAR_CHECKER=false disables the object')
print(f'{checks} CJK / checker checks passed.')
