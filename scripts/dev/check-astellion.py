#!/usr/bin/env python3
"""Validate an external ASTELLION pack and real SDL_GPU rendering.
Only temporary copies and synthetic pixel fixtures are modified.
Usage: python3 scripts/dev/check-astellion.py path/to/KuroakiGimmick.dll path/to/song
"""
import argparse
import copy
import hashlib
import json
import math
from pathlib import Path
import shutil
import struct
import subprocess
import tempfile
import zlib

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('assembly', type=Path)
parser.add_argument('song', type=Path)
parser.add_argument('--dotnet', default='dotnet')
args = parser.parse_args()
assembly, song = args.assembly.resolve(), args.song.resolve()
checks = 0

def check(ok, message):
    global checks
    if not ok:
        raise AssertionError(message)
    checks += 1
    print('PASS', message, flush=True)

def invoke(*arguments, errors=False):
    result = subprocess.run([args.dotnet, str(assembly), *map(str, arguments)],
                            cwd='/', capture_output=True, text=True)
    if result.returncode not in ((0, 2) if errors else (0,)):
        raise RuntimeError(result.stdout + result.stderr)
    return result.stdout

def hashes(root):
    return {str(p.relative_to(root)): hashlib.sha256(p.read_bytes()).hexdigest()
            for p in root.rglob('*') if p.is_file()}

def png(path, width, height, colour):
    def chunk(kind, data):
        return struct.pack('>I', len(data)) + kind + data + struct.pack('>I', zlib.crc32(kind + data))
    pixels = b''.join(b'\0' + bytes(c for x in range(width) for c in colour(x, y)) for y in range(height))
    path.write_bytes(b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', struct.pack('>IIBBBBB', width, height, 8, 6, 0, 0, 0))
                     + chunk(b'IDAT', zlib.compress(pixels)) + chunk(b'IEND', b''))

original = hashes(song)
for difficulty in ('OPENING', 'MIDDLE', 'FINALE', 'ENCORE'):
    report = json.loads(invoke('--inspect', song / (difficulty + '.vsb')))
    check(report['modCount'] == 1259 and report['bpm'][0]['Bpm'] == 174,
          difficulty + ': original 1259 effects and 174 BPM retained')
    ast = report['nativeGimmick']
    check(len(ast['sprites']) == 7 and not ast['issues'] and ast['callbacks'].get('sides', 0) == 796 and ast['callbacks'].get('astbars', 0) == 128,
          difficulty + ': all sprite groups and callbacks load from the song')
    check(len(report['diagnostics']) == 1 and '随机序列/公共粒子对象未逐版确认' in report['diagnostics'][0]['Message'],
          difficulty + ': particle fidelity limitation remains explicit')

