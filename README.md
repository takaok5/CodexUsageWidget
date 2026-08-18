# ChatGPT Codex Usage Widget for Windows 11

A lightweight, Codex-only taskbar widget built with **C#**, **.NET 8**, and
**WPF**. It reads local Codex session data and keeps the weekly remaining
allowance visible without opening a usage dashboard.

![ChatGPT Codex Usage Widget v2.1.0](screenshots/chatgpt-codex-usage-widget-v2.1.0.png)

## Highlights

- Native C#/.NET 8 WPF application—no PowerShell runtime or companion script.
- Self-contained Windows x64 EXE; users do not need to install .NET separately.
- Compact taskbar panel with weekly remaining percentage and reset date.
- 5-hour and 7-day usage details in the tooltip.
- Live, stale, no-data, and Codex-not-running states.
- Green, amber, red, and gray quota status colors.
- Refresh intervals of 30 seconds, 2 minutes, 5 minutes, or manual only.
- Drag positioning, position lock, and display switching.
- Automatic reattachment after Windows Explorer restarts.
- Short, four-position Codex work-mode slider embedded directly in the taskbar
  panel for Default, 300K, 600K, or 1M token context.
- Atomic updates to the user-level Codex `config.toml` with a one-step backup.
- Automatic synchronization of a configured `model_catalog_json`, so a custom
  catalog cannot silently clamp the selected context window.
- No additional sign-in, API key, or browser cookie is required.

## Run

1. Download and extract the release ZIP.
2. Double-click `CodexTaskbarWidget.exe`.

The application is self-contained and does not require administrator access.
Right-click the panel to refresh usage, change the refresh interval, move it,
lock its position, or exit.

## Codex work modes

Move the short slider inside the taskbar widget to apply a work mode globally.
Each slider position updates the user-level Codex configuration as soon as it
is selected.

| Mode | Context window | Auto-compaction | Intended use |
| --- | ---: | ---: | --- |
| Default | Model default | Model default | No custom context overrides |
| Balanced | 300,000 | 240,000 | Normal coding tasks |
| Large Codebase | 600,000 | 500,000 | More repository and tool history |
| Long-Running Investigation | 1,000,000 | 900,000 | Reverse engineering, migrations, and debugging |

The three custom modes update `model_context_window` plus
`model_auto_compact_token_limit` without changing the user's selected model.
**Default** removes only those two context keys and leaves every other setting
untouched. Existing non-preset values appear as **Custom** and remain unchanged
until the user explicitly moves the slider.
All changes apply to `%USERPROFILE%\.codex\config.toml`. A changed mode applies
to the next new Codex task without restarting the app; a task that is already
open keeps the configuration snapshot it started with. Before replacing the
existing file, the widget copies it to
`config.toml.codex-usage-widget.bak` in the same directory.

If `config.toml` selects a custom `model_catalog_json`, the widget keeps the
`gpt-5.6-sol` `max_context_window` ceiling at 1,000,000. Codex caches custom
catalogs when its backend starts, so the first expansion of an older catalog
requires one restart; after that, switching modes applies live to new tasks.
The widget saves the unmodified catalog once as
`<catalog>.codex-usage-widget.bak`. **Default** removes the two TOML context
overrides, so the original `context_window` and standard compaction remain
active even though the harmless 1M ceiling stays available. Other model
definitions and catalog fields are preserved.

## Start automatically with Windows

Double-click `Install Automatic Startup.vbs`. It creates a shortcut for the
current Windows account and starts the widget about 15 seconds after sign-in so
Windows Explorer and the taskbar have time to initialize.

Alternatively, run `Install Taskbar Startup.ps1` to register the delayed
launcher under the current user's Windows Run key.

Run `Uninstall Taskbar Startup.ps1` to remove both current and legacy startup
entries.

## Privacy

The widget reads usage events from the known local Codex session location:

```text
%USERPROFILE%\.codex\sessions
```

When you move the work-mode slider, it writes only the three documented
model/context keys in the user-level Codex configuration file. If that file
selects a custom model catalog, the widget updates only the
`gpt-5.6-sol.max_context_window` field as described above.

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

## Version 2.1.0

- Added a global four-position Codex context slider, including a non-overriding
  Default option.
- Refined the taskbar layout by removing redundant branding and placing the
  quota percentage directly beside the usage bar.
- Added atomic global configuration saves and automatic backups.
- Added a stable custom model-catalog ceiling for restart-free mode changes on
  new Codex tasks.
- Added tests for TOML preservation, profile changes, and backups.

## Version 2.0.0

- Rewritten from PowerShell/WPF to native C#/.NET 8 WPF.
- Replaced the launcher-plus-script package with one self-contained EXE.
- Preserved the v1.0.8 taskbar layout, quota parsing, reset date, and tooltip.
- Preserved taskbar embedding, dragging, display presets, refresh settings, and
  icon animation.
- Preserved the existing AppData settings format for an in-place upgrade.
- Updated automatic startup names, logs, documentation, and build metadata.

See `VERSION.txt` for the current build information.
