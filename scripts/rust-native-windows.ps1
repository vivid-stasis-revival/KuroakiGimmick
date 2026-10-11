# Native Rust DLL staging for the PowerShell Windows publisher.
# Requires rustup/cargo and the Visual Studio linker for MSVC targets.
function Build-KgRustNative {
    param([Parameter(Mandatory)][string]$Rid,
          [Parameter(Mandatory)][string]$OutputPath)
    $Root = Split-Path $PSScriptRoot -Parent
    foreach ($Tool in @('rustup','rustc','cargo')) {
        if (-not (Get-Command $Tool -ErrorAction SilentlyContinue)) {
            throw "Rust Optimization Engine is required for publish: missing $Tool."
        }
    }
    $HostTriple = ((& rustc -vV | Select-String '^host:').ToString() -replace '^host:\s*','').Trim()
    if ($LASTEXITCODE -ne 0 -or -not $HostTriple) { throw 'Cannot determine Rust host target.' }
    switch ($Rid) {
        'win-x64' {
            if ($HostTriple -match '^x86_64-pc-windows-(msvc|gnu)$') { $Target = $HostTriple }
            else { $Target = 'x86_64-pc-windows-msvc' }
        }
        'win-arm64' { $Target = 'aarch64-pc-windows-msvc' }
        default { throw "Unsupported Rust Windows RID: $Rid" }
    }
    $InstalledTargets = @(& rustup target list --installed)
    if ($LASTEXITCODE -ne 0) { throw 'Cannot list installed Rust targets.' }
    if ($Target -notin $InstalledTargets) {
        & rustup target add $Target
        if ($LASTEXITCODE -ne 0) { throw "Failed installing Rust target $Target" }
    }
    Write-Host "Building Rust Optimization Engine for $Rid ($Target)"
    & cargo build --manifest-path (Join-Path $Root 'rust/kg-native-core/Cargo.toml') --release --target $Target `
        --target-dir (Join-Path $Root 'rust/kg-native-core/target')
    if ($LASTEXITCODE -ne 0) { throw "Rust build failed for $Target" }
    $Dll = Join-Path $Root "rust/kg-native-core/target/$Target/release/kg_native_core.dll"
    if (-not (Test-Path $Dll) -or (Get-Item $Dll).Length -eq 0) { throw "Rust DLL missing: $Dll" }
    $null = New-Item -ItemType Directory -Force -Path $OutputPath
    Copy-Item -LiteralPath $Dll -Destination (Join-Path $OutputPath 'kg_native_core.dll') -Force
    if ((Get-Item (Join-Path $OutputPath 'kg_native_core.dll')).Length -eq 0) { throw 'Staged Rust DLL is empty.' }
    Write-Host "Staged Rust Optimization Engine: $OutputPath/kg_native_core.dll"
}
