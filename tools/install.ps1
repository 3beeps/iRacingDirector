# Builds iRacing Director and installs it for the current user.
#   powershell -ExecutionPolicy Bypass -File tools/install.ps1
# Installs to %LOCALAPPDATA%\Programs\iRacing Director and adds a Start Menu shortcut.
# Re-run any time to update; settings in %APPDATA%\iRacing Director are kept.
$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..')
$publish = Join-Path $root 'publish'
$target = Join-Path $env:LOCALAPPDATA 'Programs\iRacing Director'

dotnet publish (Join-Path $root 'src\iRacingDirector\iRacingDirector.csproj') -c Release -r win-x64 --self-contained false `
    -p:PublishSingleFile=true -p:DebugType=none -o $publish
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

# Close a running copy so the exe can be replaced.
Get-Process iRacingDirector -ErrorAction SilentlyContinue | Where-Object { $_.Path -like "$target*" } | ForEach-Object {
    Write-Host "Closing running iRacing Director..."
    $_.CloseMainWindow() | Out-Null
    Start-Sleep 1
    if (-not $_.HasExited) { Stop-Process $_ -Force }
}

New-Item -ItemType Directory -Force $target | Out-Null
Copy-Item (Join-Path $publish 'iRacingDirector.exe') $target -Force

$shortcut = Join-Path ([Environment]::GetFolderPath('Programs')) 'iRacing Director.lnk'
$shell = New-Object -ComObject WScript.Shell
$lnk = $shell.CreateShortcut($shortcut)
$lnk.TargetPath = Join-Path $target 'iRacingDirector.exe'
$lnk.WorkingDirectory = $target
$lnk.Description = 'Launches and closes your sim racing apps alongside iRacing'
$lnk.Save()

Write-Host "Installed to $target"
Write-Host "Start Menu shortcut: $shortcut"
