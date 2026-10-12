# Rust Optimization Engine · v0.1.5 / 18.0

This is the current guide for the hybrid C#/Rust application. The native crate is
`kg_native_core` version `0.3.0-alpha.0`; its version is independent of the application.

## Responsibilities

Rust runs VSC/VSB/VSM parsing, BPM construction, batched Note motion, Symphonia
audio decoding, and batched evaluation of Timeline base tracks. C# retains event
construction, callbacks, aliases, per-frame bindings, particles, editor state, SDL
audio playback, video encoding, and GPU command submission. See the focused guides:
[Note motion](RUSTBRIDGE.md), [parsing/BPM](RUSTBRIDGE_ENGINE.md),
[Timeline](RUST_RUNTIME_V030.md).

## Build and verify

Install a .NET SDK able to build `net8.0`, Rust stable (`rustup`, `cargo`, `rustc`),
and the platform prerequisites for the existing SDL3 renderer.

```sh
bash scripts/build-rustbridge.sh
# Windows PowerShell:
# pwsh -File scripts/build-rustbridge.ps1
```

The scripts run Rust unit tests, build the cdylib and C# app, place the native
library beside the managed executable, then run motion, parser/BPM, Timeline and
audio self-tests without SDL. Standalone CPU regression projects are retained
only in the local `tests` workspace and are not tracked or packaged.

A plain `dotnet build` compiles C# only; without the native library, auto mode
reports the reason and uses the compatibility path. It does not validate Rust.

## Defaults and overrides

Settings > Performance saves device-wide preferences in `settings.json`, outside
song projects. Defaults are Optimization/Rust calculation and audio, parallel
loading on, decoded-audio cache on. Changes apply when opening the next chart.
Environment variables take priority over preferences.

| Variable | Values / default | Scope |
| --- | --- | --- |
| `KG_RUST_ENGINE` | `auto/off/verify/required`; default follows calculation preference (`auto` initially) | Parsing, BPM, Timeline |
| `KG_RUST_BRIDGE` | `auto/off/verify/required`; default `off` when calculation is off, otherwise `auto` | Note motion |
| `KG_AUDIO_ENGINE` | `optimization/nvorbis`; default follows audio preference (`optimization` initially) | Rust decoder or NVorbis/FFmpeg |
| `KG_AUDIO_CACHE` | `0/1`; initially `1` | Memory and disk PCM cache |
| `KG_PARALLEL_LOADING` | `0/1`; initially `1` | Decode audio while chart/resources load |
| `KG_RUST_AUDIO_REQUIRED` | `1` | Throw on native decode failure in optimization audio mode |
| `KG_LOAD_PROFILE` | `1` | Load-stage timing logs |
| `KG_RUST_TIMELINE_PROFILE` | `1` | Batch track timings every 600 evaluated frames |

`auto` uses Rust when available and falls back on unsupported input/native errors.
`verify` also compares managed and native results and falls back on divergence.
`required` fails when the native operation cannot succeed. `off` uses C#.
Calculation and audio settings are independent: `KG_RUST_ENGINE=required` alone
does not require native audio. Explicit `KG_RUST_BRIDGE` can override calculation.

```sh
KG_RUST_ENGINE=verify KG_RUST_BRIDGE=verify dotnet run -c Release --no-build -- /path/to/chart.vsc
# C# baseline (audio/cache settings also explicit):
KG_RUST_ENGINE=off KG_RUST_BRIDGE=off KG_AUDIO_ENGINE=nvorbis KG_AUDIO_CACHE=0 KG_PARALLEL_LOADING=0 \
  dotnet run -c Release --no-build -- /path/to/chart.vsc
```

Use `auto` versus `off` for performance comparisons. `verify` intentionally
computes both implementations. `[K/G Load] Audio decode/reuse (wait)` measures the
blocking wait, not full decode CPU time; `[K/G Audio] ... wall` starts at job launch.

## Audio, compatibility and limits

