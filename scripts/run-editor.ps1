param([string]$Project = 'Samples/EditorDemo/demo.sgv.json')
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
$Dotnet = if ($env:KUROAKI_DOTNET) { $env:KUROAKI_DOTNET } else { 'dotnet' }
$null = Get-Command $Dotnet -ErrorAction Stop
& $Dotnet build KuroakiGimmick.csproj -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw 'Build failed. See compiler output above.' }
& $Dotnet run --project KuroakiGimmick.csproj -c Release --no-build -- --editor $Project
exit $LASTEXITCODE
