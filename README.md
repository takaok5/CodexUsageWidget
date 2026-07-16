# ChatGPT Codex Usage Widget for Windows 11

A lightweight, Codex-only taskbar widget built with **C#**, **.NET 8**, and
**WPF**. It reads local Codex session data and keeps the weekly remaining
allowance visible without opening a usage dashboard.

![ChatGPT Codex Usage Widget v2.0.0](screenshots/chatgpt-codex-usage-widget-v2.0.0.png)

## Highlights

- Native C#/.NET 8 WPF application—no PowerShell runtime or companion script.
- Self-contained Windows x64 EXE; users do not need to install .NET separately.
- Compact taskbar panel with weekly remaining percentage and reset date.
- 5-hour and 7-day usage details in the tooltip.
- Live, stale, no-data, and Codex-not-running states.
- Green, amber, red, and gray quota status colors.
- Refresh intervals of 30 seconds, 2 minutes, 5 minutes, or manual only.
- Drag positioning, position lock, display switching, and optional icon pulse.
- Automatic reattachment after Windows Explorer restarts.
- No additional sign-in, API key, or browser cookie is required.

## Run

1. Download and extract the release ZIP.
2. Double-click `CodexTaskbarWidget.exe`.

The application is self-contained and does not require administrator access.
Right-click the panel to refresh usage, change the refresh interval, move it,
lock its position, control icon animation, or exit.

## Start automatically with Windows

Double-click `Install Automatic Startup.vbs`. It creates a shortcut for the
current Windows account and starts the widget about 15 seconds after sign-in so
Windows Explorer and the taskbar have time to initialize.

Alternatively, run `Install Taskbar Startup.ps1` to register the delayed
launcher under the current user's Windows Run key.

Run `Uninstall Taskbar Startup.ps1` to remove both current and legacy startup
entries.

## Privacy

The widget reads only the known local Codex session location:

```text
%USERPROFILE%\.codex\sessions
```

It does not send usage data to the internet. Position, animation, and refresh
settings are stored in:

```text
%APPDATA%\CodexUsageWidget\widget-state.json
```

Diagnostic logs are stored in the same folder and never include conversation
text, API keys, cookies, or credentials.

## Build from source

Requirements:

- Windows 10/11 x64
- .NET 8 SDK

```powershell
dotnet restore src\CodexUsageWidget\CodexUsageWidget.csproj --configfile NuGet.Config
dotnet publish src\CodexUsageWidget\CodexUsageWidget.csproj -c Release -o publish\single
```

The published self-contained executable is written to:

```text
publish\single\CodexTaskbarWidget.exe
```

## Version 2.0.0

- Rewritten from PowerShell/WPF to native C#/.NET 8 WPF.
- Replaced the launcher-plus-script package with one self-contained EXE.
- Preserved the v1.0.8 taskbar layout, quota parsing, reset date, and tooltip.
- Preserved taskbar embedding, dragging, display presets, refresh settings, and
  icon animation.
- Preserved the existing AppData settings format for an in-place upgrade.
- Updated automatic startup names, logs, documentation, and build metadata.

See `VERSION.txt` for the current build information.
