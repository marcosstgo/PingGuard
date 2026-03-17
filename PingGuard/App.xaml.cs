using System.Windows;
using PingGuard.Models;
using PingGuard.Services;
using WinFormsApp = System.Windows.Forms;
using WpfApp      = System.Windows.Application;

namespace PingGuard;

public partial class App : WpfApp
{
    private MainWindow?                       _win;
    private System.Windows.Forms.NotifyIcon?  _tray;
    private PingMonitorService?               _monitor;
    private SettingsService?                  _settings;

    // ── Icon colors ──────────────────────────────────────────────────────────
    private static readonly System.Drawing.Color CGreen  = System.Drawing.Color.FromArgb(74, 222, 128);
    private static readonly System.Drawing.Color CYellow = System.Drawing.Color.FromArgb(251, 191, 36);
    private static readonly System.Drawing.Color CRed    = System.Drawing.Color.FromArgb(239, 68,  68);
    private static readonly System.Drawing.Color CGray   = System.Drawing.Color.FromArgb(107, 114, 135);

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _settings = new SettingsService();
        var prefs = _settings.Load();

        _monitor = new PingMonitorService { Target = prefs.Target };

        _tray = new System.Windows.Forms.NotifyIcon
        {
            Text    = "Ping Guard — iniciando...",
            Visible = true,
            Icon    = MakeIcon(CGray)
        };

        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("Mostrar",  null, (_, _) => ShowWindow());
        menu.Items.Add("-");
        menu.Items.Add("Salir",    null, (_, _) => ExitApp());
        _tray.ContextMenuStrip = menu;
        _tray.MouseClick += (_, ev) =>
        {
            if (ev.Button == System.Windows.Forms.MouseButtons.Left) ToggleWindow();
        };

        _win = new MainWindow(_monitor, _settings, prefs);
        _win.Closing += (_, ev) => { ev.Cancel = true; _win.Hide(); };

        _monitor.SampleAdded += OnSample;
        _monitor.Start();

        _win.Show();
    }

    // ── Tray icon update ─────────────────────────────────────────────────────

    private int     _alertStreak   = 0;
    private bool    _alertFired    = false;
    private DateTime _alertCooldown = DateTime.MinValue;

    private void OnSample(PingSample s)
    {
        Dispatcher.InvokeAsync(() =>
        {
            var prefs    = _win?.CurrentSettings ?? new AppSettings();
            int threshold = prefs.AlertThresholdMs;

            System.Drawing.Color iconColor;
            string tooltip;

            if (!s.Success)
            {
                iconColor = CGray;
                tooltip   = "Ping Guard — sin respuesta";
                _alertStreak = 0;
            }
            else
            {
                iconColor = s.LatencyMs < threshold * 0.7  ? CGreen :
                            s.LatencyMs < threshold         ? CYellow : CRed;
                tooltip   = $"Ping Guard — {s.LatencyMs} ms → {prefs.Target}";

                // Alert on 3 consecutive spikes, with 60s cooldown
                if (s.LatencyMs >= threshold)
                {
                    _alertStreak++;
                    if (_alertStreak >= 3 && DateTime.Now > _alertCooldown)
                    {
                        _tray?.ShowBalloonTip(5000,
                            "⚠ Ping alto detectado",
                            $"{s.LatencyMs} ms → {prefs.Target}  (umbral: {threshold} ms)",
                            System.Windows.Forms.ToolTipIcon.Warning);
                        _alertCooldown = DateTime.Now.AddSeconds(60);
                        _alertFired    = true;
                    }
                }
                else
                {
                    if (_alertFired && _alertStreak >= 3)
                    {
                        _tray?.ShowBalloonTip(3000,
                            "✅ Ping normalizado",
                            $"Volvió a {s.LatencyMs} ms",
                            System.Windows.Forms.ToolTipIcon.Info);
                    }
                    _alertStreak = 0;
                    _alertFired  = false;
                }
            }

            // Update tray icon
            var old = _tray?.Icon;
            if (_tray != null)
            {
                _tray.Icon    = MakeIcon(iconColor);
                _tray.Text    = tooltip.Length > 63 ? tooltip[..63] : tooltip;
            }
            old?.Dispose();
        });
    }

    // ── Window helpers ───────────────────────────────────────────────────────

    private void ShowWindow()
    {
        if (_win is null) return;
        _win.Show();
        _win.WindowState = WindowState.Normal;
        _win.Activate();
    }

    private void ToggleWindow()
    {
        if (_win?.IsVisible == true) _win.Hide();
        else ShowWindow();
    }

    private void ExitApp()
    {
        _monitor?.Stop();
        _monitor?.Dispose();
        _tray?.Dispose();
        _win?.ForceClose();
        Shutdown();
    }

    // ── Icon factory ─────────────────────────────────────────────────────────

    private static System.Drawing.Icon MakeIcon(System.Drawing.Color color)
    {
        using var bmp = new System.Drawing.Bitmap(16, 16);
        using var g   = System.Drawing.Graphics.FromImage(bmp);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.Clear(System.Drawing.Color.Transparent);
        using var brush = new System.Drawing.SolidBrush(color);
        g.FillEllipse(brush, 1, 1, 14, 14);
        var handle = bmp.GetHicon();
        return System.Drawing.Icon.FromHandle(handle);
    }
}
