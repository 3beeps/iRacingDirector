# Builds dist/iRacingDirector-Setup-<version>.exe: one file you can send to friends.
#   powershell -ExecutionPolicy Bypass -File tools/build-installer.ps1
# The app is published self-contained, so friends don't need .NET installed.
$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..')
$project = Join-Path $root 'src\iRacingDirector\iRacingDirector.csproj'
$publish = Join-Path $root 'publish'

[xml]$csproj = Get-Content $project
$version = $csproj.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1

Remove-Item $publish -Recurse -Force -ErrorAction SilentlyContinue
dotnet publish $project -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=none -o $publish
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

$iscc = @(
    (Get-Command iscc.exe -ErrorAction SilentlyContinue).Source,
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
) | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
if (-not $iscc) { throw "Inno Setup not found. Install it with: winget install JRSoftware.InnoSetup" }

& $iscc "/DAppVersion=$version" (Join-Path $root 'installer\iRacingDirector.iss')
if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed" }

$setup = Join-Path $root "dist\iRacingDirector-Setup-$version.exe"
Write-Host ""
Write-Host "Installer ready: $setup ($([math]::Round((Get-Item $setup).Length / 1MB, 1)) MB)"
