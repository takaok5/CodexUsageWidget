param(
    [Parameter()]
    [string]$WidgetPath = (Join-Path (Split-Path -Parent $PSScriptRoot) 'CodexTaskbarWidget.exe'),

    [Parameter()]
    [ValidateRange(0, [int]::MaxValue)]
    [int]$CodexPid = 0,

    [Parameter()]
    [ValidateRange(1, 60)]
    [int]$InitialWaitSeconds = 15,

    [Parameter()]
    [ValidateRange(1, 60)]
    [int]$CloseWaitSeconds = 15
)

$ErrorActionPreference = 'Stop'
$watchdogMutex = $null
$ownsWatchdogMutex = $false
$logDirectory = Join-Path $env:APPDATA 'CodexUsageWidget'
$logPath = Join-Path $logDirectory 'watchdog.log'

function Write-WatchdogLog {
    param([Parameter(Mandatory)][string]$Message)

    try {
        New-Item -ItemType Directory -Path $logDirectory -Force | Out-Null
        Add-Content -LiteralPath $logPath -Value "$(Get-Date -Format o) $Message"
    }
    catch {
    }
}

function Get-CodexDesktopProcess {
    param(
        [Parameter(Mandatory)][int]$RequestedPid,
        [Parameter(Mandatory)][int]$WaitSeconds
    )

    if ($RequestedPid -gt 0) {
        return Get-Process -Id $RequestedPid -ErrorAction Stop
    }

    $deadline = [DateTime]::UtcNow.AddSeconds($WaitSeconds)
    do {
        $candidate = Get-CimInstance Win32_Process -Filter "Name = 'ChatGPT.exe'" |
            Where-Object {
                $_.ExecutablePath -match '(?i)\\WindowsApps\\OpenAI\.Codex_' -and
                ([string]::IsNullOrWhiteSpace($_.CommandLine) -or
                 $_.CommandLine -notmatch '(?i)(^|\s)--type=')
            } |
            Sort-Object CreationDate |
            Select-Object -First 1

        if ($null -ne $candidate) {
            return Get-Process -Id $candidate.ProcessId -ErrorAction Stop
        }

        Start-Sleep -Milliseconds 250
    } while ([DateTime]::UtcNow -lt $deadline)

    return $null
}

