using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace CodexUsageWidget;

public partial class MainWindow : Window
{
    private static readonly SolidColorBrush GreenBrush = Brush("#FF70D88B");
    private static readonly SolidColorBrush AmberBrush = Brush("#FFF59E0B");
    private static readonly SolidColorBrush RedBrush = Brush("#FFEF4444");
    private static readonly SolidColorBrush BlueBrush = Brush("#FF5BAEFF");
    private static readonly SolidColorBrush GrayBrush = Brush("#FF777777");
    private static readonly SolidColorBrush TextBrush = Brush("#FFF3F3F3");
    private static readonly SolidColorBrush MutedTextBrush = Brush("#FFAAAAAA");

    private readonly DispatcherTimer _refreshTimer = new();
    private readonly DispatcherTimer _taskbarTimer = new();
    private readonly DispatcherTimer _sessionTimer = new();
    private readonly DispatcherTimer _refreshFeedbackTimer = new();
    private readonly string _configDirectory;
    private readonly string _stateFile;
    private readonly string _logFile;

    private WidgetState _state = new();
    private nint _windowHandle;
    private nint _taskbarHandle;
    private bool _isEmbedded;
    private bool _isDragging;
    private bool _hiddenForAutoHide;
    private double _screenLeft;
    private double _screenRight = SystemParameters.PrimaryScreenWidth;
    private double _windowTop;
    private double _widgetLeft;
    private double _dipPerPixel = 1;
    private int _dragStartX;
    private double _dragStartLeft;
    private string _lastLogMessage = string.Empty;
    private DateTimeOffset? _lastPulsedEventTime;
    private FileSystemWatcher? _sessionWatcher;
    private bool _sessionRefreshQueued;

    public MainWindow()
    {
        InitializeComponent();

        _configDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CodexUsageWidget");
        Directory.CreateDirectory(_configDirectory);
        _stateFile = Path.Combine(_configDirectory, "widget-state.json");
        _logFile = Path.Combine(_configDirectory, "widget-errors.log");

        SourceInitialized += OnSourceInitialized;
        Closed += OnClosed;

        Root.MouseLeftButtonDown += OnMouseLeftButtonDown;
        Root.MouseMove += OnMouseMove;
        Root.MouseLeftButtonUp += OnMouseLeftButtonUp;

        MoveLeftMenu.Click += (_, _) => SetTaskbarPosition(_screenLeft);
        PlaceWeatherMenu.Click += (_, _) => SetTaskbarPosition(_screenLeft + 205);
        MoveRightMenu.Click += (_, _) => SetTaskbarPosition(_screenRight - Width);
        NextDisplayMenu.Click += (_, _) => MoveToNextDisplay();
        RefreshMenu.Click += (_, _) => RefreshUsage(manualRequest: true);
        ContextDefaultMenu.Click += (_, _) => ApplyContextMode(0);
        Context300KMenu.Click += (_, _) => ApplyContextMode(1);
        Context600KMenu.Click += (_, _) => ApplyContextMode(2);
        Context1MMenu.Click += (_, _) => ApplyContextMode(3);
        Refresh30Menu.Click += (_, _) => SetRefreshMode("30 seconds");
        Refresh2MinuteMenu.Click += (_, _) => SetRefreshMode("2 minutes");
        Refresh5MinuteMenu.Click += (_, _) => SetRefreshMode("5 minutes");
        RefreshManualMenu.Click += (_, _) => SetRefreshMode("Manual only");
        LockPositionMenu.Click += (_, _) =>
        {
            _state.Locked = LockPositionMenu.IsChecked;
            Root.Cursor = _state.Locked ? System.Windows.Input.Cursors.Arrow : System.Windows.Input.Cursors.SizeWE;
            SaveState();
        };
        ExitMenu.Click += (_, _) => Close();

        _refreshTimer.Tick += (_, _) =>
        {
            if (_state.RefreshMode != "Manual only") RefreshUsage();
            SyncDisplayGeometry();
        };
        _taskbarTimer.Interval = TimeSpan.FromSeconds(1);
        _taskbarTimer.Tick += (_, _) =>
        {
            nint currentTaskbar = FindTaskbarForSelectedDisplay();
            if (currentTaskbar != _taskbarHandle)
            {
                SyncDisplayGeometry();
                AttachWidgetToTaskbar();
            }
        };
        _sessionTimer.Interval = TimeSpan.FromMilliseconds(500);
        _sessionTimer.Tick += (_, _) =>
        {
            _sessionTimer.Stop();
            if (!_sessionRefreshQueued || _state.RefreshMode == "Manual only") return;
            _sessionRefreshQueued = false;
            RefreshUsage();
        };
        _refreshFeedbackTimer.Interval = TimeSpan.FromSeconds(6);
        _refreshFeedbackTimer.Tick += (_, _) =>
        {
            _refreshFeedbackTimer.Stop();
            RefreshMenu.Header = "Refresh usage";
        };
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _windowHandle = new WindowInteropHelper(this).Handle;
        _dipPerPixel = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice.M11 ?? 1;
        LoadState();
        _widgetLeft = _state.Left;
        ApplyStateToMenus();
        ReloadContextModeMenu();
        SetRefreshTimerInterval();
        ConfigureSessionMonitoring();
        SyncDisplayGeometry();
        SetTaskbarPosition(_widgetLeft);
        AttachWidgetToTaskbar();
        RefreshUsage();
        _refreshTimer.Start();
        _taskbarTimer.Start();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _refreshTimer.Stop();
        _taskbarTimer.Stop();
        _sessionTimer.Stop();
        _refreshFeedbackTimer.Stop();
        _sessionWatcher?.Dispose();
        SaveState();
        System.Windows.Application.Current.Shutdown();
    }

