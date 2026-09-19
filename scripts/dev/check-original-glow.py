#!/usr/bin/env python3
"""Compare real GPU frames to an independent CPU reading of the dumped Disk Glow.
Requires Python numpy/Pillow; application itself does not require Python."""
import argparse,json,subprocess,tempfile
from pathlib import Path
import numpy as np
from PIL import Image
p=argparse.ArgumentParser();p.add_argument('assembly',type=Path);p.add_argument('--dotnet',default='dotnet');a=p.parse_args();assembly=a.assembly.resolve();checks=0

def check(ok,why):
 global checks
 if not ok:raise AssertionError(why)
 checks+=1;print('PASS',why,flush=True)

def post_edge(image):
 # custom Draw_74 clamps UV to 1/res even with all distortion strengths zero.
 result=image.copy();result[0]=result[1];result[:,0]=result[:,1];return result

def cpu_glow(image,quality,radius,gamma,intensity):
 original=image.copy();previous=image.astype(np.float32)/255;acc=original
 ys,xs=np.indices((180,320),dtype=np.float32)
 mult=radius**(1/quality);rad=mult
 for i in range(quality):
  sums=np.zeros_like(previous);weights=np.zeros_like(previous)
  rr=np.float32(1);x=np.float32(.1666667)*np.float32(rad);y=np.float32(0)
  for j in range(36):
   rr+=np.float32(1)/rr
   x,y=x*np.float32(-.7373688)+y*np.float32(-.6754904),x*np.float32(.6754904)+y*np.float32(-.7373688)
   ix=np.clip(np.floor(xs+.5+x*(rr-1)).astype(int),0,319);iy=np.clip(np.floor(ys+.5+y*(rr-1)).astype(int),0,179)
   sampled=previous[iy,ix];w=np.exp(sampled*np.float32(gamma))/rr;sums+=sampled*w;weights+=w
  previous=np.rint(np.clip(sums/weights,0,1)*255)/255
  acc=np.maximum(acc,np.rint(previous*np.floor(intensity*255)))
  rad*= -mult
 return post_edge(acc)

with tempfile.TemporaryDirectory(prefix='kuroaki-glow-') as tmp:
 root=Path(tmp);y,x=np.indices((180,320));rgb=np.zeros((180,320,3),dtype=np.uint8)
 rgb[35:43,45:61]=[255,100,23];rgb[78:95,122:139]=[10,70,245];rgb[145:150,291:317]=255
 rgb[2:8,1:7]=[115,250,60];Image.fromarray(rgb).save(root/'jacket.png')
 (root/'cgmk_config.json').write_text('{"JACKET_MANAGE_MODE":"custom","ENABLE_ANGELSTAR_CHECKER":false}')
 (root/'fixture.vsc').write_text('10000,0,0')
 def render(layer,quality,radius,gamma,intensity):
  (root/'fixture.vsm').write_text('!obj:obj_custom_gimmick\n'+ '\n'.join(f'0,0,linear,{v},{v},{n},-1' for n,v in {'particle_alpha':0,'ditortedBG_alp':1,'bgalph':0,'holdoverlayalpha':0,'fx_glow':intensity,'fx_particleglow':intensity}.items()))
  (root/'gimmick-fx.json').write_text(json.dumps({'room':'explicit-glow-test','layers':[{'name':layer,'filter':'_effect_glow','depth':500 if layer=='glow' else -1700,'parameters':{'g_GlowRadius':radius,'g_GlowQuality':quality,'g_GlowGamma':gamma,'g_GlowIntensity':intensity,'g_GlowAlpha':1}}]}))
  proj=root/'fixture.sgv.json';proj.write_text(json.dumps({'Chart':'fixture.vsc','Gimmick':'fixture.vsm','GameUiEnabled':False,'Notes':False,'RenderWidth':320}))
  out=root/'frame.ppm';r=subprocess.run([a.dotnet,str(assembly),'--snapshot',str(proj),'--time','1','--scene','--out',str(out)],capture_output=True,text=True)
  if r.returncode:raise RuntimeError(r.stdout+r.stderr)
  return np.asarray(Image.open(out)).astype(float)
 baseline=render('glow',5,256,0,0)
 check(np.array_equal(baseline,post_edge(rgb)),'zero intensity preserves pixels through the source final UV clamp')
 for layer,quality,radius,gamma in [('glow',1,9,0),('glow',5,256,0),('FX_glow',5,141,0),('glow',3,48,3)]:
  result=render(layer,quality,radius,gamma,.73);expected=cpu_glow(rgb,quality,radius,gamma,.73);delta=np.abs(result-expected)
  check(delta.max()<=2 and delta.mean()<.08,f'{layer} / {quality} passes / radius {radius} / gamma {gamma}: original 36 samples and MAX composition (max error {delta.max()}, mean {delta.mean():.4f})')
 check(not np.array_equal(render('glow',5,256,0,.73),rgb),'background glow visibly affects surrounding pixels')
print(f'{checks} original glow GPU/CPU checks passed.')
