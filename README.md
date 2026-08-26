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

![ChatGPT Codex Usage Widget showing weekly and 5-hour remaining quota on the Windows taskbar](screenshots/v2.1.2/usage-overview.png)

## Start in under a minute

1. Download and extract the latest Windows x64 ZIP from [Releases](https://github.com/anekhirun/CodexUsageWidget/releases/latest).
2. Double-click **CodexTaskbarWidget.exe**.
3. Right-click the widget to refresh usage, choose a refresh interval or context mode, move it, lock it, or exit.

No administrator access, API key, browser cookie, or .NET installation is required.

![Widget right-click menu](screenshots/v2.1.2/context-menu.png)

## What the widget shows

| Signal | What it means |
| --- | --- |
| Weekly remaining | Remaining quota from the current long-window Codex rate limit. |
| Reset date | The next reset date when Codex provides it. |
| 5-hour remaining | Displayed beside Weekly when Codex supplies a short-window limit; otherwise it shows unavailable. |
| Freshness | The tooltip separates the latest Codex data time from the time the widget last checked it. |
| Status icon | The leftmost ChatGPT icon pulses when Codex writes a newer usage event. |
| Status color | Green, blue, amber, red, or gray indicates healthy, lower, critical, or stale data. |

The widget reads local Codex session events. It does not call a usage API or send your usage data anywhere.

## Refresh and data freshness

Use the right-click menu to choose the refresh behavior that fits your workflow.

![Refresh interval menu](screenshots/v2.1.2/refresh-interval-menu.png)

| Setting | Behavior |
| --- | --- |
| 30 seconds | Checks usage frequently and reacts to local session-file changes. |
| 2 minutes / 5 minutes | Uses less background activity. |
| Manual only | Refreshes only when you choose **Refresh usage**. |

A refresh can succeed without changing the percentage: Codex may not have written newer rate-limit data yet, and the source may report whole-number percentages.

## Codex context modes

Choose a context mode from the widget's right-click menu. The selected profile applies to new Codex tasks; existing tasks keep the configuration snapshot with which they started.

![Context mode submenu](screenshots/v2.1.2/context-mode-menu.png)

| Mode | Context window | Auto-compaction | Best for |
| --- | ---: | ---: | --- |
| Default | Model default | Standard behavior | Removing custom context overrides. |
| Balanced | 300,000 | 240,000 | Everyday coding tasks. |
| Large Codebase | 600,000 | 500,000 | More repository and tool history. |
| Long-Running Investigation | 1,000,000 | 900,000 | Investigations, migrations, and deep debugging. |

### Configuration safety

- Selecting a mode preserves your selected Codex model.
- It changes only **model_context_window** and **model_auto_compact_token_limit**.
- **Default** removes only those two overrides and leaves unrelated settings untouched.
- A non-preset pair is shown as **CUSTOM** in the context menu and stays unchanged until you deliberately select a preset.
- Existing configuration files are backed up beside the original before replacement.

<details>
<summary>Advanced: custom model catalogs</summary>

If your Codex configuration uses a custom model catalog, the widget maintains a 1M maximum context ceiling for gpt-5.6-sol and creates a one-time backup of that catalog. This allows later context-mode changes to apply to new tasks without repeatedly expanding the catalog. The first expansion of an older catalog can require one Codex restart.

</details>

## Start automatically with Windows

Double-click **Install Automatic Startup.vbs** to create a shortcut for the current Windows account. The widget starts about 15 seconds after sign-in so Windows Explorer and the taskbar have time to initialize.

Alternatively, run **Install Taskbar Startup.ps1** to create the current-user delayed startup entry. Run **Uninstall Taskbar Startup.ps1** to remove both current and legacy startup entries.

## Privacy and local files

The widget reads usage events only from:

    %USERPROFILE%\.codex\sessions

Its position, animation, and refresh preferences are stored in:

    %APPDATA%\CodexUsageWidget\widget-state.json

The context-mode menu writes only its documented overrides to:

    %USERPROFILE%\.codex\config.toml

Diagnostic logs live beside the widget state. They do not include conversation text, API keys, cookies, or credentials.

## Troubleshooting

| Situation | What to check |
| --- | --- |
| The widget shows --% | Start or use Codex so a local session event exists, then choose **Refresh usage**. |
| The percentage does not change | Check the tooltip: the widget may have refreshed successfully while Codex has no newer rate-limit event. |
| The 5-hour value is -- | Codex did not include a short-window rate limit in that event. |
| Context mode shows CUSTOM | Your existing context values do not exactly match a preset. Nothing is written until you select a preset. |
| A mode change is not visible in an open task | Start a new Codex task; existing tasks keep their original configuration snapshot. |
| The taskbar restarted | The widget attempts to reattach automatically. If needed, restart the widget. |

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
| screenshots/v2.1.2 | Current screenshots used in this README. |
| CHANGELOG.md | Version-by-version release notes. |

## Contributing

Bug reports and pull requests are welcome. For UI or behavior changes, include the Windows version, the widget version, clear reproduction steps, and a screenshot when possible.
