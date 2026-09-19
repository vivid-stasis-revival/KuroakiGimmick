#!/usr/bin/env python3
"""Render identical chart times with two actual application builds.

Writes paired PPMs, amplified difference images, candidate diagnostics and a
machine-readable summary into a NEW output directory. Never edits the chart.
The baseline may be the user's pre-refactor SDL_GPU build or an older reference.
Requires a supported local GPU. A saved screenshot alone is not a passing test.
"""
from __future__ import annotations

import argparse
import json
import math
from pathlib import Path
import shutil
import subprocess
import sys


def invoke(dotnet: str, assembly: Path, arguments: list[str]) -> subprocess.CompletedProcess:
    result = subprocess.run([dotnet, str(assembly), *arguments], cwd=assembly.parent,
                            capture_output=True, text=True, encoding="utf-8", errors="replace")
    return result


def read_ppm(path: Path) -> tuple[int, int, bytes]:
    # Canvas.SavePpm writes this exact uncompressed P6 layout.
    header = path.read_bytes().split(b"\n", 3)
    if len(header) != 4 or header[0] != b"P6" or header[2] != b"255":
        raise ValueError("Unexpected PPM format: " + str(path))
    width, height = map(int, header[1].split())
    if width <= 0 or height <= 0 or len(header[3]) != width * height * 3:
        raise ValueError("Invalid pixel length: " + str(path))
    return width, height, header[3]


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("baseline", type=Path)
    parser.add_argument("candidate", type=Path)
    parser.add_argument("chart", type=Path)
    parser.add_argument("--times", type=float, nargs="+", required=True)
    parser.add_argument("--out", type=Path, required=True)
    parser.add_argument("--dotnet", default="dotnet")
    parser.add_argument("--max-error", type=int, default=4)
    parser.add_argument("--mean-error", type=float, default=.1)
    args = parser.parse_args()
    output = args.out.resolve()
    results = []
    output_created = False
    try:
        assemblies = [args.baseline.resolve(), args.candidate.resolve()]
        chart = args.chart.resolve()
        if not all(path.is_file() for path in assemblies) or not chart.exists():
            raise FileNotFoundError("Both compiled DLLs and the chart/project must exist")
        if shutil.which(args.dotnet) is None:
            raise FileNotFoundError("dotnet is required; no renderer was executed")
        if any(not math.isfinite(time) or time < 0 for time in args.times):
            raise ValueError("Times must be finite nonnegative seconds")
        if args.max_error < 0 or args.mean_error < 0 or not math.isfinite(args.mean_error):
            raise ValueError("Pixel tolerances must be finite and nonnegative")
        output.mkdir(parents=True, exist_ok=False)
        output_created = True
        for index, time in enumerate(args.times):
            paths = []
            for label, assembly in zip(("baseline", "candidate"), assemblies):
                frame = output / f"{index:03d}-{time:.6f}-{label}.ppm"
                command = ["--snapshot", str(chart), "--scene", "--time", repr(time),
                           "--render-width", "320", "--note-alignment", "top", "--out", str(frame)]
                if label == "candidate":
                    command += ["--strict", "--report", str(frame.with_suffix(".report.json"))]
                run = invoke(args.dotnet, assembly, command)
                frame.with_suffix(".log").write_text(run.stdout + "\n" + run.stderr, encoding="utf-8")
                if run.returncode:
                    raise RuntimeError(f"{label} exited {run.returncode}; see {frame.with_suffix('.log')}")
                paths.append(frame)
            a_width, a_height, before = read_ppm(paths[0])
            b_width, b_height, after = read_ppm(paths[1])
            if (a_width, a_height) != (b_width, b_height):
                raise AssertionError("Scene dimensions differ at " + repr(time))
            differences = [abs(left - right) for left, right in zip(before, after)]
            maximum, mean = max(differences), sum(differences) / len(differences)
            passed = maximum <= args.max_error and mean <= args.mean_error
            report = json.loads(paths[1].with_suffix(".report.json").read_text(encoding="utf-8"))
            errors = [item for item in report.get("diagnostics", []) if item.get("Error")]
            passed = passed and not errors
            diff_path = output / f"{index:03d}-{time:.6f}-difference-x16.ppm"
            diff_path.write_bytes(f"P6\n{a_width} {a_height}\n255\n".encode("ascii") +
                                  bytes(min(255, value * 16) for value in differences))
            results.append({"seconds": time, "passed": passed, "maxChannelError": maximum,
                            "meanChannelError": mean, "errorDiagnostics": errors})
            print(f"{'PASS' if passed else 'FAIL'} {time:.6f}s: max={maximum}, mean={mean:.6f}", flush=True)
        success = bool(results) and all(result["passed"] for result in results)
        summary = {"status": "passed" if success else "failed", "baseline": str(assemblies[0]),
                   "candidate": str(assemblies[1]), "chart": str(chart), "frames": results}
        (output / "comparison.json").write_text(json.dumps(summary, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
        return 0 if success else 1
    except (OSError, ValueError, RuntimeError, AssertionError) as error:
        print("FAIL " + str(error), file=sys.stderr)
        # Only touch an output directory created by this invocation.
        if output_created:
            (output / "comparison.failed.json").write_text(
                json.dumps({"status": "failed", "error": str(error), "completedFrames": results}, indent=2) + "\n", encoding="utf-8")
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
