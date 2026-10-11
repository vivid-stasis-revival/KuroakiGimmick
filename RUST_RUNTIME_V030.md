# Rust Timeline Runtime · crate 0.3.0-alpha.0 / application v0.1.5

This is a **real incremental Rust frame evaluator**, not the complete timeline rewrite.

## What actually runs in Rust

- The existing C# constructor still applies original VSM semantics: event ordering, `_` sentinel resolution, callback registration, particle construction, specialized native-object behavior.
- After construction, `Timeline` freezes its ordinary `Tracks` into Rust-owned, contiguous arrays using a C ABI (`kg_core_tracks_create`). Each track stores its original pre-event initial value, C# sequence and proxy ID.
- `SceneRenderer.Render()` calls `Timeline.PrepareFrame(time)` once. Rust uses a binary search per track and evaluates the last-starting segment's interpolation using the matching 31 easing IDs, returning one `double[]` of all base track values.
- The rest of the same frame's `Timeline.Get(name, time, proxy)` reads its thread-local snapshot. It does not call FFI per mod or note.
- Original C# remains responsible for aliases, per-frame scalar expressions and callback fades, which retain the previous recursion and priority rules. Queries for arbitrary past/future times still use C# if no matching prepared snapshot exists.
- The native handle is owned by a managed `SafeHandle` and freed when no longer reachable; the Rust runtime owns no SDL or C# pointers beyond each synchronous call.

## Run / test on macOS

Build the current checkout using `bash scripts/build-rustbridge.sh` (Windows:
`pwsh -File scripts/build-rustbridge.ps1`). Full prerequisites and runtime defaults
are in [OPTIMIZATION_ENGINE.md](OPTIMIZATION_ENGINE.md). The initial calculation
mode is `auto`; the Compatibility/C# preference selects `off`.

The build scripts run `cargo test` and `--rust-timeline-test` **without SDL**, including 31 easing names, proxy isolation, tie order, reverse seeks and per-frame parity against C#.

```bash
KG_RUST_ENGINE=verify KG_RUST_BRIDGE=off dotnet run -c Release --no-build -- --rust-timeline-test
KG_RUST_ENGINE=verify KG_LOAD_PROFILE=1 dotnet run -c Release --no-build -- /path/to/chart.vsc
KG_RUST_ENGINE=auto KG_RUST_BRIDGE=off KG_RUST_TIMELINE_PROFILE=1 dotnet run -c Release --no-build -- /path/to/chart.vsc
KG_RUST_ENGINE=off KG_RUST_BRIDGE=off dotnet run -c Release --no-build -- /path/to/chart.vsc
```

Expected engine activation log: `[RustTimeline] batch evaluator ready: N tracks, M segments (auto).`
With `KG_RUST_TIMELINE_PROFILE=1`, every 600 evaluated frames it emits native batch wall-time average. **Do not benchmark with `verify`: it deliberately computes both C# and Rust values.**

## Failure behavior

- `auto`: missing ABI, unknown easing, native errors or output inconsistency in explicit validation disable the runtime and use original C#.
- `verify`: compares every track each frame using the existing C# evaluator; on mismatch disable the native path, reporting name, proxy and time.
- `required`: missing/incompatible native runtime raises an exception rather than silently benchmarking C#.
- `off`: C# only.

## Not yet migrated

- Building timeline events / callbacks, initial mod semantics, special objects, per-frame bindings, native-gimmick expressions and particle baking still run in C#.
- Note/Score indexes, EditorDocument/VsmDocument operations and draw command generation remain in C# (the previous Rust NoteMotion batch remains available independently).
- No claim is made about faster FPS until run-time benchmarks are performed with a real chart on the target device.

The native crate does not include proprietary game assets.

## Large-chart limits

- Removes the arbitrary 2,000,000-note limit in Rust VSC/VSB parsing and in
  C# VSC/VSB fallbacks, plus the managed Rust packet decoder count check.
- Removes the inherited 2,000,000-BPM-change ABI count gate. Buffer length and
  pointer-size validation remain mandatory.
- Does **not** disable the 128 MiB chart file, 512 MiB bridge packet, or managed
  `int`-indexed collection safety bounds. Out-of-memory is still possible on
  extreme charts; those are byte/memory limitations, not a note-count policy.
- Also corrects the RustTimeline self-test: the custom gimmick fixture now
  declares 8 proxies, and a seek to negative time expects the unmodified value.
- Optional heavy regression: `cargo test --manifest-path rust/kg-native-core/Cargo.toml --release vsc_more_than_two_million_notes -- --ignored`.
