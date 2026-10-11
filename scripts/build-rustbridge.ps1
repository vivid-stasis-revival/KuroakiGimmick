$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '..')
cargo test --manifest-path rust/kg-native-core/Cargo.toml
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
cargo build --release --manifest-path rust/kg-native-core/Cargo.toml
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
dotnet build -c Release -p:PublishAot=false
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
$out = 'bin/Release/net8.0'
New-Item -ItemType Directory -Force -Path $out | Out-Null
Copy-Item rust/kg-native-core/target/release/kg_native_core.dll "$out/kg_native_core.dll" -Force
$env:KG_RUST_BRIDGE = 'verify'
dotnet run -c Release --no-build -- --rust-bridge-test
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
$env:KG_RUST_ENGINE = 'verify'
dotnet run -c Release --no-build -- --rust-engine-test
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
dotnet run -c Release --no-build -- --rust-timeline-test
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
$env:KG_RUST_AUDIO_REQUIRED = '1'
dotnet run -c Release --no-build -- --rust-audio-test
exit $LASTEXITCODE