if (-not ('CodexUsageWidget.NativeWindowController' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Text;

namespace CodexUsageWidget
{
    public sealed class WindowFollowResult
    {
        public string Status { get; set; }
        public bool Moved { get; set; }
        public long OwnerMonitor { get; set; }
        public int TargetLeft { get; set; }
        public int TargetTop { get; set; }
    }

    public static class NativeWindowController
    {
        private const uint WmClose = 0x0010;
        private const uint MonitorDefaultToNearest = 2;
        private const uint GetAncestorRoot = 2;
        private const uint SwpNoSize = 0x0001;
        private const uint SwpNoMove = 0x0002;
        private const uint SwpNoActivate = 0x0010;
        private const uint SwpShowWindow = 0x0040;
        private static readonly IntPtr HwndTopmost = new IntPtr(-1);
        private static int? horizontalOffset;
        private static IntPtr lastOwnerWindow = IntPtr.Zero;

        private delegate bool EnumWindowProc(IntPtr window, IntPtr parameter);

        [StructLayout(LayoutKind.Sequential)]
        private struct Rect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MonitorInfo
        {
            public int Size;
            public Rect Monitor;
            public Rect WorkArea;
            public uint Flags;
        }

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowProc callback, IntPtr parameter);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(
            IntPtr window,
            out uint processId);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr window, out Rect rect);

        [DllImport("user32.dll")]
        private static extern bool IsWindow(IntPtr window);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr window);

        [DllImport("user32.dll")]
        private static extern IntPtr GetParent(IntPtr window);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern IntPtr GetAncestor(IntPtr window, uint flags);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(
            IntPtr window,
            StringBuilder text,
            int maximumCount);

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);

        [DllImport("user32.dll")]
        private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(
            IntPtr window,
            IntPtr insertAfter,
            int x,
            int y,
            int width,
            int height,
            uint flags);

        [DllImport("user32.dll")]
        private static extern bool PostMessage(
            IntPtr window,
            uint message,
            IntPtr wParam,
            IntPtr lParam);

        public static bool RequestClose(int widgetProcessId)
        {
            IntPtr widgetWindow = FindWidgetWindow(widgetProcessId);
            return widgetWindow != IntPtr.Zero &&
                   PostMessage(widgetWindow, WmClose, IntPtr.Zero, IntPtr.Zero);
        }

        public static bool IsSafeTopLevelWidget(int widgetProcessId)
        {
            IntPtr widgetWindow = FindWidgetWindow(widgetProcessId);
            return widgetWindow == IntPtr.Zero || GetParent(widgetWindow) == IntPtr.Zero;
        }

        public static WindowFollowResult Follow(
            int ownerProcessId,
            long fallbackOwnerWindowHandle,
            int widgetProcessId)
        {
            var result = new WindowFollowResult { Status = "WindowUnavailable" };
            IntPtr ownerWindow = FindOwnerWindow(
                ownerProcessId,
                new IntPtr(fallbackOwnerWindowHandle));
            IntPtr widgetWindow = FindWidgetWindow(widgetProcessId);
            if (ownerWindow == IntPtr.Zero)
            {
                result.Status = "OwnerWindowUnavailable";
                return result;
            }
            if (widgetWindow == IntPtr.Zero)
            {
                result.Status = "WidgetWindowUnavailable";
                return result;
            }
            if (GetParent(widgetWindow) != IntPtr.Zero)
            {
                result.Status = "WidgetNotTopLevel";
                return result;
            }

            IntPtr ownerMonitor = MonitorFromWindow(ownerWindow, MonitorDefaultToNearest);
            IntPtr widgetMonitor = MonitorFromWindow(widgetWindow, MonitorDefaultToNearest);
            result.OwnerMonitor = ownerMonitor.ToInt64();
            if (ownerMonitor == IntPtr.Zero || widgetMonitor == IntPtr.Zero)
            {
                result.Status = "MonitorUnavailable";
                return result;
            }

            MonitorInfo ownerInfo;
            MonitorInfo widgetInfo;
            if (!TryGetMonitorInfo(ownerMonitor, out ownerInfo) ||
                !TryGetMonitorInfo(widgetMonitor, out widgetInfo))
            {
                result.Status = "MonitorInfoUnavailable";
                return result;
            }

            Rect widgetRect;
            if (!GetWindowRect(widgetWindow, out widgetRect))
            {
                result.Status = "WidgetRectUnavailable";
                return result;
            }

            int widgetWidth = Math.Max(1, widgetRect.Right - widgetRect.Left);
            int currentOffset = Math.Max(0, widgetRect.Left - widgetInfo.Monitor.Left);
            if (ownerMonitor == widgetMonitor)
            {
                horizontalOffset = Math.Min(
                    currentOffset,
                    Math.Max(0, ownerInfo.Monitor.Right - ownerInfo.Monitor.Left - widgetWidth));
                bool raised = SetWindowPos(
                    widgetWindow,
                    HwndTopmost,
                    0,
                    0,
                    0,
                    0,
                    SwpNoSize | SwpNoMove | SwpNoActivate | SwpShowWindow);
                result.Status = raised ? "Aligned" : "ZOrderFailed";
                return result;
            }

            int bottomGap = ownerInfo.Monitor.Bottom - ownerInfo.WorkArea.Bottom;
            int topGap = ownerInfo.WorkArea.Top - ownerInfo.Monitor.Top;
            int leftGap = ownerInfo.WorkArea.Left - ownerInfo.Monitor.Left;
            int rightGap = ownerInfo.Monitor.Right - ownerInfo.WorkArea.Right;
            if (Math.Max(leftGap, rightGap) > Math.Max(topGap, bottomGap))
            {
                result.Status = "VerticalTaskbarUnsupported";
                return result;
            }

            int taskbarHeight;
            if (bottomGap > 0)
            {
                result.TargetTop = ownerInfo.WorkArea.Bottom;
                taskbarHeight = bottomGap;
            }
            else if (topGap > 0)
            {
                result.TargetTop = ownerInfo.Monitor.Top;
                taskbarHeight = topGap;
            }
            else
            {
                taskbarHeight = 48;
                result.TargetTop = ownerInfo.Monitor.Bottom - taskbarHeight;
            }

            int offset = horizontalOffset ?? currentOffset;
            int maximumOffset = Math.Max(
                0,
                ownerInfo.Monitor.Right - ownerInfo.Monitor.Left - widgetWidth);
            offset = Math.Min(Math.Max(0, offset), maximumOffset);

            result.TargetLeft = ownerInfo.Monitor.Left + offset;
            result.Moved = SetWindowPos(
                widgetWindow,
                HwndTopmost,
                result.TargetLeft,
                result.TargetTop,
                widgetWidth,
                Math.Max(1, taskbarHeight),
                SwpNoActivate | SwpShowWindow);
            result.Status = result.Moved ? "Moved" : "MoveFailed";
            return result;
        }

        private static IntPtr FindOwnerWindow(int expectedProcessId, IntPtr fallbackWindow)
        {
            IntPtr foreground = GetAncestor(GetForegroundWindow(), GetAncestorRoot);
            if (IsOwnedVisibleWindow(foreground, expectedProcessId))
            {
                lastOwnerWindow = foreground;
                return foreground;
            }
            if (IsOwnedVisibleWindow(lastOwnerWindow, expectedProcessId))
                return lastOwnerWindow;
            if (IsOwnedVisibleWindow(fallbackWindow, expectedProcessId))
            {
                lastOwnerWindow = fallbackWindow;
                return fallbackWindow;
            }

            IntPtr largestWindow = IntPtr.Zero;
            long largestArea = 0;
            EnumWindows((window, parameter) =>
            {
                if (!IsOwnedVisibleWindow(window, expectedProcessId)) return true;
                Rect rect;
                if (!GetWindowRect(window, out rect)) return true;
                long area = (long)Math.Max(0, rect.Right - rect.Left) *
                            Math.Max(0, rect.Bottom - rect.Top);
                if (area <= largestArea) return true;
                largestArea = area;
                largestWindow = window;
                return true;
            }, IntPtr.Zero);
            lastOwnerWindow = largestWindow;
            return largestWindow;
        }

        private static bool IsOwnedVisibleWindow(IntPtr window, int expectedProcessId)
        {
            if (window == IntPtr.Zero || !IsWindow(window) || !IsWindowVisible(window))
                return false;
            uint processId;
            GetWindowThreadProcessId(window, out processId);
            return processId == (uint)expectedProcessId;
        }

        private static IntPtr FindWidgetWindow(int expectedProcessId)
        {
            IntPtr result = IntPtr.Zero;
            EnumWindows((window, parameter) =>
            {
                uint processId;
                GetWindowThreadProcessId(window, out processId);
                if (processId != (uint)expectedProcessId) return true;

                var title = new StringBuilder(256);
                GetWindowText(window, title, title.Capacity);
                if (String.Equals(
                    title.ToString(),
                    "ChatGPT Codex Usage Widget",
                    StringComparison.Ordinal))
                {
                    result = window;
                    return false;
                }
                return true;
            }, IntPtr.Zero);
            return result;
        }

        private static bool TryGetMonitorInfo(IntPtr monitor, out MonitorInfo info)
        {
            info = new MonitorInfo { Size = Marshal.SizeOf(typeof(MonitorInfo)) };
            return GetMonitorInfo(monitor, ref info);
        }

    }
}
'@
}