Symphonia 0.5.4 is configured for OGG/Vorbis, FLAC, WAV/PCM, MP3 and MP4/AAC.
Unsupported or malformed audio falls back to NVorbis for OGG or FFmpeg for other
formats unless native audio is explicitly required. Rust preserves source rate
and channel count; the legacy FFmpeg path converts non-OGG to 48 kHz stereo.
Compare representative files when assessing audio parity. SDL still owns playback.

The Rust decoder returns interleaved float32 PCM, copied once into C# and freed
through the paired native function. The one-track memory cache is limited to
128 MiB. Versioned disk files are under `Paths.LogDirectory/audio-cache` and have
no LRU eviction. Source canonical path, file size and modification time invalidate
entries; same-size edits with unchanged timestamps cannot be detected.

VSM trailing `//` comments are read with a non-fatal warning, including metadata,
event rows and `mpf` callbacks. Remove them before using the source in the original
game. Standalone comment rows and bare `mods` markers retain compatibility errors.

VSC/VSB files are checked against 128 MiB before buffer allocation; VSM files are
limited to 64 MiB. Native packets remain limited to 512 MiB. Notes and BPM changes
have no fixed two-million count limit; managed collection bounds and available
memory still apply. Empty Note extras share `Note.EmptyExtra` in both paths.
VSM events remain limited to one million. Decoded PCM is limited to 1,024,000,000
bytes. There is no cancellation for an in-flight decode if scene loading fails.

## Publishing (v0.1.5 / 18.0)

The Rust Optimization Engine is a **required native sidecar** for release archives.
The managed single-file executable and NativeAOT executable both require a native
library beside the actual executable:

| RID | Rust target | Required native sidecar |
| --- | --- | --- |
| `osx-arm64` | `aarch64-apple-darwin` | `KuroakiGimmick.app/Contents/MacOS/libkg_native_core.dylib` |
| `osx-x64` | `x86_64-apple-darwin` | `KuroakiGimmick.app/Contents/MacOS/libkg_native_core.dylib` |
| `win-x64` | Windows native Rust host or `x86_64-pc-windows-gnu` | `kg_native_core.dll` beside `.exe` |
| `win-arm64` | `aarch64-pc-windows-msvc` | `kg_native_core.dll` beside `.exe` |

Supported official entry points:

- Managed single-file: `bash scripts/publish-mac.sh arm64`, `bash scripts/publish-win.sh x64`, `pwsh -File scripts/publish-windows.ps1 -Arch x64`.
- NativeAOT: `bash scripts/publish-aot-mac.sh arm64` and `bash scripts/publish-aot-win.sh x64` (the latter on a Windows Git Bash/MSVC host).
- GitHub Actions NativeAOT uses the same `scripts/rust-native-common.sh` target mapping and checks for the staged library.

`dotnet publish` invoked **directly**, outside these entry points, does **not**
automatically build the Rust library. Use the official publish scripts or
stage the native library yourself before distributing the output.

Cross compilation requires a matching Rust target and linker. The scripts install
missing Rust standard-library targets with `rustup target add`; `win-x64` from
macOS/Linux additionally requires `x86_64-w64-mingw32-gcc`. Windows ARM64
requires a Windows MSVC host with ARM64 build tools. Unsupported cross builds
fail explicitly rather than quietly shipping an incomplete package.

Before publishing, verify that the library was included and functional:

```bash
# Within the final executable directory (macOS example)
KG_RUST_BRIDGE=verify ./KuroakiGimmick --rust-bridge-test
KG_RUST_ENGINE=verify ./KuroakiGimmick --rust-engine-test
KG_RUST_ENGINE=verify ./KuroakiGimmick --rust-timeline-test
KG_RUST_AUDIO_REQUIRED=1 ./KuroakiGimmick --rust-audio-test
```

Do not distribute a release without the sidecar native library. Both this
library and the executable must match the target CPU architecture.
