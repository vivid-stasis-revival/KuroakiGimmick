#!/usr/bin/env python3
"""Compile a linked managed closure to static ARM64 Mono Full AOT objects."""
import argparse
import concurrent.futures
import json
import pathlib
import re
import subprocess

p = argparse.ArgumentParser(description=__doc__)
p.add_argument('--managed', type=pathlib.Path, required=True)
p.add_argument('--compiler', type=pathlib.Path, required=True)
p.add_argument('--output', type=pathlib.Path, required=True)
p.add_argument('--jobs', type=int, default=3)
p.add_argument('--llvm-tools', type=pathlib.Path,
               help='Enable LLVM AOT using the specified opt/llc directory.')
a = p.parse_args()
a.managed = a.managed.resolve()
a.compiler = a.compiler.resolve()
a.output = a.output.resolve()
a.output.mkdir(parents=True, exist_ok=True)
if a.llvm_tools:
    a.llvm_tools = a.llvm_tools.resolve()
    for tool in ('opt', 'llc'):
        if not (a.llvm_tools / tool).is_file():
            raise SystemExit('Missing LLVM tool: ' + str(a.llvm_tools / tool))
logs = a.output.parent / 'logs'
logs.mkdir(exist_ok=True)
assemblies = sorted(a.managed.glob('*.dll'))
if not assemblies or not (a.managed / 'System.Private.CoreLib.dll').is_file():
    raise SystemExit('Incomplete managed closure.')

def compile(file):
    obj = a.output / (file.name + '.o')
    log = logs / (file.stem + '.log')
    options = 'full,static,direct-icalls,print-skipped-methods'
    if a.llvm_tools:
        # Horizon NROs are relocated at load time. The cross compiler otherwise
        # asks llc for static relocation, which produced the broken pre-PIC build.
        options += ',llvm,llvmllc=-relocation-model=pic,llvm-path=' + str(a.llvm_tools) + '/'
    # The runtime allocates specific call trampolines from CoreLib's pool.
    # Hardware startup exhausted the compiler's default 4096 before frame one.
    if file.name == 'System.Private.CoreLib.dll':
        # Loading the first chart also exhausted type 3's default 512 wrappers.
        options += ',ntrampolines=65536,ngsharedvt-trampolines=8192'
    with log.open('w') as stream:
        result = subprocess.run([str(a.compiler)] + (['--llvm'] if a.llvm_tools else []) + ['--path=' + str(a.managed),
            '--aot=' + options + ',tool-prefix=aarch64-none-elf-,outfile=' + str(obj),
            str(file)], stdout=stream, stderr=subprocess.STDOUT)
    text = log.read_text()
    symbol = re.search(r"Linking symbol: '([A-Za-z0-9_]+)'", text)
    counts = re.search(r'Compiled:\s*(\d+)/(\d+)', text)
    llvm = re.search(r'LLVM:\s*(\d+)\s*\((\d+)%\)', text)
    if result.returncode or not obj.is_file() or not symbol or not counts:
        raise RuntimeError('AOT failed for ' + file.name + '\n' + text[-3000:])
    skipped = [line for line in text.splitlines() if line.startswith('Skip (')]
    # Open generic templates need not have their own code when concrete/shared
    # instances are emitted. Do not mistake those candidates for JIT fallback.
    # Strict MONO_AOT_MODE_FULL on hardware remains the coverage check.
    if ('Failed to AOT compile' in text or 'Unable to compile method' in text or
        any(not line.startswith('Skip (gshared failure):') for line in skipped)):
        raise RuntimeError('Incomplete Full AOT for ' + file.name + '\n' + text[-3000:])
    print(f'{file.name}: {counts[1]}/{counts[2]} methods, {obj.stat().st_size} object bytes', flush=True)
    return {'assembly': file.name, 'symbol': symbol[1], 'methods': int(counts[1]),
            'aot_options': options,
            'backend': 'llvm' if a.llvm_tools else 'mono',
            'llvm_methods': int(llvm[1]) if llvm else 0,
            'method_candidates': int(counts[2]), 'skipped_generic_templates': len(skipped),
            'object_bytes': obj.stat().st_size}

with concurrent.futures.ThreadPoolExecutor(max_workers=a.jobs) as pool:
    records = list(pool.map(compile, assemblies))
records.sort(key=lambda r: r['assembly'])
header = a.output.parent / 'kg_aot_modules.h'
header.write_text('/* Generated static Full AOT registration. */\n' + ''.join(
    'KG_AOT_MODULE(' + r['symbol'] + ');\n' for r in records))
(a.output.parent / 'aot-manifest.json').write_text(json.dumps(records, indent=2) + '\n')
print('Full AOT closure compiled: ' + str(len(records)) + ' assemblies.', flush=True)
