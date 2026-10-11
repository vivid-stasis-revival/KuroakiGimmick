# Rust Note motion bridge · ABI 0.1.0 / application v0.1.5

This guide covers the motion ABI, whose version remains `0x00010000` even though
the crate is now `0.3.0-alpha.0`. See [the current engine guide](OPTIMIZATION_ENGINE.md)
for full responsibilities, defaults, audio, parsing and publishing.

The per-note hot path in `SceneRenderer.Playfield.cs` batches `XFromValues`,
`YFromScroll`, standard/custom Boost and Hold end-Y into one native call per
visible-note batch. C# selects notes, evaluates per-lane mods, computes cutoffs,
handles score/judgment, orders sprites and submits GPU work. Rust retains no Note,
GPU or SDL pointers after a motion call.

## Build / verify (macOS Apple Silicon)

Requires Rust stable and .NET 8 SDK, plus the same SDL3 dependencies already needed by K/G.

```sh
./scripts/build-rustbridge.sh
KG_RUST_BRIDGE=verify dotnet run -c Release --no-build -- --rust-bridge-test
KG_RUST_BRIDGE=auto dotnet run -c Release --no-build
```

`--rust-bridge-test` requires the Rust library and runs **32,768 randomized C#-vs-Rust input comparisons**, without initializing SDL or loading a chart. Rust's own `cargo test` includes ABI sizes, FFI error handling, standard/custom note formulas and Hold endpoints. Use snapshot/scene comparison against your real project as an additional regression, especially on ARM64 Metal.

To publish a NativeAOT binary on macOS:

```sh
dotnet publish -c Release -r osx-arm64 -p:PublishAot=true
cp rust/kg-native-core/target/release/libkg_native_core.dylib bin/Release/net8.0/osx-arm64/publish/
KG_RUST_BRIDGE=verify bin/Release/net8.0/osx-arm64/publish/KuroakiGimmick
```

The Rust `cdylib` must match the **same target architecture** as the C# executable. On macOS it is `libkg_native_core.dylib`, Windows `kg_native_core.dll`, Linux `libkg_native_core.so`. Do not use the old `SDL3_shadercross` library: this hybrid project uses the original C# SDL_GPU implementation.

## Runtime switches

Set `KG_RUST_BRIDGE` before starting the process:

- `auto` (initial default; without an explicit override, follows the calculation engine on/off preference): use native motion if available, automatically fall back to the original C# implementation if library is unavailable, incompatible or returns an error.
- `off`: force original C# note motion, useful for A/B baseline screenshots/perf comparisons.
- `verify`: calculate every native result and compare against original C# with a relative tolerance of `1e-8`; on divergence, switch back to C# and log the mismatch.
- `required`: insist on native evaluation; fail explicitly when DLL is missing or returns incorrect status. To verify numerical parity in required mode, use `--rust-bridge-test`.

One C ABI version query on first use, then one batch call per note-containing frame. Input/output buffers and candidate list are reused across frames. No UI recreation, no Rust callbacks and no per-note P/Invoke traffic.

## Files introduced / changed

- `rust/kg-native-core/`: Rust crate, minimal `#[repr(C)]` motion batch API, native tests; Symphonia is used by the audio module.
- `src/Bridge/RustNoteBridge.cs`: P/Invoke + struct layout, ABI validation, fallback, diff-oracle, CLI self-test.
- `src/Graphics/Scene/SceneRenderer.cs`: persistent reusable input/output buffers.
- `src/Graphics/Scene/SceneRenderer.Playfield.cs`: actual integration into Note rendering, C# fallback unchanged mathematically.
- `src/App/Program.cs`: `--rust-bridge-test` available before chart loading/SDL initialization.
- `scripts/build-rustbridge.sh` / `.ps1`: build, copy native lib, test.
- `.github/workflows/rustbridge.yml`: cross-platform compile and differential test workflow.

## Acceptance limits

- `cargo test`, `dotnet build`, `--rust-bridge-test` and real Metal/SDL screenshots need to pass **on a machine with toolchains installed**. Test results apply to the toolchain, platform and commit actually tested.
- Rust floating-point math may have small target-dependent differences from C# `Math.Sin` / `Math.Pow`; verify mode is provided to detect them before enabling it for production.
- The per-frame input gathering and native boundary impose overhead. Do **not** assume faster frame time until benchmarked on dense charts.
- If rendering wrong or native crashes, set `KG_RUST_BRIDGE=off` to immediately restore the original C# path. Native code memory corruption cannot be recovered from by managed exceptions; only use trusted matching builds.

## Other engine components

See [RUSTBRIDGE_ENGINE.md](RUSTBRIDGE_ENGINE.md) for parsing/BPM and
[RUST_RUNTIME_V030.md](RUST_RUNTIME_V030.md) for Timeline base-track evaluation.
