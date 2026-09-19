#!/usr/bin/env python3
"""Object-profile and real GPU regressions; all fixtures are temporary.
Optional --song checks a supported external chart without changing its files.
"""
import argparse
import copy
import hashlib
import json
from pathlib import Path
import shutil
import struct
import subprocess
import tempfile
import uuid
import zlib

p = argparse.ArgumentParser(description=__doc__)
p.add_argument('assembly', type=Path)
p.add_argument('--song', type=Path)
p.add_argument('--dotnet', default='dotnet')
a = p.parse_args()
assembly = a.assembly.resolve()
assets = assembly.parent / 'Assets'
if not assets.is_dir():
    assets = next(parent.parent / 'Assets' for parent in assembly.parents if parent.name.endswith('.app'))
checks = 0

def check(ok, message):
    global checks
    if not ok:
        raise AssertionError(message)
    checks += 1
    print('PASS', message, flush=True)

def invoke(*args, errors=False):
    run = subprocess.run([a.dotnet, str(assembly), *map(str, args)], cwd='/', capture_output=True, text=True)
    if run.returncode not in ((0, 2) if errors else (0,)) or 'native-gimmick shader ' in run.stderr:
        raise RuntimeError(run.stdout + run.stderr)
    return run.stdout

def hashes(root):
    return {str(f.relative_to(root)): hashlib.sha256(f.read_bytes()).hexdigest()
            for f in root.rglob('*') if f.is_file() and f.name != '.DS_Store'}

def png(path, width, height, colour):
    def chunk(kind, data):
        return struct.pack('>I', len(data)) + kind + data + struct.pack('>I', zlib.crc32(kind + data))
    raw = b''.join(b'\0' + bytes(colour)*width for _ in range(height))
    path.write_bytes(b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', struct.pack('>IIBBBBB', width, height, 8, 6, 0, 0, 0))
                     + chunk(b'IDAT', zlib.compress(raw)) + chunk(b'IEND', b''))

