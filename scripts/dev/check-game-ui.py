#!/usr/bin/env python3
"""Real SDL/OpenGL regressions; generated colored fixtures are not game artwork."""
import argparse,json,struct,subprocess,tempfile,zlib
from pathlib import Path
p=argparse.ArgumentParser();p.add_argument('assembly',type=Path);p.add_argument('--dotnet',default='dotnet');a=p.parse_args();assembly=a.assembly.resolve();checks=0

def check(ok,name):
 global checks
 if not ok:raise AssertionError(name)
 checks+=1;print('PASS',name,flush=True)

def png(path,w,h,color):
 def chunk(k,d):return struct.pack('>I',len(d))+k+d+struct.pack('>I',zlib.crc32(k+d))
 raw=b''.join(b'\0'+bytes(c for x in range(w) for c in color(x,y)) for y in range(h))
 path.write_bytes(b'\x89PNG\r\n\x1a\n'+chunk(b'IHDR',struct.pack('>IIBBBBB',w,h,8,6,0,0,0))+chunk(b'IDAT',zlib.compress(raw))+chunk(b'IEND',b''))

with tempfile.TemporaryDirectory(prefix='kuroaki-ui-') as temp:
 root=Path(temp);pack=root/'GameUI';pack.mkdir();manifest={'version':1,'sprites':{},'fonts':{}}
 for name,w,h,count in [('sp_gameplayoverlay2024',320,180,1),('sp_2024cc_score',61,14,3),('sp_newdifficultyindicator',46,10,6),('sp_newdifficultylevel',21,5,6),('sp_newdifficultynumbers',16,7,28)]:
  files=[]
  for i in range(count):
   file=f'{name}_{i}.png';files.append(file)
   if name=='sp_gameplayoverlay2024':color=lambda x,y:(255,255,255,255) if x<35 and 2<=y<14 else (0,0,0,255 if y>=165 else 0)
   else:color=lambda x,y,i=i:(30+i*7,70+i*3,100,255)
   png(pack/file,w,h,color)
  manifest['sprites'][name]={'width':w,'height':h,'originX':0,'originY':0,'frames':files}
 png(pack/'font.png',6,8,lambda x,y:(255,255,255,255) if x<5 and y<7 else (0,0,0,0))
 for name in ['fnt_monacovs','fnt_credits']:
  manifest['fonts'][name]={'file':'font.png','size':10,'lineHeight':14,'scaleX':1,'scaleY':1,'glyphs':[{'character':i,'x':0,'y':0,'w':6,'h':8,'advance':7,'offset':0} for i in range(32,127)]}
 png(pack/'monaco.png',4,8,lambda x,y:(255,255,255,255) if x<3 and 2<=y<7 else (0,0,0,0))
 manifest['fonts']['fnt_phosphor']={'file':'monaco.png','size':10,'lineHeight':10,'ascenderOffset':2,'scaleX':1,'scaleY':1,'glyphs':[{'character':i,'x':0,'y':0,'w':4,'h':6,'advance':5,'offset':0} for i in range(32,127)]}
 (pack/'game-ui.gameui.json').write_text(json.dumps(manifest));(root/'ENCORE.vsc').write_text('10000,0,0')
 (root/'info.json').write_text(json.dumps({'name':'TITLE','artist':'ARTIST','difficulty_display_4':'15+'}))
 project={'Chart':'ENCORE.vsc','Gimmick':'ENCORE.vsm','GameUi':'GameUI','ScrollSpeed':1,'PostProcessing':False}
 def run(args):
  result=subprocess.run([a.dotnet,str(assembly),*args],text=True,capture_output=True)
  if result.returncode:raise RuntimeError(result.stdout+result.stderr)
  return result
 def render(mods=(),changes=None,notes=None):
  current=project| (changes or {});(root/'fixture.sgv.json').write_text(json.dumps(current))
  if notes is not None:(root/'ENCORE.vsc').write_text(notes)
  (root/'ENCORE.vsm').write_text('!obj:obj_base_gimmick\n'+'\n'.join(f'0,0,linear,{v},{v},{k},{proxy}' for k,v,proxy in mods))
  run(['--snapshot',str(root/'fixture.sgv.json'),'--scene','--time','1','--out',str(root/'frame.ppm')])
  magic,size,maximum,frame=(root/'frame.ppm').read_bytes().split(b'\n',3);w,h=map(int,size.split());return frame,w,h
 def pixel(frame,x,y):data,w,h=frame;i=(y*w+x)*3;return tuple(data[i:i+3])
 def bbox(frame):
  data,w,h=frame;points=[(i//3%w,i//3//w) for i in range(0,len(data),3) if any(data[i:i+3])];return min(x for x,y in points),min(y for x,y in points),max(x for x,y in points),max(y for x,y in points)
 full=render();check(pixel(full,4,5)==(255,255,255),'original overlay asset is rendered at its declared coordinates')
 check(pixel(full,114,170)==(0,0,0),'rail does not occupy title footer')
 hidden=render((('uialpha',0,-1),));check(pixel(hidden,4,5)==(0,0,0) and pixel(hidden,114,20)==(255,255,255),'uialpha hides HUD without hiding rail')
 half=render((('uialpha',.5,-1),));check(all(abs(c-128)<=1 for c in pixel(half,4,5)),'HUD alpha is applied once')
 check(render(changes={'GameUiEnabled':False})==hidden,'VS UI switch matches hidden HUD')
 check(pixel(full,3,168)[0]>200 and pixel(full,3,174)[2]>pixel(full,3,168)[2],'title gradient follows original top/bottom colors')
 monaco=render(changes={'GameUiFont':'Monaco'});check(pixel(monaco,38,4)==(255,255,255) and pixel(monaco,38,8)==(255,255,255) and pixel(monaco,38,9)==(0,0,0),'Monaco atlas ascender offset preserves top and bottom glyph rows')
 check(pixel(full,80,4)==(0,0,0) and pixel(monaco,80,4)==(0,0,0) and pixel(full,74,4)==(255,255,255) and pixel(monaco,74,4)==(0,0,0),'Monaco and Default retain distinct glyph advances')
 check(render(changes={'GameUiFont':'Default'})==full,'Default font alias restores the original pixels')
 # Exercise the actual settings buttons in one Viewer / SceneRenderer session.
 run(['--smoke-ui',str(root/'fixture.sgv.json'),'--out',str(root/'settings.ppm')]);check(True,'font settings buttons switch both ways without reloading the session')
 shifted=(('pra',1,0),('prx',-110,0))
 under=render(shifted,{'GameUiEnabled':False});over=render(shifted)
 check(pixel(under,25,5)!=(255,255,255) and pixel(under,25,5)!=(0,0,0) and pixel(over,25,5)==(255,255,255),'PAUSE covers a track proxy moved into the top-left HUD')
 check(render(shifted+(('uialpha',0,-1),))==under,'uialpha=0 exposes the moved track beneath PAUSE')
 half_moved=render(shifted+(('uialpha',.5,-1),))
 check(all(abs(c-(255+d)/2)<=2 for c,d in zip(pixel(half_moved,25,5),pixel(under,25,5))),'half-opacity PAUSE blends once over moved track')
 check(render(shifted+(('hom',1,-1),))==under,'hom hides fixed HUD while proxy remains visible')
 faded=render(shifted+(('hom',.5,-1),('uialpha',.5,-1)))
 check(all(abs(c-(255*.25+d*.75))<=2 for c,d in zip(pixel(faded,25,5),pixel(under,25,5))),'hom and uialpha combine without squaring HUD opacity')
 angled=shifted+(('prrz',17,0),('pry',10,0))
 below=render(angled,{'GameUiEnabled':False});above=render(angled)
 crossings=[(x,y) for y in range(2,14) for x in range(35) if any(pixel(below,x,y))]
 check(crossings and all(pixel(above,x,y)==(255,255,255) for x,y in crossings),'rotated proxies cannot overwrite PAUSE pixels')
 bottom=render(shifted+(('pry',20,0),));check(pixel(bottom,3,168)==pixel(full,3,168) and pixel(bottom,25,179)==(0,0,0),'fixed title and its footer cover track moved below y=165')
 report=json.loads(run(['--inspect',str(root/'fixture.sgv.json')]).stdout)
 check(report['gameUi']['song']['Name']=='TITLE' and report['gameUi']['song']['Artist']=='ARTIST' and report['gameUi']['song']['Level']=='15+','song / artist / difficulty come from info.json')
 png(root/'front.png',320,180,lambda x,y:(255,0,0,255));project['Images']='ENCORE.vsp'
 def image_layer(priority):
  (root/'ENCORE.vsp').write_text(f'#Layer\nfront,{priority}\n#Image\nfront:\nstatic,front,front.png,0,320,180\n')
 image_mods=(('imgx_front',160,-1),('imgy_front',90,-1),('imgalp_front',1,-1))
 image_layer(-100);frame=render(image_mods);check(pixel(frame,114,20)==(255,0,0) and pixel(frame,126,147)==(39,39,39),'priority -100 covers rail, remains behind gray overlay')
 image_layer(10);frame=render(image_mods,notes='1500,0,0');check(pixel(frame,126,147)==(255,0,0) and pixel(frame,126,97)==(238,238,238),'priority 10 covers overlay but remains behind note')
 image_layer(500);frame=render(image_mods);check(pixel(frame,126,97)==(255,0,0) and pixel(frame,4,5)==(255,255,255),'priority 500 covers note but remains behind HUD')
 image_layer(1500);frame=render(image_mods);check(pixel(frame,4,5)==(255,0,0),'priority 1500 can cover gameplay HUD')
 frame=render(image_mods+(('uialpha',0,-1),));check(pixel(frame,114,170)==(255,0,0) and pixel(frame,4,5)==(255,0,0),'fake titles/images remain visible when original HUD is hidden')
 # A small image begins inside the source track and only reaches PAUSE
 # through the proxy; it must keep its transform after HUD separation.
 (root/'ENCORE.vsp').write_text('#Layer\nfront,1500\n#Image\nfront:\nstatic,front,front.png,0,12,10\n')
 moving_image=(('imgx_front',137,-1),('imgy_front',10,-1),('imgalp_front',1,-1))+shifted
 image_over=render(moving_image);check(pixel(image_over,25,7)==(255,0,0),'high-priority image keeps proxy motion and covers fixed PAUSE')
 (root/'ENCORE.vsp').write_text('#Layer\nfront,500\n#Image\nfront:\nstatic,front,front.png,0,12,10\n')
 image_under=render(moving_image);check(pixel(image_under,25,7)==(255,255,255),'lower-priority proxy image stays behind fixed HUD')
 del project['Images'];(root/'ENCORE.vsp').unlink();project['GameUiEnabled']=False
 bare=(('bgalph',0,-1),('holdoverlayalpha',0,-1))
 top=render(bare,notes='1500,0,0');bottom=render(bare,{'NoteAlignment':1});check(bbox(top)[1]-bbox(bottom)[1]==7,'Top / Bottom alignment differs by original seven pixels')
 delay=render(bare,{'VisualDelayMs':100});check(bbox(top)[1]-bbox(delay)[1]==10,'100 ms visual delay changes actual note position by 10 pixels at scroll 1')
 late=render(bare,{'VisualDelayMs':600},notes='500,0,0');check(any(late[0]),'positive visual delay preserves a delayed note after nominal hit time')
 # Rotated geometry at high resolution must contain sub-native-pixel edges.
 rotated=render(bare+(('noterot',17,-1),),{'RenderWidth':640,'VisualDelayMs':0},notes='1500,0,0')
 check(rotated[1:]==(640,360),'render setting changes real scene framebuffer dimensions')
 data,w,h=rotated;check(any(pixel(rotated,x,y)!=pixel(rotated,x//2*2,y//2*2) for y in range(h) for x in range(w)),'higher resolution rasterizes rotated geometry; it is not nearest enlargement of 320x180')
 high=render(bare,{'RenderWidth':3840},notes='1500,0,0');check(high[1:]==(3840,2160) and len(high[0])==3840*2160*3,'4K allocates and renders the complete scene')
 check(render(bare,notes='1500,0,0')==top,'returning from 4K to native preserves pixels')
 bad=json.loads(json.dumps(manifest));bad['fonts']['fnt_monacovs']['file']='../front.png';(pack/'game-ui.gameui.json').write_text(json.dumps(bad))
 (root/'fixture.sgv.json').write_text(json.dumps(project));result=subprocess.run([a.dotnet,str(assembly),'--inspect',str(root/'fixture.sgv.json')],text=True,capture_output=True)
 check(result.returncode==2 and 'must stay inside its pack' in result.stdout,'UI pack rejects paths outside its directory')
print(f'{checks} UI / settings / layer GPU checks passed.')
