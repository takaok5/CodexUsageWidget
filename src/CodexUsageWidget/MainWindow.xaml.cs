using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace CodexUsageWidget;

public partial class MainWindow : Window
{
    private static readonly SolidColorBrush GreenBrush = Brush("#FF70D88B");
    private static readonly SolidColorBrush AmberBrush = Brush("#FFF59E0B");
    private static readonly SolidColorBrush RedBrush = Brush("#FFEF4444");
    private static readonly SolidColorBrush GrayBrush = Brush("#FF777777");
    private static readonly SolidColorBrush TextBrush = Brush("#FFF3F3F3");
    private static readonly SolidColorBrush MutedTextBrush = Brush("#FFAAAAAA");

    private readonly DispatcherTimer _refreshTimer = new();
    private readonly DispatcherTimer _taskbarTimer = new();
    private readonly DispatcherTimer _refreshFeedbackTimer = new();
    private readonly string _configDirectory;
    private readonly string _stateFile;
    private readonly string _logFile;

    private WidgetState _state = new();
    private nint _windowHandle;
    private nint _taskbarHandle;
    private bool _isAnchoredToTaskbar;
    private bool _isDragging;
    private bool _hiddenForAutoHide;
    private double _screenLeft;
    private double _screenRight = SystemParameters.PrimaryScreenWidth;
    private double _windowTop;
    private double _widgetLeft;
    private double _logicalWidgetWidth = 279;
    private double _dipPerPixel = 1;
    private int _dragStartX;
    private double _dragStartLeft;
    private string _lastLogMessage = string.Empty;
    private bool _loadingContextMode;

    public MainWindow()
    {
        InitializeComponent();
        Opacity = 0;

        _configDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CodexUsageWidget");
        Directory.CreateDirectory(_configDirectory);
        _stateFile = Path.Combine(_configDirectory, "widget-state.json");
        _logFile = Path.Combine(_configDirectory, "widget-errors.log");

        SourceInitialized += OnSourceInitialized;
        ContentRendered += OnContentRendered;
        Closed += OnClosed;

        Root.MouseLeftButtonDown += OnMouseLeftButtonDown;
        Root.MouseMove += OnMouseMove;
        Root.MouseLeftButtonUp += OnMouseLeftButtonUp;

        MoveLeftMenu.Click += (_, _) => SetTaskbarPosition(_screenLeft);
        PlaceWeatherMenu.Click += (_, _) => SetTaskbarPosition(_screenLeft + 205);
        MoveRightMenu.Click += (_, _) => SetTaskbarPosition(_screenRight - PhysicalWidgetWidth());
        NextDisplayMenu.Click += (_, _) => MoveToNextDisplay();
        RefreshMenu.Click += (_, _) => RefreshUsage(manualRequest: true);
        TaskbarModeSlider.ValueChanged += (_, _) => ApplyTaskbarContextMode();
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
        };
        _taskbarTimer.Interval = TimeSpan.FromSeconds(1);
        _taskbarTimer.Tick += (_, _) =>
        {
            nint currentTaskbar = FindTaskbarForSelectedDisplay();
            if (currentTaskbar != _taskbarHandle)
            {
                SyncDisplayGeometry();
                AnchorWidgetToTaskbar();
            }
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
        if (double.IsFinite(Width) && Width > 1) _logicalWidgetWidth = Width;
        _dipPerPixel = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice.M11 ?? 1;
        LoadState();
        _widgetLeft = _state.Left;
        ApplyStateToMenus();
        ReloadTaskbarContextMode();
        SetRefreshTimerInterval();
        SyncDisplayGeometry();
        SetTaskbarPosition(_widgetLeft);
        RefreshUsage();
        _refreshTimer.Start();
        _taskbarTimer.Start();
    }

    private void OnContentRendered(object? sender, EventArgs e)
    {
        ContentRendered -= OnContentRendered;
        AnchorWidgetToTaskbar();
        Opacity = 1;
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _refreshTimer.Stop();
        _taskbarTimer.Stop();
        _refreshFeedbackTimer.Stop();
        SaveState();
        System.Windows.Application.Current.Shutdown();
    }

