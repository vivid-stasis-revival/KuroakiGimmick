#!/usr/bin/env python3
"""Prepare a private, platform-specific resource ZIP locally. Never run in public CI."""
import argparse
from pathlib import Path
import shutil
import subprocess
import tempfile
import zipfile

ROOT = Path(__file__).resolve().parents[1]

def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('platform', choices=['android', 'ios', 'switch'])
    p.add_argument('--assets', type=Path, required=True)
    p.add_argument('--output', type=Path, required=True)
    p.add_argument('--dotnet', default='dotnet')
    p.add_argument('--shader-root', type=Path, action='append', default=[])
    args = p.parse_args()
    output = args.output.resolve()
    if output.exists(): p.error('Output already exists')
    if output.is_relative_to(ROOT): p.error('Keep private resource ZIPs outside the repository')
    assets = args.assets.resolve()
    for name in ('Brand/kuroaki.png', 'Shaders', 'GameCommon'):
        if not (assets / name).exists(): p.error(f'Missing resource: {name}')
    with tempfile.TemporaryDirectory(prefix='kg-resource-pack-') as temporary:
        stage = Path(temporary)
        baker = stage / 'baker'
        subprocess.run([args.dotnet, 'build', str(ROOT / 'KuroakiGimmick.csproj'), '-c', 'Release',
            '-p:KuroakiShaderBaker=true', '-p:KuroakiExternalAssets=true',
            f'-p:BaseIntermediateOutputPath={stage}/obj/', f'-p:NuGetLockFilePath={stage}/packages.lock.json',
            '-o', str(baker)], cwd=ROOT, check=True)
        shutil.rmtree(baker / 'Assets', ignore_errors=True)
        shutil.copytree(assets, baker / 'Assets', ignore=shutil.ignore_patterns('SwitchShaders', 'MobileShaders'))
        destination = baker / 'Assets' / ('SwitchShaders' if args.platform == 'switch' else 'MobileShaders')
        command = ['--bake-switch-shaders'] if args.platform == 'switch' else ['--bake-mobile-shaders', args.platform]
        subprocess.run([args.dotnet, str(baker / 'KuroakiGimmick.dll'), *command, str(destination),
            *[str(x.resolve()) for x in args.shader_root]], check=True)
        output.parent.mkdir(parents=True, exist_ok=True)
        with zipfile.ZipFile(output, 'x', zipfile.ZIP_DEFLATED) as archive:
            for file in sorted((baker / 'Assets').rglob('*')):
                if file.is_file(): archive.write(file, file.relative_to(baker))
    print(f'Private {args.platform} resource pack: {output}')

if __name__ == '__main__': main()
