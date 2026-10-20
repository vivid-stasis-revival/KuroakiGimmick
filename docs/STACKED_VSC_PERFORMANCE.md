# Dense repeated VSC loading, 2026-10-20

Tested locally on macOS arm64 against branch baseline b3f6f5f, using the same Release
.NET harness and each revision's native library. The private input contains
1,073,741,820 bytes and 83,307,555 note/timing records, unlike the earlier padded
million-record workload. Source data is not included in the repository.

The native parser now probes at most 4,096 lines. Repetitive inputs use a bounded
short-row cache and a compact occurrence-ID packet. All occurrences remain present,
with stable timestamp ordering, metadata and diagnostics preserved. Diagnostic rows
are never cached. Unique inputs keep the ordinary packet. Older native libraries
remain supported through the existing entry point. The managed decoder shares
immutable parsed records but retains one list entry per occurrence.

ScoreState allocates its final hit array directly, avoiding List growth and a second
full array, and skips sorting when hit times are already ordered. Holds and delayed
mines still use the original ordering comparison when a sort is needed.

## Measurements

Single samples, warm filesystem cache; these are not UI FPS or end-to-end window
opening measurements. The harness forces GC between stages; the stage times exclude
that GC and checksum work. RSS is sampled at 100 ms, not an instantaneous OS maximum.

| Same 16 MiB prefix (1,301,677 records) | Before | After |
|---|---:|---:|
| Parse | 506.56 ms | 93.88 ms |
| BPM | 5.16 ms | 4.76 ms |
| NoteIndex | 135.49 ms | 100.65 ms |
| ScoreState | 183.58 ms | 12.78 ms |
| Retained managed heap after final stage | 310.0 MB | 149.6 MB |

Both prefix checksums are `D2A05FEEFB11FD2F`.

The full candidate completed: parse 4.458 s, BPM 0.157 s, NoteIndex 2.344 s,
ScoreState 0.734 s (7.692 s total timed stages). Whole process including forced GC
and checksum: 15.07 s. Sampled RSS peak: 9.13 GB; final retained managed heap: 9.56 GB.
These differ because retained managed heap and resident pages measure different things.
All 83,307,555 records remain, checksum `592054FFA8EF1DFF`.

The full baseline crossed the 12 GiB watchdog threshold at 11.07 s (sampled
13.06 GB) before reporting a completed parse and was terminated. No full-input
speedup ratio or baseline final-memory figure is claimed.

An initial always-compact version regressed the unique million-record fixture from
367.68 to 389.30 ms; both checksums were `B175D245CADCF097`. This prompted the bounded
probe and ordinary-packet fallback described above. The raw bundle retains these
initial observations rather than presenting them as final adaptive-path results.
The final adaptive path measured 377.59 ms with the same checksum on that unique
fixture (single samples are not evidence of a general unique-chart speedup).

Raw local measurements: [stacked-vsc-2026-10-20.json](performance/stacked-vsc-2026-10-20.json).

## Validation and limits

Native parser tests include compact-versus-original occurrence order, extensions and
repeated diagnostics. Managed `--large-chart-test` compares native output to C# for
repeated rows and cache saturation, alongside existing encoding/index/editor checks.
The final macOS NativeAOT package also passed `--large-chart-test`.
The existing score oracle and note-index regression checks passed. The broader local
regression runner later stopped at an unrelated sandbox-denied backup-history write;
it is not reported as an all-green full suite.

Index, runtime motion and score arrays still scale with occurrence count. The full
application also adds session snapshots, assets and GPU resources. This change does
not establish smooth rendering of tens of millions of simultaneous notes, and does
not drop overlapping notes to achieve its loading improvements.

Final adaptive-version full-input rerun: load 5.306 s, bpm 0.154 s, note-index 2.466 s, score 0.71 s. Whole process 16.02 s; sampled RSS 8.83 GB. Count and checksum match the first completed candidate run.
