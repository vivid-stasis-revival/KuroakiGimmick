#!/usr/bin/env python3
"""Stage only tracked application branding/tips; never copy private game resources."""
import argparse
from pathlib import Path
import shutil
import subprocess

ROOT = Path(__file__).resolve().parents[2]
ALLOWED = {'Assets/App/Kuroaki.png', 'Assets/App/Kuroaki.ico', 'Assets/App/Kuroaki.icns', 'Assets/tips.json'}

def stage(destination):
    destination = destination.resolve()
    if destination.exists():
        raise ValueError('Choose a new staging directory')
    tracked = set(subprocess.check_output(['git', 'ls-files', '-z', '--', 'Assets'], cwd=ROOT).decode().split('\0'))
    destination.mkdir(parents=True)
    for name in sorted(ALLOWED & tracked):
        target = destination / Path(name).relative_to('Assets')
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(ROOT / name, target)
    if not (destination / 'App/Kuroaki.png').is_file():
        raise ValueError('Tracked application icon is missing')
    print('Staged public application files only; users supply game assets separately.')

if __name__ == '__main__':
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('destination', type=Path)
    stage(p.parse_args().destination)
