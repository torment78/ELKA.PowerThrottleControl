# ELKA Power Throttle Control

![ELKA Power Throttle Control](docs/assets/github-social-preview.jpg)

A focused Windows desktop application for managing per-application power throttling with Windows' built-in `powercfg` commands.

## Download

Download the current Windows installer or portable ZIP from the [Releases](https://github.com/torment78/ELKA.PowerThrottleControl/releases) page.

The installer is self-contained for 64-bit Windows; users do not need to install .NET separately.

## Features

- Discovers executable-backed applications from machine-wide and per-user registry entries, App Paths, and Start Menu shortcuts.
- Select one or many applications.
- Disable or enable power throttling through a visible elevated Command Prompt.
- Reads Windows' authoritative `powercfg /powerthrottling list` output and synchronizes every row with the real system state.
- Green means Windows lists the application as never throttled; red means enabled/default; gray means the state has not been confirmed.
- UAC cancellation and command failures leave status gray instead of showing an incorrect value.
- Light, dark, and Windows-system themes.
- The main app runs normally; elevation is requested only for `powercfg` actions and status queries.

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

To build the self-contained portable package and installer, install Inno Setup 6 and run:

```powershell
.\scripts\Build-Release.ps1 -Version 1.3.1
```

Outputs are written under `artifacts/installer`.

## Settings storage

The theme preference is stored per user under:

```text
%LOCALAPPDATA%\ELKA.PowerThrottleControl
```

## License

MIT. See [LICENSE](LICENSE).
