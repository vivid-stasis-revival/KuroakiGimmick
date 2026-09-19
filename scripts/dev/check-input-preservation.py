#!/usr/bin/env python3
"""Verify retained SDL_GPU implementation tokens and input resource bytes.

This is a lexical/source-data contract, NOT C# compilation or a GPU runtime test.
Without --baseline, use the reviewed input fingerprints stored in tests/Fixtures.
With --baseline, recompute fingerprints from the actual uploaded source ZIP.
Only whitespace/comments, splitting partial classes and explicit braces around
single controlled statements are normalized. Shader literals and ABI fields are
included exactly; expression/operator/constant changes fail the comparison.
"""
from __future__ import annotations

import argparse
from collections import Counter, defaultdict
import hashlib
import json
from pathlib import Path
import sys
import zipfile

from source_lexer import lex, top_types

ROOT = Path(__file__).resolve().parents[2]
FIXTURE = ROOT / "tests/Fixtures/input-sdl-gpu-contract.json"
SOURCE_PATHS = [
    "Graphics/Canvas.cs", "Graphics/GpuDevice.cs", "Graphics/Shader.cs",
    "Graphics/ShaderCompiler.cs", "Graphics/Texture.cs", "Native/Host.cs",
    "Native/Sdl.cs", "Native/Sdl.Gpu.cs", "Native/ShaderTools.cs", "Core/GpuSelfTest.cs",
]
ALLOWED_ASSET_CHANGES = {"Assets/Gimmicks/obj_scarletdeath_gimmick/manifest.json"}


def code_tokens(source: str):
    return [token for token in lex(source) if token.kind not in {"ws", "comment", "directive"}]


def explicit_control_bodies(source: str) -> str:
    """Normalize omitted control braces without moving/deleting any expression."""
    tokens = code_tokens(source)
    pairs, stack = {}, []
    for index, token in enumerate(tokens):
        if token.value in {"(", "{", "[", "?["}:
            stack.append(index)
        elif token.value in {")", "}", "]"}:
            pairs[stack.pop()] = index

    def end_statement(index: int) -> int:
        value = tokens[index].value
        if value == "{":
            return pairs[index] + 1
        if value == "if":
            end = end_statement(pairs[index + 1] + 1)
            return end_statement(end + 1) if end < len(tokens) and tokens[end].value == "else" else end
        if value in {"for", "foreach", "while", "using", "fixed", "lock"} and tokens[index + 1].value == "(":
            return end_statement(pairs[index + 1] + 1)
        while index < len(tokens):
            if tokens[index].value == ";":
                return index + 1
            index = pairs[index] + 1 if tokens[index].value in {"(", "{", "[", "?["} else index + 1
        raise ValueError("Unsupported controlled statement")

    additions = defaultdict(list)
    for index, token in enumerate(tokens):
        body = None
        if token.value in {"if", "for", "foreach", "while", "using", "fixed", "lock"} and index + 1 < len(tokens) and tokens[index + 1].value == "(":
            body = pairs[index + 1] + 1
        elif token.value == "else" and tokens[index + 1].value not in {"{", "if"}:
            body = index + 1
        if body is not None and tokens[body].value not in {"{", ";"}:
            end = end_statement(body)
            additions[tokens[body].start].append("{ ")
            additions[tokens[end - 1].end].append(" }")
    for offset, pieces in sorted(additions.items(), reverse=True):
        source = source[:offset] + "".join(pieces) + source[offset:]
    return source


def member_hashes(declaration: str) -> list[str]:
    """Fingerprint independent top-level members, keeping nested bodies intact."""
    source = explicit_control_bodies(declaration)
    tokens = code_tokens(source)
    start = next(index for index, token in enumerate(tokens) if token.value == "{")
    pairs, stack = {}, []
    for index, token in enumerate(tokens):
        if token.value in {"(", "{", "[", "?["}:
            stack.append(index)
        elif token.value in {")", "}", "]"}:
            pairs[stack.pop()] = index
    outer_end = pairs[start]
    if any(token.value == "enum" for token in tokens[:start]):
        groups = [tokens[start + 1:outer_end]]
    else:
        groups, index = [], start + 1
        while index < outer_end:
            begin, expression = index, False
            while index < outer_end:
                value = tokens[index].value
                if value in {"=", "=>"}:
                    expression = True
                if value == ";":
                    index += 1
                    break
                if value in {"(", "[", "?["}:
                    index = pairs[index] + 1
                    continue
                if value == "{":
                    index = pairs[index] + 1
                    if expression:
                        continue
                    if index < outer_end and tokens[index].value == "=":
                        expression = True
                        continue
                    if index < outer_end and tokens[index].value == ";":
                        index += 1
                    break
                index += 1
            groups.append(tokens[begin:index])
    return [hashlib.sha256(json.dumps([(token.kind, token.value) for token in group], ensure_ascii=False).encode()).hexdigest() for group in groups]


