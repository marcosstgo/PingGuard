using System.Media;
using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;
using PingGuard.Models;
using PingGuard.Services;
using WpfKeyArgs      = System.Windows.Input.KeyEventArgs;
using WpfKey          = System.Windows.Input.Key;
using WpfKeyboard     = System.Windows.Input.Keyboard;
using WpfMouseBtnArgs = System.Windows.Input.MouseButtonEventArgs;
using WpfMouseBtn     = System.Windows.Input.MouseButton;

namespace PingGuard;

public partial class MainWindow : Window
{
    private readonly PingMonitorService    _monitor;
    private readonly PingMonitorService[]  _secondary;
    private readonly SettingsService       _svc;
    private          AppSettings           _prefs;
    private          UpdateService.UpdateInfo? _pendingUpdate;

    // Sound alert state
    private int      _soundStreak   = 0;
    private DateTime _soundCooldown = DateTime.MinValue;

    private static string FormatMs(int ms) => ms == 0 ? "<1" : ms.ToString();

    // Ping color thresholds
    private static readonly MediaColor CGreen  = MediaColor.FromRgb(74,  222, 128);
    private static readonly MediaColor CYellow = MediaColor.FromRgb(251, 191, 36);
    private static readonly MediaColor COrange = MediaColor.FromRgb(251, 146, 60);
    private static readonly MediaColor CRed    = MediaColor.FromRgb(239, 68,  68);
    private static readonly MediaColor CGray   = MediaColor.FromRgb(107, 114, 135);

    public AppSettings CurrentSettings => _prefs;

    public MainWindow(PingMonitorService monitor, PingMonitorService[] secondary,
                      SettingsService svc, AppSettings prefs)
    {
        InitializeComponent();
        _monitor   = monitor;
        _secondary = secondary;
        _svc       = svc;
        _prefs     = prefs;

        ApplySettings();
        _monitor.SampleAdded += OnSample;

        // Wire up secondary monitors
        if (_secondary.Length > 0) _secondary[0].SampleAdded += s => OnSecondarySample(s, 0);
        if (_secondary.Length > 1) _secondary[1].SampleAdded += s => OnSecondarySample(s, 1);

        if (!double.IsNaN(prefs.WindowLeft) && !double.IsNaN(prefs.WindowTop))
        {
            Left = prefs.WindowLeft;
            Top  = prefs.WindowTop;
        }
        else
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        _ = CheckForUpdateAsync();
    }

    // ── Sample handlers ───────────────────────────────────────────────────────

    private void OnSample(PingSample s)
    {
        Dispatcher.InvokeAsync(() =>
        {
            UpdatePingDisplay(s);
            UpdateStats();
            RenderSparkline();
            MaybeSoundAlert(s);
        });
    }

    private void OnSecondarySample(PingSample s, int index)
    {
        Dispatcher.InvokeAsync(() => UpdateSecondaryRow(s, index));
    }

    private void UpdatePingDisplay(PingSample s)
    {
        int threshold = _prefs.AlertThresholdMs;

        if (!s.Success)
        {
            PingValue.Text       = "—";
            PingValue.Foreground = new SolidColorBrush(CGray);
            OnlineDot.Fill       = new SolidColorBrush(CGray);
            StatusLabel.Text     = "SIN RESPUESTA";
            StatusDot.Fill       = new SolidColorBrush(CGray);
            return;
        }

        PingValue.Text = FormatMs(s.LatencyMs);

        MediaColor c = s.LatencyMs < threshold * 0.7 ? CGreen :
                  s.LatencyMs < threshold        ? CYellow :
                  s.LatencyMs < threshold * 1.5  ? COrange : CRed;

        PingValue.Foreground = new SolidColorBrush(c);
        OnlineDot.Fill       = new SolidColorBrush(c);
        StatusDot.Fill       = new SolidColorBrush(c);
        StatusLabel.Text     = s.LatencyMs < threshold ? "ONLINE" : "LATENCIA ALTA";
    }

