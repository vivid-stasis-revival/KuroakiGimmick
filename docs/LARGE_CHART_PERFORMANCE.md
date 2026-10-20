# Large-chart loading and editing: 2026-10-19

Baseline: `840b6acde9882ab1e343a765d8175755ac2c540d` (`main`). Measurements use the same public-API harness against separate baseline/candidate assemblies and native libraries. All inputs are generated; no game or personal chart data is included.

## Changes

- **Source-size-independent input buffering.** The managed VSM file path no longer reads the entire file into a UTF-16 string before deciding whether Rust is enabled. In-memory fallback and editor loading use a bounded line scanner instead of whole-source normalization/splitting or a retained regex match graph. UTF-8/16/32 BOMs, strict editor decoding, runtime replacement decoding, CR/LF/CRLF and source line numbers remain covered by regression tests.
- **Bounded native input staging.** Inputs up to 4 MiB retain the simple in-memory path. Larger inputs are mapped; on Unix they are first copied with a 64 KiB buffer into a private, owner-only temporary snapshot so external truncation cannot SIGBUS the process. Windows uses its read-sharing lock. Required/verify still use the native parser; unusual encodings or bare CR use bounded UTF-8 transcoding. Packet decoding avoids per-string byte arrays, validates counts before reserving exact list capacity, and shares frequently repeated event names/easings through a bounded per-load cache.
- **Compact interval tree.** Sorted starts allow ordered traversal to terminate at the first start after the query window. Only internal subtree end maxima are stored: `8 * nextPowerOfTwo(N)` bytes instead of `32 * nextPowerOfTwo(N)` for the tree arrays. Crossing Holds, inclusive boundaries, stable ordering and incremental end updates remain intact. Entries/runtime note data are still retained.
- **Indexed editor operations.** Source-line/ID and preserved-line/proxy-header queries are cached. A batch of K replacements no longer performs K full-source scans. Public List mutations, including same-count replacement/sort/reverse and duplicate IDs, preserve the former behavior; replacing the source list immediately releases the old cache references. The first ID lookup still builds an O(N) dictionary.
- **No duplicate clean VSM string.** Editor dirty tracking compares its existing immutable line snapshot directly, including equivalent text with different line partitions. It no longer retains another whole UTF-16 document just to compare after insert/delete.
- **Native repeat expansion.** Rust uses a constant-size beat-range descriptor and appends validated events directly. It no longer retains a separate expanded beat vector and temporary ModEvent vector. Per-row easing normalization happens once. Final vector growth/capacity behavior is retained.

## Measurement boundaries

Linux x86-64 cloud VM, AMD EPYC 9V74, 9 available logical CPUs, approximately 9.7 GiB RAM, .NET SDK 8.0.426/runtime 8.0.32, Rust 1.99.1. Release managed/JIT builds; no SDL window or GPU render tests. A disk-backed workspace was used for `TMPDIR`; a tmpfs temp directory would instead charge the snapshot against memory.

Each large process ran alone under the repository's 6 GiB RSS / 600 second watchdog. `sampled peak RSS` is process RSS sampled every 100 ms, not managed heap, total allocations, GPU memory, or a guaranteed instantaneous OS maximum. Allocated bytes are cumulative managed allocations and can substantially exceed retained heap/RSS. GC is forced between benchmark stages; timing excludes the harness's post-stage GC and checksum. Cold parser/native/JIT setup is included where not covered by the harness warmup. Source file cache is not flushed, and these are not disk cold-start or UI FPS measurements.

The raw result bundle records stage timings, allocation counters, retained managed heap, counts, checksums, engine mode and watchdog outcome: [large-charts-2026-10-19.json](performance/large-charts-2026-10-19.json). Reproduce with [scripts/large-charts](../scripts/large-charts/README.md).

## Results

### >1 GiB source files (same bytes, one sample per mode)

VSM: **1,100,000,033 bytes**, 1,000,000 real event rows. Baseline `off`, `auto` and `required` each throw `OutOfMemoryException`; the watchdog does not kill them. The candidate completes all three with exactly 1,000,000 events and checksum `914D2EEA01967524`, also matching the unpadded fixture's runtime result.

