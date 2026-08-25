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
        # Explicit ownership makes the lifecycle independently testable. The installed
        # task does not use this path and always resolves the packaged Codex process below.
        return Get-Process -Id $RequestedPid -ErrorAction Stop
    }

    $deadline = [DateTime]::UtcNow.AddSeconds($WaitSeconds)
    do {
        $candidate = Get-CimInstance Win32_Process -Filter "Name = 'ChatGPT.exe'" -ErrorAction SilentlyContinue |
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

if (-not ('CodexUsageWidget.ExternalWindowCloser' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace CodexUsageWidget
{
    public static class ExternalWindowCloser
    {
        private const uint WM_CLOSE = 0x0010;
        private delegate bool EnumWindowProc(IntPtr window, IntPtr parameter);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowProc callback, IntPtr parameter);

        [DllImport("user32.dll")]
        private static extern bool EnumChildWindows(
            IntPtr parent,
            EnumWindowProc callback,
            IntPtr parameter);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(
            IntPtr window,
            out uint processId);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(
            IntPtr window,
            StringBuilder text,
            int maximumCount);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetParent(IntPtr child, IntPtr newParent);

        [DllImport("user32.dll")]
        private static extern IntPtr GetParent(IntPtr child);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(
            IntPtr window,
            StringBuilder className,
            int maximumCount);

        [DllImport("user32.dll")]
        private static extern bool PostMessage(
            IntPtr window,
            uint message,
            IntPtr wParam,
            IntPtr lParam);

        public static int RequestClose(int processId)
        {
            var windows = FindWidgetWindows(processId);
            var posted = 0;
            foreach (var window in windows)
            {
                var detach = Task.Run(() => SetParent(window, IntPtr.Zero));
                try
                {
                    detach.Wait(2000);
                }
                catch (AggregateException)
                {
                }
                if (PostMessage(window, WM_CLOSE, IntPtr.Zero, IntPtr.Zero))
                    posted++;
            }

            return posted;
        }

        public static bool IsDetachedFromTaskbar(int processId)
        {
            foreach (var window in FindWidgetWindows(processId))
            {
                var parent = GetParent(window);
                var className = new StringBuilder(256);
                GetClassName(parent, className, className.Capacity);
                if (String.Equals(className.ToString(), "Shell_TrayWnd", StringComparison.Ordinal) ||
                    String.Equals(className.ToString(), "Shell_SecondaryTrayWnd", StringComparison.Ordinal))
                    return false;
            }

            return true;
        }

        private static ISet<IntPtr> FindWidgetWindows(int processId)
        {
            var windows = new HashSet<IntPtr>();
            EnumWindowProc collect = (window, parameter) =>
            {
                AddOwnedWidgetWindow(window, processId, windows);
                return true;
            };

            EnumWindows((topLevelWindow, parameter) =>
            {
                collect(topLevelWindow, IntPtr.Zero);
                EnumChildWindows(topLevelWindow, collect, IntPtr.Zero);
                return true;
            }, IntPtr.Zero);
            return windows;
        }

        private static void AddOwnedWidgetWindow(
            IntPtr window,
            int expectedProcessId,
            ISet<IntPtr> windows)
        {
            uint processId;
            GetWindowThreadProcessId(window, out processId);
            if (processId != (uint)expectedProcessId)
                return;

            var title = new StringBuilder(256);
            GetWindowText(window, title, title.Capacity);
            if (String.Equals(
                title.ToString(),
                "ChatGPT Codex Usage Widget",
                StringComparison.Ordinal))
                windows.Add(window);
        }
    }

    public sealed class WindowFollowResult
    {
        public string Status { get; set; }
        public bool Moved { get; set; }
        public long OwnerMonitor { get; set; }
        public int TargetLeft { get; set; }
        public int TargetTop { get; set; }
    }

    public static class WindowFollower
    {
        private const uint MonitorDefaultToNearest = 2;
        private const uint SwpNoActivate = 0x0010;
        private const uint SwpShowWindow = 0x0040;
        private static readonly IntPtr HwndTopmost = new IntPtr(-1);
        private static int? horizontalOffset;

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
        private static extern bool EnumChildWindows(
            IntPtr parent,
            EnumWindowProc callback,
            IntPtr parameter);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(
            IntPtr window,
            out uint processId);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr window);

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr window);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr window, out Rect rect);

        [DllImport("user32.dll")]
        private static extern IntPtr GetParent(IntPtr window);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(
            IntPtr window,
            StringBuilder text,
            int maximumCount);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(
            IntPtr window,
            StringBuilder className,
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

        public static WindowFollowResult Follow(int ownerProcessId, int widgetProcessId)
        {
            var result = new WindowFollowResult { Status = "WindowUnavailable" };
            IntPtr ownerWindow = FindLargestTopLevelWindow(ownerProcessId);
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
                result.Status = "Aligned";
                return result;
            }

            var parentClass = new StringBuilder(64);
            GetClassName(GetParent(widgetWindow), parentClass, parentClass.Capacity);
            if (String.Equals(parentClass.ToString(), "Shell_TrayWnd", StringComparison.Ordinal) ||
                String.Equals(parentClass.ToString(), "Shell_SecondaryTrayWnd", StringComparison.Ordinal))
            {
                result.Status = "LegacyTaskbarChild";
                return result;
            }

            Rect taskbarRect;
            if (!TryFindTaskbar(ownerInfo.Monitor, out taskbarRect))
            {
                result.Status = "TaskbarUnavailable";
                return result;
            }
            if ((taskbarRect.Right - taskbarRect.Left) < (taskbarRect.Bottom - taskbarRect.Top))
            {
                result.Status = "VerticalTaskbarUnsupported";
                return result;
            }

            int offset = horizontalOffset ?? currentOffset;
            int maximumOffset = Math.Max(
                0,
                ownerInfo.Monitor.Right - ownerInfo.Monitor.Left - widgetWidth);
            offset = Math.Min(Math.Max(0, offset), maximumOffset);

            int targetLeft = ownerInfo.Monitor.Left + offset;
            int targetTop = taskbarRect.Top;
            int targetHeight = Math.Max(1, taskbarRect.Bottom - taskbarRect.Top);
            result.TargetLeft = targetLeft;
            result.TargetTop = targetTop;
            result.Moved = SetWindowPos(
                widgetWindow,
                HwndTopmost,
                targetLeft,
                targetTop,
                widgetWidth,
                targetHeight,
                SwpNoActivate | SwpShowWindow);
            result.Status = result.Moved ? "Moved" : "MoveFailed";
            return result;
        }

        private static IntPtr FindLargestTopLevelWindow(int expectedProcessId)
        {
            IntPtr bestWindow = IntPtr.Zero;
            long bestArea = 0;
            EnumWindows((window, parameter) =>
            {
                uint processId;
                GetWindowThreadProcessId(window, out processId);
                if (processId != (uint)expectedProcessId ||
                    !IsWindowVisible(window) ||
                    IsIconic(window))
                    return true;

                Rect rect;
                if (!GetWindowRect(window, out rect)) return true;
                long area = (long)Math.Max(0, rect.Right - rect.Left) *
                            Math.Max(0, rect.Bottom - rect.Top);
                if (area <= bestArea) return true;
                bestArea = area;
                bestWindow = window;
                return true;
            }, IntPtr.Zero);
            return bestWindow;
        }

        private static IntPtr FindWidgetWindow(int expectedProcessId)
        {
            IntPtr result = IntPtr.Zero;
            EnumWindowProc collect = (window, parameter) =>
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
                    result = window;
                return true;
            };

            EnumWindows((topLevelWindow, parameter) =>
            {
                collect(topLevelWindow, IntPtr.Zero);
                EnumChildWindows(topLevelWindow, collect, IntPtr.Zero);
                return result == IntPtr.Zero;
            }, IntPtr.Zero);
            return result;
        }

        private static bool TryGetMonitorInfo(IntPtr monitor, out MonitorInfo info)
        {
            info = new MonitorInfo { Size = Marshal.SizeOf(typeof(MonitorInfo)) };
            return GetMonitorInfo(monitor, ref info);
        }

        private static bool TryFindTaskbar(Rect monitorRect, out Rect taskbarRect)
        {
            Rect bestRect = new Rect();
            long bestOverlap = 0;
            EnumWindows((window, parameter) =>
            {
                var className = new StringBuilder(64);
                if (GetClassName(window, className, className.Capacity) == 0) return true;
                string value = className.ToString();
                if (value != "Shell_TrayWnd" && value != "Shell_SecondaryTrayWnd") return true;

                Rect rect;
                if (!GetWindowRect(window, out rect)) return true;
                int overlapWidth = Math.Max(
                    0,
                    Math.Min(monitorRect.Right, rect.Right) -
                    Math.Max(monitorRect.Left, rect.Left));
                int overlapHeight = Math.Max(
                    0,
                    Math.Min(monitorRect.Bottom, rect.Bottom) -
                    Math.Max(monitorRect.Top, rect.Top));
                long overlap = (long)overlapWidth * overlapHeight;
                if (overlap <= bestOverlap) return true;
                bestOverlap = overlap;
                bestRect = rect;
                return true;
            }, IntPtr.Zero);
            taskbarRect = bestRect;
            return bestOverlap > 0;
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

    $postedMessages = [CodexUsageWidget.ExternalWindowCloser]::RequestClose($WidgetProcess.Id)
    if ($WidgetProcess.WaitForExit($WaitSeconds * 1000)) {
        Write-WatchdogLog "Widget PID $($WidgetProcess.Id) closed cleanly after $postedMessages WM_CLOSE request(s)."
        return $true
    }

    if (-not [CodexUsageWidget.ExternalWindowCloser]::IsDetachedFromTaskbar($WidgetProcess.Id)) {
        Write-WatchdogLog "Widget PID $($WidgetProcess.Id) ignored $postedMessages clean close request(s) and is still attached to the taskbar; it was left running."
        return $false
    }

    Stop-Process -Id $WidgetProcess.Id -ErrorAction Stop
    if (-not $WidgetProcess.WaitForExit(5000)) {
        Write-WatchdogLog "Widget PID $($WidgetProcess.Id) could not be stopped after its window was detached from the taskbar."
        return $false
    }
    Write-WatchdogLog "Widget PID $($WidgetProcess.Id) ignored $postedMessages clean close request(s) and was terminated only after its window was detached from the taskbar."
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
        Write-WatchdogLog "Started the original widget as PID $($widgetProcess.Id) for Codex PID $($codexProcess.Id)."
    }
    else {
        Write-WatchdogLog "Using the already-running widget PID $($widgetProcess.Id) for Codex PID $($codexProcess.Id)."
    }

    $lastFollowStatus = $null
    $lastOwnerMonitor = 0L
    while (-not $codexProcess.HasExited -and -not $widgetProcess.HasExited) {
        $follow = [CodexUsageWidget.WindowFollower]::Follow(
            $codexProcess.Id,
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
        Write-WatchdogLog "Widget PID $($widgetProcess.Id) exited while Codex PID $($codexProcess.Id) was still active."
        exit 0
    }

    Write-WatchdogLog "Codex PID $($codexProcess.Id) exited; requesting a clean widget shutdown."
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