    private void ReloadTaskbarContextMode()
    {
        ContextConfigState state = CodexConfigService.ReadState(CodexConfigService.GlobalConfigPath);
        _loadingContextMode = true;
        TaskbarModeSlider.Value = state.IsCustom ? CustomSliderPosition(state) : state.Mode!.Index;
        TaskbarModeText.Text = state.IsCustom ? "CUSTOM" : CompactModeLabel(state.Mode!);
        TaskbarModeSlider.IsEnabled = true;
        _loadingContextMode = false;
        if (state.IsCustom)
            UpdateCustomContextToolTip(state);
        else
            UpdateContextToolTip(state.Mode!);
    }

    private void ApplyTaskbarContextMode()
    {
        ContextMode mode = CodexConfigService.Modes[(int)Math.Round(TaskbarModeSlider.Value)];
        if (_loadingContextMode) return;

        ConfigWriteResult result = CodexConfigService.ApplyGlobal(mode);
        if (!result.Success)
        {
            ReloadTaskbarContextMode();
            string currentState = TaskbarModeSlider.ToolTip as string ?? "Current global setting preserved";
            TaskbarModeSlider.ToolTip = $"{result.Message}\n{currentState}";
            return;
        }

        TaskbarModeText.Text = CompactModeLabel(mode);
        UpdateContextToolTip(mode, result.Message);
    }

    private void UpdateContextToolTip(ContextMode mode, string? resultMessage = null)
    {
        string summary = $"{mode.Name} · {mode.ContextLabel} · Global Codex setting";
        TaskbarModeSlider.ToolTip = resultMessage is null ? summary : $"{resultMessage}\n{summary}";
    }

    private void UpdateCustomContextToolTip(ContextConfigState state)
    {
        string detail = state.ReadFailed
            ? "Could not read the current context settings"
            : $"{FormatTokenCount(state.ContextWindow)} context · {FormatTokenCount(state.CompactLimit)} auto-compaction";
        TaskbarModeSlider.ToolTip =
            $"Custom · {detail} · config.toml is preserved until you move the slider";
    }

