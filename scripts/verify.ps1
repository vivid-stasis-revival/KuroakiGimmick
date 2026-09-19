param([switch]$Gpu)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$Root = Split-Path $PSScriptRoot -Parent
$Dotnet = if ($env:KUROAKI_DOTNET) { $env:KUROAKI_DOTNET } else { 'dotnet' }
$null = Get-Command $Dotnet -ErrorAction Stop
$Project = Join-Path $Root 'KuroakiGimmick.csproj'

# PowerShell does not automatically throw for a native process's nonzero exit.
# Check every command, so an old output cannot impersonate a successful build.
& $Dotnet restore $Project --locked-mode
if ($LASTEXITCODE -ne 0) { throw 'Dependency restore failed; build/tests were not run.' }
& $Dotnet build $Project -c Release --no-restore
if ($LASTEXITCODE -ne 0) { throw 'Compilation failed; runtime tests were not run.' }
$Dll = Join-Path $Root 'bin/Release/net8.0/KuroakiGimmick.dll'
foreach ($Test in @('--self-test','--editor-self-test','--reference-self-test','--authoring-self-test','--text-film-self-test','--custom-adaptation-self-test','--native-sequence-self-test','--layout-image-self-test','--image-object-self-test')) {
    & $Dotnet $Dll $Test
    if ($LASTEXITCODE -ne 0) { throw "Managed self-test failed: $Test" }
}
if ($Gpu) {
    & $Dotnet $Dll --gpu-test
    if ($LASTEXITCODE -ne 0) { throw 'GPU self-tests failed.' }
} else {
    Write-Host 'SKIP GPU runtime tests; use -Gpu on a supported local machine.'
}
