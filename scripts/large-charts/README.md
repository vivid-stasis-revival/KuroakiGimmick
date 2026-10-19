# Large-chart headless benchmarks

This standalone .NET 8 runner exercises the public APIs in either checkout. It does not
launch SDL, read personal charts unless `--input` names one, or change application
preferences. Reflection enters the existing internal load-limit scope and, for the
session case, installs explicit in-memory engine defaults without loading saved
preferences. Timed operations use public APIs. `--unlimited` is explicit and scoped to
this process.

## Build the same runner against either revision

From the candidate repository:

```sh
# Set these to clean, separate checkouts. Do not reset a working repository.
BASE=/absolute/path/to/base-checkout
CURRENT=/absolute/path/to/candidate-checkout
HARNESS="$CURRENT/scripts/large-charts/LargeChartBench.csproj"

# Use different artifact roots so assembly references and restore state cannot mix.
dotnet build "$HARNESS" -c Release -p:KuroakiRepo="$BASE" \
  -p:PublishAot=false -p:PublishSingleFile=false --artifacts-path /tmp/kg-bench-base
dotnet build "$HARNESS" -c Release -p:KuroakiRepo="$CURRENT" \
  -p:PublishAot=false -p:PublishSingleFile=false --artifacts-path /tmp/kg-bench-current

BASE_RUN=/tmp/kg-bench-base/bin/LargeChartBench/release/LargeChartBench.dll
CURRENT_RUN=/tmp/kg-bench-current/bin/LargeChartBench/release/LargeChartBench.dll
```

Use an installed .NET 8 SDK. Native parser experiments additionally require the matching
`kg_native_core` dynamic library discoverable by that process (for example,
`LD_LIBRARY_PATH` on Linux or `DYLD_LIBRARY_PATH` on macOS). Build the Rust library from
each respective checkout; do not compare a candidate C# assembly with the baseline
native library. The default `--engine off` forces the managed parser and does not need
a native library. `required` fails rather than silently falling back. `auto` may fall
back and is therefore insufficient evidence that a native path ran.

## Small and dense workloads

Run one process at a time. The default is 10,000 records and 1,000 operations:

```sh
dotnet "$CURRENT_RUN" interval --count 10000 --label current
dotnet "$CURRENT_RUN" vsm-editor --count 10000 --operations 1000 --label current
dotnet "$CURRENT_RUN" editor-session --count 10000 --label current
dotnet "$CURRENT_RUN" vsm-load --count 10000 --label current
dotnet "$CURRENT_RUN" vsc-load --count 10000 --label current
```

Repeat each case for `--count 100000` and `--count 1000000`, using both assemblies and
saving separate `--json /absolute/path/result.json` reports. Use multiple fresh processes
for useful timing distributions; a single timing is not a P95 or a guarantee.

- `interval`: construction of sorted overlapping intervals, followed by deterministic
  queries. Every 64th interval crosses 4,096 time units; equal starts preserve order.
- `vsm-editor`: file loading, first ID lookup, warm lookup/count operations, then
  in-memory replacement of up to `--operations` evenly distributed clips. Fixtures use
  a late proxy declaration, exposing full-document scans inside bulk replacement.
- `vsm-load`: runtime `VsmReader.ReplaceMods` loading.
- `editor-session`: actual headless `Session` initialization from the VSM, followed by
  `new EditorDocument(session)`, then clean/history-state, source-line, editable-event and
  runtime-event checks. The project uses the core profile, disabled game UI, no audio,
  no reference chart and no GPU. The checksum streams existing source partitions;
  the harness never calls whole-document `Text`. This includes the editor's saved
  source snapshot, unlike `vsm-editor`. Normal read-only companion-file discovery
  still applies, so use an isolated fixture directory for reproducible comparisons.
- `vsc-load`: `VscReader.Load` loading; every 16th record is a Hold and four records share
  each start time. No generated records are dropped for performance.

The fixed 1,000 operations default deliberately avoids making the baseline execute
one trillion scans for a million-record batch. Use `--operations 1000000` only when
that full operation count is intended. Query and lookup checksums/counts should match
between revisions; the final VSM editor checksum includes the same deterministic
replacements, whitespace and newline preservation. Record GUIDs are excluded because
parsing assigns new GUIDs. VSM/VSC runtime checksums include order, timing, metadata and
diagnostics. Generated loads fail if the returned record count differs from the request.

