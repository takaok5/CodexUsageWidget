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
    private static readonly SolidColorBrush GrayBrush = Brush("#FF777777");
    private static readonly SolidColorBrush TextBrush = Brush("#FFF3F3F3");
    private static readonly SolidColorBrush MutedTextBrush = Brush("#FFAAAAAA");

    private readonly DispatcherTimer _refreshTimer = new();
    private readonly DispatcherTimer _taskbarTimer = new();
    private readonly DispatcherTimer _sessionTimer = new();
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
    private DateTime _lastSessionWriteUtc = DateTime.MinValue;
    private string _activeAnimationKey = string.Empty;
    private string _lastLogMessage = string.Empty;
    private ContextSettingsWindow? _contextSettingsWindow;
    private bool _loadingContextMode;

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
        RefreshMenu.Click += (_, _) => RefreshUsage();
        ContextModeMenu.Click += (_, _) => ShowContextSettings();
        ContextButton.Click += (_, _) => ShowContextSettings();
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
        AnimateIconMenu.Click += (_, _) =>
        {
            _state.Animate = AnimateIconMenu.IsChecked;
            SetIconAnimation();
            SaveState();
        };
        AnimationSlowMenu.Click += (_, _) => SetAnimationSpeed("Slow");
        AnimationNormalMenu.Click += (_, _) => SetAnimationSpeed("Normal");
        AnimationFastMenu.Click += (_, _) => SetAnimationSpeed("Fast");
        ExitMenu.Click += (_, _) => Close();

        _refreshTimer.Tick += (_, _) =>
        {
            if (_state.RefreshMode != "Manual only") RefreshUsage();
            SetIconAnimation();
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
        _sessionTimer.Interval = TimeSpan.FromSeconds(2);
        _sessionTimer.Tick += (_, _) =>
        {
            DateTime latest = UsageReader.GetLatestWriteTimeUtc();
            if (latest > _lastSessionWriteUtc)
            {
                _lastSessionWriteUtc = latest;
                RefreshUsage();
            }
        };
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _windowHandle = new WindowInteropHelper(this).Handle;
        _dipPerPixel = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice.M11 ?? 1;
        LoadState();
        _widgetLeft = _state.Left;
        ApplyStateToMenus();
        ReloadTaskbarContextMode();
        SetRefreshTimerInterval();
        SyncDisplayGeometry();
        SetTaskbarPosition(_widgetLeft);
        AttachWidgetToTaskbar();
        SetIconAnimation();
        RefreshUsage();
        _lastSessionWriteUtc = UsageReader.GetLatestWriteTimeUtc();
        _refreshTimer.Start();
        _taskbarTimer.Start();
        _sessionTimer.Start();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _refreshTimer.Stop();
        _taskbarTimer.Stop();
        _sessionTimer.Stop();
        SaveState();
        System.Windows.Application.Current.Shutdown();
    }

    internal void ShowContextSettings()
    {
        if (_contextSettingsWindow is { IsLoaded: true })
        {
            _contextSettingsWindow.Activate();
            return;
        }

        _contextSettingsWindow = new ContextSettingsWindow(
            _state,
            SaveState,
            ReloadTaskbarContextMode,
            GetPopupAnchor);
        _contextSettingsWindow.Closed += (_, _) => _contextSettingsWindow = null;
        _contextSettingsWindow.Show();
    }

    private void ReloadTaskbarContextMode()
    {
        ContextMode? configuredMode;
        bool projectScope = _state.ContextScope == "Project";
        bool projectAvailable = projectScope &&
                                !string.IsNullOrWhiteSpace(_state.ContextProjectPath) &&
                                Directory.Exists(_state.ContextProjectPath);

        if (projectAvailable)
        {
            string projectConfig = CodexConfigService.GetProjectConfigPath(_state.ContextProjectPath);
            configuredMode = CodexConfigService.ReadMode(projectConfig);
        }
        else
        {
            configuredMode = CodexConfigService.ReadMode(CodexConfigService.GlobalConfigPath);
        }

        ContextMode mode = configuredMode ?? CodexConfigService.Modes[0];
        _loadingContextMode = true;
        TaskbarModeSlider.Value = mode.Index;
        TaskbarModeText.Text = CompactModeLabel(mode);
        TaskbarModeSlider.IsEnabled = !projectScope || projectAvailable;
        _loadingContextMode = false;
        UpdateContextToolTips(mode, projectScope, projectAvailable);
    }

    private void ApplyTaskbarContextMode()
    {
        ContextMode mode = CodexConfigService.Modes[(int)Math.Round(TaskbarModeSlider.Value)];
        if (_loadingContextMode) return;
        TaskbarModeText.Text = CompactModeLabel(mode);

        ConfigWriteResult result;
        if (_state.ContextScope == "Project")
        {
            if (string.IsNullOrWhiteSpace(_state.ContextProjectPath) || !Directory.Exists(_state.ContextProjectPath))
            {
                ContextButton.ToolTip = "Choose an available project before moving the context slider.";
                ReloadTaskbarContextMode();
                ShowContextSettings();
                return;
            }

            result = CodexConfigService.ApplyProject(_state.ContextProjectPath, mode);
            if (result.Success && _state.ContextAlsoGlobal)
            {
                ConfigWriteResult globalResult = CodexConfigService.ApplyGlobal(mode);
                if (!globalResult.Success)
                {
                    string partialMessage = $"Project updated, global update failed: {globalResult.Message}";
                    ContextButton.ToolTip = partialMessage;
                    _contextSettingsWindow?.RefreshTargetStatus(partialMessage, false);
                    return;
                }
            }
        }
        else
        {
            result = CodexConfigService.ApplyGlobal(mode);
        }

        string message = result.Message;
        UpdateContextToolTips(mode, _state.ContextScope == "Project", true, message);
        _contextSettingsWindow?.RefreshTargetStatus(message, result.Success);
    }

    private void UpdateContextToolTips(
        ContextMode mode,
        bool projectScope,
        bool projectAvailable,
        string? resultMessage = null)
    {
        string target = projectScope
            ? projectAvailable
                ? $"Project: {Path.GetFileName(_state.ContextProjectPath)}"
                : "Project not selected"
            : "Global defaults";
        string summary = $"{mode.Name} · {mode.ContextLabel} · {target}";
        TaskbarModeSlider.ToolTip = resultMessage is null ? summary : $"{resultMessage}\n{summary}";
        ContextButton.ToolTip = $"Context target: {target}\nClick to choose global or project";
    }

    private static string CompactModeLabel(ContextMode mode) => mode.IsDefault
        ? "DEFAULT"
        : mode.ContextLabel
            .Replace(" context", string.Empty, StringComparison.Ordinal)
            .Replace("~", string.Empty, StringComparison.Ordinal);

    private PopupAnchor? GetPopupAnchor()
    {
        if (_windowHandle == 0 || !NativeMethods.GetWindowRect(_windowHandle, out NativeMethods.Rect rect))
            return null;

        System.Windows.Forms.Screen[] screens = System.Windows.Forms.Screen.AllScreens;
        if (screens.Length == 0) return null;
        int index = Math.Clamp(_state.ScreenIndex, 0, screens.Length - 1);
        System.Drawing.Rectangle work = screens[index].WorkingArea;
        double scale = _dipPerPixel > 0 ? _dipPerPixel : 1;
        return new PopupAnchor(
            rect.Left * scale,
            rect.Top * scale,
            rect.Right * scale,
            rect.Bottom * scale,
            work.Left * scale,
            work.Top * scale,
            work.Right * scale,
            work.Bottom * scale);
    }

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

        if (_state.AnimationSpeed is not ("Slow" or "Normal" or "Fast"))
            _state.AnimationSpeed = "Normal";
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
        AnimateIconMenu.IsChecked = _state.Animate;
        AnimationSlowMenu.IsChecked = _state.AnimationSpeed == "Slow";
        AnimationNormalMenu.IsChecked = _state.AnimationSpeed == "Normal";
        AnimationFastMenu.IsChecked = _state.AnimationSpeed == "Fast";
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

    private void SetAnimationSpeed(string speed)
    {
        _state.AnimationSpeed = speed;
        _activeAnimationKey = string.Empty;
        SetIconAnimation();
        SaveState();
    }

    private void SetIconAnimation()
    {
        bool allowMotion = SystemParameters.ClientAreaAnimation;
        try
        {
            var power = System.Windows.Forms.SystemInformation.PowerStatus;
            if (power.PowerLineStatus == System.Windows.Forms.PowerLineStatus.Offline &&
                power.BatteryLifePercent is >= 0 and <= 0.20f)
            {
                allowMotion = false;
            }
        }
        catch
        {
        }

        bool enabled = _state.Animate && allowMotion;
        string key = enabled ? _state.AnimationSpeed : "Off";
        if (key == _activeAnimationKey) return;

        IconHost.BeginAnimation(OpacityProperty, null);
        IconHost.Opacity = 1;
        if (enabled)
        {
            double seconds = _state.AnimationSpeed switch
            {
                "Slow" => 2.4,
                "Fast" => 0.9,
                _ => 1.6
            };
            var animation = new DoubleAnimation(1, 0.72, TimeSpan.FromSeconds(seconds))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
            };
            IconHost.BeginAnimation(OpacityProperty, animation);
        }

        _activeAnimationKey = key;
        ApplyStateToMenus();
    }

    private void RefreshUsage()
    {
        UsageSnapshot? snapshot = UsageReader.GetLatestSnapshot();
        if (snapshot is null)
        {
            PercentText.Text = "--%";
            TimeText.Text = "Weekly Reset unavailable";
            RemainingBar.Width = 0;
            Root.ToolTip = "ChatGPT Codex - No usage data found";
            WriteLog("No Codex usage data found");
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
                         $"{freshness}; updated {snapshot.EventTime:HH:mm:ss}";
        if (!IsCodexRunning()) tooltip += "; Codex is not running";
        Root.ToolTip = tooltip;
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
