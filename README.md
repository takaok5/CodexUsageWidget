# ChatGPT Codex Usage Widget for Windows 11

A lightweight, Codex-only taskbar widget for Windows 11. It reads local Codex
session data and keeps the weekly remaining allowance visible without opening a
usage dashboard.

![ChatGPT Codex Usage Widget v1.0.8](screenshots/chatgpt-codex-usage-widget-v1.0.8.png)

## Highlights

- Compact Windows 11 taskbar panel with a transparent native appearance.
- Weekly remaining percentage, progress bar, and reset date.
- 5-hour and 7-day usage details in the tooltip.
- Live, stale, no-data, and Codex-not-running states.
- Green, amber, red, and gray quota status colors.
- Refresh intervals of 30 seconds, 2 minutes, 5 minutes, or manual only.
- Drag positioning, position lock, display switching, and optional icon pulse.
- Automatic reattachment after Windows Explorer restarts.
- No additional sign-in, API key, or browser cookie is required.

## Run

1. Download and extract the release ZIP.
2. Keep `CodexTaskbarWidget.exe` and `CodexTaskbarWidget.ps1` in the same folder.
3. Double-click `CodexTaskbarWidget.exe`.

The EXE is a small hidden launcher for the PowerShell/WPF widget. Administrator
access is not required.

Right-click the panel to refresh usage, change the refresh interval, move it
between displays, lock its position, control icon animation, or exit.

## Start automatically with Windows

Double-click `Install Automatic Startup.vbs`. It creates a shortcut for the
current Windows account and starts the widget about 15 seconds after sign-in so
Windows Explorer and the taskbar have time to initialize.

Alternatively, run `Install Taskbar Startup.ps1` to register the delayed
launcher under the current user's Windows Run key.

Run `Uninstall Taskbar Startup.ps1` to remove both startup methods.

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

## Version 1.0.8

- Updated the visible identity to ChatGPT Codex.
- Redesigned the two-line layout for weekly usage and remaining allowance.
- Added the weekly reset date and detailed 5-hour/7-day tooltip.
- Added clearer live, stale, not-running, and no-data states.
- Added configurable refresh intervals.
- Moved settings and logs to the current user's AppData folder.
- Rebuilt the hidden Windows launcher and added its C# source.

See `VERSION.txt` for the current build information.
