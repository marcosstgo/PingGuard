namespace PingGuard.Models;

public sealed class PingSample
{
    public DateTime Timestamp { get; init; }
    public int      LatencyMs { get; init; }  // -1 = timeout / error
    public bool     Success   { get; init; }
}
