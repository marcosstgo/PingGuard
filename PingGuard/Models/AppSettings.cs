namespace PingGuard.Models;

public sealed class AppSettings
{
    public string Target           { get; set; } = "1.1.1.1";
    public int    AlertThresholdMs { get; set; } = 150;
    public bool   AlwaysOnTop      { get; set; } = false;
    public bool   StartWithWindows { get; set; } = false;
    public double WindowLeft       { get; set; } = double.NaN;
    public double WindowTop        { get; set; } = double.NaN;
}
