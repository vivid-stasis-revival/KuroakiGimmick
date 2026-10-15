#!/usr/bin/env python3
"""Compile SDL header layout assertions using a host C compiler (64-bit only)."""
import argparse
import json
import pathlib
import re
import subprocess
import tempfile
p = argparse.ArgumentParser()
p.add_argument('managed_abi', type=pathlib.Path)
p.add_argument('sdl', type=pathlib.Path)
p.add_argument('--cc', default='gcc')
a = p.parse_args()
abi = json.loads(a.managed_abi.read_text())
headers = '\n'.join(f.read_text() for f in (a.sdl / 'include/SDL3').glob('*.h'))
source = ['#include <SDL3/SDL.h>', '#include <stddef.h>', '_Static_assert(sizeof(void*) == 8, "64-bit host required");']
count = 0
for name, item in abi.items():
    native = {'FColor': 'SDL_FColor', 'IntRect': 'SDL_Rect', 'AudioSpec': 'SDL_AudioSpec', 'Event': 'SDL_Event'}.get(name, 'SDL_' + name)
    if not re.search(r'\b' + native + r'\b', headers):
        raise SystemExit('Missing native ABI type: ' + native)
    source.append(f'_Static_assert(sizeof({native}) == {item["Size"]}, "{name} size mismatch");')
    if name == 'Event':
        mapping = {'Type': 'type', 'WindowID': 'key.windowID', 'Scan': 'key.scancode', 'Repeat': 'key.repeat',
                   'Modifiers': 'key.mod', 'Key': 'key.key', 'Button': 'button.button', 'Clicks': 'button.clicks',
                   'X': 'motion.x', 'Y': 'motion.y', 'TextData': 'text.text', 'DropData': 'drop.data',
                   'DropX': 'drop.x', 'DropY': 'drop.y', 'WheelX': 'wheel.x', 'WheelY': 'wheel.y'}
    elif name == 'AudioSpec':
        mapping = {'Format': 'format', 'Channels': 'channels', 'Frequency': 'freq'}
    else:
        mapping = {f: f for f in item['Fields']}
        # This byte was reserved in SDL 3.2.x, now a feature flag in 3.4.x. K/G leaves it zero.
        if name == 'GPUMultisampleState': mapping['enable_alpha_to_coverage'] = 'padding1'
    for field, offset in item['Fields'].items():
        if field.startswith('padding') or field not in mapping: continue
        source.append(f'_Static_assert(offsetof({native}, {mapping[field]}) == {offset}, "{name}.{field} offset mismatch");')
        count += 1
source.append('int main(void) { return 0; }')
with tempfile.TemporaryDirectory() as d:
    file = pathlib.Path(d) / 'abi.c'
    file.write_text('\n'.join(source))
    subprocess.run([a.cc, '-std=c11', '-fsyntax-only', '-I', str(a.sdl / 'include'), str(file)], check=True)
print(f'Checked {len(abi)} struct sizes and {count} field offsets against SDL headers (host ABI, not Horizon execution).')