    private static double CustomSliderPosition(ContextConfigState state)
    {
        if (state.ContextWindow is null) return 0.5;

        ContextMode nearest = CodexConfigService.Modes
            .Where(mode => !mode.IsDefault)
            .OrderBy(mode => Math.Abs((long)mode.ContextWindow!.Value - state.ContextWindow.Value) +
                             (state.CompactLimit is null
                                 ? 0
                                 : Math.Abs((long)mode.CompactLimit!.Value - state.CompactLimit.Value) / 2))
            .First();
        return nearest.Index >= 3 ? 2.5 : nearest.Index + 0.5;
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

    private void RefreshUsage(bool manualRequest = false)
    {
        DateTimeOffset checkedAt = DateTimeOffset.Now;
        UsageSnapshot? snapshot = UsageReader.GetLatestSnapshot();
        if (snapshot is null)
        {
            PercentText.Text = "--%";
            TimeText.Text = "Weekly Reset unavailable";
            RemainingBar.Width = 0;
            Root.ToolTip = "ChatGPT Codex - No usage data found";
            WriteLog("No Codex usage data found");
            if (manualRequest) ShowRefreshFeedback(checkedAt, hasUsageData: false);
            return;
        }

        double left = Math.Clamp(Math.Round(100 - snapshot.Long.UsedPercent), 0, 100);
        PercentText.Text = $"{left:0}%";
        RemainingBar.Width = 108 * left / 100;

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

        double? shortLeft = snapshot.Short is null
            ? null
            : Math.Clamp(Math.Round(100 - snapshot.Short.UsedPercent), 0, 100);
        string shortText = shortLeft is null ? "--" : shortLeft.Value.ToString("0", CultureInfo.InvariantCulture);
        string resetText = "reset unavailable";
        if (snapshot.Long.ResetsAt is long resetUnix)
        {
            DateTimeOffset reset = DateTimeOffset.FromUnixTimeSeconds(resetUnix).ToLocalTime();
            TimeSpan remaining = reset - DateTimeOffset.Now;
            resetText = remaining.TotalSeconds <= 0
                ? "ready to reset"
                : remaining.TotalDays >= 1
                    ? $"in {Math.Floor(remaining.TotalDays):0}d {remaining.Hours}h"
                    : $"in {Math.Floor(remaining.TotalHours):0}h {remaining.Minutes}m";
            TimeText.Text = $"Weekly Resets {reset.ToString("MMM d", CultureInfo.InvariantCulture)}";
        }
        else
        {
            TimeText.Text = "Weekly Reset unavailable";
        }

        string tooltip = $"ChatGPT Codex - 7D {left:0}% left; 5H {shortText}% left; {resetText}; " +
                         $"{freshness}; Codex data {snapshot.EventTime:HH:mm:ss}; checked {checkedAt:HH:mm:ss}";
        if (!IsCodexRunning()) tooltip += "; Codex is not running";
        Root.ToolTip = tooltip;
        if (manualRequest) ShowRefreshFeedback(checkedAt, hasUsageData: true);
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
        double target = _dragStartLeft + deltaPixels;
        _widgetLeft = Math.Clamp(
            target,
            _screenLeft,
            Math.Max(_screenLeft, _screenRight - PhysicalWidgetWidth()));
        if (_isAnchoredToTaskbar) PositionAnchoredWidget(); else Left = _widgetLeft * _dipPerPixel;
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
        _widgetLeft = Math.Clamp(
            left,
            _screenLeft,
            Math.Max(_screenLeft, _screenRight - PhysicalWidgetWidth()));
        if (_isAnchoredToTaskbar)
        {
            PositionAnchoredWidget();
        }
        else
        {
            Left = _widgetLeft * _dipPerPixel;
            Top = _windowTop;
        }
        SaveState();
    }

    private double PhysicalWidgetWidth()
    {
        double scale = _dipPerPixel > 0 ? _dipPerPixel : 1;
        return _logicalWidgetWidth / scale;
    }

    private void PositionAnchoredWidget()
    {
        if (!_isAnchoredToTaskbar || _windowHandle == 0 || _taskbarHandle == 0) return;
        if (!NativeMethods.GetWindowRect(_taskbarHandle, out NativeMethods.Rect rect)) return;
        int widgetX = (int)Math.Round(_widgetLeft);
        int widgetWidth = (int)Math.Round(PhysicalWidgetWidth());
        int widgetHeight = Math.Max(1, rect.Bottom - rect.Top);
        NativeMethods.SetWindowPos(
            _windowHandle,
            NativeMethods.HwndTopmost,
            widgetX,
            rect.Top,
            widgetWidth,
            widgetHeight,
            NativeMethods.SwpNoActivate | NativeMethods.SwpShowWindow);
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
        _screenLeft = bounds.Left;
        _screenRight = bounds.Right;

        if (_isAnchoredToTaskbar)
        {
            _widgetLeft = Math.Clamp(
                _widgetLeft,
                _screenLeft,
                Math.Max(_screenLeft, _screenRight - PhysicalWidgetWidth()));
            PositionAnchoredWidget();
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
        Left = Math.Clamp(
            _widgetLeft,
            _screenLeft,
            Math.Max(_screenLeft, _screenRight - PhysicalWidgetWidth())) * _dipPerPixel;
    }

    private void ShowAfterAutoHide()
    {
        if (!_hiddenForAutoHide) return;
        Show();
        _hiddenForAutoHide = false;
    }

    private void AnchorWidgetToTaskbar()
    {
        if (_windowHandle == 0) return;
        nint taskbar = FindTaskbarForSelectedDisplay();
        if (taskbar == 0) return;

        _taskbarHandle = taskbar;
        _isAnchoredToTaskbar = true;
        Topmost = true;
        PositionAnchoredWidget();
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
