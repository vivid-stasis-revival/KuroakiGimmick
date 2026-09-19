#!/usr/bin/env python3
"""Read-only metadata/PNG/shader checks against an actual external resource ZIP.

Does not run the C# loader, compile GLSL, decode audio or render any frame.
The archive and game-owned files are never modified or added to the app package.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import math
from pathlib import Path, PurePosixPath
import re
import struct
import zipfile

ROOT = Path(__file__).resolve().parents[2]


def member_name(root: str, relative: str) -> str:
    path = PurePosixPath(relative)
    if not relative or path.is_absolute() or ".." in path.parts or "\\" in relative or ":" in relative:
        raise ValueError("Unsafe resource path: " + relative)
    return root + relative


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("definition", type=Path)
    parser.add_argument("pack", type=Path)
    args = parser.parse_args()
    definition = json.loads(args.definition.read_text(encoding="utf-8-sig"))
    required = definition["resourcePack"]
    frames = 0
    uniform_components = 0
    with zipfile.ZipFile(args.pack) as archive:
        candidates = [name for name in archive.namelist() if name == required or name.endswith("/" + required)]
        if len(candidates) != 1:
            raise ValueError("Expected exactly one resource manifest, found " + str(len(candidates)))
        location = candidates[0]
        root = location.rsplit("/", 1)[0] + "/"
        data = json.loads(archive.read(location))
        if data.get("version") != 1:
            raise ValueError("Expected v1 resource pack")
        sprites = data["sprites"]
        for name, expected in definition.get("requiredSprites", {}).items():
            sprite = sprites[name]
            if (sprite["width"], sprite["height"], len(sprite["frames"])) != (
                expected["width"], expected["height"], expected["frames"]
            ):
                raise ValueError("Sprite declaration differs: " + name)
            for relative in sprite["frames"]:
                header = archive.read(member_name(root, relative))[:24]
                if header[:8] != b"\x89PNG\r\n\x1a\n" or struct.unpack(">II", header[16:24]) != (sprite["width"], sprite["height"]):
                    raise ValueError("PNG header dimensions differ: " + relative)
                frames += 1
        parameters = data
        for part in definition.get("parameterObjectPath", "background.parameters").split("."):
            parameters = parameters[part]
        sources = {key: archive.read(member_name(root, relative)).decode("utf-8-sig")
                   for key, relative in definition.get("shaders", {}).items()}
        for key, source in sources.items():
            if not re.search(r"\bvoid\s+main\s*\(", source):
                raise ValueError("Shader entry point missing: " + key)
        modes = list(definition.get("postModes", {}).values())
        background_mode = definition.get("background", {}).get("effect")
        if background_mode:
            modes.append(background_mode)
        for mode in modes:
            declared = set(re.findall(r"\buniform\s+\w+\s+(\w+)", sources[mode["shader"]]))
            for name, values in mode.get("uniforms", {}).items():
                if name not in declared:
                    raise ValueError("Bound uniform not declared in source: " + name)
                for scalar in values:
                    if "parameter" in scalar:
                        value = parameters[scalar["parameter"]]
                        index = scalar.get("parameterIndex", -1)
                        if index >= 0:
                            value = value[index]
                        if not isinstance(value, (float, int)) or not math.isfinite(value):
                            raise ValueError("Non-numeric shader parameter: " + name)
                        minimum = scalar.get("parameterMinimumExclusive")
                        if minimum is not None and value <= minimum:
                            raise ValueError("Shader parameter below required minimum: " + name)
                    uniform_components += 1
            if not set(mode.get("samplers", {})).issubset(declared):
                raise ValueError("Bound sampler not declared in shader")
        resolved_textures = {}
        for key, texture in definition.get("textures", {}).items():
            candidates = texture.get("candidates", [])
            if texture.get("parameter"):
                parameter = parameters[texture["parameter"]]
                candidates = texture.get("aliases", {}).get(parameter, [{"scope": "pack", "path": parameter}])
            for item in candidates:
                relative = item["path"]
                if item.get("scope", "pack") == "pack":
                    path = member_name(root, relative)
                    exists = path in archive.namelist()
                elif item["scope"] == "shared":
                    member_name("", relative)
                    path = str(ROOT / "Assets" / relative)
                    exists = Path(path).is_file()
                else:
                    raise ValueError("This archive audit supports pack/shared texture scopes only")
                if exists:
                    resolved_textures[key] = path
                    break
            else:
                raise ValueError("No texture candidate exists: " + key)
    print(json.dumps({"status": "passed", "scope": "external archive metadata only",
                      "pack": args.pack.name, "packSha256": hashlib.sha256(args.pack.read_bytes()).hexdigest(),
                      "spriteGroups": len(definition.get("requiredSprites", {})), "pngFrames": frames,
                      "shaderFiles": len(sources), "uniformComponents": uniform_components,
                      "textures": resolved_textures, "csharpLoaderExecuted": False, "gpuExecuted": False},
                     ensure_ascii=False, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
