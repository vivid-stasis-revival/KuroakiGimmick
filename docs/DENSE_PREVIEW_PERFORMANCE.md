# Repeated-stack interactive preview, 2026-10-20

The 1 GiB private input has 83,307,555 source records, including 81,456,276
playable notes in roughly three seconds. Optimizing parsing did not remove
per-frame traversal and submission of millions of coincident sprites.

## Behavior

The viewer and editor can use a separate bounded-detail snapshot. At least
100,000 records are required to consider reduction. Identical note geometry at
the same timestamp is indexed up to 1,024 occurrences, in original order, with
an ordinal for each occurrence. Queries apply the selected 16–1,024 layer limit
(default 256) without rebuilding the snapshot or rescanning the source. The same
rule applies to identical explicit hit effects and hold-tick streams. If a
candidate snapshot exceeds 262,144 records or the minimum limit does not halve
the original count, the normal path is retained. Sparse events are never removed to fit the budget.

This is explicitly a preview fidelity tradeoff: very deep translucent stacks
and particle layers can differ from complete compositing. The main preview
displays `STACK LIMIT` when reduced snapshots are available. It is not claimed
to be pixel-equivalent to arbitrary full-detail dense scenes.

Settings > Video now exposes `STACK LIMIT`, enabled by default (including old
settings files without the field). It applies immediately to game notes, effects,
combo particles and the editor note timeline, persists between launches, and is
enabled by Reset Defaults. Layer count defaults to 256; normal minus/plus clicks
change it by 64, and either Shift key changes the step to 16. Both bounds are
clamped and persisted. Turning it off uses the complete drawing path without
reloading the chart. These are device preferences, not changes to the song.

All source notes, score arithmetic, combo, full queries and video export keep
every occurrence. Preview mode is scoped to Viewer rendering and is restored on
exit, including exceptions. Native feedback cache keys include preview detail
so an export cannot reuse reduced feedback. The editor uses the same preview
note table; counts still come from the authoritative index.

The FPS display now smooths by elapsed wall time instead of keeping 95% of an
old value after every arbitrarily long stalled frame.

## Local GPU measurements

Apple M4 Pro / Metal, Release .NET, actual SDL_GPU rendering and synchronous
pixel readback; a hidden window, not a display-FPS or sustained playback test.
The input and its assets remain private. These are short samples. The table
below records the earlier fixed 32-layer implementation, not the new default
of 256 or the 1,024-layer maximum.

| Input / path | Initial completed frame | Subsequent completed frames |
|---|---:|---:|
| Same source, 16 MiB prefix, full detail | 272.47 ms | 256.37 ms |
| Full 1 GiB, bounded game preview | 209.34 ms | 1.14–8.80 ms |
| Full 1 GiB, Viewer UI + preview | 365.15 ms | 2.85–11.46 ms |
| Full 1 GiB, editor + note timeline | 446.89 ms | 3.45–20.71 ms |

The prefix has 1,301,677 source records. It is not the same workload as the full
input, so no same-input speedup ratio is claimed. The full baseline was not
rendered, to avoid another enormous submission.

Forward/reverse game-frame checksums agree at repeated timestamps. Both full
input runs retain 81,456,276 playable notes and judgements. Nine bounded game
frames submit 2,208 note candidates total and about 1.60 MB of GPU uploads;
the two full-detail prefix frames submit 404,970 candidates and 166.65 MB.

The original snapshots' `noteReduction`/`hitReduction` fields mean that reduced
tables are available; `full` mode still bypasses them.

Reproduction: [preview-performance](../scripts/preview-performance/README.md).
The headless `--large-chart-test` additionally checks crossing holds, native
motion table indices, exact scores/full queries, hold ticks and sparse fallback.

The same-renderer full/preview/full check passed on the dense prefix: both
full-detail frame checksums match after leaving preview mode. The ordinary
example chart matches across all three frames. The final macOS NativeAOT
package passes `--large-chart-test`, including the hold-tick reduction checks,
and its application signature verifies.

Raw results: [dense-preview-2026-10-20.json](performance/dense-preview-2026-10-20.json).

## Configurable-layer validation

Actual Video buttons passed normal +/-64, left/right Shift +/-16, endpoint,
persistence, reset-to-256 and unchanged-Session checks. Core checks cover every
selected limit across notes, crossing holds, explicit effects and hold ticks,
including stacks shorter than the 1,024-layer capacity and full-detail bypass.

A single loaded full 1 GiB session was rendered at the same time with limits
16 -> 1,024 -> 256 -> 16. Candidate counts were 128 -> 8,192 -> 2,048 -> 128,
respectively. The repeated 16-layer frames have the same pixel checksum.
After first-frame initialization, completed GPU/readback samples were 26.63 ms
at 1,024, 8.14 ms at 256, and 2.58 ms at 16. These are single offscreen samples,
not sustained display FPS. Raw data: [dense-preview-layers-2026-10-20.json](performance/dense-preview-layers-2026-10-20.json).