function Request-WidgetClose {
    param(
        [Parameter(Mandatory)][System.Diagnostics.Process]$WidgetProcess,
        [Parameter(Mandatory)][int]$WaitSeconds
    )

    $posted = [CodexUsageWidget.NativeWindowController]::RequestClose($WidgetProcess.Id)
    if ($posted -and $WidgetProcess.WaitForExit($WaitSeconds * 1000)) {
        Write-WatchdogLog "Widget PID $($WidgetProcess.Id) closed cleanly."
        return $true
    }

    if (-not [CodexUsageWidget.NativeWindowController]::IsSafeTopLevelWidget($WidgetProcess.Id)) {
        Write-WatchdogLog "Widget PID $($WidgetProcess.Id) is not top-level; it was left running."
        return $false
    }

    Stop-Process -Id $WidgetProcess.Id -ErrorAction Stop
    if (-not $WidgetProcess.WaitForExit(5000)) {
        Write-WatchdogLog "Widget PID $($WidgetProcess.Id) could not be stopped."
        return $false
    }
    Write-WatchdogLog "Widget PID $($WidgetProcess.Id) was stopped after its clean-close timeout."
    return $true
}

try {
    $resolvedWidgetPath = [IO.Path]::GetFullPath($WidgetPath)
    if (-not (Test-Path -LiteralPath $resolvedWidgetPath -PathType Leaf)) {
        throw "Widget executable was not found at $resolvedWidgetPath"
    }

    $watchdogMutex = [Threading.Mutex]::new(
        $true,
        'Local\CodexUsageWidgetWatchdog',
        [ref]$ownsWatchdogMutex)
    if (-not $ownsWatchdogMutex) {
        Write-WatchdogLog 'Another Codex widget watchdog is already active.'
        exit 0
    }

    $codexProcess = Get-CodexDesktopProcess -RequestedPid $CodexPid -WaitSeconds $InitialWaitSeconds
    if ($null -eq $codexProcess) {
        Write-WatchdogLog 'The main Codex desktop process was not found; the widget was not started.'
        exit 0
    }

    $widgetProcess = Get-Process -Name 'CodexTaskbarWidget' -ErrorAction SilentlyContinue |
        Sort-Object StartTime |
        Select-Object -First 1
    if ($null -eq $widgetProcess) {
        $widgetProcess = Start-Process -FilePath $resolvedWidgetPath `
            -WorkingDirectory (Split-Path -Parent $resolvedWidgetPath) `
            -PassThru
        Write-WatchdogLog "Started widget PID $($widgetProcess.Id) for Codex PID $($codexProcess.Id)."
    }
    else {
        Write-WatchdogLog "Using widget PID $($widgetProcess.Id) for Codex PID $($codexProcess.Id)."
    }

    $lastFollowStatus = $null
    $lastOwnerMonitor = 0L
    while (-not $codexProcess.HasExited -and -not $widgetProcess.HasExited) {
        $codexProcess.Refresh()
        $follow = [CodexUsageWidget.NativeWindowController]::Follow(
            $codexProcess.Id,
            $codexProcess.MainWindowHandle.ToInt64(),
            $widgetProcess.Id)
        if ($follow.Status -ne $lastFollowStatus -or
            $follow.OwnerMonitor -ne $lastOwnerMonitor) {
            if ($follow.Status -eq 'Moved') {
                Write-WatchdogLog (
                    "Moved widget PID $($widgetProcess.Id) to the Codex monitor " +
                    "at $($follow.TargetLeft),$($follow.TargetTop).")
            }
            elseif ($follow.Status -notin @(
                'Aligned',
                'OwnerWindowUnavailable',
                'WidgetWindowUnavailable')) {
                Write-WatchdogLog "Monitor follow status: $($follow.Status)."
            }
            $lastFollowStatus = $follow.Status
            $lastOwnerMonitor = $follow.OwnerMonitor
        }
        $null = $codexProcess.WaitForExit(500)
    }

    if ($widgetProcess.HasExited) {
        Write-WatchdogLog "Widget PID $($widgetProcess.Id) exited while Codex PID $($codexProcess.Id) was active."
        exit 0
    }

    Write-WatchdogLog "Codex PID $($codexProcess.Id) exited; requesting widget shutdown."
    $null = Request-WidgetClose -WidgetProcess $widgetProcess -WaitSeconds $CloseWaitSeconds
}
catch {
    Write-WatchdogLog "Watchdog failed: $($_.Exception.Message)"
    exit 1
}
finally {
    if ($ownsWatchdogMutex -and $null -ne $watchdogMutex) {
        try {
            $watchdogMutex.ReleaseMutex()
        }
        catch {
        }
    }
    if ($null -ne $watchdogMutex) {
        $watchdogMutex.Dispose()
    }
}
