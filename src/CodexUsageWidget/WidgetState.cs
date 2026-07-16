namespace CodexUsageWidget;

internal sealed class WidgetState
{
    public double Left { get; set; } = 205;
    public bool Locked { get; set; }
    public bool Animate { get; set; } = true;
    public string AnimationSpeed { get; set; } = "Normal";
    public int ScreenIndex { get; set; }
    public string RefreshMode { get; set; } = "30 seconds";
}