    private void UpdateSecondaryRow(PingSample s, int index)
    {
        var dot  = index == 0 ? SecDot1  : SecDot2;
        var ping = index == 0 ? SecPing1 : SecPing2;
        int threshold = _prefs.AlertThresholdMs;

        if (!s.Success)
        {
            dot.Fill  = new SolidColorBrush(CGray);
            ping.Text = "—";
            ping.Foreground = new SolidColorBrush(CGray);
            return;
        }

        MediaColor c = s.LatencyMs < threshold * 0.7 ? CGreen :
                  s.LatencyMs < threshold        ? CYellow :
                  s.LatencyMs < threshold * 1.5  ? COrange : CRed;

        dot.Fill        = new SolidColorBrush(c);
        ping.Text       = FormatMs(s.LatencyMs) + " ms";
        ping.Foreground = new SolidColorBrush(c);
    }

    private void UpdateStats()
    {
        var (avg, p95, jitter, loss) = _monitor.GetStats(120);
        double uptime = _monitor.GetUptimePercent();

        StatAvg.Text    = avg    > 0 ? $"{avg:F0}" : "—";
        StatP95.Text    = p95    > 0 ? $"{p95:F0}" : "—";
        StatJitter.Text = jitter > 0 ? $"{jitter:F1}" : "—";
        StatLoss.Text   = $"{loss:F1}%";
        StatUptime.Text = $"{uptime:F1}%";

        StatLoss.Foreground   = loss   > 5  ? new SolidColorBrush(CRed)    :
                                loss   > 1  ? new SolidColorBrush(CYellow) :
                                              new SolidColorBrush(MediaColor.FromRgb(192, 192, 204));
        StatUptime.Foreground = uptime < 95 ? new SolidColorBrush(CYellow) :
                                              new SolidColorBrush(MediaColor.FromRgb(192, 192, 204));
    }

    // ── Sound alert ───────────────────────────────────────────────────────────

    private void MaybeSoundAlert(PingSample s)
    {
        if (!_prefs.SoundAlert) return;
        if (!s.Success || s.LatencyMs >= _prefs.AlertThresholdMs)
        {
            _soundStreak++;
            if (_soundStreak >= 3 && DateTime.Now > _soundCooldown)
            {
                Task.Run(() => SystemSounds.Beep.Play());
                _soundCooldown = DateTime.Now.AddSeconds(30);
            }
        }
        else
        {
            _soundStreak = 0;
        }
    }

    // ── Sparkline ────────────────────────────────────────────────────────────

    private void RenderSparkline()
    {
        SparkCanvas.Children.Clear();

        var samples = _monitor.GetSamples().TakeLast(120).ToList();
        if (samples.Count < 2) return;

        double w = SparkCanvas.ActualWidth;
        double h = SparkCanvas.ActualHeight;
        if (w <= 0 || h <= 0) return;

        var valid = samples.Where(s => s.Success).ToList();
        double maxMs = valid.Count > 0
            ? Math.Max(valid.Max(s => s.LatencyMs) * 1.1, 100)
            : 100;

        double alertMs = _prefs.AlertThresholdMs;

        // Threshold line
        if (alertMs > 0 && alertMs <= maxMs)
        {
            double ty = h - alertMs / maxMs * h;
            ty = Math.Clamp(ty, 0, h);
            SparkCanvas.Children.Add(new Line
            {
                X1 = 0, Y1 = ty, X2 = w, Y2 = ty,
                Stroke          = new SolidColorBrush(MediaColor.FromArgb(80, 239, 68, 68)),
                StrokeThickness = 1,
                StrokeDashArray = new DoubleCollection { 4, 3 }
            });
        }

        // Build point list
        var points = new PointCollection();
        for (int i = 0; i < samples.Count; i++)
        {
            double x  = (double)i / (samples.Count - 1) * w;
            double ms = samples[i].Success ? samples[i].LatencyMs : maxMs;
            double y  = Math.Clamp(h - ms / maxMs * h, 0, h);
            points.Add(new WpfPoint(x, y));
        }

        // Fill area
        var fillPts = new PointCollection(points)
        {
            new WpfPoint(w, h),
            new WpfPoint(0, h)
        };
        SparkCanvas.Children.Add(new Polygon
        {
            Points = fillPts,
            Fill   = new LinearGradientBrush(
                MediaColor.FromArgb(35, 0, 240, 255),
                MediaColor.FromArgb(4,  0, 240, 255),
                new WpfPoint(0, 0), new WpfPoint(0, 1)),
            Stroke = WpfBrushes.Transparent
        });

        // Line
        SparkCanvas.Children.Add(new Polyline
        {
            Points          = points,
            Stroke          = new SolidColorBrush(MediaColor.FromArgb(180, 0, 240, 255)),
            StrokeThickness = 1.5,
            StrokeLineJoin  = PenLineJoin.Round
        });
    }