    private void ReloadContextModeMenu()
    {
        ContextConfigState state = CodexConfigService.ReadState(CodexConfigService.GlobalConfigPath);
        ContextDefaultMenu.IsChecked = state.Mode?.Index == 0;
        Context300KMenu.IsChecked = state.Mode?.Index == 1;
        Context600KMenu.IsChecked = state.Mode?.Index == 2;
        Context1MMenu.IsChecked = state.Mode?.Index == 3;
        if (state.IsCustom)
            UpdateCustomContextToolTip(state);
        else
            UpdateContextToolTip(state.Mode!);
    }

    private void ApplyContextMode(int index)
    {
        ContextMode mode = CodexConfigService.Modes[index];
        ConfigWriteResult result = CodexConfigService.ApplyGlobal(mode);
        ReloadContextModeMenu();
        ContextModeMenu.ToolTip = result.Message;
    }

    private void UpdateContextToolTip(ContextMode mode, string? resultMessage = null)
    {
        string summary = $"{mode.Name} · {mode.ContextLabel} · Global Codex setting";
        ContextModeMenu.Header = $"Context mode ({CompactModeLabel(mode)})";
        ContextModeMenu.ToolTip = resultMessage is null ? summary : $"{resultMessage}\n{summary}";
    }

    private void UpdateCustomContextToolTip(ContextConfigState state)
    {
        string detail = state.ReadFailed
            ? "Could not read the current context settings"
            : $"{FormatTokenCount(state.ContextWindow)} context · {FormatTokenCount(state.CompactLimit)} auto-compaction";
        ContextModeMenu.Header = "Context mode (Custom)";
        ContextModeMenu.ToolTip =
            $"Custom · {detail} · config.toml is preserved until you select a preset";
    }

    private static string FormatTokenCount(int? value)
    {
        if (value is null) return "model default";
        return value.Value >= 1_000_000 && value.Value % 1_000_000 == 0
            ? $"{value.Value / 1_000_000}M"
            : value.Value % 1_000 == 0
                ? $"{value.Value / 1_000}K"
                : value.Value.ToString("N0", CultureInfo.InvariantCulture);
    }

    private static string CompactModeLabel(ContextMode mode) => mode.IsDefault
        ? "DEFAULT"
        : mode.ContextLabel
            .Replace(" context", string.Empty, StringComparison.Ordinal)
            .Replace("~", string.Empty, StringComparison.Ordinal);

