#!/usr/bin/env python3
"""Complete the checked Mesa 26.2.4 SDK with its matching Rust static runtime."""
import argparse
import pathlib
import re
import shutil
import subprocess

p = argparse.ArgumentParser(description=__doc__)
p.add_argument('--sdk', type=pathlib.Path, required=True, help='Installed Switch portlibs prefix')
p.add_argument('--rust-libs', type=pathlib.Path, required=True, help='Matching aarch64-unknown-linux-gnu Rust lib directory')
p.add_argument('--nm', default='aarch64-none-elf-nm')
a = p.parse_args()
pc = a.sdk / 'lib/pkgconfig/vulkan.pc'
metadata = pc.read_text()
if 'Version: 26.2.4\n' not in metadata:
    raise SystemExit('This workaround is only verified with the checked Mesa 26.2.4 SDK.')

def compiler_version(file):
    match = re.search(rb'rustc version [^\x00\n]*?\([0-9a-f]+ [0-9-]+\)', file.read_bytes())
    if not match: raise SystemExit('Missing Rust compiler identity: ' + str(file))
    return match.group()

core = list(a.rust_libs.glob('libcore-*.rlib'))
if len(core) != 1: raise SystemExit('Expected exactly one Rust core archive.')
version = compiler_version(a.sdk / 'lib/libnak_rs.a')
if compiler_version(core[0]) != version:
    raise SystemExit('Rust compiler mismatch; use the same nightly as the Mesa SDK.')

# The checked SDK advertises libelf despite containing no references to it.
# Verify that before removing the unavailable portlib from its metadata.
undefined = subprocess.check_output([a.nm, '-u', str(a.sdk / 'lib/libvulkan.a')], text=True)
if re.search(r'\bU\s+(?:elf_|gelf_)', undefined):
    raise SystemExit('This SDK needs libelf; cannot remove its pkg-config dependency.')
metadata = metadata.replace('Requires.private: libelf, ', 'Requires.private: ')
archives = [f for f in sorted(a.rust_libs.glob('*.rlib'))
            if not f.name.startswith(('libpanic_abort-', 'libtest-', 'libprofiler_builtins-', 'libproc_macro-'))]
for prefix in ['libstd-', 'liballoc-', 'libpanic_unwind-', 'libcompiler_builtins-']:
    if not any(f.name.startswith(prefix) for f in archives):
        raise SystemExit('Incomplete Rust runtime: ' + prefix)
destination = a.sdk / 'lib/rust'
destination.mkdir(exist_ok=True)
for file in archives: shutil.copy2(file, destination / file.name)
flags = ' '.join('${libdir}/rust/' + f.name for f in archives)
metadata = re.sub(r'^Libs.private:.*$', 'Libs.private: -lnx -lm -lstdc++ ' + flags, metadata, flags=re.M)
pc.write_text(metadata)
print('Completed Mesa SDK with ' + version.decode() + '; ' + str(len(archives)) + ' Rust archives.')