To reuse identical bytes instead of generating a temporary fixture for every run:

```sh
dotnet "$CURRENT_RUN" generate-vsm --count 100000 --fixture /tmp/kg-100k.vsm
dotnet "$BASE_RUN" vsm-editor --input /tmp/kg-100k.vsm --json /tmp/kg-base.json
dotnet "$CURRENT_RUN" vsm-editor --input /tmp/kg-100k.vsm --json /tmp/kg-current.json
```

`--fixture` creates a new file and refuses to overwrite an existing path. Without
`--input` or `--fixture`, benchmark cases generate a unique temporary file and delete
only that file on completion. `--input` is read-only, including the editor case. A
`generate-vsm` / `generate-vsc` command requires `--fixture` and reports a file SHA-256.

## Explicit >1 GiB source-file workload

A source-size test and a dense-record test answer different questions. `--row-bytes`
sets the minimum ASCII byte length of each real event row, including its LF, by adding
trailing spaces. It does not add opaque comment-only filler and never truncates a row.
JSON labels this a **synthetic padded event-row workload**. With `--input`, pass the
same `--row-bytes` value to label the existing fixture; it declares its provenance and
does not rewrite or pad the input. The default is zero padding.
One million 1,100-byte event rows produce about 1.10 GB (1.024 GiB) of source, while
retaining one million actual events. This is not evidence that an arbitrary unpadded
1 GiB chart with tens of millions of events fits in the same memory budget.

Generate the fixture separately, then run under the existing process-group guard:

```sh
# Disk requirement: about 1.10 GB per fixture. Never run large cases concurrently.
dotnet "$CURRENT_RUN" generate-vsm --count 1000000 --row-bytes 1100 \
  --fixture /tmp/kg-padded-1gb.vsm
python3 "$CURRENT/scripts/workspace-memory-check.py" \
  --log /tmp/kg-padded-current.log --seconds 600 --rss-gib 6 -- \
  dotnet "$CURRENT_RUN" vsm-load --input /tmp/kg-padded-1gb.vsm --row-bytes 1100 --unlimited \
  --engine off --label current --json /tmp/kg-padded-current.json
```

Use the same command with `BASE_RUN` for comparison, `vsm-editor` for the editable
source representation, or `editor-session` for the actual headless editor initialization
and saved source snapshot. Reuse exactly the same `--input` file for both revisions.
The baseline may exceed the memory budget while materializing a whole-file string;
record that failure instead of raising the budget or treating it as success.
For VSC use `generate-vsc` / `vsc-load`. Also run the unpadded
million-event/index cases above. The 6 GiB watchdog checks RSS every 100 ms, so brief
overshoot is possible. It stops only the child process group it launched, and produces
`.memory.jsonl` samples and a `.result.json` exit/stop report next to its log. A guard
termination is a bounded failure, not a successful load or a measured completion time.
Do not start a second large workload until the first process has exited.

## Metric boundaries and limitations

Each stage forces GC before measurement. `ElapsedMs` and allocation/collection counters
cover the requested operation; checksum validation and forced GC after it are excluded.
`RetainedAfterGcBytes` is the total live managed heap after the stage, not merely the
stage's incremental allocation; subtract `ManagedBeforeBytes` with care because earlier
objects may become collectible. Thread and process-total allocation counters are both
reported. Application objects remain alive through the stage's retained-memory sample.

`WorkingSetBeforeBytes` / `WorkingSetAfterBytes` are instantaneous RSS, not private
bytes. `ProcessLifetimePeakWorkingSetBytes` is the OS high-water mark for the entire
process, including setup and earlier stages; it is null when the runtime reports zero.
Use the external watchdog's samples for a time series. First loader calls can include
JIT/native initialization; fixture generation is excluded from operation timings.
These headless tests do not measure editor frame rate, GPU behavior, audio underruns,
or prolonged Editor/Viewer switching.

The adjacent `native-vsm-repeat-bench.py` and `.rs` provide an isolated Rust repeated
VSM expansion benchmark; run that script with `--help` for its baseline/candidate
checkout options and allocator/RSS metrics.
