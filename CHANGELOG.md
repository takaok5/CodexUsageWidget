# Changelog

All notable changes to ChatGPT Codex Usage Widget are documented here.

## Unreleased

- Removes the process-local snapshot cache that required a cold scan of every historical session.
- Removes the recursive session-archive watcher; updates now use the configured timer or manual refresh.
- Limits usage discovery to the 32 most recently modified session files.
- Uses a 64 KB fast tail with a 2 MB fallback limited to the four newest files.
- Anchors the widget over the taskbar as a top-level window after the first WPF render, avoiding cross-process WPF child-window deadlocks.

## 2.1.1 — 2026-08-20

- Selects the newest token_count event across all local Codex sessions.
- Replaces the two-second session-directory polling loop with file-change monitoring and cached parsing.
- Makes Manual only stop background usage refreshes.
- Separates the manual refresh check time from the latest Codex data time.
- Adds regression coverage for multi-session selection, missing short-window data, and incomplete JSONL records.

## 2.1.0 — 2026-08-17

- Adds a global four-position context slider: Default, 300K, 600K, and 1M.
- Preserves the selected model and treats non-preset context values as CUSTOM without changing them at startup.
- Adds atomic configuration updates and backup files.
- Supports a custom model catalog ceiling for gpt-5.6-sol.

## 2.0.0 — 2026-07-16

- Rewrites the widget as a native C# / .NET 8 WPF application.
- Publishes a self-contained Windows x64 executable.
- Preserves quota parsing, taskbar embedding, settings, and startup behavior from the prior implementation.
