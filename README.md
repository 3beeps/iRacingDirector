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

To update, send the new installer. It closes the running copy and keeps their settings. You can uninstall from Windows Settings > Apps, and it asks whether to delete your settings too.

Settings and logs live in `%APPDATA%\iRacing Director`.

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
- `tools/make-icon.ps1`: regenerates the app icon

To recognize another app automatically, add it to `DefaultApps.Catalog`.

Command-line flags:

- `--no-elevate` skips the automatic administrator relaunch.
- `--tray` starts hidden in the tray.
- `--exit` closes a running copy (used by the installer).
- `--uninstall` closes it and removes the startup entry (used by the uninstaller).
- `IRACING_DIRECTOR_DATA=<folder>` runs a separate copy against a different settings folder.