    // ── Settings application ─────────────────────────────────────────────────

    private void ApplySettings()
    {
        TargetInput.Text   = _prefs.Target;
        ExtraInput1.Text   = _prefs.ExtraTarget1;
        ExtraInput2.Text   = _prefs.ExtraTarget2;
        AlertInput.Text    = _prefs.AlertThresholdMs.ToString();
        AlwaysOnTopCheck.IsChecked = _prefs.AlwaysOnTop;
        AutoStartCheck.IsChecked   = _svc.IsStartWithWindowsEnabled();
        SoundCheck.IsChecked       = _prefs.SoundAlert;
        Topmost = _prefs.AlwaysOnTop;
        TargetLabel.Text = $"TARGET · {_prefs.Target}";

        // Show secondary rows for configured extra targets
        UpdateSecondaryRowVisibility();
    }

    private void UpdateSecondaryRowVisibility()
    {
        if (!string.IsNullOrWhiteSpace(_prefs.ExtraTarget1))
        {
            SecRow1.Visibility = Visibility.Visible;
            SecLabel1.Text     = _prefs.ExtraTarget1.Trim();
        }
        else
        {
            SecRow1.Visibility = Visibility.Collapsed;
        }

        if (!string.IsNullOrWhiteSpace(_prefs.ExtraTarget2))
        {
            SecRow2.Visibility = Visibility.Visible;
            SecLabel2.Text     = _prefs.ExtraTarget2.Trim();
        }
        else
        {
            SecRow2.Visibility = Visibility.Collapsed;
        }
    }

    private void SaveSettings()
    {
        _prefs.WindowLeft = Left;
        _prefs.WindowTop  = Top;
        _svc.Save(_prefs);
    }

    // ── UI event handlers ────────────────────────────────────────────────────

    private void TitleBar_MouseLeftButtonDown(object sender, WpfMouseBtnArgs e)
        => DragMove();

    private void MinimizeBtn_Click(object sender, RoutedEventArgs e) => Hide();

    private void CloseBtn_Click(object sender, RoutedEventArgs e) => Hide();

    private void PinBtn_Click(object sender, RoutedEventArgs e)
    {
        _prefs.AlwaysOnTop = !_prefs.AlwaysOnTop;
        Topmost            = _prefs.AlwaysOnTop;
        AlwaysOnTopCheck.IsChecked = _prefs.AlwaysOnTop;
        PinBtn.Opacity = _prefs.AlwaysOnTop ? 1.0 : 0.35;
        SaveSettings();
    }

    private void AlwaysOnTop_Changed(object sender, RoutedEventArgs e)
    {
        _prefs.AlwaysOnTop = AlwaysOnTopCheck.IsChecked == true;
        Topmost            = _prefs.AlwaysOnTop;
        PinBtn.Opacity     = _prefs.AlwaysOnTop ? 1.0 : 0.35;
        SaveSettings();
    }

    private void AutoStart_Changed(object sender, RoutedEventArgs e)
    {
        bool enable = AutoStartCheck.IsChecked == true;
        _svc.SetStartWithWindows(enable);
        _prefs.StartWithWindows = enable;
        SaveSettings();
    }

    private void Sound_Changed(object sender, RoutedEventArgs e)
    {
        _prefs.SoundAlert = SoundCheck.IsChecked == true;
        _soundStreak = 0;
        SaveSettings();
    }

    private void TargetInput_LostFocus(object sender, RoutedEventArgs e)   => ApplyTarget();
    private void TargetInput_KeyDown(object sender, WpfKeyArgs e)
    { if (e.Key == WpfKey.Enter) { ApplyTarget(); WpfKeyboard.ClearFocus(); } }

