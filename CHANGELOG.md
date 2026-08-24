# Changelog

All notable changes to ChatGPT Codex Usage Widget are documented here.

## Unreleased

- Replaces Windows sign-in startup with a current-user task triggered by the packaged Codex desktop app event.
- Adds an external watchdog that starts the unchanged widget only while the main Codex process is active.
- Detaches the widget HWND from Explorer and requests a normal shutdown with `WM_CLOSE`; a blocked widget is terminated only after the watchdog verifies that it is no longer parented to the taskbar.
- Removes the legacy Windows Startup installers and includes one-time cleanup and watchdog uninstall scripts.

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
