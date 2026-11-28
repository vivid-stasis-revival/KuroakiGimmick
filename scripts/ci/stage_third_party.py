#!/usr/bin/env python3
"""Copy only distributable ThirdParty files into a release staging directory.

KenomaGMK is a Git submodule and must not be shipped as source, and build
outputs from any third-party checkout must not leak into the package.
Licenses from the submodule are copied separately as KenomaGMK-licenses.
"""

from __future__ import annotations

import shutil
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
IGNORE = shutil.ignore_patterns("bin", "obj", "target", ".git", ".vs", "TestResults")


def main() -> None:
    if len(sys.argv) != 2:
        raise SystemExit("Usage: stage_third_party.py <destination>")
    source = ROOT / "ThirdParty"
    destination = Path(sys.argv[1]).resolve()
    destination.mkdir(parents=True, exist_ok=True)
    for entry in sorted(source.iterdir()):
        if entry.name == "KenomaGMK":
            continue
        target = destination / entry.name
        if entry.is_dir():
            shutil.copytree(entry, target, ignore=IGNORE, symlinks=True)
        else:
            shutil.copy2(entry, target)
    licenses = source / "KenomaGMK" / "licenses"
    if licenses.is_dir():
        shutil.copytree(licenses, destination / "KenomaGMK-licenses", symlinks=True)
    if (destination / "KenomaGMK").exists():
        raise SystemExit("Refusing to ship the KenomaGMK submodule source")
    print(f"Staged third-party notices: {destination}")


if __name__ == "__main__":
    main()
