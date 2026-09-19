#!/usr/bin/env python3
"""Offline structural checks. This is explicitly NOT a C# or GLSL compiler.

Run from any directory. Source files, examples and declarations are read-only.
Native runtime tests and GPU regressions remain separate commands.
"""
from __future__ import annotations

import ast
import json
from pathlib import Path
import re
import shutil
import struct
import subprocess
import sys
import xml.etree.ElementTree as ET

from source_lexer import lex, top_types

ROOT = Path(__file__).resolve().parents[2]
COUNT = 0


def check(condition: bool, message: str) -> None:
    global COUNT
    if not condition:
        raise AssertionError(message)
    COUNT += 1


def read_json(path: Path):
    def invalid_constant(value):
        raise ValueError(f"Non-finite JSON constant in {path}: {value}")
    return json.loads(path.read_text(encoding="utf-8-sig"), parse_constant=invalid_constant)


def check_source() -> None:
    source_files = sorted(ROOT.glob("src/**/*.cs")) + sorted(ROOT.glob("tests/SelfTests/**/*.cs"))
    check(bool(source_files), "C# source files must exist")
    for path in source_files:
        text = path.read_text(encoding="utf-8-sig")
        tokens = lex(text)
        stack = []
        for token in tokens:
            if token.kind in {"literal", "comment", "directive", "ws"}:
                continue
            if token.value in {"{", "(", "[", "?["}:
                stack.append("[" if token.value == "?[" else token.value)
            elif token.value in {"}", ")", "]"}:
                wanted = {"}": "{", ")": "(", "]": "["}[token.value]
                check(bool(stack) and stack.pop() == wanted, f"Unbalanced delimiters in {path}")
        check(not stack, f"Unclosed delimiters in {path}")
        types = top_types(text)
        check(len(types) == 1, f"Expected one top-level type in {path}")
        check(types[0][0] == path.name.split(".")[0], f"Type/file name mismatch in {path}")
        check("\t" not in text, f"Tab indentation in {path}")
        if path.is_relative_to(ROOT / "src"):
            check(not re.search("astellion", text, re.IGNORECASE), f"Song-specific runtime implementation: {path}")
            object_literals = set(re.findall(r'"(obj_[A-Za-z0-9_]+)"', text))
            check(object_literals <= {"obj_base_gimmick", "obj_custom_gimmick"}, f"Hard-coded object identity outside the base/custom protocols: {path}")
    project = ET.parse(ROOT / "KuroakiGimmick.csproj").getroot()
    check(project.findtext(".//TargetFramework") == "net8.0", "Target must remain net8.0")
    check(project.findtext(".//EnableDefaultCompileItems") == "false", "Explicit compile roots required")
    compile_items = {part for node in project.findall(".//Compile") for part in node.attrib.get("Include", "").split(";")}
    check(compile_items == {"src/**/*.cs", "tests/SelfTests/**/*.cs"}, "Unexpected compile roots")
    check(not list(ROOT.glob("*.cs")), "No stale C# entry points in the root directory")
    print(f"PASS {len(source_files)} C# files: lexical structure, type layout, compile roots and runtime decoupling")


