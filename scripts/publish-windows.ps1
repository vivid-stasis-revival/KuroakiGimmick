param([ValidateSet('x64','arm64','all')][string]$Arch = 'x64')
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$ProjectRoot = Split-Path $PSScriptRoot -Parent
$Dotnet = if ($env:KUROAKI_DOTNET) { $env:KUROAKI_DOTNET } else { 'dotnet' }
$null = Get-Command $Dotnet -ErrorAction Stop
$ProjectFile = Join-Path $ProjectRoot 'KuroakiGimmick.csproj'
$Version = ((& $Dotnet msbuild $ProjectFile -getProperty:Version -nologo) | Out-String).Trim()
if ($LASTEXITCODE -ne 0) { throw 'Unable to read project Version.' }
$BuildNumber = ((& $Dotnet msbuild $ProjectFile -getProperty:BuildNumber -nologo) | Out-String).Trim()
if ($LASTEXITCODE -ne 0 -or -not $Version -or -not $BuildNumber) { throw 'Unable to read project build version.' }
$Dist = Join-Path $ProjectRoot 'dist'
$null = New-Item -ItemType Directory -Path $Dist -Force
$Architectures = if ($Arch -eq 'all') { @('x64','arm64') } else { @($Arch) }
foreach ($Cpu in $Architectures) {
    $Rid = "win-$Cpu"
    $Stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
    $Name = "KuroakiGimmick-v$Version-$Rid-$BuildNumber-$Stamp"
    $Destination = Join-Path $Dist $Name
    if ((Test-Path $Destination) -or (Test-Path "$Destination.zip")) { throw "Output already exists: $Destination" }
    $Stage = Join-Path $Dist ('.publish-' + [guid]::NewGuid().ToString('N'))
    $Package = Join-Path $Stage $Name
    $Log = Join-Path $Dist "publish-$Rid-$Stamp.log"
    $null = New-Item -ItemType Directory -Path $Stage
    try {
        & $Dotnet publish $ProjectFile -c Release -r $Rid `
            --self-contained true --nologo -o $Package `
            -p:UseAppHost=true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishTrimmed=false `
            -p:PublishReadyToRun=false -p:DebugType=None -p:DebugSymbols=false `
            "-p:NuGetLockFilePath=$Stage/publish.lock.json" 2>&1 | Tee-Object -FilePath $Log
        if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed; see $Log" }
        foreach ($Required in @('KuroakiGimmick.exe','Assets/GameUI/game-ui.gameui.json','Assets/Fonts/cjk-editor.png','Assets/Fonts/cjk-editor.json','Assets/Fonts/editor-help-sans.png','Assets/Fonts/editor-help-sans.json','Assets/Fonts/editor-help-sans-bold.png','Assets/Fonts/editor-help-sans-bold.json','Assets/GimmickExtras/scene_gameplay.runtime.fx.json','Assets/Documentation/vsm-reference.json')) {
            if (-not (Test-Path (Join-Path $Package $Required))) { throw "Publish output missing: $Required" }
        }
        # The package root must hold the single .exe and nothing else executable:
        # loose .dll/.deps.json/.runtimeconfig.json means PublishSingleFile did not apply.
        $LooseBuildFiles = @(Get-ChildItem -LiteralPath $Package -File | Where-Object { $_.Extension -in @('.dll','.json','.pdb') })
        if ($LooseBuildFiles.Count -gt 0) { throw "Single-file output leaked build files: $($LooseBuildFiles.Name -join ', ')" }
        foreach ($Item in @('README.md','CHANGELOG.md','THIRD_PARTY_NOTICES.md','ThirdParty','docs')) {
            Copy-Item -LiteralPath (Join-Path $ProjectRoot $Item) -Destination $Package -Recurse
        }
        if ($env:KUROAKI_FFMPEG) {
            Copy-Item -LiteralPath $env:KUROAKI_FFMPEG -Destination (Join-Path $Package 'ffmpeg.exe')
        }
        Compress-Archive -LiteralPath $Package -DestinationPath (Join-Path $Stage "$Name.zip")
        Move-Item -LiteralPath $Package -Destination $Destination
        Move-Item -LiteralPath (Join-Path $Stage "$Name.zip") -Destination "$Destination.zip"
        Write-Host "Published: $Destination.zip"
        Write-Host 'This local package contains private vivid/stasis Assets. Do not upload it publicly. Keep the whole folder when moving the app.'
    }
    finally {
        Remove-Item -LiteralPath $Stage -Recurse -Force
    }
}
