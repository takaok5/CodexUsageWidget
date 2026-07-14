# Codex Usage Widget for Windows 11

A lightweight desktop widget inspired by the reference image. It reads local
Codex session logs, draws the last 24 hours of token activity, and displays the
latest rate-limit percentage and reset time reported by Codex.

## Run

If you downloaded the ZIP, click **Extract all** first. Do not launch the files
while browsing inside the compressed ZIP. Then double-click **Start Codex
Widget.vbs**. No installation or administrator access is required.

- Drag anywhere on the widget to move it.
- Click **↻** to refresh.
- Click **●** to toggle always-on-top.
- Click **×** to close.

## Bottom-left taskbar mode

Double-click **CodexTaskbarWidget.exe** (recommended). **Start Taskbar Mode.vbs**
remains as a compatibility launcher. The EXE starts the PowerShell UI in a
hidden STA process, avoiding the visible console and the unreliable embedded
runspace behavior seen in earlier builds.

The installed Codex app icon stays at the front. The first row aligns the larger
Codex name with the exact reset date and time. The second row spans the same
width with `7D`, a long remaining-quota bar, and the percentage left.
Day and month names always use English. Hovering the panel reveals a subtle
rounded Windows 11-style highlight and border, matching native taskbar buttons.
Typography uses deliberate optical spacing: an 11-pixel icon/name gap, a
5-pixel `7D`/bar gap, and a 6-pixel bar/percentage gap. The quota track is 110
pixels wide and slightly thicker for easier scanning.
The icon uses a subtle 1.6-second ease-in/out pulse between full and 72 percent
opacity, avoiding a sharp or distracting blink.

Usage is read from the newest token-count event near the end of the active Codex
session. The panel detects active-session file changes every two seconds, keeps
a 30-second fallback refresh, and shows the last update time in its tooltip.
Use **Refresh usage** in the right-click menu for an immediate update. The
compact taskbar display intentionally shows only the weekly limit.

The right-click menu can move the panel between displays, lock its position,
toggle icon animation, and choose slow, normal, or fast pulse speed. Animation
automatically stops when Windows UI animations are disabled or when the machine
is on battery at 20 percent or below. Saved settings survive restarts.

The panel tracks the selected display's taskbar work area, follows top or bottom
taskbar placement, handles auto-hide, and restores its z-order when Windows
Explorer recreates the taskbar. Stale data is shown in gray. Diagnostic errors
are rotated locally in `widget-errors.log` without recording conversation text.

Hold the left mouse button on the Codex panel and drag horizontally to place it
anywhere along the taskbar. It snaps back to taskbar height and remembers the
last position. The right-click menu also provides left-edge, beside-weather,
and right-edge presets.

Choose **Lock position** in the right-click menu to prevent accidental dragging.
The lock state is remembered with the saved position.

Version 1.0.7 uses **Embedded Taskbar Mode**. Windows Explorer's taskbar becomes
the native parent of the panel instead of the panel floating above it as a
topmost window. Its background is genuinely transparent, so the real taskbar
color and acrylic effect remain visible. Because the panel now shares the
taskbar lifecycle and clipping area, opening or minimizing another application
does not trigger a topmost z-order fight. The hover highlight and gentle icon
animation remain enabled.

Dragging is handled inside the taskbar and saves the same horizontal position.
The two-second usage check updates data only and never rewrites panel geometry.
If Explorer restarts, the panel automatically attaches to the recreated taskbar.

Windows does not expose the built-in weather slot for arbitrary apps, so this
mode is a topmost panel beside the native weather button rather than a system
widget. Keep **Settings > Personalization > Taskbar > Widgets** enabled to show
weather and Codex side by side.

## System Tray mode (recommended)

Double-click **Start System Tray Mode.vbs**. A genuine notification-area icon
shows the current long-window percentage. Hover for the reset time, left-click
for a detailed card, or right-click to refresh and exit. Windows may initially
place the icon under the `^` overflow menu; drag it onto the visible tray if you
want it shown permanently.

## Start automatically with Windows

Right-click **Install Startup.ps1** and choose **Run with PowerShell**. Use
**Uninstall Startup.ps1** to remove the startup shortcut.

For taskbar mode, run **Install Taskbar Startup.ps1**. It adds the compiled EXE
to the current Windows account's standard startup list through Windows Script
Host. Startup waits 15 seconds for Explorer and the taskbar to finish loading
before opening the embedded widget. **Uninstall Taskbar Startup.ps1** removes
both the startup entry and any shortcut created by an older version.

If Windows ignores registry startup entries, double-click **Install Automatic
Startup.vbs** once. It creates a real shortcut in the current account's Startup
folder, removes the older registry entry, and confirms success in a dialog.
Each automatic launch is recorded in `startup-launcher.log` for diagnosis.

The widget only reads `%USERPROFILE%\.codex\sessions`. It does not send data to
the internet. Position and pin state are stored locally in `widget-state.json`.
