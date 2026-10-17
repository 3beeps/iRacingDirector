# iRacing Director

A small Windows tray app that starts your sim racing support apps when an iRacing session starts and closes them when it ends. It replaces "iRacing Manager".

## What it does

- **Sets itself up for each person.** On first run it finds iRacing and the sim racing apps on that PC (SimHub, Crew Chief, Garage 61, Trading Paints, VRS One, iOverlay, RaceLab, Kapps, trophi.ai, wheelbase software and more). You switch on the ones you want. Any other program can be added from a searchable list of your Start Menu, or by browsing for the file.
- **Watches for the sim** (`iRacingSim64DX11.exe`). When the sim starts, Director starts every app set to *Auto start*, in list order, with optional per-app delays.
- **Closes apps when you're done.** After the sim exits, Director waits a short grace period (15s by default). If you join another session in that time, nothing closes. After that it asks each *Auto close* app to close, then force-closes anything still running.
- **Handles admin apps.** With *Run as administrator* on, Director can close apps like SimHub that run elevated. When *Start when I sign in* is also on, Director uses an elevated scheduled task, so there's no UAC prompt at sign-in. Setup turns this on automatically when it sees an app that needs it.
- **Handles launcher-style apps.** *Extra process names* (wildcards allowed) let Director track and close apps whose launcher starts a differently named process. For example, Garage 61 uses `garage61-agent*`. Known apps come with this preset.
- **Gives you quick controls** in the window and the tray menu: Launch iRacing, Start apps now, Close apps now, Close iRacing and apps, and an Automatic mode toggle.
- **Imports from iRacing Manager** if it finds `Documents\iRacingManager\settings.xml`.

## Sharing with friends

Build the installer:

```bash
powershell -ExecutionPolicy Bypass -File tools/build-installer.ps1
```

Send them `dist\iRacingDirector-Setup-<version>.exe` (about 47 MB). It includes everything it needs, so friends don't need .NET. It installs without admin rights to `%LOCALAPPDATA%\Programs\iRacing Director` and adds a Start Menu shortcut.

What your friends will see:

1. **"Windows protected your PC"**: the installer isn't code-signed, so SmartScreen warns about it. Click **More info**, then **Run anyway**.
2. **The setup screen** on first launch, listing the racing apps it found. They switch on the ones they use, then click **Finish setup**.
3. **An administrator prompt**, if they kept *Run as administrator* on (recommended with SimHub). They only see it once; after that Director starts at sign-in without asking.

Friends get new versions on their own (see [Releasing an update](#releasing-an-update)). Director checks GitHub when it starts and once a day. When there's a newer release it shows an **Update available** banner at the top of the window and a tray notification. Clicking the banner downloads the new installer and runs it silently: Director closes, updates, keeps its settings and starts again. Friends can turn this off under Settings > *Check for updates automatically*. You can uninstall from Windows Settings > Apps, and it asks whether to delete your settings too.

Settings and logs live in `%APPDATA%\iRacing Director`.

## Releasing an update

Releases are published to GitHub ([3beeps/iRacingDirector](https://github.com/3beeps/iRacingDirector/releases)), which is where the update check looks. The repo must stay public so friends' copies can read releases without signing in.

One-time setup:

```bash
winget install GitHub.cli
```

```bash
gh auth login
```

You also need Inno Setup (`winget install JRSoftware.InnoSetup`), the same as for building the installer.

To release, commit your changes, then run:

```bash
powershell -ExecutionPolicy Bypass -File tools/release.ps1 -Version 1.2.0 -Notes "What changed"
```

The script:

1. Checks that the working tree is clean and that the version is newer than the one in `iRacingDirector.csproj`.
2. Sets `<Version>` in `src/iRacingDirector/iRacingDirector.csproj`.
3. Runs `tools/build-installer.ps1` to build `dist\iRacingDirector-Setup-1.2.0.exe`.
4. Commits the version bump as "Release v1.2.0", tags it `v1.2.0`, and pushes the commit and tag.
5. Runs `gh release create v1.2.0` with the setup exe attached. Without `-Notes`, GitHub writes the notes from the commits.

Running copies pick it up at their next start or within 24 hours. The update check only uses the latest non-draft, non-prerelease release, and only if its tag looks like `vX.Y.Z` and it has an `iRacingDirector-Setup-*.exe` asset. Copies older than 1.2.0 have no update check, so friends on those still need the installer sent once.

## Develop

```bash
dotnet build iRacingDirector.sln
```

```bash
dotnet test iRacingDirector.sln
```

- `src/iRacingDirector/Core`: models, session state machine, process matching (no UI)
- `src/iRacingDirector/Services`: Director orchestrator, app catalog and detection, process control, settings, startup and elevation, importer
- `src/iRacingDirector/UI`: WPF windows (main, setup, program picker, app editor), view model, tray icon
- `installer/iRacingDirector.iss`: Inno Setup script (`winget install JRSoftware.InnoSetup`)
- `tools/build-installer.ps1`: self-contained publish plus installer
- `tools/release.ps1`: version bump, installer, tag and GitHub release (see above)
- `tools/make-icon.ps1`: regenerates the app icon

To recognize another app automatically, add it to `DefaultApps.Catalog`.

Command-line flags:

- `--no-elevate` skips the automatic administrator relaunch.
- `--tray` starts hidden in the tray.
- `--exit` closes a running copy (used by the installer).
- `--uninstall` closes it and removes the startup entry (used by the uninstaller).
- `IRACING_DIRECTOR_DATA=<folder>` runs a separate copy against a different settings folder.