def add_types(destination: dict, source: str) -> None:
    for name, start, end in top_types(source):
        destination.setdefault(name, []).extend(member_hashes(source[start:end]))


def baseline_from_zip(path: Path) -> dict:
    with zipfile.ZipFile(path) as archive:
        names = archive.namelist()
        projects = [name for name in names if name.endswith("KuroakiGimmick.csproj")]
        if len(projects) != 1:
            raise ValueError("Expected exactly one root KuroakiGimmick.csproj")
        prefix = projects[0][:-len("KuroakiGimmick.csproj")]
        types = {}
        for relative in SOURCE_PATHS:
            add_types(types, archive.read(prefix + relative).decode("utf-8-sig"))
        assets = {name[len(prefix):]: hashlib.sha256(archive.read(name)).hexdigest()
                  for name in names if name.startswith(prefix + "Assets/") and not name.endswith("/")}
        return {"baselineZip": path.name, "baselineSha256": hashlib.sha256(path.read_bytes()).hexdigest(),
                "sourceContract": "Exact member tokens after normalizing control braces and ignoring trivia/type wrappers",
                "members": {key: sorted(value) for key, value in types.items()}, "assets": assets,
                "packagesLockSha256": hashlib.sha256(archive.read(prefix + "packages.lock.json")).hexdigest()}


def verify(contract: dict) -> dict:
    actual = {}
    paths = list((ROOT / "src/Graphics/SdlGpu").glob("*.cs"))
    paths += list((ROOT / "src/Graphics/Primitives").glob("*.cs"))
    paths += list((ROOT / "src/Native").glob("*.cs"))
    paths.append(ROOT / "tests/SelfTests/GpuSelfTest.cs")
    for path in paths:
        add_types(actual, path.read_text(encoding="utf-8-sig"))
    if set(actual) != set(contract["members"]):
        raise AssertionError(f"Changed backend type set: {set(actual) ^ set(contract['members'])}")
    for name, expected in contract["members"].items():
        if Counter(actual[name]) != Counter(expected):
            raise AssertionError(f"Changed SDL_GPU member expressions/ABI/literal in {name}: "
                                 f"missing {sum((Counter(expected)-Counter(actual[name])).values())}, "
                                 f"extra {sum((Counter(actual[name])-Counter(expected)).values())}")
    retained = 0
    for relative, expected in contract["assets"].items():
        path = ROOT / relative
        if not path.is_file():
            raise AssertionError("Removed input asset: " + relative)
        if relative in ALLOWED_ASSET_CHANGES:
            continue
        if hashlib.sha256(path.read_bytes()).hexdigest() != expected:
            raise AssertionError("Changed protected input asset: " + relative)
        retained += 1
    if hashlib.sha256((ROOT / "packages.lock.json").read_bytes()).hexdigest() != contract["packagesLockSha256"]:
        raise AssertionError("Dependency lock differs from SDL_GPU input")
    return {"status": "passed", "baselineSha256": contract["baselineSha256"],
            "backendTypes": len(actual), "backendMembers": sum(map(len, actual.values())),
            "unchangedInputAssets": retained, "allowedMetadataChanges": sorted(ALLOWED_ASSET_CHANGES),
            "compiled": False, "gpuExecuted": False}


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--baseline", type=Path)
    parser.add_argument("--write-fixture", action="store_true", help="Requires --baseline; records expected input, never candidate hashes")
    parser.add_argument("--json", type=Path)
    args = parser.parse_args()
    try:
        contract = baseline_from_zip(args.baseline) if args.baseline else json.loads(FIXTURE.read_text())
        if args.write_fixture:
            if not args.baseline:
                raise ValueError("--write-fixture requires the actual input ZIP")
            FIXTURE.write_text(json.dumps(contract, indent=2, ensure_ascii=False) + "\n")
        result = verify(contract)
        print(json.dumps(result, indent=2, ensure_ascii=False))
        if args.json:
            args.json.parent.mkdir(parents=True, exist_ok=True)
            args.json.write_text(json.dumps(result, indent=2, ensure_ascii=False) + "\n")
        return 0
    except (OSError, ValueError, AssertionError, KeyError, zipfile.BadZipFile) as error:
        print("FAIL " + str(error), file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
