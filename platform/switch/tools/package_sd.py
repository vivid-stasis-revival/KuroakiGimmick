#!/usr/bin/env python3
"""Assemble a private SD tree, preserving existing destinations by refusing reuse."""
import argparse
import pathlib
import shutil
import struct

p = argparse.ArgumentParser()
p.add_argument('--managed', type=pathlib.Path, required=True)
p.add_argument('--nro', type=pathlib.Path, required=True)
p.add_argument('--probe', type=pathlib.Path, help='Optional asset-free Vulkan probe NRO')
p.add_argument('--assets', type=pathlib.Path, required=True)
p.add_argument('--shaders', type=pathlib.Path, required=True)
p.add_argument('--mono-sd', type=pathlib.Path, required=True, help='mono-nx sd_files, containing mono/')
p.add_argument('--output', type=pathlib.Path, required=True)
a = p.parse_args()
if a.output.exists(): raise SystemExit('Output already exists; choose a fresh staging directory.')
for file in [a.managed / 'KuroakiGimmick.dll', a.nro, a.assets / 'Brand/kuroaki.png', a.assets / 'Shaders/proxy.frag',
             a.mono_sd / 'mono/etc/icudt77l.dat']:
    if not file.is_file(): raise SystemExit('Missing runtime input: ' + str(file))
for folder in ['lib_net9.0', 'framework_net9.0', 'etc']:
    if not (a.mono_sd / 'mono' / folder).is_dir(): raise SystemExit('Missing Mono BCL: ' + folder)
if len(list(a.shaders.glob('*.kgsp'))) < 6: raise SystemExit('Incomplete baked shader directory.')
for nro in [a.nro] + ([a.probe] if a.probe else []):
    with nro.open('rb') as f: header = f.read(0x80)
    if len(header) < 0x80 or header[0x10:0x14] != b'NRO0':
        raise SystemExit('Invalid NRO header: ' + str(nro))
    image_size = struct.unpack_from('<I', header, 0x18)[0]
    if image_size < 0x80 or image_size > nro.stat().st_size:
        raise SystemExit('Invalid NRO image size: ' + str(nro))
app = a.output / 'switch/KuroakiGimmick'
app.mkdir(parents=True)
# Only managed assemblies; no desktop native assets or host .NET runtime payload.
for file in a.managed.glob('*.dll'): shutil.copy2(file, app / file.name)
shutil.copy2(a.nro, app / 'KuroakiGimmick.nro')
if a.probe: shutil.copy2(a.probe, app / 'KG-Vulkan-Probe.nro')
shutil.copytree(a.assets, app / 'Assets', ignore=shutil.ignore_patterns('.DS_Store', '._*'))
shutil.copytree(a.shaders, app / 'Assets/SwitchShaders', dirs_exist_ok=True)
repo = pathlib.Path(__file__).resolve().parents[3]
shutil.copytree(repo / 'ThirdParty', app / 'ThirdParty')
shutil.copy2(repo / 'LICENSE', app / 'LICENSE')
shutil.copy2(repo / 'THIRD_PARTY_NOTICES.md', app / 'THIRD_PARTY_NOTICES.md')
for folder in ['UserData', 'Exports', 'Temp', 'Songs']: (app / folder).mkdir()
for folder in ['lib_net9.0', 'framework_net9.0', 'etc']:
    shutil.copytree(a.mono_sd / 'mono' / folder, a.output / 'mono' / folder)
(app / 'mono.ini').write_text('''[mono]
logging = true
icu = /mono/etc/icudt77l.dat
assembly_dir = "/mono/lib_net9.0;/mono/framework_net9.0"
config_dir = /mono/etc
[nx]
file_io_redirect = /switch/KuroakiGimmick/UserData/mono.log
force_full_application = true
exit_process_on_end = true
''')
(app / 'launch.args').write_text('--render-width\n640\n--language\nzh-CN\n')
print('SD staging complete: ' + str(a.output.resolve()))
print('Launch through hbmenu title takeover. Hardware execution is unverified.')
