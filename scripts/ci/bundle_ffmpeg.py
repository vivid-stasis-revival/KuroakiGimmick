#!/usr/bin/env python3
"""Bundle a pinned, checksummed FFmpeg binary in desktop CI releases.

Only FFmpeg's CLI binary is bundled. KuroakiGimmick already launches it as a
child process, so no additional libav DLL/dylib/so dependencies are necessary.
Never package whichever ffmpeg happens to be installed on the runner.
"""
from __future__ import annotations

import argparse
import hashlib
import os
from pathlib import Path, PurePosixPath
import shutil
import stat
import subprocess
import sys
import tarfile
import tempfile
import urllib.request
import zipfile


# BtbN's 2026-09-30 build is the retained month-end snapshot.
# The macOS binary is separately built for Apple Silicon and includes libx264.
# Archive hashes are from the corresponding GitHub Release asset metadata.
SOURCES = {
    "win-x64": {
        "url": "https://github.com/BtbN/FFmpeg-Builds/releases/download/autobuild-2026-09-30-13-08/ffmpeg-n8.1.3-9-g29e619e767-win64-gpl-8.1.zip",
        "sha256": "7b801cdd3a1a0bb54ae6f572187e68b4ed54f52086cfee133fab2180bfb429fa",
        "archive": "zip",
        "executable": "ffmpeg.exe",
    },
    "osx-arm64": {
        "url": "https://github.com/vanloctech/ffmpeg-macos/releases/download/ffmpeg-2026.06.11/ffmpeg-macos-arm64.tar.gz",
        "sha256": "26f5133f88d5ab1254cc03499acef42dae2dbc574dd26e0dade8c968420127e9",
        "archive": "tar.gz",
        "executable": "ffmpeg",
    },
    "linux-x64": {
        "url": "https://github.com/BtbN/FFmpeg-Builds/releases/download/autobuild-2026-09-30-13-08/ffmpeg-n8.1.3-9-g29e619e767-linux64-gpl-8.1.tar.xz",
        "sha256": "97ce978979194b5cf7e06a5e68020dbdaa7a4f3294c5452b6a1bc347100dbb79",
        "archive": "tar.xz",
        "executable": "ffmpeg",
    },
}

MAX_DOWNLOAD_BYTES = 250 * 1024 * 1024
MAX_BINARY_BYTES = 400 * 1024 * 1024


def download_verified(url: str, sha256: str, destination: Path) -> None:
    request = urllib.request.Request(url, headers={"User-Agent": "KuroakiGimmick-CI-FFmpeg/1.0"})
    digest = hashlib.sha256()
    received = 0
    with urllib.request.urlopen(request, timeout=90) as response, destination.open("wb") as output:
        while chunk := response.read(1024 * 1024):
            received += len(chunk)
            if received > MAX_DOWNLOAD_BYTES:
                raise RuntimeError("FFmpeg archive exceeded the allowed download size")
            output.write(chunk)
            digest.update(chunk)
    if digest.hexdigest() != sha256:
        raise RuntimeError(f"FFmpeg SHA-256 mismatch: got {digest.hexdigest()}, expected {sha256}")
    print(f"Verified FFmpeg archive: {received:,} bytes, SHA-256 {sha256}", flush=True)


def extract_exact_executable(archive: Path, archive_type: str, executable: str, destination: Path) -> None:
    if archive_type == "zip":
        with zipfile.ZipFile(archive) as bundle:
            candidates = [
                entry for entry in bundle.infolist()
                if not entry.is_dir() and PurePosixPath(entry.filename).name == executable
            ]
            if len(candidates) != 1:
                raise RuntimeError(f"Expected one {executable} in FFmpeg ZIP; found {len(candidates)}")
            entry = candidates[0]
            mode = entry.external_attr >> 16
            if stat.S_ISLNK(mode):
                raise RuntimeError("Refusing symlink executable in FFmpeg ZIP")
            if entry.file_size > MAX_BINARY_BYTES:
                raise RuntimeError("FFmpeg executable exceeded the allowed size")
            with bundle.open(entry) as source, destination.open("wb") as output:
                shutil.copyfileobj(source, output)
    else:
        with tarfile.open(archive, "r:gz" if archive_type == "tar.gz" else "r:xz") as bundle:
            candidates = [
                entry for entry in bundle.getmembers()
                if entry.isfile() and PurePosixPath(entry.name).name == executable
            ]
            if len(candidates) != 1:
                raise RuntimeError(f"Expected one {executable} in FFmpeg TAR; found {len(candidates)}")
            entry = candidates[0]
            if entry.size > MAX_BINARY_BYTES:
                raise RuntimeError("FFmpeg executable exceeded the allowed size")
            source = bundle.extractfile(entry)
            if source is None:
                raise RuntimeError("Could not read FFmpeg executable from archive")
            with source, destination.open("wb") as output:
                shutil.copyfileobj(source, output)
    if destination.stat().st_size < 1024 * 1024:
        raise RuntimeError("Bundled FFmpeg is unexpectedly small")
    if os.name != "nt":
        destination.chmod(destination.stat().st_mode | 0o755)


