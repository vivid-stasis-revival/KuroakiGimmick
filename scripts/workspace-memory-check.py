#!/usr/bin/env python3
"""Run one reproduction under a resident-memory/time budget; stop only its process group."""
import argparse
import json
import os
import signal
import subprocess
import time

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("--log", required=True)
parser.add_argument("--seconds", type=float, default=60)
parser.add_argument("--rss-gib", type=float, default=3)
parser.add_argument("command", nargs=argparse.REMAINDER)
args = parser.parse_args()
command = args.command[1:] if args.command[:1] == ["--"] else args.command
if not command or not 0 < args.seconds <= 600 or not 0 < args.rss_gib <= 8:
    parser.error("supply a command, 0 < seconds <= 600 and 0 < rss-gib <= 8")

started = time.monotonic()
peak = 0
reason = ""
with open(args.log, "w") as log, open(args.log + ".memory.jsonl", "w") as memory:
    process = subprocess.Popen(command, stdout=log, stderr=subprocess.STDOUT, start_new_session=True)
    try:
        while process.poll() is None:
            row = subprocess.run(["ps", "-o", "rss=", "-p", str(process.pid)],
                                 capture_output=True, text=True).stdout.strip()
            rss = int(row) * 1024 if row else 0
            peak = max(peak, rss)
            elapsed = time.monotonic() - started
            memory.write(json.dumps({"seconds": elapsed, "rss": rss}) + "\n")
            memory.flush()
            if rss > args.rss_gib * 1024**3 or elapsed > args.seconds:
                reason = "RSS budget exceeded" if rss > args.rss_gib * 1024**3 else "time budget exceeded"
                os.killpg(process.pid, signal.SIGKILL)
                break
            time.sleep(.1)
    finally:
        if process.poll() is None:
            os.killpg(process.pid, signal.SIGKILL)
        process.wait()
result = {"pid": process.pid, "exit": process.returncode, "peakRss": peak,
          "seconds": time.monotonic() - started, "stopReason": reason}
print(json.dumps(result))
with open(args.log + ".result.json", "w") as report:
    json.dump(result, report, indent=2)
raise SystemExit(137 if reason.startswith("RSS") else 124 if reason else process.returncode)
