# Reproducible performance and GPU regression checks

Run `bash scripts/performance-check.sh /absolute/output/directory` on macOS with the local game assets and FFmpeg. The script builds Rust and C#, runs each GPU workload sequentially, and stores raw output. GPU tests require a real SDL_GPU backend. Linux commands are usable for diagnostics; Linux application support is not added by this change.

On Windows, build with `scripts/build-rustbridge.ps1`, build `tests/Performance`, and copy `kg_native_core.dll` to `tests/Performance/bin/Release/net8.0/`. Run the equivalent commands below. `encoder-failure` uses a Unix test executable; that specific test needs a Windows replacement. Metal, Vulkan and D3D12 should each be tested on the target platform before release.

```sh
dotnet build tests/Performance -c Release
dotnet tests/Performance/bin/Release/net8.0/Performance.dll cpu
dotnet tests/Performance/bin/Release/net8.0/Performance.dll skin 10000 12
dotnet tests/Performance/bin/Release/net8.0/Performance.dll skin 100000 12
dotnet tests/Performance/bin/Release/net8.0/Performance.dll skin 1000000 6
dotnet tests/Performance/bin/Release/net8.0/Performance.dll scene 1000000 6
dotnet tests/Performance/bin/Release/net8.0/Performance.dll ui 1000000 12
dotnet tests/Performance/bin/Release/net8.0/Performance.dll uniform 1000000 8
dotnet tests/Performance/bin/Release/net8.0/Performance.dll vsm 1000000 8
dotnet tests/Performance/bin/Release/net8.0/Performance.dll holds 10000 8
dotnet tests/Performance/bin/Release/net8.0/Performance.dll modulated 10000 8
dotnet tests/Performance/bin/Release/net8.0/Performance.dll hit 1000000 3
dotnet tests/Performance/bin/Release/net8.0/Performance.dll readback
dotnet tests/Performance/bin/Release/net8.0/Performance.dll shaders
dotnet tests/Performance/bin/Release/net8.0/Performance.dll cancel 1000
dotnet tests/Performance/bin/Release/net8.0/Performance.dll encoder-failure 1000
SDL_AUDIODRIVER=dummy dotnet tests/Performance/bin/Release/net8.0/Performance.dll audio
```

`skin` draws every note at the same Y with mixed chip, bumper and mine textures and alpha 0.7. `scene` uses the complete SceneRenderer before a simultaneous judgement. `hit` includes all judgement particles after the hit; it emits every sprite and reuses only the calculations of identical sprites. `ui` combines one million uniformly distributed notes with one million VSM events and calls the real Viewer.Draw. `uniform` exercises bounded rendering queries with one million total notes; `vsm` parses and evaluates one million events. `modulated` has 10,000 BPM changes and 10,000 Scroll/Velocity changes plus Wave, Driven, Boost and beat motion; run it with `KG_RUST_BRIDGE=verify`.

The render harness performs two unreported warmup frames. Its reported CPU phase includes CPU recording, uploads, native command submission and any driver/buffer backpressure. The second timing waits for a completed GPU frame and includes RGBA readback. This is a reproducible throughput test, **not interactive display FPS**. The JSON breaks out explicit GPU waits, recording, uploads, native motion, selection and geometry. Quantiles use the recorded samples, so P95/P99 of short 3–12 sample runs often equal the maximum; they are not a long-session latency estimate. Tiered JIT and GPU scheduling can still affect short runs.

Allocation measurements include the deliberately synchronous RGBA measurement readback (921,600 bytes at 640×360), unlike the pooled asynchronous export path. `sampledPeakWorkingSet` is sampled process memory; use `/usr/bin/time -l` on macOS for the OS maximum resident set size. The early measurement logs' `peakWorkingSet=0` is an unsupported .NET macOS peak counter, not zero memory use.

For an actual reference revision, export the baseline outside the workspace, compile this same harness with `-p:Baseline=true -p:KGProject=/absolute/baseline/KuroakiGimmick.csproj`, and build/copy that revision's Rust core into its harness output. On macOS use canonical `/private/tmp/...` paths, not the `/tmp` symlink, to avoid duplicate MSBuild embedded-resource names. Do not reset or replace the working tree. Assets must match between both runs.

For long runs, set `KG_PROFILE=1 KG_PROFILE_OUT=/absolute/path/profile.json` when running the application. Detailed logging stays off. The fixed sample window holds 4096 samples per stage; means/maxima cover the entire run and quantiles cover the retained window. GC pause values observe `GC.GetGCMemoryInfo().PauseDurations` on collection changes and may miss earlier collections between samples. Audio underruns count observed empty input queues while more PCM remains; the dummy-driver test does not measure physical device glitches. `VisibleNotes` is the renderer's conservative note culling count before Proxy composition, not GPU per-pixel occlusion.

Fallback switches: `KG_NOTE_INSTANCING=off`, `KG_NOTE_ATLAS=off`, `KG_JUDGMENT_INSTANCING=off`. These retain the old geometry/texture behavior. Rust `auto/off/verify/required` remain available; an older compatible core without the indexed entry point uses the legacy native batch. No note or particle quantity is capped. The 4096 motion block, 8192 sprite staging block, 64 MiB GPU recording boundary and bounded export channel limit temporary memory, not displayed content.

## Bounded real-workspace regression

Use the actual local song; these commands do not modify its source files. The Python guard stops only its child process group at 3 GiB RSS or the configured time budget. NativeAOT `--smoke-workspace-memory` copies user settings to a temporary file, installs the native menu, exercises the physical audio supply with volume zero, enters Editor, and switches back to Viewer every 30 frames near 124.72 seconds. The `KG_WORKSPACE_MEMORY_FRAMES` override is clamped to 1..10,000. `KG_SWITCH_PAUSED=1` fixes the dense time; `KG_WORKSPACE_MEMORY_LOOP=1` seeks back to that section at each switch during playing. Logs contain RSS/managed memory separately, not macOS Force Quit's application-memory metric.

```sh
KG_WORKSPACE_MEMORY_FRAMES=1500 KG_WORKSPACE_MEMORY_LOOP=1 python3 scripts/workspace-memory-check.py --log /tmp/kg-workspace.log --seconds 180 -- /absolute/path/KuroakiGimmick.app/Contents/MacOS/KuroakiGimmick /absolute/song/folder --smoke-workspace-memory
KG_SWITCH_PAUSED=1 KG_SWITCH_EDITOR_ONLY=1 KG_SWITCH_USER_SETTINGS=1 KG_SWITCH_AUDIO_MENU=1 python3 scripts/workspace-memory-check.py --log /tmp/kg-editor.log -- dotnet tests/Performance/bin/Release/net8.0/Performance.dll switch-memory /absolute/project.sgv.json 60
# Add KG_TIMELINE_INSTANCING=off to measure the five-rectangle compatibility path.
dotnet tests/Performance/bin/Release/net8.0/Performance.dll timeline-pixels
```

The guard is for controlled tests, not a production memory cutoff. No production notes or frames are removed to satisfy its budget. A passing short run does not establish absence of a long-term leak.
