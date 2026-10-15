#!/usr/bin/env python3
"""Bundle exact AOT metadata in ROMFS and generate its isolated SD cache manifest."""
import argparse
import hashlib
import json
import pathlib
import shutil
import zlib

p = argparse.ArgumentParser(description=__doc__)
p.add_argument('build', type=pathlib.Path)
a = p.parse_args()
files = sorted((a.build / 'managed').glob('*.dll'))
if not files: raise SystemExit('Missing linked assemblies.')
digest = hashlib.sha256()
records = []
dest = a.build / 'romfs/metadata'
dest.mkdir(parents=True, exist_ok=True)
for file in files:
    data = file.read_bytes()
    digest.update(file.name.encode()); digest.update(data)
    shutil.copy2(file, dest / file.name)
    records.append((file.name, len(data), zlib.crc32(data)))
root = '/switch/KuroakiGimmick/AOT-' + digest.hexdigest()[:12]
config = f'''[mono]
logging = true
icu = /mono/etc/icudt77l.dat
assembly_dir = {root}
config_dir = /mono/etc
[nx]
file_io_redirect = /switch/KuroakiGimmick/UserData/mono-aot.log
force_full_application = true
exit_process_on_end = true
'''
header = [
    '/* Generated AOT metadata manifest; matches the linked native modules. */',
    '#define KG_AOT_RUNTIME_ROOT ' + json.dumps(root),
    '#define KG_AOT_CONFIG_PATH ' + json.dumps(root + '/mono-full-aot.ini'),
    '#define KG_AOT_ASSEMBLY_PATH ' + json.dumps(root + '/KuroakiGimmick.dll'),
    'struct kg_aot_file { const char *name; unsigned int size; unsigned long crc; };',
    'static const struct kg_aot_file kg_aot_files[] = {']
header += ['    {' + json.dumps(n) + f', {s}U, 0x{c:08x}UL' + '},' for n, s, c in records]
header += ['};', 'static const char kg_aot_config[] = ' + json.dumps(config) + ';', '']
(a.build / 'kg_aot_payload.h').write_text('\n'.join(header))
(a.build / 'payload-manifest.json').write_text(json.dumps({'sd_runtime': root, 'files': records}, indent=2) + '\n')
print('AOT metadata ROMFS prepared; isolated runtime cache: ' + root)
