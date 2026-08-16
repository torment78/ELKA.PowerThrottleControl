# ELKA Power Throttle Control

![ELKA Power Throttle Control](docs/assets/github-social-preview.jpg)

A focused Windows desktop application for managing per-application power throttling with Windows' built-in `powercfg` commands.

## Download

Download the current Windows installer or portable ZIP from the [Releases](https://github.com/torment78/ELKA.PowerThrottleControl/releases) page.

The installer is self-contained for 64-bit Windows; users do not need to install .NET separately.

## Features

- Discovers installed applications from machine-wide and per-user registry entries, App Paths, and Start Menu shortcuts.
- Select one or many executable-backed applications.
- Disable or enable power throttling through a visible elevated Command Prompt.
- Open Windows' authoritative `powercfg /powerthrottling list` output.
- Remembers the displayed application state between runs.
- A general Network workspace searches all discovered applications and filters them by name or executable path.
- Opens TCP, UDP, or both for single ports, comma-separated port lists, and ranges such as `80,443,5000-5010`.
- Configures inbound, outbound, or both directions on selectable Private, Domain, and Public profiles.
- A separate VBAN workspace detects running and installed VoiceMeeter, Macro Buttons, VBAN, Matrix, and Matrix Coconut executables, including x86 and x64 editions.
- Offers confirmed advanced full-traffic options, targeted ELKA-rule removal, and elevated rule lists.
- Light, dark, and Windows-system themes.
- The main app runs normally; elevation is requested only for `powercfg` and firewall actions.

## Commands used

```text
powercfg /powerthrottling disable /path "<full executable path>"
powercfg /powerthrottling enable /path "<full executable path>"
powercfg /powerthrottling list
```

## Firewall commands

The general Network workspace creates deterministic, removable program rules for the selected protocol, direction, ports, and profiles. For example:

```text
netsh advfirewall firewall add rule name="ELKA Network - <app> - TCP - In" dir=in action=allow program="<full executable path>" protocol=TCP localport=80,443,5000-5010 profile=private enable=yes
netsh advfirewall firewall add rule name="ELKA Network - <app> - TCP - Out" dir=out action=allow program="<full executable path>" protocol=TCP remoteport=80,443,5000-5010 profile=private enable=yes
```

The dedicated VBAN workspace retains its focused defaults. VBAN uses UDP port 6980 by default according to the [official VoiceMeeter documentation](https://vb-audio.com/Voicemeeter/VoicemeeterBanana_UserManual.pdf).
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
.\scripts\Build-Release.ps1 -Version 1.2.0
```

Outputs are written under `artifacts/installer`.

## State storage

Application state and theme preferences are stored per user under:

```text
%LOCALAPPDATA%\ELKA.PowerThrottleControl
```

## License

MIT. See [LICENSE](LICENSE).
