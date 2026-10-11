#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
cargo test --manifest-path rust/kg-native-core/Cargo.toml
cargo build --release --manifest-path rust/kg-native-core/Cargo.toml
case "$(uname -s)" in
  Darwin) name=libkg_native_core.dylib ;;
  Linux) name=libkg_native_core.so ;;
  *) echo 'Windows: use scripts/build-rustbridge.ps1 instead' >&2; exit 2 ;;
esac
# dotnet build (no RID) keeps a JIT fallback build available for fast verification.
dotnet build -c Release -p:PublishAot=false
out="bin/Release/net8.0"
mkdir -p "$out"
cp "rust/kg-native-core/target/release/$name" "$out/$name"
KG_RUST_BRIDGE=verify dotnet run -c Release --no-build -- --rust-bridge-test
KG_RUST_ENGINE=verify dotnet run -c Release --no-build -- --rust-engine-test
KG_RUST_ENGINE=verify dotnet run -c Release --no-build -- --rust-timeline-test
KG_RUST_AUDIO_REQUIRED=1 dotnet run -c Release --no-build -- --rust-audio-test
printf '\nNative core copied to %s/%s\n' "$out" "$name"
printf 'Run K/G normally: KG_RUST_BRIDGE=verify dotnet run -c Release --no-build\n'