def check_definitions() -> None:
    # Check source inputs, not old app bundles, build caches or Finder sidecars.
    json_files = list(ROOT.glob("*.json"))
    for directory in ("Assets", "Samples", "tests", "docs", "Integrations"):
        json_files.extend((ROOT / directory).rglob("*.json"))
    json_files = [path for path in json_files if not path.name.startswith("._")]
    for path in json_files:
        read_json(path)
    print(f"PASS {len(json_files)} JSON files parsed")
    stages = {"before-rails", "before-playfield", "fixed-judgment", "gui"}
    definitions = list((ROOT / "Assets/Gimmicks").glob("*/manifest.json"))
    definitions += list((ROOT / "Samples").rglob("gimmick-object.json"))
    for path in definitions:
        definition = read_json(path)
        check(definition.get("version") == 1, f"Definition version: {path}")
        check(bool(definition.get("objectName")), f"Missing object name: {path}")
        if path.parent.parent == ROOT / "Assets/Gimmicks":
            check(definition["objectName"] == path.parent.name, f"Object directory mismatch: {path}")
        extra = definition.get("extraMods", [])
        check(len(extra) <= 127 and all(isinstance(name, str) and name for name in extra), f"Invalid extension IDs: {path}")
        for key in ("initialBpm", "fixedBpm"):
            value = definition.get(key)
            check(value is None or 0 < value <= 10000, f"Invalid BPM: {path}")
        sprites = definition.get("sprites", {})
        sprite_names = set(sprites) | set(definition.get("requiredSprites", {}))
        drawings = list(definition.get("overlays", []))
        for values in definition.get("callbacks", {}).values():
            check(bool(values), f"Empty callback: {path}")
            drawings.extend(values)
        if definition.get("background"):
            drawings.append(definition["background"]["drawing"])
        for drawing in drawings:
            check(drawing.get("stage", "gui") in stages, f"Invalid drawing stage: {path}")
            check(drawing.get("sprite") in sprite_names, f"Undeclared drawing sprite: {path}")
            check(0 < drawing.get("lifetime", 1) <= 60, f"Drawing lifetime: {path}")
        for key in definition.get("callbackConstraints", {}):
            check(key in definition.get("callbacks", {}), f"Constraint references unknown callback: {path}")
        for fade in definition.get("callbackFades", {}).values():
            check(fade["callback"] in definition.get("callbacks", {}), f"Fade references unknown callback: {path}")
        for shader in definition.get("shaders", {}).values():
            if definition.get("resourcePack"):
                # External song-owned shaders are validated when that pack is loaded.
                check(not Path(shader).is_absolute() and ".." not in Path(shader).parts, f"Unsafe external shader path: {path}")
                continue
            check((path.parent / shader).is_file(), f"Missing declared shader: {path.parent / shader}")
        modes = list(definition.get("postModes", {}).values())
        background = definition.get("background")
        if background and background.get("effect"):
            modes.append(background["effect"])
        for mode in modes:
            check(mode["shader"] in definition.get("shaders", {}), f"Undeclared shader binding: {path}")
            for sampler in mode.get("samplers", {}).values():
                check(sampler in sprite_names or sampler in definition.get("textures", {}), f"Undeclared sampler: {path}")
        for sprite in sprites.values():
            scope = sprite.get("scope")
            if scope == "pack" or scope is None and definition.get("resourcePack"):
                # User-owned external song resources are intentionally not bundled.
                continue
            base = ROOT / "Assets" if scope == "shared" else path.parent
            for frame in sprite["frames"]:
                image = base / frame
                check(image.is_file(), f"Missing sprite frame: {image}")
                if image.suffix.lower() == ".png":
                    header = image.read_bytes()[:24]
                    check(header[:8] == b"\x89PNG\r\n\x1a\n", f"Invalid PNG header: {image}")
                    width, height = struct.unpack(">II", header[16:24])
                    check((width, height) == (sprite["width"], sprite["height"]), f"Sprite contract dimensions: {image}")
    example = ROOT / "Samples/ReusableObject"
    sample = read_json(example / "gimmick-object.json")
    check(f"!obj:{sample['objectName']}" in (example / "ENCORE.vsm").read_text(), "Example VSM/object mismatch")
    check("v_vTexcoord" in (example / "tint.frag").read_text() and "gm_BaseTexture" in (example / "tint.frag").read_text(), "Example shader/Canvas interface mismatch")
    print(f"PASS {len(definitions)} object definitions: IDs, declared references, bundled shaders and sprite dimensions")


def check_scripts() -> None:
    python_files = list((ROOT / "scripts").rglob("*.py"))
    for path in python_files:
        ast.parse(path.read_text(encoding="utf-8-sig"), filename=str(path))
        check(True, f"Python parses: {path.name}")
    shell_files = list((ROOT / "scripts").rglob("*.sh")) + list((ROOT / "scripts").rglob("*.command"))
    bash = shutil.which("bash")
    if bash:
        for path in shell_files:
            subprocess.run([bash, "-n", str(path)], check=True, capture_output=True, text=True)
            check(True, f"Bash parses: {path.name}")
        print(f"PASS {len(python_files)} Python scripts and {len(shell_files)} Bash scripts parse")
    else:
        print(f"PASS {len(python_files)} Python scripts parse; SKIP Bash syntax (bash unavailable)")


def main() -> int:
    try:
        check_source()
        check_definitions()
        check_scripts()
    except (AssertionError, ValueError, OSError, KeyError, subprocess.CalledProcessError) as error:
        print(f"FAIL {error}", file=sys.stderr)
        return 1
    print(f"PASS {COUNT} low-level structural assertions. C#/GLSL compilation and runtime behavior NOT tested.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