| Candidate VSM runtime mode | Load | Managed allocations | Retained managed heap | Sampled peak RSS |
|---|---:|---:|---:|---:|
| off | 3.143 s | 3,341.6 MB | 120.7 MB | 190.2 MB |
| auto | 3.054 s | 168.3 MB | 88.3 MB | 1,370.2 MB |
| required | 2.894 s | 168.3 MB | 88.3 MB | 1,349.3 MB |

MB above and below means decimal bytes / 1,000,000. The mapped native source is resident during parsing, so its RSS is much higher than managed streaming despite substantially fewer managed allocations. No baseline completion time/speedup is claimed for a failed load.

The same VSM also exercises two distinct editor levels:

| Path | Baseline | Candidate | Candidate sampled peak RSS |
|---|---|---|---:|
| `VsmDocument` source editor | OutOfMemoryException | Load 8.773 s; first lookup 300 ms; 1,000 warm lookup/count operations 4.9 ms; 1,000 replacements 326 ms | 2,847.0 MB |
| Actual `Session` + `EditorDocument` initialization | OutOfMemoryException | Session load 5.581 s; editor open 9.433 s; clean/history-state/count checks 229 ms | 3,027.6 MB |

The headless editor contains 1,000,002 source lines, 1,000,000 editable events and 1,000,000 runtime events, starts clean with empty undo/redo history, and retains about 2,701 MB managed heap. Session initialization reports four diagnostics (recorded in the raw report); this is not a full UI/preview/export acceptance test. Watchdog whole-process duration was 26.33 s, including setup, forced collections and checksum work excluded from the stage timings.

VSC: **1,100,000,000 bytes**, 1,000,000 notes, crossing Holds and equal starts. All six baseline/candidate × off/auto/required loads succeed with checksum `B175D245CADCF097`. For `required`, managed allocations fall from **1,187.8 MB to 79.2 MB (93.3%)**; load time is effectively unchanged in this single sample (2.072 → 2.084 s), and sampled peak RSS is **1,273.3 → 1,311.9 MB**, not an RSS reduction. Managed `off` stays around 2,499 MB cumulative allocation and 149 MB sampled peak RSS.

### Unpadded runtime loads (median of three fresh processes)

Times include cold parser/native setup. `required` proves use of Rust; managed `off` remains supported. MiB = bytes / 1,048,576.

| Records | Parser | Mode | Load ms, before → after | Allocated MiB, before → after | Retained MiB, before → after |
|---:|---|---|---:|---:|---:|
| 10,000 | VSM | off | 40.02 → 35.29 | 11.63 → 10.71 | 1.35 → 1.47 |
| 10,000 | VSM | required | 36.13 → 39.72 | 3.54 → 2.75 | 1.68 → 1.12 |
| 10,000 | VSC | off | 30.53 → 29.55 | 2.57 → 2.57 | 0.96 → 1.08 |
| 10,000 | VSC | required | 30.11 → 35.85 | 1.03 → 0.85 | 0.96 → 1.03 |
| 1,000,000 | VSM | off | 1,549.42 → 1,585.92 | 1,163.55 → 1,065.82 | 114.96 → 115.09 |
| 1,000,000 | VSM | required | 1,778.31 → 1,460.62 | 351.74 → 160.49 | 152.74 → 84.20 |
| 1,000,000 | VSC | off | 710.18 → 719.52 | 262.22 → 262.22 | 75.87 → 75.99 |
| 1,000,000 | VSC | required | 710.19 → 709.42 | 95.20 → 75.48 | 75.87 → 75.62 |

There is **no universal latency improvement**: small native cold loads are 3.6–5.7 ms slower in these samples, and million-record managed VSM is about 2.4% slower. The intended gains are peak source buffering, native allocation/retention, and repeated editor operations. These samples are too few to establish latency tails.

### Unpadded VSM editing and interval indexing (single samples)

The 1,000 warm operations each perform the same lookup/source-line/preserved-count checks. Replacements target the same evenly distributed clips, with a late proxy declaration, and preserve matching checksums.

| Event count | VSM load ms, before → after | 1,000 warm lookup/count operations ms | 1,000 replacements ms |
|---:|---:|---:|---:|
| 10,000 | 59.92 → 32.54 | 342.72 → 0.53 | 837.72 → 5.59 |
| 100,000 | 625.94 → 405.06 | 1,712.31 → 1.17 | 6,118.48 → 20.33 |
| 1,000,000 | 4,053.96 → 2,715.53 | 32,787.16 → 1.25 | 72,361.13 → 92.33 |

