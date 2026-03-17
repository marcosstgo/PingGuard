using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;
using PingGuard.Models;
using PingGuard.Services;
using WpfKeyArgs      = System.Windows.Input.KeyEventArgs;
using WpfKey          = System.Windows.Input.Key;
using WpfKeyboard     = System.Windows.Input.Keyboard;
using WpfMouseBtnArgs = System.Windows.Input.MouseButtonEventArgs;

namespace PingGuard;

public partial class MainWindow : Window
{
    private readonly PingMonitorService _monitor;
    private readonly SettingsService    _svc;
    private          AppSettings        _prefs;
    private          bool               _forceClose;

    // Ping color thresholds
    private static readonly MediaColor CGreen  = MediaColor.FromRgb(74,  222, 128);
    private static readonly MediaColor CYellow = MediaColor.FromRgb(251, 191, 36);
    private static readonly MediaColor COrange = MediaColor.FromRgb(251, 146, 60);
    private static readonly MediaColor CRed    = MediaColor.FromRgb(239, 68,  68);
    private static readonly MediaColor CGray   = MediaColor.FromRgb(107, 114, 135);

    public AppSettings CurrentSettings => _prefs;

    public MainWindow(PingMonitorService monitor, SettingsService svc, AppSettings prefs)
    {
        InitializeComponent();
        _monitor = monitor;
        _svc     = svc;
        _prefs   = prefs;

        ApplySettings();
        _monitor.SampleAdded += OnSample;

        if (!double.IsNaN(prefs.WindowLeft) && !double.IsNaN(prefs.WindowTop))
        {
            Left = prefs.WindowLeft;
            Top  = prefs.WindowTop;
        }
        else
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }
    }

    // ── Sample handler ───────────────────────────────────────────────────────

    private void OnSample(PingSample s)
    {
        Dispatcher.InvokeAsync(() =>
        {
            UpdatePingDisplay(s);
            UpdateStats();
            RenderSparkline();
        });
    }

    private void UpdatePingDisplay(PingSample s)
    {
        int threshold = _prefs.AlertThresholdMs;

        if (!s.Success)
        {
            PingValue.Text      = "—";
            PingValue.Foreground = new SolidColorBrush(CGray);
            OnlineDot.Fill      = new SolidColorBrush(CGray);
            StatusLabel.Text    = "SIN RESPUESTA";
            StatusDot.Fill      = new SolidColorBrush(CGray);
            return;
        }

        PingValue.Text = s.LatencyMs.ToString();

        MediaColor c = s.LatencyMs < threshold * 0.7 ? CGreen :
                  s.LatencyMs < threshold        ? CYellow :
                  s.LatencyMs < threshold * 1.5  ? COrange : CRed;

        PingValue.Foreground = new SolidColorBrush(c);
        OnlineDot.Fill       = new SolidColorBrush(c);
        StatusDot.Fill       = new SolidColorBrush(c);
        StatusLabel.Text     = s.LatencyMs < threshold ? "ONLINE" : "LATENCIA ALTA";
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

        // Color loss and uptime based on severity
        StatLoss.Foreground   = loss   > 5  ? new SolidColorBrush(CRed)    :
                                loss   > 1  ? new SolidColorBrush(CYellow) :
                                              new SolidColorBrush(MediaColor.FromRgb(192, 192, 204));
        StatUptime.Foreground = uptime < 95 ? new SolidColorBrush(CYellow) :
                                              new SolidColorBrush(MediaColor.FromRgb(192, 192, 204));
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
        AlertInput.Text    = _prefs.AlertThresholdMs.ToString();
        AlwaysOnTopCheck.IsChecked = _prefs.AlwaysOnTop;
        AutoStartCheck.IsChecked   = _svc.IsStartWithWindowsEnabled();
        Topmost = _prefs.AlwaysOnTop;
        TargetLabel.Text = $"TARGET · {_prefs.Target}";
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

    private void TargetInput_LostFocus(object sender, RoutedEventArgs e)   => ApplyTarget();
    private void TargetInput_KeyDown(object sender, WpfKeyArgs e)
    { if (e.Key == WpfKey.Enter) { ApplyTarget(); WpfKeyboard.ClearFocus(); } }

    private void AlertInput_LostFocus(object sender, RoutedEventArgs e)    => ApplyAlert();
    private void AlertInput_KeyDown(object sender, WpfKeyArgs e)
    { if (e.Key == WpfKey.Enter) { ApplyAlert(); WpfKeyboard.ClearFocus(); } }

    private void ApplyTarget()
    {
        var t = TargetInput.Text.Trim();
        if (string.IsNullOrWhiteSpace(t)) { TargetInput.Text = _prefs.Target; return; }
        _prefs.Target        = t;
        _monitor.Target      = t;
        TargetLabel.Text     = $"TARGET · {t}";
        _monitor.Restart();
        SaveSettings();
    }

    private void ApplyAlert()
    {
        if (int.TryParse(AlertInput.Text, out int v) && v > 0)
        {
            _prefs.AlertThresholdMs = v;
            SaveSettings();
            RenderSparkline(); // redraw threshold line
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

    public void ForceClose()
    {
        _monitor.SampleAdded -= OnSample;
        _forceClose = true;
        Close();
    }
}
