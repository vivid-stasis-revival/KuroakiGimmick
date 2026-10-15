#!/usr/bin/env python3
"""Exercise the real libnx Mono TLS block with libnx-compatible thread exit."""
import argparse
import pathlib
import subprocess
import tempfile
p = argparse.ArgumentParser(description=__doc__)
p.add_argument('--runtime', type=pathlib.Path, required=True)
p.add_argument('--cc', default='clang')
a = p.parse_args()
src = a.runtime / 'src/mono/mono/utils/mono-tls.c'
s = src.read_text()
block = s[s.index('// libnx has a limit'):s.rindex('#endif')]
template = pathlib.Path(__file__).with_name('mono_tls_cleanup_fixture.c').read_text()
with tempfile.TemporaryDirectory(prefix='kg-mono-tls-') as folder:
    root = pathlib.Path(folder)
    file = root / 'tls.c'
    file.write_text(template.replace('/* MONO_TLS_SOURCE_INSERT */', block))
    exe = root / 'tls-check'
    subprocess.run([a.cc, '-std=gnu11', '-O1', '-g', '-fsanitize=address,undefined',
                    str(file), '-pthread', '-o', str(exe)], check=True)
    for case in ['0', '1', '2', '3', '4']:
        subprocess.run([str(exe), case], check=True)
        print('PASS TLS cleanup case ' + case, flush=True)
