#!/usr/bin/env python3
"""r7 pixel checks through the compiled SDL/OpenGL viewer. Python stdlib only."""
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
            (root/'fixture.sgv.json').write_text(json.dumps({'Chart':'fixture.vsc','Gimmick':'fixture.vsm','Bpm':120,
                    'ScrollSpeed':1,'Notes':notes,'PostProcessing':post,'GameUiEnabled':False,'RoomPreset':room}))
            r=subprocess.run([a.dotnet,str(assembly),'--snapshot',str(root/'fixture.sgv.json'),'--scene','--time',str(time),'--out',str(root/'frame.ppm')],capture_output=True,text=True)
            if r.returncode:raise RuntimeError(r.stdout+r.stderr)
            header,size,limit,data=(root/'frame.ppm').read_bytes().split(b'\n',3)
            assert (header,size,limit,len(data))==(b'P6',b'320 180',b'255',320*180*3)
            return data

        background=render(post=False)
        check(all(pixel(background,x,y)==colour(x,y) for x,y in [(10,10),(80,60),(200,120)]),
              'distorted-background alpha reveals the original local jacket through white underlay')
        dark=render({'ditortedBG_alp':.5},post=False)
        check(all(abs(v-round(c*.5))<=1 for x,y in [(10,10),(80,60),(200,120)] for v,c in zip(pixel(dark,x,y),colour(x,y))),
              'background alpha is applied once, before jacket multiplication')
        tinted=render({'ditortedBG_col_rgb':0xFF0000},post=False)
        check(pixel(tinted,80,60)==(80,0,0),'background RGB tint uses original RGB channel order')
        blur=render({'BG_blurRadius':5})
        check(blur!=render(),'background blur uses the original 32-sample shader and noise asset')
        check(render({'BG_blurRadius':5})==blur,'original-noise background blur reproduces after seek')

        # Ordinary particles are off. Side callbacks create the separate slow system.
        png(root/'jacket.png',lambda x,y:(255,255,255))
        left=render({'pburstleft':40},obj='obj_base_gimmick',post=False,time=.1)
        right=render({'pburstright':40},obj='obj_base_gimmick',post=False,time=.1)
        check(bool(points(left)) and max(x for x,y in points(left))<=134,'left burst leaves source x=130 towards the left')
        check(bool(points(right)) and min(x for x,y in points(right))>=186,'right burst leaves source x=190 towards the right')
        stationary={'pburstleft':40,'pburstspeed':0}
        early=render(stationary,obj='obj_base_gimmick',post=False,time=1)
        late=render(stationary,obj='obj_base_gimmick',post=False,time=3)
        check(any(late) and sum(late)<sum(early),'side particles remain alive after two seconds and fade across four seconds')
        check(not any(render(stationary,obj='obj_base_gimmick',post=False,time=4.1)),'side particles disappear after four seconds')
        wind=render(stationary|{'particlexpower':1},obj='obj_base_gimmick',post=False,time=1)
        check(wind!=early and min(x for x,y in points(wind))>150,'side particles follow global horizontal motion power')

        # Per-lane motion changes only the selected lane's note, not the original skin.
        opts={'ditortedBG_alp':0}
        chart='1100,0,0\n1200,0,1\n10000,0,3'
        base=render(opts,notes=True,post=False,chart=chart)
        shifted=render(opts|{'xoffsetind0':40},notes=True,post=False,chart=chart)
        check(pixel(base,126,137)!=pixel(shifted,126,137) and pixel(shifted,166,137)==pixel(base,126,137)
              and pixel(base,149,127)==pixel(shifted,149,127),'per-lane xoffset moves the chosen note while preserving its neighbour')
        down=render(opts|{'yoffsetind0':50},notes=True,post=False,chart=chart)
        check(pixel(down,126,142)==pixel(base,126,137) and pixel(down,149,127)==pixel(base,149,127),
              'per-lane yoffset uses milliseconds and preserves neighbouring note timing')
        shear=render(opts|{'bgalph':1},notes=True,post=False,obj='obj_base_gimmick',proxy={'pra':1,'prsy':.5})
        check(pixel(shear,114,10)==(0,0,0) and pixel(shear,206,10)==(255,255,255),'proxy prsy shears the actual lane texture vertically')

        png(root/'jacket.png',colour)
        base=render()
        for i in range(1,5):
            twisted=render({f'twx{i}':.5,f'twy{i}':.5,f'twa{i}':1,f'twr{i}':.4})
            check(twisted!=base,f'twist {i} is wired to its original shader uniform')
        for mod in ['sina','cosa','tana']:
            check(render({mod:.5})!=base,mod+' drives its original shader wave')
        check(render({'sina':.5,'sinp':0})==base,'zero wave period remains finite and disables the source wave')
        gray=render({'fx_hue_saturation':0})
        check(all(max(pixel(gray,x,y))-min(pixel(gray,x,y))<=1 for x,y in [(60,40),(100,80),(200,120)]),
              'original YIQ hue shader produces grayscale at zero saturation')
        red=render({'fx_red':1})
        def colourise(rgb,tint):
            lum=sum(v*w/255 for v,w in zip(rgb,(.299,.587,.114)))
            tintlum=sum(v*w for v,w in zip(tint,(.299,.587,.114)))
            out=[v*lum/tintlum for v in tint] if lum<tintlum else [v+(1-v)*(lum-tintlum)/(1-tintlum) for v in tint]
            return tuple(round(v*255) for v in out)
        check(all(abs(v-e)<=2 for x,y in [(60,40),(100,80)] for v,e in zip(pixel(red,x,y),colourise(pixel(base,x,y),(1,0,0)))),
              'fx_red uses source luminance colourise, not a red overlay')
        blue=render({'fx_colorise_intensity':1,'fx_colorise_col_rgb':0x0000FF})
        check(all(abs(v-e)<=2 for x,y in [(60,40),(100,80)] for v,e in zip(pixel(blue,x,y),colourise(pixel(base,x,y),(0,0,1)))),
              'custom tint layer uses original colourise formula with RGB input')
        check(render({'fx_red':1,'wflash':1})==bytes([255])*320*180*3,'white flash stays white when the red filter is enabled')

        check(render({'fx_chroma_distort':8})==base,'missing original FX_chroma settings apply no invented replacement')
        png(root/'noise.png',lambda x,y:(255,0,128))
        heat={'name':'FX_chroma','filter':'_filter_heathaze','depth':-100,'parameters':{
            'g_Distort1Speed':0,'g_Distort2Speed':0,'g_Distort1Scale':[1,1],'g_Distort2Scale':[1,1],
            'g_Distort1Amount':0,'g_Distort2Amount':0,'g_ChromaSpreadAmount':2,'g_CamOffsetScale':1,'g_DistortTexture':'noise.png'}}
        profile.write_text(json.dumps({'layers':[heat]}))
        chroma=render({'fx_chroma_distort':8})
        expected=(colour(112,68)[0],colour(108,72)[1],colour(104,76)[2])
        check(all(abs(v-e)<=2 for v,e in zip(pixel(chroma,100,80),expected)),
              'FX_chroma uses g_Distort2Amount and original per-channel noise offsets from the supplied room profile')
        heat['visible']=False
        profile.write_text(json.dumps({'layers':[heat]}))
        check(render({'fx_chroma_distort':8})==base,'hidden room FX layers do not process the scene')
        heat['visible']=True;heat['enabled']=False
        profile.write_text(json.dumps({'layers':[heat]}))
        check(render({'fx_chroma_distort':8})==base,'disabled room FX layers do not process the scene')
        heat['enabled']=True;heat['parameters']['g_Distort1Amount']=8
        heat['parameters']['g_ChromaSpreadAmount']=0
        profile.write_text(json.dumps({'layers':[heat]}))
        neutral=render({'fx_chroma_distort':0})
        check(all(abs(v-e)<=2 for v,e in zip(pixel(neutral,100,80),colour(104,76))),
              'neutral chroma mod preserves the original room first distortion amount')
        heat['name']='LBG';heat['parameters']['g_ChromaSpreadAmount']=0
        profile.write_text(json.dumps({'layers':[heat]}))
        haze=render({'BG_ditortAmount':4,'BG_ditortScale':1})
        check(all(abs(v-e)<=2 for v,e in zip(pixel(haze,100,80),colour(104,76))),
              'background heat haze applies both source distortion amounts before notes and text')
        profile.write_text(json.dumps({'layers':[{'name':'FX_contrast','filter':'_filter_colourise','depth':-100,
            'parameters':{'g_TintCol':[1,1,1,1],'g_Intensity':0}}]}))
        contrast=render({'fx_contrast':0})
        check(max(pixel(contrast,100,80))-min(pixel(contrast,100,80))<=1 and contrast!=base,
              'FX_contrast respects its actual supplied filter and binds one minus mod value')
        red_layer={'name':'FX_red','filter':'_filter_colourise','depth':-100,
                   'parameters':{'g_TintCol':[1,0,0,1],'g_Intensity':1}}
        both={'fx_red':1,'fx_colorise_intensity':1,'fx_colorise_col_rgb':0x0000FF}
        profile.write_text(json.dumps({'layers':[red_layer]}))
        layered=render(both)
        check(all(abs(v-e)<=2 for x,y in [(60,40),(100,80)] for v,e in zip(pixel(layered,x,y),colourise(pixel(red,x,y),(0,0,1)))),
              'custom tint at depth -2400 follows a deeper red layer')
        red_layer['depth']=-3000
        profile.write_text(json.dumps({'layers':[red_layer]}))
        layered=render(both)
        check(all(abs(v-e)<=2 for x,y in [(60,40),(100,80)] for v,e in zip(pixel(layered,x,y),colourise(pixel(blue,x,y),(1,0,0)))),
              'custom tint precedes a shallower red layer according to original depth')
        profile.unlink()
        (root/'cgmk_config.json').write_text('{"JACKET_MANAGE_MODE":"custom","ENABLE_NON_BASE_FX":false}')
        check(render(both)==base,'custom non-base FX setting disables red and tint together')

    print(f'{checks} additional gimmick pixel checks passed.')


if __name__=='__main__':main()