    private void LoadState()
    {
        if (!File.Exists(_stateFile)) return;
        try
        {
            WidgetState? saved = JsonSerializer.Deserialize<WidgetState>(File.ReadAllText(_stateFile));
            if (saved is not null) _state = saved;
        }
        catch (Exception exception)
        {
            WriteLog($"Could not load settings: {exception.Message}");
        }

        if (_state.RefreshMode is not ("30 seconds" or "2 minutes" or "5 minutes" or "Manual only"))
            _state.RefreshMode = "30 seconds";
        if (_state.LayoutVersion < 2)
        {
            _state.Left = 0;
            _state.LayoutVersion = 2;
        }
    }

    private void SaveState()
    {
        _state.Left = _widgetLeft;
        try
        {
            string json = JsonSerializer.Serialize(_state, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_stateFile, json);
        }
        catch (Exception exception)
        {
            WriteLog($"Could not save settings: {exception.Message}");
        }
    }

    private void WriteLog(string message)
    {
        if (string.IsNullOrWhiteSpace(message) || message == _lastLogMessage) return;
        _lastLogMessage = message;
        try
        {
            if (File.Exists(_logFile) && new FileInfo(_logFile).Length > 262_144)
                File.Move(_logFile, _logFile + ".old", true);
            File.AppendAllText(_logFile, $"{DateTime.UtcNow:u} {message}{Environment.NewLine}");
        }
        catch
        {
        }
    }

    private void ApplyStateToMenus()
    {
        LockPositionMenu.IsChecked = _state.Locked;
        Root.Cursor = _state.Locked ? System.Windows.Input.Cursors.Arrow : System.Windows.Input.Cursors.SizeWE;
        Refresh30Menu.IsChecked = _state.RefreshMode == "30 seconds";
        Refresh2MinuteMenu.IsChecked = _state.RefreshMode == "2 minutes";
        Refresh5MinuteMenu.IsChecked = _state.RefreshMode == "5 minutes";
        RefreshManualMenu.IsChecked = _state.RefreshMode == "Manual only";
    }

    private void SetRefreshMode(string mode)
    {
        _state.RefreshMode = mode;
        ApplyStateToMenus();
        SetRefreshTimerInterval();
        ConfigureSessionMonitoring();
        SaveState();
    }

    private void SetRefreshTimerInterval()
    {
        int seconds = _state.RefreshMode switch
        {
            "2 minutes" => 120,
            "5 minutes" => 300,
            "Manual only" => 3600,
            _ => 30
        };
        _refreshTimer.Interval = TimeSpan.FromSeconds(seconds);
    }

