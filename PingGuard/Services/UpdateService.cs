using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;

namespace PingGuard.Services;

public static class UpdateService
{
    private static readonly HttpClient _http = new()
    {
        Timeout = TimeSpan.FromSeconds(15)
    };

    public record UpdateInfo(string Version, string DownloadUrl);

    public static async Task<UpdateInfo?> CheckAsync()
    {
        try
        {
            const string api = "https://api.github.com/repos/marcosstgo/PingGuard/releases/latest";
            using var req = new HttpRequestMessage(HttpMethod.Get, api);
            req.Headers.UserAgent.ParseAdd("PingGuard/1.0.0");
            using var resp = await _http.SendAsync(req).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) return null;

            var json = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            using var doc  = JsonDocument.Parse(json);
            var root       = doc.RootElement;
            var tag        = root.GetProperty("tag_name").GetString() ?? "";
            var latest     = tag.TrimStart('v');
            var current    = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";

            string? url = null;
            if (root.TryGetProperty("assets", out var assets))
                foreach (var a in assets.EnumerateArray())
                {
                    var name = a.GetProperty("name").GetString() ?? "";
                    if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    {
                        url = a.GetProperty("browser_download_url").GetString();
                        break;
                    }
                }

            if (Version.TryParse(latest,  out var lv) &&
                Version.TryParse(current, out var cv) &&
                lv > cv && url != null)
                return new UpdateInfo(latest, url);
        }
        catch { /* silent */ }
        return null;
    }

    /// <summary>
    /// Downloads the new exe to %TEMP%, writes a bat that replaces the current exe and relaunches.
    /// Progress: 0–100.
    /// </summary>
    public static async Task DownloadAndInstallAsync(
        UpdateInfo info,
        Action<int> onProgress,
        CancellationToken ct = default)
    {
        var tempExe = Path.Combine(Path.GetTempPath(), $"PingGuard-update-{info.Version}.exe");

        using var resp = await _http.GetAsync(info.DownloadUrl,
            HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();
        var total = resp.Content.Headers.ContentLength ?? 0;

        await using var src = await resp.Content.ReadAsStreamAsync(ct);
        await using var dst = File.Create(tempExe);
        var buf        = new byte[81920];
        long downloaded = 0;
        int  read;
        while ((read = await src.ReadAsync(buf, ct)) > 0)
        {
            await dst.WriteAsync(buf.AsMemory(0, read), ct);
            downloaded += read;
            if (total > 0) onProgress((int)((double)downloaded / total * 100));
        }
        dst.Close();

        var currentExe = Environment.ProcessPath
            ?? Process.GetCurrentProcess().MainModule?.FileName
            ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Ping Guard.exe");

        var bat = Path.Combine(Path.GetTempPath(), "pinggua_update.bat");
        File.WriteAllText(bat,
            $"@echo off\r\n" +
            $"ping 127.0.0.1 -n 6 > nul\r\n" +
            $":retry\r\n" +
            $"move /Y \"{tempExe}\" \"{currentExe}\"\r\n" +
            $"if errorlevel 1 (ping 127.0.0.1 -n 3 > nul & goto retry)\r\n" +
            $"start \"\" \"{currentExe}\"\r\n" +
            $"del \"%~f0\"\r\n");

        Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"{bat}\"")
        {
            CreateNoWindow  = true,
            UseShellExecute = false,
            WindowStyle     = ProcessWindowStyle.Hidden
        });

        System.Windows.Application.Current.Dispatcher.Invoke(
            System.Windows.Application.Current.Shutdown);
    }
}
