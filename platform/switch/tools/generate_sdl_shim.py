#!/usr/bin/env python3
"""Generate typed static exports from K/G's SDL imports; fail on missing ABI."""
import argparse
import pathlib
import re

parser = argparse.ArgumentParser()
parser.add_argument('--repo', type=pathlib.Path, required=True)
parser.add_argument('--sdl', type=pathlib.Path, required=True)
parser.add_argument('--out', type=pathlib.Path, required=True)
args = parser.parse_args()
symbols = set()
for file in (args.repo / 'src/Native').glob('Sdl*.cs'):
    symbols.update(re.findall(r'\bextern\s+\w+\s+(SDL_\w+)\s*\(', file.read_text()))
# Windows-only import: managed code never calls it on Switch.
symbols.discard('SDL_SetWindowsMessageHook')
headers = '\n'.join(p.read_text() for p in (args.sdl / 'include/SDL3').glob('*.h'))
missing = [s for s in sorted(symbols) if not re.search(r'\b' + s + r'\s*\(', headers)]
# Device-properties query was introduced after the selected SDL fork. K/G already catches
# EntryPointNotFoundException for this optional hardware-name diagnostic.
optional = {'SDL_GetGPUDeviceProperties'}
required = set(missing) - optional
if required:
    raise SystemExit('Missing required SDL ABI: ' + ', '.join(sorted(required)))
symbols.difference_update(missing)
lines = ['/* Generated from K/G imports. Do not edit. */', '#define SDL_MAIN_HANDLED', '#include <SDL3/SDL.h>', '#include <SDL3/SDL_main.h>',
         '#include <string.h>', 'void *getsym_SDL3(const char *name)', '{']
for symbol in sorted(symbols):
    lines.append(f'    if (!strcmp(name, "{symbol}")) return (void *){symbol};')
lines.extend(['    return NULL;', '}', ''])
args.out.parent.mkdir(parents=True, exist_ok=True)
args.out.write_text('\n'.join(lines))
print(f'Registered {len(symbols)} SDL3 symbols; optional unavailable: {", ".join(missing) or "none"}')
