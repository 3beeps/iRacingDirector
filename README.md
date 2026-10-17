# iRacing Director

A small Windows tray app that starts your sim racing support apps when an iRacing session starts and closes them when it ends. It replaces "iRacing Manager".

## What it does

- **Watches for the sim** (`iRacingSim64DX11.exe`). When the sim starts, Director starts every app set to *Auto start*, in list order, with optional per-app delays.
- **Closes apps when you're done.** After the sim exits, Director waits a short grace period (15s by default). If you join another session in that time, nothing closes. After that it asks each *Auto close* app to close, then force-closes anything still running.
- **Handles admin apps.** With *Run as administrator* on, Director can close apps like SimHub that run elevated (iRacing Manager failed with "Access is denied" here). When *Start when I sign in* is also on, Director uses an elevated scheduled task, so there's no UAC prompt at sign-in.
- **Handles launcher-style apps.** *Extra process names* (wildcards allowed) let Director track and close apps whose launcher starts a differently named process. For example, Garage 61 uses `garage61-agent*`.
- **Gives you quick controls** in the window and the tray menu: Launch iRacing, Start apps now, Close apps now, Close iRacing and apps, and an Automatic mode toggle.
- **Imports from iRacing Manager** (`Documents\iRacingManager\settings.xml`). This happens automatically on first run, or from the button on the Apps page.

## Install / update

Requires the .NET 8 Desktop Runtime (included with the .NET 8 SDK).

```bash
powershell -ExecutionPolicy Bypass -File tools/install.ps1
```

This installs to `%LOCALAPPDATA%\Programs\iRacing Director` and adds a Start Menu shortcut. Settings and logs live in `%APPDATA%\iRacing Director`.

## Develop

```bash
dotnet build iRacingDirector.sln
```

```bash
dotnet test iRacingDirector.sln
```

- `src/iRacingDirector/Core`: models, session state machine, process matching (no UI)
- `src/iRacingDirector/Services`: Director orchestrator, process control, settings, startup and elevation, importer
- `src/iRacingDirector/UI`: WPF windows, view model, tray icon
- `tools/make-icon.ps1`: regenerates the app icon

Troubleshooting flags:

- `--no-elevate` skips the automatic administrator relaunch.
- `--tray` starts hidden in the tray.
- `IRACING_DIRECTOR_DATA=<folder>` runs a separate copy against a different settings folder.
