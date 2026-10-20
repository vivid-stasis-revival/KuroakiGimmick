#!/usr/bin/env python3
"""Compare native VSM repeat expansion in two source checkouts without Cargo dependencies.

Example:
  python3 scripts/large-charts/native-vsm-repeat-bench.py \
    --baseline /tmp/kg-baseline --candidate . --events 1000000 --samples 3 \
    --output-json /tmp/native-vsm-repeat.json

Each source can be a repository root or its rust/kg-native-core/src directory.
Only chart.rs and load_limits.rs are copied into temporary build directories.
Allocation counters add overhead to timings. Peak logical heap excludes allocator
metadata/fragmentation and transient realloc internals. RSS is separate, process-wide,
and available when os.wait4 is supported. This is a parser microbenchmark, not an app,
UI, or on-disk 1 GB chart measurement. Repeat counts expand into final ModEvents.
"""

import argparse
import hashlib
import json
import os
from pathlib import Path
import platform
import shutil
import subprocess
import sys
import tempfile
import time


def source_directory(path):
    path = Path(path).resolve()
    source = path if (path / "chart.rs").is_file() else path / "rust/kg-native-core/src"
    for name in ("chart.rs", "load_limits.rs"):
        if not (source / name).is_file():
            raise ValueError(f"missing source: {source / name}")
    return source


def run_sample(binary, events, timeout):
    command = [str(binary), str(events)]
    if not hasattr(os, "wait4"):
        result = subprocess.run(command, capture_output=True, text=True, check=True, timeout=timeout)
        return dict(json.loads(result.stdout), peak_rss_bytes=None)
    process = subprocess.Popen(command, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
    deadline = time.monotonic() + timeout
    while True:
        pid, status, usage = os.wait4(process.pid, os.WNOHANG)
        if pid:
            process.returncode = os.waitstatus_to_exitcode(status)
            break
        if time.monotonic() >= deadline:
            process.kill()
            process.communicate()
            raise TimeoutError(f"benchmark exceeded {timeout} seconds")
        time.sleep(0.01)
    stdout, stderr = process.communicate()
    if process.returncode:
        raise RuntimeError(f"benchmark failed ({process.returncode}): {stderr}")
    # Linux/BSD report KiB; macOS reports bytes.
    rss_bytes = usage.ru_maxrss * (1 if sys.platform == "darwin" else 1024)
    return dict(json.loads(stdout), peak_rss_bytes=rss_bytes)


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--baseline", required=True, type=source_directory)
    parser.add_argument("--candidate", required=True, type=source_directory)
    parser.add_argument("--events", type=int, default=1_000_000)
    parser.add_argument("--samples", type=int, default=3)
    parser.add_argument("--rustc", default="rustc")
    parser.add_argument("--timeout-seconds", type=float, default=120)
    parser.add_argument("--output-json", type=Path)
    args = parser.parse_args()
    if min(args.events, args.samples, args.timeout_seconds) <= 0:
        parser.error("events, samples and timeout must be positive")
    report = {
        "scenario": "one VSM repeat row, unlimited mode",
        "measurement_scope": "isolated parser with counting-allocator overhead; not whole-app latency",
        "platform": platform.platform(),
        "rustc": subprocess.check_output([args.rustc, "--version"], text=True).strip(),
        "events": args.events,
        "sources": {},
        "samples": [],
    }
    harness = Path(__file__).with_suffix(".rs")
    with tempfile.TemporaryDirectory(prefix="kg-vsm-repeat-") as temp:
        binaries = {}
        for mode in ("baseline", "candidate"):
            source = getattr(args, mode)
            build = Path(temp) / mode
            build.mkdir()
            for name in ("chart.rs", "load_limits.rs"):
                shutil.copyfile(source / name, build / name)
            shutil.copyfile(harness, build / "main.rs")
            report["sources"][mode] = {
                "path": str(source),
                "chart_sha256": hashlib.sha256((source / "chart.rs").read_bytes()).hexdigest(),
                "load_limits_sha256": hashlib.sha256((source / "load_limits.rs").read_bytes()).hexdigest(),
            }
            binary = build / ("bench.exe" if os.name == "nt" else "bench")
            subprocess.run([args.rustc, "--edition=2021", "-C", "opt-level=3", str(build / "main.rs"), "-o", str(binary)], check=True, timeout=args.timeout_seconds)
            binaries[mode] = binary
        for sample in range(1, args.samples + 1):
            for mode, binary in binaries.items():
                result = dict(run_sample(binary, args.events, args.timeout_seconds), mode=mode, sample=sample)
                report["samples"].append(result)
                print(json.dumps(result, sort_keys=True), flush=True)
    if args.output_json:
        args.output_json.parent.mkdir(parents=True, exist_ok=True)
        args.output_json.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")


if __name__ == "__main__":
    main()
