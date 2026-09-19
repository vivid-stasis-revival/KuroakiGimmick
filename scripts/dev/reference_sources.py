"""Locate original documentation bytes without depending on legacy ZIP filename decoding.
Normal application builds only consume vsm-reference.json. This is for offline
maintainer checks; no source is rewritten and content mismatches remain errors.
"""
from __future__ import annotations
import hashlib
import json
from pathlib import Path


def read_source(directory: Path, name: str, expected_hash: str | None = None) -> bytes:
    direct = directory / name
    if direct.is_file():
        return direct.read_bytes()
    # Several pre-16.2 archives stored the same verified original bytes with a
    # CP437/UTF-8-misdecoded filename. Match the digest, never a similar title.
    if expected_hash is None:
        index = directory / 'vsm-reference.json'
        if index.is_file():
            data = json.loads(index.read_text(encoding='utf-8'))
            expected_hash = next((s['Sha256'] for s in data['Sources'] if s['File'] == name), None)
    if expected_hash:
        matches = []
        for path in directory.glob('*.md'):
            if path.stat().st_size > 8 * 1024 * 1024:
                continue
            raw = path.read_bytes()
            if hashlib.sha256(raw).hexdigest() == expected_hash:
                matches.append(raw)
        if matches and all(raw == matches[0] for raw in matches):
            return matches[0]
    raise FileNotFoundError('Original documentation is missing or has changed: ' + name)
