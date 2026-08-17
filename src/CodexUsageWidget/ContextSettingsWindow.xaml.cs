using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace CodexUsageWidget;

internal sealed record PopupAnchor(
    double Left,
    double Top,
    double Right,
    double Bottom,
    double WorkLeft,
    double WorkTop,
    double WorkRight,
    double WorkBottom);

public partial class ContextSettingsWindow : Window
{
    private static readonly SolidColorBrush SuccessBrush = Brush("#FF78D692");
    private static readonly SolidColorBrush ErrorBrush = Brush("#FFFF8B81");
    private static readonly SolidColorBrush MutedBrush = Brush("#FFA9A9A9");

    private readonly WidgetState _state;
    private readonly Action _saveState;
    private readonly Action _targetChanged;
    private readonly Func<PopupAnchor?> _anchorProvider;
    private readonly DispatcherTimer _projectRefreshTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    private bool _loading = true;
    private bool _projectsLoaded;
    private string _projectSignature = string.Empty;

    internal ContextSettingsWindow(
        WidgetState state,
        Action saveState,
        Action targetChanged,
        Func<PopupAnchor?> anchorProvider)
    {
        InitializeComponent();
        _state = state;
        _saveState = saveState;
        _targetChanged = targetChanged;
        _anchorProvider = anchorProvider;

        Loaded += OnLoaded;
        Closed += (_, _) => _projectRefreshTimer.Stop();
        KeyDown += OnKeyDown;
        CloseButton.Click += (_, _) => Close();
        RefreshProjectsButton.Click += (_, _) => RefreshProjects(true);
        GlobalScopeButton.Checked += (_, _) => SetScope(false);
        ProjectScopeButton.Checked += (_, _) => SetScope(true);
        ProjectComboBox.SelectionChanged += (_, _) => OnProjectChanged();
        AlsoGlobalCheckBox.Click += (_, _) =>
        {
            _state.ContextAlsoGlobal = AlsoGlobalCheckBox.IsChecked == true;
            _saveState();
            _targetChanged();
            LoadModeForScope();
        };
        _projectRefreshTimer.Tick += (_, _) => RefreshProjects(false);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        RefreshProjects(false);
        AlsoGlobalCheckBox.IsChecked = _state.ContextAlsoGlobal;
        if (_state.ContextScope == "Project")
            ProjectScopeButton.IsChecked = true;
        else
            GlobalScopeButton.IsChecked = true;

        _loading = false;
        LoadModeForScope();
        Dispatcher.BeginInvoke(PositionAboveTaskbar, DispatcherPriority.Loaded);
        _projectRefreshTimer.Start();
        Activate();
        Focus();
    }

    private void SetScope(bool project)
    {
        ProjectPanel.Visibility = project ? Visibility.Visible : Visibility.Collapsed;
        _state.ContextScope = project ? "Project" : "Global";
        Dispatcher.BeginInvoke(PositionAboveTaskbar, DispatcherPriority.Loaded);
        if (!_loading)
        {
            _saveState();
            _targetChanged();
            LoadModeForScope();
        }
    }

    private void RefreshProjects(bool showStatus)
    {
        string? selectedPath = (ProjectComboBox.SelectedItem as CodexProject)?.Path ?? _state.ContextProjectPath;
        IReadOnlyList<CodexProject> projects = CodexProjectReader.GetProjects();
        string signature = string.Join('\n', projects.Select(project => project.Path));
        if (_projectsLoaded && string.Equals(signature, _projectSignature, StringComparison.Ordinal))
        {
            if (showStatus)
            {
                StatusText.Foreground = projects.Count > 0 ? SuccessBrush : ErrorBrush;
                StatusText.Text = projects.Count > 0
                    ? $"Project list is current ({projects.Count} from the Codex database)."
                    : "No existing project directories were found in the Codex database.";
            }
            return;
        }

        _projectsLoaded = true;
        _projectSignature = signature;
        ProjectComboBox.ItemsSource = projects;
        ProjectComboBox.IsEnabled = projects.Count > 0;
        EmptyProjectsText.Visibility = projects.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        CodexProject? selection = projects.FirstOrDefault(project =>
            string.Equals(project.Path, selectedPath, StringComparison.OrdinalIgnoreCase));
        ProjectComboBox.SelectedItem = selection ?? projects.FirstOrDefault();

        if (showStatus)
        {
            StatusText.Foreground = projects.Count > 0 ? SuccessBrush : ErrorBrush;
            StatusText.Text = projects.Count > 0
                ? $"Loaded {projects.Count} projects from the Codex database."
                : "No existing project directories were found in the Codex database.";
        }
    }

