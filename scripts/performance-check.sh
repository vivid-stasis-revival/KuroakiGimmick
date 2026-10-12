#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
kg_root="$(pwd -P)"
kg_results="${1:-$kg_root/tests/Performance/results/local}"
mkdir -p "$kg_results"
cargo test --manifest-path rust/kg-native-core/Cargo.toml > "$kg_results/rust-tests.txt" 2>&1
cargo build --release --manifest-path rust/kg-native-core/Cargo.toml > "$kg_results/rust-build.txt" 2>&1
dotnet build tests/Performance -c Release > "$kg_results/build.txt" 2>&1
dotnet build tests/Regression -c Release > "$kg_results/regression-build.txt" 2>&1
case "$(uname -s)" in
  Darwin) kg_native=libkg_native_core.dylib ;;
  Linux) kg_native=libkg_native_core.so ;;
  *) echo 'Use the commands in tests/Performance/README.md on Windows.' >&2; exit 2 ;;
esac
cp "rust/kg-native-core/target/release/$kg_native" bin/Release/net8.0/
cp "rust/kg-native-core/target/release/$kg_native" tests/Performance/bin/Release/net8.0/
cp "rust/kg-native-core/target/release/$kg_native" tests/Regression/bin/Release/net8.0/
KG_RUST_ENGINE=verify KG_RUST_BRIDGE=verify dotnet run --project tests/Regression -c Release > "$kg_results/regression.txt" 2>&1
kg_bench="tests/Performance/bin/Release/net8.0/Performance.dll"
for kg_case in 'cpu 0 0' 'skin 10000 12' 'skin 100000 12' 'skin 1000000 6' 'scene 200 30' 'scene 1000000 6' 'ui 1000000 12' 'uniform 1000000 8' 'vsm 1000000 8' 'holds 10000 8' 'modulated 10000 8' 'hit 1000000 3' 'readback 0 0' 'shaders 0 0' 'timeline-pixels 0 0' 'cancel 1000 0' 'encoder-failure 1000 0'; do
  read -r kg_mode kg_count kg_samples <<< "$kg_case"
  echo "Running $kg_mode $kg_count"
  KG_RUST_ENGINE=auto KG_RUST_BRIDGE=verify dotnet "$kg_bench" "$kg_mode" "$kg_count" "$kg_samples" > "$kg_results/$kg_mode-$kg_count.txt" 2>&1
done
SDL_AUDIODRIVER=dummy dotnet "$kg_bench" audio > "$kg_results/audio.txt" 2>&1
# Compare to the retained geometry/standalone-texture compatibility implementation.
KG_NOTE_INSTANCING=off KG_NOTE_ATLAS=off KG_JUDGMENT_INSTANCING=off dotnet "$kg_bench" pixels 0 0 "$kg_results/legacy.rgba" > "$kg_results/pixels-legacy.txt" 2>&1
dotnet "$kg_bench" pixels 0 0 "$kg_results/instances.rgba" > "$kg_results/pixels-instances.txt" 2>&1
cmp "$kg_results/legacy.rgba" "$kg_results/instances.rgba"
KG_JUDGMENT_INSTANCING=off dotnet "$kg_bench" hit 200 4 "$kg_results/hit-legacy.rgba" > "$kg_results/hit-legacy.txt" 2>&1
dotnet "$kg_bench" hit 200 4 "$kg_results/hit-instances.rgba" > "$kg_results/hit-instances.txt" 2>&1
cmp "$kg_results/hit-legacy.rgba" "$kg_results/hit-instances.rgba"
dotnet "$kg_bench" export 10000 0 "$kg_results/export.mp4" > "$kg_results/export.txt" 2>&1
ffprobe -v error -count_frames -show_entries stream=nb_read_frames,r_frame_rate,width,height -of json "$kg_results/export.mp4" > "$kg_results/export-probe.json"
echo "PASS. Results: $kg_results"