This trades a one-time O(N) dictionary build for warm O(1) lookup. At one million events, first lookup takes **46.40 → 278.36 ms**. Retained managed heap after load is 427.7 → 352.7 MB, and after first lookup is 435.7 → 405.3 MB, including the new dictionary. This is a source-operation benchmark, not editor FPS.

At one million intervals, construction allocation is **99.1 → 73.9 MB** and retained managed heap is **65.7 → 40.7 MB**. The tree backing arrays specifically shrink **32 → 8 MiB (75%)**; this is not a 75% reduction in total application memory. Construction is 72.96 → 68.12 ms; 1,000 queries are 71.48 → 69.74 ms with identical checksum `BE4AED81B89FA8BA`.

### Native repeat expansion (isolated counting-allocator benchmark)

A single valid VSM repeat row expands to one million events. Median of three processes:

| Metric | Baseline | Candidate |
|---|---:|---:|
| Peak live native heap | 223.1 MB | 119.1 MB |
| Total requested allocations | 340.1 MB | 228.1 MB |
| Peak process RSS | 279.9 MB | 169.0 MB |
| Parser time, including counting-allocator overhead | 176.11 ms | 85.79 ms |

Final retained heap and vector capacity are identical. Peak live heap drops **46.6%** by avoiding simultaneous expansion buffers, without dropping events. This is an algorithm microbenchmark, not a >1 GiB input or whole-application timing.

## Verification

- Release build: zero warnings/errors.
- `--large-chart-test`: randomized linear interval oracle, crossing Holds/equal starts/inclusive boundaries/updates, zero-allocation warm query loops, mutable-List cache invalidation, duplicate IDs, 12,000 replacements, dirty/undo/redo, exact source delimiters/BOM/encoding, native off/verify/required parity and split UTF-8 validation.
- Existing headless suites: `--chart-size-test`, `--rust-engine-test`, `--rust-timeline-test`, `--rust-bridge-test` (32,768 comparisons), `--rust-audio-test`, `--mpf-test`, `--gimmick-definition-test`, `--proxy-pick-test`, `--dock-layout-test`, `--render-workers-default-test` passed.
- Rust: 25 passed, 2 ignored by default; explicit >1M-event repeat stress passed. 10,000 deterministic old/new native parser comparisons matched all chart fields, diagnostics/errors and beat bits.
- Independent review reproduced and fixed an invalid UTF-8 sequence straddling a decoder chunk, verified owner-only Unix temporary-file permissions, and compared another 2,000 randomized newline/source strings with the old regex oracle.
- The new headless suite is added to the existing Windows/macOS bridge CI and macOS NativeAOT checks. Platform CI results belong to the exact PR commit; local Linux JIT tests do not substitute for those results.

## Limits and tradeoffs

These changes do not discard notes/events, reduce visual fidelity, change file formats, or automatically enable unlimited chart size. Existing default size limits and desktop opt-in remain unchanged.

A **source-size** test and a **dense-event** test answer different questions. The >1 GiB fixtures contain one million real event/note rows padded with trailing spaces to 1,100 bytes each. They are explicitly synthetic, not an arbitrary unpadded 1 GiB chart with tens of millions of records. The separate unpadded million-event tests establish dense-data behavior at that count only.

Native Chart/packet/managed result memory and all retained editor records still grow with actual event count. Native mapping eliminates a source-sized managed allocation; resident mapped pages still count toward RSS. Unix large native loads require source-sized temporary disk space; non-UTF-8/bare-CR inputs can additionally require a normalized UTF-8 temporary file. Insufficient disk space or an unsupported mapping platform follows the existing fallback/error policy. Required mode does not silently become managed mode.

The initial lookup dictionary costs O(N) time and additional retained memory. Full undo snapshots still copy line-reference arrays. Source-text views, normalized preview rebuilding and export paths can still materialize whole strings; this PR does not establish unrestricted editing/export of every >1 GiB VSM. It also does not optimize every window/native-sequence scan or rendering hot path. GPU frame rate, real audio playback, all native gimmicks, long-session stability and actual user-owned 1 GiB dense charts were not validated in this cloud environment.
