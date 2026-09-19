#!/usr/bin/env python3
"""Compare two renderer builds with identical synthetic charts and asset packs.

Pass a pre-migration assembly and the SDL_GPU assembly. Both must have the
same Assets contents beside them. Only temporary fixture/output files are made.
"""
import argparse
import json
import subprocess
import tempfile
import struct
import zlib
from pathlib import Path

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('baseline', type=Path)
parser.add_argument('candidate', type=Path)
parser.add_argument('--dotnet', default='dotnet')
args = parser.parse_args()

with tempfile.TemporaryDirectory(prefix='kuroaki-gpu-parity-') as temp:
    root = Path(temp)
    def chunk(kind,data):
        return struct.pack('>I',len(data))+kind+data+struct.pack('>I',zlib.crc32(kind+data))
    rows=b''.join(b'\0'+bytes(c for x in range(320) for c in (x%256,y,(x+y)%256)) for y in range(180))
    (root/'jacket.png').write_bytes(b'\x89PNG\r\n\x1a\n'+chunk(b'IHDR',struct.pack('>IIBBBBB',320,180,8,2,0,0,0))+chunk(b'IDAT',zlib.compress(rows))+chunk(b'IEND',b''))
    (root/'cgmk_config.json').write_text('{"JACKET_MANAGE_MODE":"custom","ENABLE_NON_BASE_FX":true}')
    (root/'chart.vsc').write_text('1500,0,0\n1500,1,1\n500,2,2,1800\n1750,6,3\n')
    cases = [
        ('lane and note skin', {}, 'obj_base_gimmick', 'none'),
        ('half note alpha', {'notealp': .5}, 'obj_base_gimmick', 'none'),
        ('rotated notes', {'noterot': 90}, 'obj_base_gimmick', 'none'),
        ('proxy opacity and translation', {'pra@0': .5, 'pry@0': -20}, 'obj_base_gimmick', 'none'),
        ('proxy clip and rotation', {'pra@0': 1, 'prrz@0': 15, 'prct@0': .3}, 'obj_base_gimmick', 'none'),
        ('gray and vignette', {'gray': .7, 'vig': .4}, 'obj_custom_gimmick', 'none'),
        ('underwater', {'fx_underwater': 3}, 'obj_custom_gimmick', 'none'),
        ('posterise', {'fx_posterize': 4, 'fx_posterize_vis': 1}, 'obj_custom_gimmick', 'none'),
        ('colourise', {'fx_red': 1, 'fx_red_intensity': .5}, 'obj_custom_gimmick', 'none'),
        ('hue', {'fx_hue_hue': .3, 'fx_hue_saturation': .7}, 'obj_custom_gimmick', 'none'),
        ('gameplay glow', {'fx_glow': .7}, 'obj_custom_gimmick', 'gameplay'),
        ('Scarlet Death native post', {}, 'obj_scarletdeath_gimmick', 'scarletdeath'),
    ]
    for label, mods, obj, room in cases:
        rows = ['!obj:'+obj]
        for key, value in mods.items():
            name, _, proxy = key.partition('@')
            rows.append(f'0,0,linear,{value},{value},{name},{proxy or "-1"}')
        (root/'chart.vsm').write_text('\n'.join(rows))
        (root/'fixture.sgv.json').write_text(json.dumps({
            'Chart':'chart.vsc', 'Gimmick':'chart.vsm', 'Bpm':120, 'ScrollSpeed':1,
            'GameUiEnabled':False, 'RoomPreset':room, 'PostProcessing':True,
        }))
        inspection=subprocess.run([args.dotnet,str(args.candidate.resolve()),'--inspect',str(root/'fixture.sgv.json')],capture_output=True,text=True)
        # RoomPreset=none deliberately exercises documented fallback settings.
        # Reject malformed fixtures or misspelled/unimplemented controls.
        invalid=[d for d in json.loads(inspection.stdout)['diagnostics']
                 if d['Error'] or 'unsupported' in d['Message'].lower() or 'not implemented' in d['Message'].lower()]
        if inspection.returncode or invalid:
            raise AssertionError(f'{label}: invalid fixture: {invalid} {inspection.stderr}')
        frames = []
        for index, assembly in enumerate((args.baseline, args.candidate)):
            output=root/f'{index}.ppm'
            run=subprocess.run([args.dotnet,str(assembly.resolve()),'--snapshot',str(root/'fixture.sgv.json'),
                '--scene','--time','1','--out',str(output)],capture_output=True,text=True)
            if run.returncode: raise RuntimeError(run.stdout+run.stderr)
            header=output.read_bytes().split(b'\n',3)
            assert header[:3]==[b'P6',b'320 180',b'255']
            frames.append(header[3])
        errors=[abs(a-b) for a,b in zip(*frames)]
        maximum=max(errors); mean=sum(errors)/len(errors)
        # UNORM conversion/FP arithmetic may differ by a few channel values.
        if maximum>4 or mean>.1:
            raise AssertionError(f'{label}: max channel error {maximum}, mean {mean:.6f}')
        print(f'PASS {label}: max {maximum}, mean {mean:.6f}',flush=True)
print(f'{len(cases)} renderer parity checks passed.')