with tempfile.TemporaryDirectory(prefix='kuroaki reusable objects ') as temp:
    root = Path(temp)
    if a.song:
        song = a.song.resolve()
        before = hashes(song)
        report = json.loads(invoke('--inspect', song))
        check(not report['diagnostics'], 'external report has no remaining unsupported/missing-resource diagnostics')
        check(report['checker']['available'] and report['checker']['refreshes'] == 128,
              'object configuration enables the native checker and all refresh callbacks')
        check(report['particles']['normal'] > 0 and report['particles']['burst'] == 2720,
              'common ambient emission and side bursts are both present')
        check(sum(report['nativeGimmick']['callbacks'].values()) == 6, 'all six sprite callbacks are scheduled')
        layers = {layer['Name']: layer for layer in report['fx']['layers']}
        check(layers['glow']['Depth'] == 600 and layers['FX_glow']['Depth'] == -1800
              and layers['FX_posterize']['Depth'] == -2100, 'actual room depths retain both glows and posterise')
        for index, title in enumerate(('Completely Different Song', '另一首测试曲')):
            renamed = root / ('unrelated directory ' + str(index))
            shutil.copytree(song, renamed)
            info_path = renamed / 'info.json'
            info = json.loads(info_path.read_text())
            info['name'] = info['formatted_name'] = title
            if isinstance(info.get('enc_data'), dict):
                info['enc_data']['name'] = title
            info_path.write_text(json.dumps(info))
            for ext in ('vsb', 'vsm'):
                (renamed / ('ENCORE.' + ext)).rename(renamed / ('arbitrary_chart.' + ext))
            other = json.loads(invoke('--inspect', renamed / 'arbitrary_chart.vsb'))
            check(not other['diagnostics'] and other['nativeGimmick']['manifest'] == report['nativeGimmick']['manifest']
                  and other['nativeGimmick']['callbacks'] == report['nativeGimmick']['callbacks']
                  and other['fx']['layers'] == report['fx']['layers'],
                  'object support survives new song title, chart filename and directory: ' + title)
        check(hashes(song) == before, 'external chart/audio/artwork remain byte-identical')

    # The engine must also handle a never-before-seen object and mod names.
    object_name = 'obj_native_fixture_' + uuid.uuid4().hex
    profile_dir = assets / 'Gimmicks' / object_name
    profile_dir.mkdir(parents=True)
    try:
        png(profile_dir / 'caption.png', 7, 5, (0, 255, 0, 255))
        png(profile_dir / 'panel.png', 7, 5, (255, 0, 0, 255))
        (profile_dir / 'post.frag').write_text('varying vec2 v_vTexcoord; uniform float u_blue; void main(){gl_FragColor=vec4(0,0,u_blue,1);}')
        definition = {
            'version': 1, 'objectName': object_name, 'extraMods': ['unused', 'caption'],
            'defaults': {'background_blue': .8}, 'sprites': {
                name: {'width': 7, 'height': 5, 'originX': 3, 'originY': 2, 'frames': [name+'.png']}
                for name in ('caption', 'panel')},
            'shaders': {'test_post': 'post.frag'}, 'postModeMod': 'filter_mode',
            'postModes': {'0': {'shader': 'test_post', 'uniforms': {'u_blue': [{'mods': ['background_blue'], 'scale': .5}]}}},
            'callbacks': {'caption': [{'sprite': 'caption', 'x': 23, 'y': 37, 'depth': -100, 'lifetime': 1,
                'tweens': [{'property': 'alpha', 'delay': .4, 'duration': .5, 'from': 1, 'to': 0}]}]},
            'overlays': [{'sprite': 'panel', 'x': 23, 'y': 37, 'alphaMod': 'panel_alpha'}]
        }

        def save_definition(data=definition):
            (profile_dir / 'manifest.json').write_text(json.dumps(data))

        save_definition()
        png(root / 'jacket.png', 1, 1, (255, 255, 255, 255))
        (root / 'fixture.vsc').write_text('10000,0,0\n')
        project = {'Chart': 'fixture.vsc', 'Gimmick': 'fixture.vsm', 'GameUiEnabled': False,
                   'Notes': False, 'RoomPreset': 'none'}

        def setup(mods=None, callback=True, changes=None, obj=object_name):
            rows = [f'0,0,linear,{value},{value},{name},-1' for name, value in (mods or {}).items()]
            if callback:
                rows.append('2,0,linear,0,0,caption,-1')
            (root / 'fixture.vsm').write_text('!obj:' + obj + '\n' + '\n'.join(rows) + '\n')
            (root / 'fixture.sgv.json').write_text(json.dumps(project | (changes or {})))

        def report(**kwargs):
            setup(**kwargs)
            return json.loads(invoke('--inspect', root / 'fixture.sgv.json', errors=True))

        def render(time=1.1, **kwargs):
            setup(**kwargs)
            invoke('--snapshot', root / 'fixture.sgv.json', '--scene', '--time', time, '--out', root / 'frame.ppm')
            magic, size, maximum, frame = (root / 'frame.ppm').read_bytes().split(b'\n', 3)
            assert (magic, size, maximum) == (b'P6', b'320 180', b'255')
            return frame

        def pixel(frame, x, y):
            offset = (y*320+x)*3
            return tuple(frame[offset:offset+3])

        fresh = report()
        check(not fresh['diagnostics'] and fresh['nativeGimmick']['callbacks'] == {'caption': 1},
              'new object and callback names work without C# branches or song metadata')
        shown = render(mods={'panel_alpha': 1})
        check(pixel(shown, 23, 37) == (0, 255, 0) and pixel(shown, 19, 37) == (0, 0, 102),
              'callback uses declared sprite origin and appears above the overlay/post pass')
        check(pixel(render(time=.5, mods={'panel_alpha': 1}), 23, 37) == (255, 0, 0),
              'GUI overlay binds an arbitrary alpha mod')
        half = pixel(render(time=1.65, mods={'panel_alpha': 1}), 23, 37)
        check(all(abs(v-128) <= 1 for v in half[:2]) and half[2] == 0,
              'generic delayed tween fades the callback over lower GUI content')
        check(pixel(render(time=2.1, mods={'panel_alpha': 1}), 23, 37) == (255, 0, 0),
              'callback expires at its declared lifetime')
        render(time=8)
        check(render(mods={'panel_alpha': 1}) == shown, 'callback state reproduces after backward seeking')
        check(pixel(render(callback=False, mods={'background_blue': .4}), 0, 0) == (0, 0, 51),
              'shader bindings evaluate arbitrary mod names and scale factors')

        # Minimal VSB: the callback occupies the second object-specific ID.
        binary = (bytes([86,83,67,1,0,224,229]) + object_name.encode() + b'\0'
                  + bytes([228,1,226,233]) + struct.pack('<ffBffBb', 2, 0, 1, 0, 0, 130, -1)
                  + bytes([227,225,255]))
        (root / 'binary.vsb').write_bytes(binary)
        binary_report = json.loads(invoke('--inspect', root / 'binary.vsb', '--room', 'none'))
        check(binary_report['nativeGimmick']['callbacks'] == {'caption': 1},
              'VSB extra mod IDs use the object definition registration order')
        enabled = copy.deepcopy(definition)
        enabled['ambientParticles'] = enabled['checkerboard'] = True
        enabled['checkerMode'] = 1
        save_definition(enabled)
        enabled_report = report()
        check(enabled_report['particles']['normal'] > 0 and enabled_report['checker']['available'],
              'ambient particles and checker can be enabled by another object definition')
        save_definition()
        check(any('unverified_mod' in d['Message'] for d in report(mods={'unverified_mod': 1})['diagnostics']),
              'unrelated unsupported mods remain visible in reports')
        bad = copy.deepcopy(definition)
        bad['sprites']['caption']['frames'] = ['../outside.png']
        save_definition(bad)
        check('caption' in report()['nativeGimmick']['issues'], 'object resource traversal is rejected')
        save_definition()
        (profile_dir / 'caption.png').unlink()
        check('caption' in report()['nativeGimmick']['issues'], 'missing callback artwork is reported instead of faking support')
        png(profile_dir / 'caption.png', 1, 1, (0, 255, 0, 255))
        check('Decoded dimensions' in report()['nativeGimmick']['issues']['caption'], 'sprite dimensions are validated')
        png(profile_dir / 'caption.png', 7, 5, (0, 255, 0, 255))

        # Original room posterise rounds channels, unlike the old floor fallback.
        no_post = copy.deepcopy(definition)
        no_post['postModes'] = {}
        no_post['overlays'] = []
        save_definition(no_post)
        png(root / 'solid.png', 1, 1, (90, 0, 0, 255))
        (root / 'fixture.vsp').write_text('#Layer\nmain,-1000\n#Image\nmain:\nstatic,solid,solid.png,0,320,180\n')
        (root / 'poster.fx.json').write_text(json.dumps({'layers': [{'name': 'FX_posterize', 'filter': '_filter_posterise',
            'depth': -2100, 'parameters': {'g_ColourLevels': 2}}]}))
        quantized = render(callback=False, mods={'imgx_solid':160,'imgy_solid':90,'imgalp_solid':1,'fx_posterize':2,'fx_posterize_vis':1},
                           changes={'Images':'fixture.vsp','FxProfile':'poster.fx.json'})
        check(pixel(quantized, 20, 20) == (128, 0, 0), 'shared room posterise uses original channel rounding at its layer depth')
        save_definition()
    finally:
        shutil.rmtree(profile_dir)

print(f'{checks} reusable native-object checks passed.')
