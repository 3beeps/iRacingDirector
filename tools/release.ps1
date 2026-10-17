# Publishes a new version that friends' copies pick up through the in-app update check.
#   powershell -ExecutionPolicy Bypass -File tools/release.ps1 -Version 1.2.0
#   powershell -ExecutionPolicy Bypass -File tools/release.ps1 -Version 1.2.0 -Notes "Fixes SimHub closing too early."
# Bumps <Version> in the csproj, builds the installer, commits, tags vX.Y.Z, pushes,
# and creates the GitHub release with the setup exe attached. Needs gh (signed in) and Inno Setup.
param(
    [Parameter(Mandatory = $true)][string]$Version,
    [string]$Notes
)
$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..')
$project = Join-Path $root 'src\iRacingDirector\iRacingDirector.csproj'

function Invoke-Checked {
    & $args[0] $args[1..($args.Length - 1)]
    if ($LASTEXITCODE -ne 0) { throw "$($args[0]) $($args[1]) failed" }
}

$gh = @(
    (Get-Command gh.exe -ErrorAction SilentlyContinue).Source,
    "$env:ProgramFiles\GitHub CLI\gh.exe"
) | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
if (-not $gh) { throw "GitHub CLI not found. Install it with: winget install GitHub.cli" }

$Version = $Version.TrimStart('v', 'V')
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw "Version must look like 1.2.0" }
$tag = "v$Version"

Push-Location $root
try {
    & $gh auth status *> $null
    if ($LASTEXITCODE -ne 0) { throw "Sign in to GitHub first: gh auth login" }
    if (git status --porcelain) { throw "Commit or stash your changes first; the release is built from a clean tree." }
    if (git tag --list $tag) { throw "Tag $tag already exists." }

    $text = [IO.File]::ReadAllText($project)
    $current = [regex]::Match($text, '<Version>([^<]+)</Version>').Groups[1].Value
    if (-not $current) { throw "No <Version> in $project" }
    if ([version]$Version -le [version]$current) { throw "$Version isn't newer than the current version $current." }

    Write-Host "Releasing $current -> $Version"
    [IO.File]::WriteAllText($project, $text.Replace("<Version>$current</Version>", "<Version>$Version</Version>"))
    try {
        & (Join-Path $PSScriptRoot 'build-installer.ps1')
    }
    catch {
        [IO.File]::WriteAllText($project, $text)
        throw
    }

    $setup = Join-Path $root "dist\iRacingDirector-Setup-$Version.exe"
    if (-not (Test-Path $setup)) { throw "Installer not found: $setup" }

    Invoke-Checked git add $project
    Invoke-Checked git commit -m "Release $tag"
    Invoke-Checked git tag -a $tag -m "iRacing Director $Version"
    Invoke-Checked git push origin HEAD
    Invoke-Checked git push origin $tag

    $notesArgs = if ($Notes) { @('--notes', $Notes) } else { @('--generate-notes') }
    Invoke-Checked $gh release create $tag $setup --title "iRacing Director $Version" @notesArgs

    Write-Host ""
    Write-Host "Released $tag. Running copies will offer the update within a day (or at their next start)."
}
finally {
    Pop-Location
}
