# ELKA Power Throttle Control

![ELKA Power Throttle Control](docs/assets/github-social-preview.jpg)

A focused Windows desktop application for managing per-application power throttling with Windows' built-in `powercfg` commands.

## Download

Download the current Windows installer or portable ZIP from the [Releases](https://github.com/torment78/ELKA.PowerThrottleControl/releases) page.

The installer is self-contained for 64-bit Windows; users do not need to install .NET separately.

The standard install folder is `C:\Program Files\ElkaSoft\ELKA Power Throttle Control`.
Upgrading from the old `Elka Software` default folder relocates the application through its registered uninstaller; per-user settings and Windows throttling rules are preserved. A deliberately chosen custom installation folder is still remembered.

## Features

- Discovers executable-backed applications from machine-wide and per-user registry entries, App Paths, and Start Menu shortcuts.
- Select one or many applications.
- Disable or enable power throttling through a visible elevated Command Prompt.
- Reads Windows' authoritative `powercfg /powerthrottling list` output and synchronizes every row with the real system state.
- Green means Windows lists the application as never throttled; red means enabled/default; gray means the state has not been confirmed.
- UAC cancellation and command failures leave status gray instead of showing an incorrect value.
- Light, dark, and Windows-system themes.
- Check for updates from GitHub, then click again to download the available release.
- Installed copies verify and run Setup, then reopen automatically. Portable copies download the ZIP alongside the app and open its folder.
- The main app runs normally; only `powercfg` actions, status queries, and the update installer request elevation.

Network and VBAN firewall management were removed in version 1.3.0 so the application is once again dedicated solely to power throttling.

## Commands used

```text
powercfg /powerthrottling disable /path "<full executable path>"
powercfg /powerthrottling enable /path "<full executable path>"
powercfg /powerthrottling list
```

## Build from source

Requirements:

- Windows 10 or later
- .NET 8 SDK
- Visual Studio 2022/Insider with the .NET desktop workload

Open `ELKA.PowerThrottleControl.sln` and press F5, or run:

```powershell
dotnet build ELKA.PowerThrottleControl.sln
```

To build the self-contained portable package and installer, install Inno Setup 6.6 or newer and run:

```powershell
.\scripts\Build-Release.ps1 -Version 1.3.3
```

Outputs are written under `artifacts/installer`.

Run the isolated settings migration checks with `dotnet run --project tests/SettingsMigrationChecks`. These checks do not touch your real saved settings.

Updater checks: `dotnet run --project tests/UpdateChecks` and `powershell -NoProfile -File tests/UpdateChecks/Test-UpdateRunner.ps1`. Installer launches are mocked; these checks do not install or elevate anything. For a read-only check against the public GitHub release, append `-- --live` to the updater checks command; `-- --live --download` also downloads and verifies both packages in temporary folders without executing them.

## Settings storage

The theme preference is stored per user under:

```text
%LOCALAPPDATA%\ElkaSoft\ELKA.PowerThrottleControl\theme.txt
```

On first launch, an existing theme preference from `%LOCALAPPDATA%\ELKA.PowerThrottleControl\theme.txt` is copied to the new location. The old file is kept as a backup and an existing new preference is never overwritten during migration.

Future saved settings belong in this same per-app folder under `ElkaSoft`, never under Program Files. Power throttling rules remain managed by Windows. Temporary command files use `%TEMP%\ElkaSoft\ELKA.PowerThrottleControl` and are cleaned up after each operation.

## Updating

Use **Check for updates** below the theme button. The first click checks the latest stable release in this repository. A second click downloads the appropriate package and verifies its SHA-256 checksum before using it.

- **Installed copy:** identified by matching the running executable to this app's registered installation path. The app closes after preparing the update helper, Setup requests administrator permission and displays installation progress, and the app reopens as the original desktop user. Cancelling elevation or Setup reopens the existing app. Downloads and diagnostic logs are under `%LOCALAPPDATA%\ElkaSoft\ELKA.PowerThrottleControl\Updates`.
- **Portable copy:** downloads the ZIP into an `Updates` subfolder beside the running app, including on a USB drive, then opens Explorer with the ZIP selected. The app stays open. Extract the ZIP and replace your portable copy when ready; no installer or elevation is used. The portable folder must be writable. Settings continue to use the per-user location documented above.

Legacy settings are copied on first use, never removed. A valid preference already in the new folder takes priority over the legacy backup.

## License

MIT. See [LICENSE](LICENSE).