    private void ConfigureSessionMonitoring()
    {
        _sessionRefreshQueued = false;
        _sessionTimer.Stop();
        _sessionWatcher?.Dispose();
        _sessionWatcher = null;
        if (_state.RefreshMode == "Manual only" || !Directory.Exists(UsageReader.SessionRoot)) return;

        try
        {
            _sessionWatcher = new FileSystemWatcher(UsageReader.SessionRoot, "*.jsonl")
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                EnableRaisingEvents = true
            };
            _sessionWatcher.Changed += OnSessionFileChanged;
            _sessionWatcher.Created += OnSessionFileChanged;
            _sessionWatcher.Renamed += OnSessionFileRenamed;
            _sessionWatcher.Error += (_, _) => QueueSessionRefresh();
        }
        catch (Exception exception)
        {
            WriteLog($"Could not monitor Codex sessions: {exception.Message}");
        }
    }

    private void OnSessionFileChanged(object sender, FileSystemEventArgs e) => QueueSessionRefresh();

    private void OnSessionFileRenamed(object sender, RenamedEventArgs e) => QueueSessionRefresh();

    private void QueueSessionRefresh()
    {
        Dispatcher.InvokeAsync(() =>
        {
            if (_state.RefreshMode == "Manual only") return;
            _sessionRefreshQueued = true;
            _sessionTimer.Stop();
            _sessionTimer.Start();
        });
    }

    private void RefreshUsage(bool manualRequest = false)
    {
        DateTimeOffset checkedAt = DateTimeOffset.Now;
        UsageSnapshot? snapshot = UsageReader.GetLatestSnapshot();
        if (snapshot is null)
        {
            PercentText.Text = "--%";
            TimeText.Text = "WEEKLY · Unavailable";
            RemainingBar.Width = 0;
            RemainingBar.Background = GrayBrush;
            PercentText.Foreground = MutedTextBrush;
            SetShortUsageUnavailable();
            Root.ToolTip = "ChatGPT Codex - No usage data found";
            WriteLog("No Codex usage data found");
            if (manualRequest) ShowRefreshFeedback(checkedAt, hasUsageData: false);
            return;
        }

        double left = Math.Clamp(Math.Round(100 - snapshot.Long.UsedPercent), 0, 100);
        PercentText.Text = $"{left:0}%";
        RemainingBar.Width = 112 * left / 100;

        TimeSpan age = DateTimeOffset.Now - snapshot.EventTime;
        bool stale = age.TotalMinutes > 5;
        string freshness = stale ? "Data may be stale" : "Live";
        if (stale)
        {
            RemainingBar.Background = GrayBrush;
            PercentText.Foreground = MutedTextBrush;
        }
        else
        {
            RemainingBar.Background = left <= 10 ? RedBrush : left <= 30 ? AmberBrush : GreenBrush;
            PercentText.Foreground = TextBrush;
        }

        string resetText = FormatResetCountdown(snapshot.Long.ResetsAt);
        if (snapshot.Long.ResetsAt is long resetUnix)
        {
            DateTimeOffset reset = DateTimeOffset.FromUnixTimeSeconds(resetUnix).ToLocalTime();
            TimeText.Text = $"WEEKLY · Resets {reset.ToString("MMM d", CultureInfo.InvariantCulture)}";
        }
        else
        {
            TimeText.Text = "WEEKLY · Reset unavailable";
        }

        double? shortLeft = UpdateShortUsage(snapshot.Short, stale);
        string shortText = shortLeft is null ? "--" : shortLeft.Value.ToString("0", CultureInfo.InvariantCulture);
        string shortResetText = snapshot.Short is null ? "unavailable" : FormatResetCountdown(snapshot.Short.ResetsAt);
        string tooltip = $"ChatGPT Codex - 7D {left:0}% left; resets {resetText}; 5H {shortText}% left; resets {shortResetText}; " +
                         $"{freshness}; Codex data {snapshot.EventTime:HH:mm:ss}; checked {checkedAt:HH:mm:ss}";
        if (!IsCodexRunning()) tooltip += "; Codex is not running";
        Root.ToolTip = tooltip;
        PulseStatusForNewEvent(snapshot.EventTime);
        if (manualRequest) ShowRefreshFeedback(checkedAt, hasUsageData: true);
    }

    private double? UpdateShortUsage(RateLimit? shortLimit, bool stale)
    {
        if (shortLimit is null)
        {
            SetShortUsageUnavailable();
            return null;
        }

        double left = Math.Clamp(Math.Round(100 - shortLimit.UsedPercent), 0, 100);
        ShortPercentText.Text = $"{left:0}%";
        ShortRemainingBar.Width = 126 * left / 100;
        ShortTimeText.Text = $"5 HOURS · {FormatResetCountdown(shortLimit.ResetsAt)}";
        if (stale)
        {
            ShortRemainingBar.Background = GrayBrush;
            ShortPercentText.Foreground = MutedTextBrush;
        }
        else
        {
            ShortRemainingBar.Background = left <= 10 ? RedBrush : left <= 30 ? AmberBrush : BlueBrush;
            ShortPercentText.Foreground = TextBrush;
        }
        return left;
    }

    private void SetShortUsageUnavailable()
    {
        ShortTimeText.Text = "5 HOURS · Unavailable";
        ShortPercentText.Text = "--%";
        ShortPercentText.Foreground = MutedTextBrush;
        ShortRemainingBar.Width = 0;
        ShortRemainingBar.Background = GrayBrush;
    }

    private static string FormatResetCountdown(long? resetUnix)
    {
        if (resetUnix is not long value) return "Reset unavailable";
        TimeSpan remaining = DateTimeOffset.FromUnixTimeSeconds(value).ToLocalTime() - DateTimeOffset.Now;
        if (remaining.TotalSeconds <= 0) return "Ready to reset";
        return remaining.TotalDays >= 1
            ? $"Resets in {Math.Floor(remaining.TotalDays):0}d {remaining.Hours}h"
            : $"Resets in {Math.Floor(remaining.TotalHours):0}h {remaining.Minutes}m";
    }

    private void PulseStatusForNewEvent(DateTimeOffset eventTime)
    {
        if (_lastPulsedEventTime == eventTime) return;
        _lastPulsedEventTime = eventTime;
        StatusPulse.BeginAnimation(
            UIElement.OpacityProperty,
            new DoubleAnimation(0.72, 0, TimeSpan.FromMilliseconds(760)) { AutoReverse = false });
    }

    private void ShowRefreshFeedback(DateTimeOffset checkedAt, bool hasUsageData)
    {
        RefreshMenu.Header = hasUsageData
            ? $"Usage checked {checkedAt:HH:mm:ss}"
            : $"Checked {checkedAt:HH:mm:ss} - no data";
        _refreshFeedbackTimer.Stop();
        _refreshFeedbackTimer.Start();
    }

    private static bool IsCodexRunning()
    {
        try
        {
            return Process.GetProcessesByName("codex").Length > 0 ||
                   Process.GetProcessesByName("ChatGPT").Length > 0;
        }
        catch
        {
            return false;
        }
    }

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_state.Locked || e.ChangedButton != MouseButton.Left) return;
        _isDragging = true;
        _dragStartX = System.Windows.Forms.Cursor.Position.X;
        _dragStartLeft = _widgetLeft;
        Root.CaptureMouse();
        e.Handled = true;
    }

    private void OnMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_isDragging || e.LeftButton != MouseButtonState.Pressed) return;
        int deltaPixels = System.Windows.Forms.Cursor.Position.X - _dragStartX;
        double target = _dragStartLeft + deltaPixels * _dipPerPixel;
        _widgetLeft = Math.Clamp(target, _screenLeft, Math.Max(_screenLeft, _screenRight - Width));
        if (_isEmbedded) PositionEmbeddedWidget(); else Left = _widgetLeft;
        e.Handled = true;
    }

    private void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isDragging) return;
        _isDragging = false;
        Root.ReleaseMouseCapture();
        SetTaskbarPosition(_widgetLeft);
        e.Handled = true;
    }

    private void MoveToNextDisplay()
    {
        int count = System.Windows.Forms.Screen.AllScreens.Length;
        if (count == 0) return;
        _state.ScreenIndex = (_state.ScreenIndex + 1) % count;
        SyncDisplayGeometry();
        SetTaskbarPosition(_screenLeft);
    }

    private void SetTaskbarPosition(double left)
    {
        _widgetLeft = Math.Clamp(left, _screenLeft, Math.Max(_screenLeft, _screenRight - Width));
        if (_isEmbedded)
        {
            PositionEmbeddedWidget();
        }
        else
        {
            Left = _widgetLeft;
            Top = _windowTop;
        }
        SaveState();
    }

    private void PositionEmbeddedWidget()
    {
        if (!_isEmbedded || _windowHandle == 0 || _taskbarHandle == 0) return;
        if (!NativeMethods.GetWindowRect(_taskbarHandle, out NativeMethods.Rect rect)) return;
        double scale = _dipPerPixel > 0 ? _dipPerPixel : 1;
        int screenLeftPixels = (int)Math.Round(_widgetLeft / scale);
        int childX = screenLeftPixels - rect.Left;
        int childWidth = (int)Math.Round(Width / scale);
        int childHeight = Math.Max(1, rect.Bottom - rect.Top);
        NativeMethods.SetWindowPos(
            _windowHandle, 0, childX, 0, childWidth, childHeight,
            NativeMethods.SwpNoActivate | NativeMethods.SwpNoZOrder);
    }

    private void SyncDisplayGeometry()
    {
        if (_isDragging) return;
        System.Windows.Forms.Screen[] screens = System.Windows.Forms.Screen.AllScreens;
        if (screens.Length == 0) return;
        if (_state.ScreenIndex < 0 || _state.ScreenIndex >= screens.Length)
        {
            int primaryIndex = Array.FindIndex(screens, screen => screen.Primary);
            _state.ScreenIndex = primaryIndex >= 0 ? primaryIndex : 0;
        }

        _dipPerPixel = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice.M11 ?? _dipPerPixel;
        var screen = screens[_state.ScreenIndex];
        var bounds = screen.Bounds;
        var work = screen.WorkingArea;
        _screenLeft = bounds.Left * _dipPerPixel;
        _screenRight = bounds.Right * _dipPerPixel;

        if (_isEmbedded)
        {
            _widgetLeft = Math.Clamp(_widgetLeft, _screenLeft, Math.Max(_screenLeft, _screenRight - Width));
            PositionEmbeddedWidget();
            return;
        }

        double bottomGap = (bounds.Bottom - work.Bottom) * _dipPerPixel;
        double topGap = (work.Top - bounds.Top) * _dipPerPixel;
        double targetHeight;
        if (bottomGap >= 20)
        {
            ShowAfterAutoHide();
            _windowTop = work.Bottom * _dipPerPixel;
            targetHeight = bottomGap;
        }
        else if (topGap >= 20)
        {
            ShowAfterAutoHide();
            _windowTop = bounds.Top * _dipPerPixel;
            targetHeight = topGap;
        }
        else
        {
            bool nearEdge = System.Windows.Forms.Cursor.Position.Y >= bounds.Bottom - 64;
            bool taskbarActive = NativeMethods.GetForegroundWindow() == _taskbarHandle;
            if (!nearEdge && !taskbarActive)
            {
                if (IsVisible) Hide();
                _hiddenForAutoHide = true;
                return;
            }
            ShowAfterAutoHide();
            targetHeight = 48;
            _windowTop = bounds.Bottom * _dipPerPixel - targetHeight;
        }

        Height = targetHeight;
        Top = _windowTop;
        Left = Math.Clamp(_widgetLeft, _screenLeft, Math.Max(_screenLeft, _screenRight - Width));
    }

    private void ShowAfterAutoHide()
    {
        if (!_hiddenForAutoHide) return;
        Show();
        _hiddenForAutoHide = false;
    }

    private void AttachWidgetToTaskbar()
    {
        if (_windowHandle == 0) return;
        nint taskbar = FindTaskbarForSelectedDisplay();
        if (taskbar == 0) return;

        long style = NativeMethods.GetWindowLongPtr(_windowHandle, NativeMethods.GwlStyle).ToInt64();
        long childStyle = (style | NativeMethods.WsChild) & ~NativeMethods.WsPopup;
        NativeMethods.SetWindowLongPtr(_windowHandle, NativeMethods.GwlStyle, new nint(childStyle));
        NativeMethods.SetParent(_windowHandle, taskbar);
        _taskbarHandle = taskbar;
        _isEmbedded = NativeMethods.GetParent(_windowHandle) == taskbar;
        if (_isEmbedded)
        {
            Topmost = false;
            PositionEmbeddedWidget();
        }
        else
        {
            Topmost = true;
            WriteLog("Could not embed widget into the Windows taskbar");
        }
    }

    private nint FindTaskbarForSelectedDisplay()
    {
        System.Windows.Forms.Screen[] screens = System.Windows.Forms.Screen.AllScreens;
        if (screens.Length == 0) return NativeMethods.FindWindow("Shell_TrayWnd", null);
        int index = Math.Clamp(_state.ScreenIndex, 0, screens.Length - 1);
        System.Drawing.Rectangle bounds = screens[index].Bounds;
        return NativeMethods.FindTaskbarForBounds(new NativeMethods.Rect
        {
            Left = bounds.Left,
            Top = bounds.Top,
            Right = bounds.Right,
            Bottom = bounds.Bottom
        });
    }

    private static SolidColorBrush Brush(string color) =>
        new((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(color));
}