def verify_macos_linkage(executable: Path) -> None:
    if sys.platform != "darwin":
        return
    links = subprocess.run(["otool", "-L", str(executable)], check=True, capture_output=True, text=True).stdout
    for line in links.splitlines()[1:]:
        library = line.strip().split(" (", 1)[0]
        if not (library.startswith("/usr/lib/") or library.startswith("/System/Library/")):
            raise RuntimeError(f"macOS FFmpeg depends on a non-system library: {library}")


def verify_codec_smoke_test(executable: Path, workdir: Path) -> None:
    version = subprocess.run(
        [str(executable), "-hide_banner", "-version"],
        check=True, capture_output=True, text=True, timeout=20,
    )
    print(version.stdout.splitlines()[0], flush=True)
    probe = workdir / "encode-smoke.mp4"
    # K/G requires both the libx264 video encoder and the native AAC encoder.
    # Encode an actual tiny MP4, not just a version/codec-list probe.
    command = [
        str(executable), "-hide_banner", "-loglevel", "error",
        "-f", "lavfi", "-i", "color=c=black:s=32x32:r=10",
        "-f", "lavfi", "-i", "anullsrc=r=48000:cl=stereo",
        "-t", "0.2", "-c:v", "libx264", "-pix_fmt", "yuv420p",
        "-c:a", "aac", "-movflags", "+faststart", "-y", str(probe),
    ]
    test = subprocess.run(command, capture_output=True, text=True, timeout=75)
    if test.returncode or not probe.exists() or probe.stat().st_size < 100:
        raise RuntimeError(f"FFmpeg H.264/AAC smoke test failed ({test.returncode}):\n{test.stderr}")
    print(f"FFmpeg H.264/AAC encoding test passed: {probe.stat().st_size} bytes", flush=True)


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("rid", choices=sorted(SOURCES))
    parser.add_argument("publish_directory", type=Path)
    args = parser.parse_args()
    package = SOURCES[args.rid]
    destination = args.publish_directory.resolve()
    if not destination.is_dir():
        raise RuntimeError(f"Publish directory does not exist: {destination}")
    executable = destination / package["executable"]
    if executable.exists():
        raise RuntimeError(f"Refusing to replace an existing packaged executable: {executable}")
    with tempfile.TemporaryDirectory(prefix="kg-ffmpeg-ci-") as stage:
        staging = Path(stage)
        archive = staging / ("ffmpeg." + package["archive"].replace(".", "-"))
        download_verified(package["url"], package["sha256"], archive)
        extract_exact_executable(archive, package["archive"], package["executable"], executable)
        verify_macos_linkage(executable)
        verify_codec_smoke_test(executable, staging)
    (destination / "FFMPEG-BUILDINFO.txt").write_text(
        "FFmpeg is a separate GPL-licensed executable bundled for desktop exports.\n"
        "Binary source: " + package["url"] + "\n"
        "Archive SHA-256: " + package["sha256"] + "\n"
        "License notices: THIRD_PARTY_NOTICES.md and ThirdParty/FFmpeg-*.txt\n"
        "Source/build scripts: https://github.com/BtbN/FFmpeg-Builds "
        "and https://github.com/vanloctech/ffmpeg-macos\n",
        encoding="utf-8",
    )
    print(f"Bundled tested FFmpeg: {executable}", flush=True)


if __name__ == "__main__":
    main()