with tempfile.TemporaryDirectory(prefix='kuroaki astellion checks ') as temp:
    root = Path(temp)
    pack = root / 'astellion'
    shutil.copytree(song / 'astellion', pack)
    manifest = json.loads((pack / 'manifest.json').read_text())
    project = {'Chart': 'fixture.vsc', 'Gimmick': 'fixture.vsm', 'Notes': False,
               'PostProcessing': False, 'GameUiEnabled': False}
    (root / 'fixture.vsc').write_text('10000,0,0\n')

    def setup(mods=None, callbacks=(), changes=None, mpf=''):
        values = {'parttimer': 1e9, 'fxdist1': 0, 'fxdist2': 0} | (mods or {})
        rows = [f'0,0,linear,{v},{v},{name},-1' for name, v in values.items()]
        rows += [f'{time*174/60:.12f},0,linear,0,{value},{name},-1' for time, name, value in callbacks]
        (root / 'fixture.vsm').write_text('!obj:obj_astellion_gimmick\n' + '\n'.join(rows) + '\n' + mpf)
        (root / 'fixture.sgv.json').write_text(json.dumps(project | (changes or {})))

    def report(**kwargs):
        setup(**kwargs)
        return json.loads(invoke('--inspect', root / 'fixture.sgv.json', errors=True))

    def render(time=1, **kwargs):
        setup(**kwargs)
        invoke('--snapshot', root / 'fixture.sgv.json', '--strict', '--scene', '--time', time, '--out', root / 'frame.ppm')
        magic, size, maximum, pixels = (root / 'frame.ppm').read_bytes().split(b'\n', 3)
        assert (magic, size, maximum, len(pixels)) == (b'P6', b'320 180', b'255', 320*180*3)
        return pixels

    def pixel(frame, x, y):
        offset = (y*320+x)*3
        return tuple(frame[offset:offset+3])

    for relative in ('../outside.png', str(root / 'outside.png'), 'C:\\outside.png'):
        changed = copy.deepcopy(manifest)
        changed['sprites']['sp_ast_u_bg']['frames'][0] = relative
        (pack / 'manifest.json').write_text(json.dumps(changed))
        result = report()['nativeGimmick']
        check('sp_ast_u_bg' in result['issues'] and len(result['sprites']) == 6,
              'invalid resource path is reported without dropping valid sprite groups: ' + relative)
    (pack / 'manifest.json').write_text(json.dumps(manifest))
    target = pack / manifest['sprites']['sp_ast_u_bg']['frames'][0]
    previous = target.read_bytes()
    png(target, 1, 1, lambda x, y: (255, 0, 0, 255))
    check('Decoded dimensions' in report()['nativeGimmick']['issues']['sp_ast_u_bg'], 'decoded image dimensions are checked against the manifest')
    target.unlink()
    png(root / 'outside.png', 320, 180, lambda x, y: (0, 0, 0, 255))
    target.symlink_to(root / 'outside.png')
    check('symlink escapes' in report()['nativeGimmick']['issues']['sp_ast_u_bg'], 'resource symlinks cannot escape the song asset directory')
    target.unlink()
    check('sp_ast_u_bg' in report()['nativeGimmick']['issues'], 'missing image has a precise per-sprite error')
    target.write_bytes(previous)
    changed = copy.deepcopy(manifest)
    changed['background']['parameters']['g_DistortTexture'] = 'noise/missing.png'
    (pack / 'manifest.json').write_text(json.dumps(changed))
    check('underwater-noise' in report()['nativeGimmick']['issues'], 'missing noise reports the affected underwater effect')
    (pack / 'manifest.json').write_text(json.dumps(manifest))
    shader = pack / 'shaders/post/GLSL_Fragment.frag'
    saved_shader = shader.read_bytes()
    shader.unlink()
    check('shader_unraveling_main' in report()['nativeGimmick']['issues'], 'missing post shader is reported by name')
    shader.write_bytes(saved_shader)
    (pack / 'manifest.json').unlink()
    check('manifest' in report()['nativeGimmick']['issues'], 'missing manifest is explicitly reported')
    (pack / 'manifest.json').write_text(json.dumps(manifest))
    check(any(d['Source'] == 'native-gimmick/astbars' and d['Error'] for d in report(callbacks=[(1, 'astbars', 5)])['diagnostics']), 'out-of-range astbars cannot index a nonexistent bar')
    check(any(d['Source'] == 'native-gimmick/particles' and d['Error'] for d in report(mods={'parttimer': 0})['diagnostics']), 'nonpositive parttimer cannot hang particle emission')
    slow, fast = (report(mods={'parttimer': n})['nativeGimmick']['particles'] for n in (4, 2))
    check(fast == 2*slow and slow > 0, 'parttimer controls emitted dust density')

    # Solid/transparent temporary sprites make depth, position and alpha observable.
    def paint(name, colour):
        sprite = manifest['sprites'][name]
        for relative in sprite['frames']:
            png(pack / relative, sprite['width'], sprite['height'], colour)

    for name in manifest['sprites']:
        paint(name, lambda x, y: (0, 0, 0, 0))
    paint('sp_ast_u_bg', lambda x, y: (0, 0, 0, 255))
    paint('sp_sidebar4', lambda x, y: (255, 255, 255, 255) if x < 30 else (0, 0, 0, 0))
    sides = render(time=1.1, callbacks=[(1, 'sides', 0)])
    check(pixel(sides, 15, 20) == (102, 102, 102) and pixel(sides, 304, 20) == (102, 102, 102)
          and pixel(sides, 80, 20) == (0, 0, 0), 'sides move outward at 800 px/s with mirrored origins and alpha 0.4')
    check(not any(render(time=1.4, callbacks=[(1, 'sides', 0)])), 'side sprites disappear after leaving the source bounds')
    paint('sp_sidebar4', lambda x, y: (0, 0, 0, 0))
    paint('sp_ast_particle', lambda x, y: (255, 255, 255, 255))
    small = render(time=1.0625, callbacks=[(1, 'sides', 0)])
    full = render(time=1.125, callbacks=[(1, 'sides', 0)])
    faded = render(time=1.1875, callbacks=[(1, 'sides', 0)])
    check(sum(v > 0 for v in full) > sum(v > 0 for v in small) and sum(full) > sum(faded) > 0,
          'burst particles grow for 125 ms, then fade for 125 ms')
    check(not any(render(time=1.25, callbacks=[(1, 'sides', 0)])), 'burst particle lifetime is 250 ms')
    for callback_time in (100, 132.4):
        check(not any(render(time=callback_time+.125, callbacks=[(callback_time, 'sides', 0)])), 'burst suppression includes boundary ' + str(callback_time))
    check(any(render(time=132.526, callbacks=[(132.401, 'sides', 0)])), 'burst emission resumes after 132.4 seconds')
    paint('sp_ast_particle', lambda x, y: (0, 0, 0, 0))
    paint('sp_ast_bar', lambda x, y: (255, 255, 255, 255))
    for value, y in enumerate((125, 95, 65, 35), 1):
        frame = render(time=1.05, callbacks=[(1, 'astbars', value)])
        check(all(abs(v-64) <= 1 for v in pixel(frame, 20, y+5)) and pixel(frame, 20, y-1) == (0, 0, 0), 'astbars value ' + str(value) + ' selects y=' + str(y) + ' and fades in 100 ms')
    check(not any(render(time=1.11, callbacks=[(1, 'astbars', 1)])), 'bar disappears after 100 ms')

    paint('sp_overlaynew', lambda x, y: (255, 0, 0, 255))
    track = {'callbacks': [(2, 'sides', 0)], 'changes': {'Notes': True}}
    check(pixel(render(time=1, **track), 20, 20) == (0, 0, 0), 'dedicated room starts with invisible track decorations')
    check(pixel(render(time=6.5, **track), 20, 20) == (223, 0, 0), 'first sides fades track decorations in over nine seconds with EaseOutCubic')
    check(pixel(render(time=11, **track), 20, 20) == (255, 0, 0), 'room fade finishes at full alpha')
    paint('sp_sidebar4', lambda x, y: (255, 255, 255, 255))
    layer = render(time=12.1, callbacks=[(0, 'sides', 0), (12, 'sides', 0)], changes={'Notes': True})
    check(pixel(layer, 20, 20) == (255, 102, 102), 'depth 260 sides draw over depth 300 track decoration')
    paint('sp_holdnote_overlay', lambda x, y: (0, 255, 0, 255))
    layer = render(time=12.1, callbacks=[(0, 'sides', 0), (12, 'sides', 0)], changes={'Notes': True})
    check(pixel(layer, 20, 20) == (0, 255, 0) and pixel(layer, 140, 20) == (0, 255, 0), 'depth 100 hold decoration covers sides and depth 200 rail')

    for name in manifest['sprites']:
        paint(name, lambda x, y: (0, 0, 0, 0))
    paint('sp_ast_u_bg', lambda x, y: (x % 256, y, (x*13+y*7) % 256, 255))
    plain = render()
    proxy = render(mpf='0,0,linear,1,1,pra,0\n')
    check(proxy[165*320*3:] == plain[165*320*3:], 'proxy mode leaves the ASTELLION background visible beneath a hidden footer HUD')
    post = {'changes': {'PostProcessing': True}}
    base = render(**post)
    distort1 = render(mods={'fxdist1': 100}, **post)
    distort2 = render(mods={'fxdist2': 100}, **post)
    check(base != distort1 and base != distort2 and distort1 != distort2, 'fxdist1 and fxdist2 independently bind the two underwater amounts')
    check(distort1 != render(time=2, mods={'fxdist1': 100}, **post), 'underwater movement uses song time')
    grey = render(mods={'gray': 1}, changes={'PostProcessing': True, 'GameUiEnabled': True}, callbacks=[(0, 'sides', 0)], time=10)
    check(all(grey[i] == grey[i+1] == grey[i+2] for i in range(0, len(grey), 3)), 'post grayscale affects the complete surface including coloured HUD')
    check(render(mods={'barrel': .2}, **post) == render(mods={'barrel2': .2}, **post), 'post barrel and barrel2 are added before binding')
    for mod, value in [('fish', .4), ('vig', .8), ('hdistort', .1), ('abx', .7), ('aby', .7)]:
        check(base != render(mods={mod: value}, **post), 'post shader binds ' + mod)
    at = 1.5
    beat = at*174/60
    expected_ab = {'abx': .8*.2*math.sin(beat*.25*math.pi), 'aby': -.8*.4*math.cos(beat*.25*math.pi)}
    check(render(time=at, mods={'aberamp': .8}, mpf='mpf\n0,100,aberControl\n', **post)
          == render(time=at, mods=expected_ab, **post), 'aberControl drives post abx/aby from the effect beat')
    check(set(render(mods={'gray': 1, 'wflash': 1}, **post)) == {255}, 'white flash is drawn after the complete-surface post shader')
    paint('pt_diamonddust', lambda x, y: (255, 255, 255, 255))
    paint('sp_ast_u_bg', lambda x, y: (0, 0, 0, 255))
    white = render(mods={'parttimer': 2, 'rainbow': 0})
    rainbow = render(mods={'parttimer': 2, 'rainbow': 1})
    check(white != rainbow and all(white[i] == white[i+1] == white[i+2] for i in range(0, len(white), 3)), 'rainbow controls dust saturation')
    render(time=8, mods={'parttimer': 2, 'rainbow': 1})
    check(render(mods={'parttimer': 2, 'rainbow': 1}) == rainbow, 'particle frames reproduce exactly after seeking backward')

check(hashes(song) == original, 'external charts, music, images and reference files remain byte-identical')
print(f'{checks} ASTELLION loading/pixel checks passed.')
