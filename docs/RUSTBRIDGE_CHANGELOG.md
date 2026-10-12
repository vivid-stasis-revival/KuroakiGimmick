# RustBridge component history

The current application is `v0.1.5 / 18.0`; the native crate is `0.3.0-alpha.0`.
Current behavior and build instructions are in [OPTIMIZATION_ENGINE.md](OPTIMIZATION_ENGINE.md).

## 0.3.0-alpha.0 (included in application v0.1.5)

- Added batched Timeline base-track evaluation, retaining C# event/callback semantics.
- Added Symphonia audio decoding, bounded memory/PCM disk caches and parallel loading.
- Integrated device-wide performance preferences; calculation/audio initially use Rust.
- Published target-specific native sidecars with managed and NativeAOT releases.
- Read trailing VSM comments with matching native/managed warning diagnostics.
- Check chart size before allocating input, share empty Note extras and remove fixed
  two-million Note/BPM count limits while retaining byte and collection bounds.
- Expanded native self-tests, CPU regressions, CLI help and dependency notices.

## 0.2.0 (historical engine migration stage)

- Added VSC/VSB/VSM parsing, BPM construction and packet ABI.
- Added `KG_RUST_ENGINE`, `--rust-engine-test` and load profiling.
- At this stage audio and Timeline evaluation still ran in C#.

## 0.1.0 (historical motion bridge stage)

- Introduced batched per-note movement, C# fallback, verification and ABI tests.
- Parsing, Timeline and audio had not yet migrated at this stage.
