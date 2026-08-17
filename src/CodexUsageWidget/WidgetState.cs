namespace CodexUsageWidget;

internal sealed class WidgetState
{
    public double Left { get; set; }
    public bool Locked { get; set; }
    public bool Animate { get; set; } = true;
    public string AnimationSpeed { get; set; } = "Normal";
    public int ScreenIndex { get; set; } = -1;
    public string RefreshMode { get; set; } = "30 seconds";
    public string ContextScope { get; set; } = "Global";
    public string ContextProjectPath { get; set; } = string.Empty;
    public bool ContextAlsoGlobal { get; set; }
    public int LayoutVersion { get; set; } = 2;
}
