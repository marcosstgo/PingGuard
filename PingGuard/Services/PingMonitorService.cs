using System.Diagnostics;
using System.Net.NetworkInformation;
using PingGuard.Models;

namespace PingGuard.Services;

public sealed class PingMonitorService : IDisposable
{
    public const int MaxSamples = 600; // 10 minutes @ 1/sec

    private readonly List<PingSample> _samples = new();
    private readonly object           _lock    = new();
    private CancellationTokenSource?  _cts;

    public event Action<PingSample>? SampleAdded;

    public string Target    { get; set; } = "1.1.1.1";
    public int    TimeoutMs { get; set; } = 2000;

    // ── Start / Stop ────────────────────────────────────────────────────────

    public void Start()
    {
        Stop();
        _cts = new CancellationTokenSource();
        Task.Run(() => RunLoopAsync(_cts.Token));
    }

    public void Stop()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }

    public void Restart()
    {
        lock (_lock) _samples.Clear();
        Start();
    }

    // ── Sample access ────────────────────────────────────────────────────────

    public List<PingSample> GetSamples()
    {
        lock (_lock) return new List<PingSample>(_samples);
    }

    // ── Computed stats (last N seconds) ─────────────────────────────────────

    public (double avg, double p95, double jitter, double lossPercent) GetStats(int seconds = 60)
    {
        List<PingSample> window;
        lock (_lock)
            window = _samples.TakeLast(seconds).ToList();

        if (window.Count == 0) return (0, 0, 0, 0);

        var success = window.Where(s => s.Success).Select(s => (double)s.LatencyMs).ToList();
        double lossPercent = (1.0 - (double)success.Count / window.Count) * 100.0;

        if (success.Count == 0) return (0, 0, 0, lossPercent);

        double avg = success.Average();

        var sorted = success.OrderBy(x => x).ToList();
        int p95idx = (int)Math.Ceiling(sorted.Count * 0.95) - 1;
        double p95 = sorted[Math.Clamp(p95idx, 0, sorted.Count - 1)];

        double jitter = 0;
        if (success.Count > 1)
            jitter = success.Zip(success.Skip(1), (a, b) => Math.Abs(b - a)).Average();

        return (Math.Round(avg, 1), Math.Round(p95, 1), Math.Round(jitter, 1), Math.Round(lossPercent, 1));
    }

    public double GetUptimePercent()
    {
        lock (_lock)
        {
            if (_samples.Count == 0) return 100;
            return Math.Round(_samples.Count(s => s.Success) * 100.0 / _samples.Count, 1);
        }
    }

    // ── Internal loop ────────────────────────────────────────────────────────

    private async Task RunLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var sw = Stopwatch.StartNew();
            var sample = await DoPingAsync(ct);

            if (ct.IsCancellationRequested) break;

            lock (_lock)
            {
                _samples.Add(sample);
                if (_samples.Count > MaxSamples) _samples.RemoveAt(0);
            }

            SampleAdded?.Invoke(sample);

            var wait = TimeSpan.FromMilliseconds(1000) - sw.Elapsed;
            if (wait > TimeSpan.Zero)
            {
                try { await Task.Delay(wait, ct); }
                catch (OperationCanceledException) { break; }
            }
        }
    }

    private async Task<PingSample> DoPingAsync(CancellationToken ct)
    {
        try
        {
            using var ping = new Ping();
            var options = new PingOptions { Ttl = 64, DontFragment = true };
            var reply = await ping.SendPingAsync(Target, TimeoutMs, new byte[32], options);
            return new PingSample
            {
                Timestamp = DateTime.Now,
                LatencyMs = reply.Status == IPStatus.Success ? (int)reply.RoundtripTime : -1,
                Success   = reply.Status == IPStatus.Success
            };
        }
        catch (OperationCanceledException) { throw; }
        catch
        {
            return new PingSample { Timestamp = DateTime.Now, LatencyMs = -1, Success = false };
        }
    }

    public void Dispose() => Stop();
}
