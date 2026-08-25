# ChatGPT Codex Usage Widget for Windows

<p align="center">
  <strong>A compact, local-first Windows taskbar companion for checking Codex usage and choosing a context mode without opening a dashboard.</strong>
</p>

<p align="center">
  <a href="https://github.com/anekhirun/CodexUsageWidget/releases/latest">Download the latest Windows x64 release</a>
  &nbsp;·&nbsp;
  <a href="CHANGELOG.md">View the changelog</a>
</p>

<p align="center">
  <img alt="Latest release" src="https://img.shields.io/github/v/release/anekhirun/CodexUsageWidget?display_name=tag&sort=semver">
  <img alt="Windows 10 or 11" src="https://img.shields.io/badge/Windows-10%2F11-0078D4">
  <img alt=".NET 8" src="https://img.shields.io/badge/.NET-8-512BD4">
  <img alt="Local-first" src="https://img.shields.io/badge/Data-local--first-2EA44F">
</p>

![ChatGPT Codex Usage Widget on the Windows taskbar](screenshots/v2.1.1/context-mode-default.png)

## Start in under a minute

1. Download and extract the latest Windows x64 ZIP from [Releases](https://github.com/anekhirun/CodexUsageWidget/releases/latest).
2. Double-click **CodexTaskbarWidget.exe**.
3. Right-click the widget to refresh usage, choose a refresh interval, move it, lock it, or exit.

No administrator access, API key, browser cookie, or .NET installation is required.

## What the widget shows

| Signal | What it means |
| --- | --- |
| Weekly remaining | Remaining quota from the current long-window Codex rate limit. |
| Reset date | The next reset date when Codex provides it. |
| 5-hour remaining | Available in the tooltip when Codex supplies a short-window limit. |
| Freshness | The tooltip separates the latest Codex data time from the time the widget last checked it. |
| Status color | Green, amber, red, or gray indicates healthy, lower, critical, or stale data. |

The widget reads local Codex session events. It does not call a usage API or send your usage data anywhere.

## Refresh and data freshness

Use the right-click menu to choose the refresh behavior that fits your workflow.

![Refresh interval menu](screenshots/v2.1.1/refresh-interval-menu.png)

| Setting | Behavior |
| --- | --- |
| 30 seconds | Checks usage every 30 seconds. |
| 2 minutes / 5 minutes | Uses less background activity. |
| Manual only | Refreshes only when you choose **Refresh usage**. |

A refresh can succeed without changing the percentage: Codex may not have written newer rate-limit data yet, and the source may report whole-number percentages.

Usage discovery does not build a cache for every historical session or recursively watch the entire archive. Each refresh inspects at most the 32 most recently modified session files, reads a 64 KB tail from each, and allows a deeper 2 MB fallback only for the four newest files. Updates follow the selected refresh interval or a manual refresh, keeping startup bounded even when the local Codex session archive is very large. A usage refresh never reapplies the saved monitor position; monitor placement remains owned by the watchdog.

The native window is anchored over the selected taskbar after WPF completes its first render. It remains a top-level window instead of becoming a cross-process child of Explorer, avoiding WPF render deadlocks and unresponsive taskbar clicks on secondary or mixed-DPI displays.

## Codex context modes

The four-position slider applies a global context profile to new Codex tasks. Existing tasks keep the configuration snapshot with which they started.

<table>
  <tr>
    <td align="center"><strong>Default</strong><br><img src="screenshots/v2.1.1/context-mode-default.png" alt="Default context mode" width="360"></td>
    <td align="center"><strong>Balanced · 300K</strong><br><img src="screenshots/v2.1.1/context-mode-300k.png" alt="Balanced 300K context mode" width="360"></td>
  </tr>
  <tr>
    <td align="center"><strong>Large Codebase · 600K</strong><br><img src="screenshots/v2.1.1/context-mode-600k.png" alt="Large Codebase 600K context mode" width="360"></td>
    <td align="center"><strong>Long-Running Investigation · 1M</strong><br><img src="screenshots/v2.1.1/context-mode-1m.png" alt="Long-Running Investigation 1M context mode" width="360"></td>
  </tr>
</table>

| Mode | Context window | Auto-compaction | Best for |
| --- | ---: | ---: | --- |
| Default | Model default | Standard behavior | Removing custom context overrides. |
| Balanced | 300,000 | 240,000 | Everyday coding tasks. |
| Large Codebase | 600,000 | 500,000 | More repository and tool history. |
| Long-Running Investigation | 1,000,000 | 900,000 | Investigations, migrations, and deep debugging. |

### Configuration safety

- The slider never adds, changes, removes, or restores your selected Codex model.
- It changes only **model_context_window** and **model_auto_compact_token_limit** in `%USERPROFILE%\.codex\config.toml`.
- **Default** removes only those two overrides and leaves unrelated settings untouched.
- A non-preset pair is shown as **CUSTOM** and stays unchanged until you deliberately move the slider.
- Changes apply to the next completely new Codex task; existing, resumed, or previously created tasks keep their original configuration snapshot.
- A trusted project's `.codex\config.toml` takes priority. Remove its two context keys when that project should inherit the global slider setting.
- Existing configuration files are backed up beside the original before replacement.

<details>
<summary>Advanced: custom model catalogs</summary>

If your Codex configuration uses a custom model catalog, the first custom preset preserves each model's `context_window` and sets `max_context_window` to the official 1,050,000-token ceiling for every present GPT-5.6 Sol, Terra, and Luna entry. It also repairs `context_window` from the stable backup if an older widget version changed it. Restart Codex once after this bootstrap; later preset changes update only `config.toml` and apply to completely new tasks without restarting. The original catalog is saved once as `<catalog>.codex-usage-widget.bak`.

</details>

## Start and stop with Codex

Run **Install Codex Watchdog.ps1** once from an extracted release or repository checkout:

    powershell -NoProfile -ExecutionPolicy Bypass -File ".\Install Codex Watchdog.ps1"

The installer registers a current-user Scheduled Task triggered by the packaged Codex desktop app event. It does not add anything to Windows sign-in, the Startup folder, or the `HKCU\...\Run` key.

The external watchdog:

- identifies the main Codex desktop process (the packaged executable is currently named `ChatGPT.exe`);
- starts the bundled `CodexTaskbarWidget.exe` without lifecycle arguments;
- tracks the largest visible Codex window and moves a top-level widget to the corresponding monitor taskbar while preserving its horizontal offset;
- reasserts the widget's topmost z-order without moving or resizing it while the monitor is already correct;
- waits for that exact Codex process to exit;
- asks the widget to close normally with `WM_CLOSE` and waits for it to exit;
- retains a guarded fallback that never terminates a widget while it is still parented to Explorer.

Diagnostics are written to `%APPDATA%\CodexUsageWidget\watchdog.log`. After closing Codex and the widget, run **Uninstall Codex Watchdog.ps1** to remove the task and its copied runtime files without changing the widget executable or preferences. The installer removes known Startup-folder and Run-key entries from older releases.

## Privacy and local files

The widget reads usage events only from:

    %USERPROFILE%\.codex\sessions

Its position, animation, and refresh preferences are stored in:

    %APPDATA%\CodexUsageWidget\widget-state.json

When you move the work-mode slider, it writes only the two documented context
keys to:

    %USERPROFILE%\.codex\config.toml

During the one-time catalog bootstrap, the widget updates only
`max_context_window` for GPT-5.6 Sol, Terra, and Luna; it also repairs
`context_window` from the stable backup if an older widget version changed it.

Diagnostic logs live beside the widget state. They do not include conversation text, API keys, cookies, or credentials.

## Troubleshooting

| Situation | What to check |
| --- | --- |
| The widget shows --% | Start or use Codex so a local session event exists, then choose **Refresh usage**. |
| The percentage does not change | Check the tooltip: the widget may have refreshed successfully while Codex has no newer rate-limit event. |
| The 5-hour value is -- | Codex did not include a short-window rate limit in that event. |
| The slider shows CUSTOM | Your existing context values do not exactly match a preset. Nothing is written until you move the slider. |
| A mode change is not visible in an open task | Start a new Codex task; existing tasks keep their original configuration snapshot. |
| The taskbar restarted | The widget recalculates its taskbar anchor automatically. If needed, restart the widget. |
| The widget does not open with Codex | Confirm the **Codex Usage Widget Watchdog** task is enabled and inspect `%APPDATA%\CodexUsageWidget\watchdog.log`. |
| The widget remains after Codex closes | Check the log: the watchdog leaves it running only if a safe top-level shutdown cannot be verified. |

## Build from source

Requirements:

- Windows 10/11 x64
- .NET 8 SDK

    dotnet restore src\CodexUsageWidget\CodexUsageWidget.csproj --configfile NuGet.Config
    dotnet test tests\CodexUsageWidget.Tests\CodexUsageWidget.Tests.csproj -c Release
    dotnet publish src\CodexUsageWidget\CodexUsageWidget.csproj -c Release -o publish\single

The self-contained executable is written to:

    publish\single\CodexTaskbarWidget.exe

## Project layout

| Path | Purpose |
| --- | --- |
| src/CodexUsageWidget | Native C# / .NET 8 WPF application. |
| tests/CodexUsageWidget.Tests | Regression coverage for configuration and local usage parsing. |
| scripts/watch-codex.ps1 | External lifecycle, monitor-follow, and clean-close watchdog. |
| screenshots/v2.1.1 | Current screenshots used in this README. |
| CHANGELOG.md | Version-by-version release notes. |

## Contributing

Bug reports and pull requests are welcome. For UI or behavior changes, include the Windows version, the widget version, clear reproduction steps, and a screenshot when possible.
