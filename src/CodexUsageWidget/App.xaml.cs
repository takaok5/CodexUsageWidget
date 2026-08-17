using System.Windows;

namespace CodexUsageWidget;

public partial class App : System.Windows.Application
{
    private Mutex? _singleInstanceMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        _singleInstanceMutex = new Mutex(true, "CodexUsageTaskbarWidgetV2", out bool created);
        if (!created)
        {
            Shutdown();
            return;
        }

        base.OnStartup(e);
        var window = new MainWindow();
        MainWindow = window;
        window.Show();
        if (e.Args.Any(argument => string.Equals(argument, "--context-settings", StringComparison.OrdinalIgnoreCase)))
            window.Dispatcher.BeginInvoke(window.ShowContextSettings);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_singleInstanceMutex is not null)
        {
            try { _singleInstanceMutex.ReleaseMutex(); } catch (ApplicationException) { }
            _singleInstanceMutex.Dispose();
        }

        base.OnExit(e);
    }
}