    private void ExtraInput1_LostFocus(object sender, RoutedEventArgs e)   => ApplyExtra(0);
    private void ExtraInput1_KeyDown(object sender, WpfKeyArgs e)
    { if (e.Key == WpfKey.Enter) { ApplyExtra(0); WpfKeyboard.ClearFocus(); } }

    private void ExtraInput2_LostFocus(object sender, RoutedEventArgs e)   => ApplyExtra(1);
    private void ExtraInput2_KeyDown(object sender, WpfKeyArgs e)
    { if (e.Key == WpfKey.Enter) { ApplyExtra(1); WpfKeyboard.ClearFocus(); } }

    private void AlertInput_LostFocus(object sender, RoutedEventArgs e)    => ApplyAlert();
    private void AlertInput_KeyDown(object sender, WpfKeyArgs e)
    { if (e.Key == WpfKey.Enter) { ApplyAlert(); WpfKeyboard.ClearFocus(); } }

    private void ApplyTarget()
    {
        var t = TargetInput.Text.Trim();
        if (string.IsNullOrWhiteSpace(t)) { TargetInput.Text = _prefs.Target; return; }
        _prefs.Target   = t;
        _monitor.Target = t;
        TargetLabel.Text = $"TARGET · {t}";
        _monitor.Restart();
        SaveSettings();
    }

    private void ApplyExtra(int index)
    {
        var input = index == 0 ? ExtraInput1 : ExtraInput2;
        var host  = input.Text.Trim();

        if (index == 0) _prefs.ExtraTarget1 = host;
        else            _prefs.ExtraTarget2 = host;

        var mon = _secondary[index];
        mon.Target = host;
        if (string.IsNullOrWhiteSpace(host))
            mon.Stop();
        else
            mon.Restart();

        UpdateSecondaryRowVisibility();
        SaveSettings();
    }

    private void ApplyAlert()
    {
        if (int.TryParse(AlertInput.Text, out int v) && v > 0)
        {
            _prefs.AlertThresholdMs = v;
            SaveSettings();
            RenderSparkline();
        }
        else
        {
            AlertInput.Text = _prefs.AlertThresholdMs.ToString();
        }
    }

    private void CopyStats_Click(object sender, RoutedEventArgs e)
    {
        var (avg, p95, jitter, loss) = _monitor.GetStats(120);
        double uptime = _monitor.GetUptimePercent();
        string text = $"Ping Guard — {_prefs.Target}\n" +
                      $"AVG: {avg:F0} ms  P95: {p95:F0} ms  Jitter: {jitter:F1} ms\n" +
                      $"Loss: {loss:F1}%  Uptime: {uptime:F1}%";
        WpfClipboard.SetText(text);
    }

    // ── Lifecycle ────────────────────────────────────────────────────────────

    protected override void OnLocationChanged(EventArgs e)
    {
        base.OnLocationChanged(e);
        SaveSettings();
    }

    // ── Auto-update ──────────────────────────────────────────────────────────

    private async Task CheckForUpdateAsync()
    {
        var info = await UpdateService.CheckAsync().ConfigureAwait(false);
        if (info is null) return;
        _pendingUpdate = info;
        await Dispatcher.InvokeAsync(() =>
        {
            UpdateBadgeText.Text   = $"v{info.Version} disponible";
            UpdateBadge.Visibility = Visibility.Visible;
        });
    }

    private async void UpdateBadge_Click(object sender, WpfMouseBtnArgs e)
    {
        if (e.ChangedButton != WpfMouseBtn.Left || _pendingUpdate is null) return;

        UpdateBadge.IsHitTestVisible = false;
        UpdateBadgeText.Text = "Descargando...";

        try
        {
            await UpdateService.DownloadAndInstallAsync(
                _pendingUpdate,
                pct => Dispatcher.InvokeAsync(() =>
                    UpdateBadgeText.Text = $"Descargando... {pct}%"));
        }
        catch (Exception ex)
        {
            UpdateBadgeText.Text         = $"Error: {ex.Message}";
            UpdateBadge.IsHitTestVisible = true;
        }
    }

    public void ForceClose()
    {
        _monitor.SampleAdded -= OnSample;
        Close();
    }
}
