#!/usr/bin/env python3
"""Run actual callback bodies against a host audio-renderer simulator, without Horizon."""
import argparse
import pathlib
import subprocess
import tempfile

p = argparse.ArgumentParser(description=__doc__)
p.add_argument('sdl', type=pathlib.Path)
p.add_argument('--cc', default='cc')
a = p.parse_args()
source = (a.sdl / 'src/audio/switch/SDL_switchaudio.c').read_text()
bodies = []
for name in ['PlayDevice', 'WaitDevice', 'GetDeviceBuf', 'CloseDevice']:
    marker = 'SWITCHAUDIO_' + name + '('
    at = source.index(marker)
    start = source.rfind('\nstatic ', 0, at) + 1
    brace = source.index('{', at)
    depth = 1
    end = brace + 1
    while depth:
        if source[end] == '{': depth += 1
        elif source[end] == '}': depth -= 1
        end += 1
    bodies.append(source[start:end])
fixture = pathlib.Path(__file__).with_name('audio_backend_fixture.c').read_text()
fixture = fixture.replace('/* KG_AUDIO_CALLBACKS */', '\n\n'.join(bodies))
with tempfile.TemporaryDirectory() as directory:
    file = pathlib.Path(directory) / 'audio_callbacks.c'
    exe = pathlib.Path(directory) / 'audio_callbacks'
    file.write_text(fixture)
    subprocess.run([a.cc, '-std=c11', '-Werror', '-fsanitize=address,undefined', str(file), '-o', str(exe)], check=True)
    subprocess.run([str(exe)], check=True)
