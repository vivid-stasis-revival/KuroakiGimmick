# Periodic VSC parsing and STACK LIMIT setting, 2026-10-20

The previous repeated-row parser still stored, sorted and transferred one ID for
each of the 83,307,555 occurrences in the private 1 GiB input. The new path first
finds a candidate byte period using at most 64 KiB, then verifies byte equality
over the entire source. A changed suffix therefore cannot silently disappear.

The native packet describes equal-time subsequences and their exact repetition
counts. The managed decoder expands them with span fills/copies. It retains all
notes, stable tie order, extensions, dialect and final partial-block records.
Inputs with diagnostics, unsupported periods or non-periodic bytes retain the
existing parser. Packet counts, ranges and timestamp ordering are validated.

On macOS, mapped input uses a private file clone when available, falling back to
the existing bounded copy on other volumes. The source descriptor remains owned
by the caller, and the clone is in an owner-only temporary directory. Apple's
[clonefile specification](https://github.com/apple-oss-distributions/xnu/blob/main/bsd/man/man2/clonefile.2)
documents that writes to either file are isolated. The regression test truncates
the original after cloning and checks that the snapshot is intact.

## Same-input local results

Apple M4 Pro, macOS arm64, Release .NET with required native parsing. Three pairs
of fresh processes, old then new, run sequentially; filesystem cache is warm.
The baseline is the previous KGR1 implementation from the 14:20:54 package state.
No test input or private assets are included.

| Trial | Previous parse/load | New parse/load |
|---|---:|---:|
| 1 | 5,984.33 ms | 1,735.77 ms |
| 2 | 5,190.30 ms | 1,742.22 ms |
| 3 | 5,019.25 ms | 1,768.22 ms |
| Median | 5,190.30 ms | 1,742.22 ms |

The median is about 3.0 times faster (66.4% less elapsed time). All six runs have
83,307,555 records, no diagnostics, and checksum `592054FFA8EF1DFF`. These timings
cover the VscReader call including input handling and managed reconstruction;
they exclude later BPM/note-index/score/asset initialization and benchmark
checksum/forced-GC work. This is a result for a verified periodic input, not a
universal speedup claim for arbitrary 1 GiB charts.

The final managed note-reference list still needs about 667 MB. This optimization
does not remove the later full runtime/index/score arrays.

## Checks

- Rust: 27 passed, 2 explicitly ignored stress tests.
- Managed large-chart suite: periodic tie order, holds/extensions/negative times,
  partial tails with and without newline, changed-suffix fallback, snapshot
  isolation, default-on settings migration and false-value serialization.
- GPU/settings: actual Video navigation and toggle clicks, disabled persistence,
  reset to enabled, unchanged Session; full/preview/full/disabled-preview pixel
  checks on the dense prefix.

Raw results: [periodic-vsc-2026-10-20.json](performance/periodic-vsc-2026-10-20.json).

Final packaged NativeAOT check (`--inspect`, required native engine):
VSC parse 2,602.95 ms; complete Session construction 11,120.12 ms, including
7,150.17 ms for score/index/playback construction. This is a separate single
sample, not the JIT median above. The package also passes `--large-chart-test`
and `codesign --verify --deep --strict`. Later index/score construction remains
the dominant opening cost; the parser improvement is not an instant-open claim.
