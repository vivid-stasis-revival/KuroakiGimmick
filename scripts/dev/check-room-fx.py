#!/usr/bin/env python3
"""Isolated original room FX pixel checks plus current runtime-profile selection."""
import argparse
import json
from pathlib import Path
import struct
import subprocess
import tempfile
import zlib


def png(path, colour):
    def chunk(kind, data):
        return struct.pack('>I', len(data))+kind+data+struct.pack('>I', zlib.crc32(kind+data))
    rows=b''.join(b'\0'+bytes(c for x in range(320) for c in colour(x,y)) for y in range(180))
    path.write_bytes(b'\x89PNG\r\n\x1a\n'+chunk(b'IHDR',struct.pack('>IIBBBBB',320,180,8,2,0,0,0))+
                     chunk(b'IDAT',zlib.compress(rows))+chunk(b'IEND',b''))


def main():
    p=argparse.ArgumentParser(description=__doc__)
    p.add_argument('assembly',type=Path);p.add_argument('--dotnet',default='dotnet')
    a=p.parse_args();assembly=a.assembly.resolve();checks=0
    assets=assembly.parent/'Assets'
    if not assets.is_dir():assets=next(parent.parent/'Assets' for parent in assembly.parents if parent.name.endswith('.app'))
    def check(ok, why):
        nonlocal checks
        if not ok:raise AssertionError(why)
        checks+=1;print('PASS',why,flush=True)
    def pixel(frame,x,y):
        i=(y*320+x)*3;return tuple(frame[i:i+3])
    def points(frame):
        return [(i//3%320,i//3//320) for i in range(0,len(frame),3) if any(frame[i:i+3])]
    with tempfile.TemporaryDirectory(prefix='scarlet-gimmicks-') as temp:
        root=Path(temp)
        (root/'cgmk_config.json').write_text('{"JACKET_MANAGE_MODE":"custom"}')
        colour=lambda x,y:(x%256,y,(3*x+y)%256)
        png(root/'jacket.png',colour)
        profile=root/'gimmick-fx.json'
        def render(mods=None,*,time=1,notes=False,obj='obj_custom_gimmick',post=True,chart='10000,0,0',proxy=None,room='auto'):
            values={'particle_alpha':0,'holdoverlayalpha':0,'bgalph':0,'ditortedBG_alp':1}
            values.update(mods or {})
            lines=[f'!obj:{obj}','!proxies:1','!0:retained-metadata']
            for key,value in values.items():lines.append(f'0,0,linear,{value},{value},{key},-1')
            for key,value in (proxy or {}).items():lines.append(f'0,0,linear,{value},{value},{key},0')
            (root/'fixture.vsm').write_text('\n'.join(lines));(root/'fixture.vsc').write_text(chart)
            selected=profile if profile.exists() else None
            if selected is None and room not in ('none','auto'):
                # Verify the individual static colour/chroma formulas without
                # newer runtime glow, underwater or backdrop layers changing the input.
                source=assets/'RoomFX'/('scene_gameplay'+('' if room=='gameplay' else '_'+room)+'.fx.json')
                config=json.loads(source.read_text())
                config['layers']=[l for l in config['layers'] if l['name'] in ('FX_chroma','FX_contrast','FX_red','FX_hue')]
                selected=root/'isolated-room.fx.json';selected.write_text(json.dumps(config))
            (root/'fixture.sgv.json').write_text(json.dumps({'Chart':'fixture.vsc','Gimmick':'fixture.vsm','Bpm':120,
                    'ScrollSpeed':1,'Notes':notes,'PostProcessing':post,'GameUiEnabled':False,'RoomPreset':room,
                    'FxProfile':str(selected) if selected is not None else None}))
            r=subprocess.run([a.dotnet,str(assembly),'--snapshot',str(root/'fixture.sgv.json'),'--scene','--time',str(time),'--out',str(root/'frame.ppm')],capture_output=True,text=True)
            if r.returncode:raise RuntimeError(r.stdout+r.stderr)
            header,size,limit,data=(root/'frame.ppm').read_bytes().split(b'\n',3)
            assert (header,size,limit,len(data))==(b'P6',b'320 180',b'255',320*180*3)
            return data

        def colourise(rgb,tint):
            lum=sum(v*w/255 for v,w in zip(rgb,(.299,.587,.114)))
            tintlum=sum(v*w for v,w in zip(tint,(.299,.587,.114)))
            out=[v*lum/tintlum for v in tint] if lum<tintlum else [v+(1-v)*(lum-tintlum)/(1-tintlum) for v in tint]
            return tuple(round(v*255) for v in out)
        png(root/'noise.png',lambda x,y:(255,0,128))
        (root/'cgmk_config.json').write_text('{"JACKET_MANAGE_MODE":"custom"}')
        base=render(room='none')
        run=subprocess.run([a.dotnet,str(assembly),'--inspect',str(root/'fixture.sgv.json'),'--room','gameplay'],capture_output=True,text=True)
        runtime=json.loads(run.stdout)
        check(run.returncode==0 and runtime['fx']['path'].endswith('scene_gameplay.runtime.fx.json') and any(l['Name']=='FX_chroma' and l['Visible'] for l in runtime['fx']['layers']),
              'current gameplay preset selects the runtime profile and its visible chroma')
        check(render({'fx_chroma_distort':8},room='gameplay')==base,'isolated static gameplay room chroma remains hidden even when its amount changes')
        chroma=render({'fx_chroma_distort':8},room='plaudite')
        check(chroma!=base,'original Plaudite room enables its heat-haze chroma with the original noise')
        check(render({'fx_chroma_distort':8},room='plaudite')==chroma,'original room noise animation reproduces after seek')
        gray=render({'fx_contrast':0},room='gameplay')
        check(all(max(pixel(gray,x,y))-min(pixel(gray,x,y))<=1 for x,y in [(60,40),(100,80),(200,120)]),'bundled original contrast configuration produces grayscale at mod zero')
        check(render({'fx_contrast':1},room='gameplay')==base,'original contrast mod one leaves the image unchanged')
        balance=render({'fx_red':1},room='scarletdeath')
        def colour_balance(rgb):
            c=[v/255 for v in rgb];lum=sum(v*w for v,w in zip(c,(.299,.587,.114)))
            def smooth(a,b,x):
                t=max(0,min(1,(x-a)/(b-a)));return t*t*(3-2*t)
            tone=(1-smooth(.3,.4,lum))*(1-smooth(.6,.7,lum))
            v=[max(0,min(1,c[0]*(1+tone)+tone/1.5)),c[1],c[2]]
            den=sum(x*w for x,w in zip(v,(.299,.587,.114)))
            return tuple(round(max(0,min(1,x*lum/den if den else 0))*255) for x in v)
        check(all(abs(v-e)<=2 for x,y in [(10,10),(60,40),(100,80),(200,120)] for v,e in zip(pixel(balance,x,y),colour_balance(pixel(base,x,y)))),
              'Scarlet Death uses the original colour-balance shadow/midtone formula')
        png(root/'jacket.png',lambda x,y:(0,0,0))
        check(not any(render({'fx_red':1},room='scarletdeath')),'colour balance preserves finite black pixels')
        png(root/'jacket.png',colour)
        check(render({'fx_red':1,'fx_red_intensity':0},room='scarletdeath')==balance,'colour balance ignores nonexistent colourise intensity uniform')
        hidden_red=render({'fx_red':0},room='scarletdeath')
        check(max(abs(a-b) for a,b in zip(hidden_red,base))<=1,'Scarlet Death red visibility is controlled by the object mod')
        red=render({'fx_red':1},room='angelstar')
        check(all(abs(v-e)<=2 for x,y in [(60,40),(100,80)] for v,e in zip(pixel(red,x,y),colourise(pixel(base,x,y),(1,0,0)))),
              'Angelstar room retains colourise while custom object supplies its tint/intensity')
        check(red!=balance,'the two original red filter types produce distinct images')
        # Known constant noise isolates each original room's per-channel amplitudes.
        for name,expected in [('plaudite',(127,65,colour(104,76)[2])),('gameplay',(116,70,colour(104,76)[2]))]:
            source=assets/'RoomFX'/('scene_gameplay'+('' if name=='gameplay' else '_'+name)+'.fx.json')
            config=json.loads(source.read_text());layer=next(l for l in config['layers'] if l['name']=='FX_chroma')
            layer['visible']=True;layer['parameters']['g_DistortTexture']='noise.png'
            profile.write_text(json.dumps({'layers':[layer]}))
            frame=render({'fx_chroma_distort':8},room='none')
            check(all(abs(v-e)<=2 for v,e in zip(pixel(frame,100,80),expected)),name+' original chroma spread produces expected per-channel pixel offsets: '+str(pixel(frame,100,80))+' expected '+str(expected))
            if name=='gameplay':check(render({'fx_chroma_distort':0},room='none')==base,'visible underwater layer with both amounts zero remains a finite identity')
        profile.unlink()
        chart='1100,0,0\n1200,0,1\n10000,0,3'
        normal=render({'ditortedBG_alp':0},notes=True,post=False,chart=chart)
        hidden=render({'ditortedBG_alp':0,'notealpind0':0},notes=True,post=False,chart=chart)
        check(pixel(normal,126,137)!=(0,0,0) and pixel(hidden,126,137)==(0,0,0) and pixel(hidden,149,127)==pixel(normal,149,127),
              'per-lane note alpha hides one lane without hiding its neighbour')

        check(render({'fx_red':1},room='plaudite')==base,'explicit Plaudite room does not invent a missing red layer')
    print(f'{checks} original room FX pixel checks passed.')


if __name__=='__main__':main()