    private void OnProjectChanged()
    {
        if (ProjectComboBox.SelectedItem is CodexProject project)
        {
            ProjectComboBox.ToolTip = project.Path;
            _state.ContextProjectPath = project.Path;
            if (!_loading)
            {
                _saveState();
                _targetChanged();
                LoadModeForScope();
            }
        }
    }

    private void LoadModeForScope()
    {
        string path;
        ContextMode mode;
        if (ProjectScopeButton.IsChecked == true && ProjectComboBox.SelectedItem is CodexProject project)
        {
            ActiveTargetText.Text = project.Name;
            path = CodexConfigService.GetProjectConfigPath(project.Path);
            ContextMode? projectMode = CodexConfigService.ReadMode(path);
            if (projectMode is null)
            {
                mode = CodexConfigService.Modes[0];
                ContextMode? inheritedMode = CodexConfigService.ReadMode(CodexConfigService.GlobalConfigPath);
                string inherited = inheritedMode is null ? "the model default" : inheritedMode.Name;
                SetStatus($"No project override. This project inherits {inherited} globally.", false);
            }
            else
            {
                mode = projectMode;
                SetStatus($"{mode.Name} is configured for this project.", true);
            }
        }
        else
        {
            ActiveTargetText.Text = "Global defaults";
            path = CodexConfigService.GlobalConfigPath;
            ContextMode? globalMode = CodexConfigService.ReadMode(path);
            mode = globalMode ?? CodexConfigService.Modes[0];
            SetStatus(globalMode is null
                ? "Default is active. Codex supplies the standard context settings."
                : $"{mode.Name} is the global default.", true);
        }

        ActiveModeText.Text = mode.Name;
        ActiveContextText.Text = mode.ContextLabel.Replace(" context", string.Empty, StringComparison.Ordinal);
        ShowConfigPath(path);
    }

    internal void RefreshTargetStatus(string? message = null, bool success = true)
    {
        LoadModeForScope();
        if (!string.IsNullOrWhiteSpace(message)) SetStatus(message, success, !success);
    }

    private void SetStatus(string message, bool success, bool error = false)
    {
        StatusText.Text = message;
        StatusText.Foreground = error ? ErrorBrush : success ? SuccessBrush : MutedBrush;
    }

    private void ShowConfigPath(string path)
    {
        ConfigPathText.Text = path;
        ConfigPathText.ToolTip = path;
    }

    private void PositionAboveTaskbar()
    {
        UpdateLayout();
        PopupAnchor? anchor = _anchorProvider();
        if (anchor is null) return;

        Left = Math.Clamp(
            anchor.Right - ActualWidth,
            anchor.WorkLeft + 6,
            Math.Max(anchor.WorkLeft + 6, anchor.WorkRight - ActualWidth - 6));

        bool taskbarAboveWorkArea = anchor.Bottom <= anchor.WorkTop + 2;
        double desiredTop = taskbarAboveWorkArea
            ? anchor.Bottom + 6
            : anchor.Top - ActualHeight - 6;
        Top = Math.Clamp(
            desiredTop,
            anchor.WorkTop + 6,
            Math.Max(anchor.WorkTop + 6, anchor.WorkBottom - ActualHeight - 6));
    }

    private void OnKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        Close();
        e.Handled = true;
    }

    private static SolidColorBrush Brush(string color) =>
        new((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(color));
}
