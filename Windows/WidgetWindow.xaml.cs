// Copyright (c) 2026 LanDen Labs - Dennis Lang
using System.Diagnostics;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using WinWidgetPerf.Models;
using WinWidgetPerf.Services;
using ColorConverter = System.Windows.Media.ColorConverter;

namespace WinWidgetPerf.Windows;

public partial class WidgetWindow : Window
{
    private readonly WidgetSettings _settings;
    private readonly PerfService _perfService;
    private readonly ProcessCpuService _processCpuService;
    private readonly NetworkService _networkService;
    private readonly TemperatureService _temperatureService;
    private readonly Queue<PerfSample> _history = new();

    private System.Windows.Threading.DispatcherTimer? _updateTimer;
    private System.Windows.Threading.DispatcherTimer? _displayCheckTimer;
    private DisplayConfiguration _currentDisplayConfiguration;

    private string _bgColorHex = "#1E1E2E";
    private double _bgOpacity = 0.80;
    private SolidColorBrush _cpuBrush = new(System.Windows.Media.Colors.CornflowerBlue);
    private SolidColorBrush _diskBrush = new(System.Windows.Media.Colors.Orange);
    private SolidColorBrush _netBrush = new(System.Windows.Media.Colors.LightGreen);
    private SolidColorBrush _memBrush = new(System.Windows.Media.Color.FromRgb(0xCB, 0xA6, 0xF7));

    private double _diskUsagePercent;
    private double _memUsagePercent;
    private ulong _memUsedBytes, _memTotalBytes;
    private string _topProcessText = "";
    private string? _topProcessToolTip;
    private double? _cpuTemp, _gpuTemp;

    private static readonly SolidColorBrush TitleNameBrush = MakeFrozenBrush(0xAA, 0xFF, 0xFF, 0xFF);
    private static readonly SolidColorBrush TitleStatsBrush = MakeFrozenBrush(0x88, 0xFF, 0xFF, 0xFF);
    private static readonly SolidColorBrush TitleProcBrush = MakeFrozenBrush(0xFF, 0xE6, 0xB8, 0x60);
    private static readonly SolidColorBrush TempNormalBrush = MakeFrozenBrush(0xFF, 0xFF, 0xFF, 0xFF);   // white
    private static readonly SolidColorBrush TempWarmBrush   = MakeFrozenBrush(0xFF, 0xF9, 0xE2, 0x4F);   // yellow
    private static readonly SolidColorBrush TempHotBrush    = MakeFrozenBrush(0xFF, 0xF3, 0x5B, 0x5B);   // red

    private bool _isEmbedded;

    // Widget top-left in SCREEN PHYSICAL PIXELS — the single source of truth for position,
    // whether embedded (child of WorkerW) or a normal top-level window. See WidgetGeometry.
    private int _posX;
    private int _posY;

    // Tracks whether this widget has called ProcessCpuService.Start(),
    // so we always pair every Start() with exactly one Stop().
    private bool _processCpuServiceStarted;

    // Drag (move) and resize share the same start snapshot.
    private bool _isDragging;
    private bool _isResizing;
    private ResizeZone _resizeZone;
    private DesktopService.POINT _startCursor;
    private int _startX, _startY;
    private double _startW, _startH;

    private SettingsWindow? _settingsDialog;

    public string WidgetId => _settings.Id;

    /// <summary>Current top-left in screen physical pixels.</summary>
    public (int X, int Y) ScreenPosition => (_posX, _posY);

    private double DpiX => VisualTreeHelper.GetDpi(this).DpiScaleX;
    private double DpiY => VisualTreeHelper.GetDpi(this).DpiScaleY;

    public WidgetWindow(WidgetSettings settings, PerfService perfService, ProcessCpuService processCpuService, NetworkService networkService,
                        TemperatureService temperatureService)
    {
        InitializeComponent();

        // Widgets can be added/removed from the tray at any time, so refresh this each time the menu opens.
        ContextMenuOpening += (_, _) => RemoveMenuItem.IsEnabled = App.Settings.Widgets.Count > 1;

        _settings = settings;
        _perfService = perfService;
        _processCpuService = processCpuService;
        _networkService = networkService;
        _temperatureService = temperatureService;

        _bgColorHex = string.IsNullOrEmpty(settings.BackgroundColor) ? "#1E1E2E" : settings.BackgroundColor;
        _bgOpacity = settings.BackgroundOpacity > 0 ? settings.BackgroundOpacity : 0.80;

        Width = settings.Width > MinWidth ? settings.Width : 340;
        Height = settings.Height > MinHeight ? settings.Height : 160;

        _currentDisplayConfiguration = DisplayService.GetCurrentDisplayConfiguration();
        (_posX, _posY) = DisplayService.GetDisplayPosition(settings, _currentDisplayConfiguration);
        // Provisional (assumes 100 %); corrected for the real DPI in OnSourceInitialized before first show.
        Left = _posX;
        Top = _posY;

        RebuildBrushes();
        InitializeUpdateTimer();
        InitializeDisplayCheckTimer();

        // Start the process sampler only when this widget is configured to show it.
        _processCpuServiceStarted = settings.ShowTopProcess;
        if (_processCpuServiceStarted)
            _processCpuService.Start();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        ApplyBackgroundInternal(_bgOpacity);
        ApplyChartBackgroundInternal();
        ApplyFontScale(_settings.FontScalePercent > 0 ? _settings.FontScalePercent : 100);
        ApplyVisibilityInternal();

        DesktopService.HideFromAltTab(this);
        _isEmbedded = _settings.EmbedInWallpaper && DesktopService.EmbedInWallpaper(this);
        if (!_isEmbedded)
            DesktopService.SetAlwaysOnBottom(this);

        MoveTo(_posX, _posY);
        EnsureOnScreen();

        Sample();
    }

    // ── Position (screen pixels) ─────────────────────────────────────────────

    /// <summary>Moves the widget so its top-left is at the given screen pixel position (does not save).</summary>
    public void MoveTo(int x, int y)
    {
        _posX = x;
        _posY = y;
        if (_isEmbedded)
            DesktopService.MoveEmbeddedWindow(this, x, y, PixelWidth, PixelHeight);
        else
        {
            Left = x / DpiX;
            Top  = y / DpiY;
        }
    }

    /// <summary>Persists the current position for the current monitor layout.</summary>
    public void SavePosition()
    {
        DisplayService.SaveDisplayPosition(_settings, _currentDisplayConfiguration, _posX, _posY);
        SettingsService.Save(App.Settings);
    }

    private int PixelWidth  => (int)Math.Round(Width  * DpiX);
    private int PixelHeight => (int)Math.Round(Height * DpiY);

    private PxRect PixelBounds => new(_posX, _posY, PixelWidth, PixelHeight);

    // ── Timers ────────────────────────────────────────────────────────────────

    private void InitializeUpdateTimer()
    {
        _updateTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(Math.Max(100, _settings.UpdateInterval))
        };
        _updateTimer.Tick += (s, e) => Sample();
        _updateTimer.Start();
    }

    private void InitializeDisplayCheckTimer()
    {
        _displayCheckTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(2)
        };
        _displayCheckTimer.Tick += (s, e) => CheckDisplayConfigurationChanged();
        _displayCheckTimer.Start();
    }

    /// <summary>Pulls the widget back onto a screen if it's off every monitor (e.g. after unplugging one).</summary>
    private void EnsureOnScreen()
    {
        var fix = WidgetGeometry.EnsureVisible(PixelBounds, DisplayService.GetScreens());
        if (fix is not { } p) return;
        MoveTo(p.X, p.Y);
        SavePosition();
    }

    private void CheckDisplayConfigurationChanged()
    {
        if (_isDragging || _isResizing) return;

        var newConfig = DisplayService.GetCurrentDisplayConfiguration();
        if (newConfig.ConfigurationHash == _currentDisplayConfiguration.ConfigurationHash) return;

        _currentDisplayConfiguration = newConfig;
        var (x, y) = DisplayService.GetDisplayPosition(_settings, _currentDisplayConfiguration);
        MoveTo(x, y);
        EnsureOnScreen();
    }

    // ── Sampling + chart ─────────────────────────────────────────────────────

    /// <summary>Maximum number of points retained for the configured duration.</summary>
    private int Capacity =>
        Math.Max(2, (int)Math.Ceiling(Math.Max(1, _settings.DurationSeconds) * 1000.0 /
                                       Math.Max(100, _settings.UpdateInterval)) + 1);

    private void Sample()
    {
        double cpu = _perfService.GetCpuLoad();
        double disk = _perfService.GetDiskActivePercent(_settings.DiskDrive);
        double net = _settings.ShowNetwork ? _networkService.GetLoadPercent() : 0;

        _history.Enqueue(new PerfSample { Cpu = cpu, DiskActive = disk, Network = net });
        int cap = Capacity;
        while (_history.Count > cap) _history.Dequeue();

        if (_settings.ShowDiskSpaceBar)
        {
            _diskUsagePercent = PerfService.GetDiskUsagePercent(_settings.DiskDrive);
            DrawDiskSpaceBar();
        }

        if (_settings.ShowMemoryBar)
        {
            (_memUsagePercent, _memUsedBytes, _memTotalBytes) = PerfService.GetMemoryUsage();
            DrawMemoryBar();
        }

        CpuLegend.Text = $"CPU {Pct3(cpu)}";
        DiskLegend.Text = $"{NormalizeDrive(_settings.DiskDrive)}: {Pct3(disk)}";
        NetLegend.Text = $"Net {Pct3(net)}";
        if (_settings.ShowNetwork)
        {
            var (rate, max, label) = _networkService.GetDetail();
            NetLegendItem.ToolTip = $"{label}\n{FormatRate(rate)} of {FormatRate(max)} learned max\n" +
                                    "Click to toggle line / filled area";
        }

        UpdateTopProcess();
        UpdateTemperatures();
        RefreshTitleLine(cpu, disk, net);
        DrawChart();
    }

    private void UpdateTopProcess()
    {
        if (!_settings.ShowTopProcess)
        {
            _topProcessText = "";
            _topProcessToolTip = null;
            return;
        }

        var tops = _processCpuService.GetTopProcesses(Math.Max(1, _settings.DurationSeconds), 3);
        if (tops.Count == 0)
        {
            _topProcessText = "";
            _topProcessToolTip = null;
            return;
        }

        _topProcessText = $"🔥 {tops[0].Name} {tops[0].Percent:0}%";
        _topProcessToolTip =
            $"Top CPU — rolling {FormatDuration(_settings.DurationSeconds)}\n" +
            string.Join("\n", tops.Select((t, i) => $"{i + 1}. {t.Name}  {t.Percent:0.#}%"));
    }

    private void UpdateTemperatures()
    {
        if (_settings.ShowTemperature && _settings.ShowTitle)
        {
            _cpuTemp = _temperatureService.GetCpuTemperature();
            _gpuTemp = _temperatureService.GetGpuTemperature();
        }
        else
        {
            _cpuTemp = _gpuTemp = null;
        }
    }

    private static double ToFahrenheit(double celsius) => celsius * 9.0 / 5.0 + 32.0;

    private static SolidColorBrush TempBrush(double fahrenheit, int warm, int hot) =>
        fahrenheit >= hot ? TempHotBrush : fahrenheit >= warm ? TempWarmBrush : TempNormalBrush;

    /// <summary>
    /// Rebuilds the title-row text: widget name, then live CPU/disk/network values, then
    /// the top-process indicator. TextTrimming clips this from the right as the icon panel
    /// (shown on hover) or a narrow widget width leaves less room for it.
    /// </summary>
    private void RefreshTitleLine(double cpu, double disk, double net)
    {
        TitleText.Inlines.Clear();

        // Temperatures lead the title so right-side trimming never hides them.
        var tempTip = new List<string>();
        if (_settings.ShowTemperature)
        {
            if (_cpuTemp is double ctC)
            {
                double ct = ToFahrenheit(ctC);
                TitleText.Inlines.Add(new Run($"CPU {ct:0}°F  ")
                    { Foreground = TempBrush(ct, _settings.CpuTempWarm, _settings.CpuTempHot) });
                tempTip.Add($"CPU {ct:0.#}°F  (yellow ≥ {_settings.CpuTempWarm}°, red ≥ {_settings.CpuTempHot}°)");
            }
            if (_gpuTemp is double gtC)
            {
                double gt = ToFahrenheit(gtC);
                TitleText.Inlines.Add(new Run($"GPU {gt:0}°F  ")
                    { Foreground = TempBrush(gt, _settings.GpuTempWarm, _settings.GpuTempHot) });
                tempTip.Add($"GPU {gt:0.#}°F  (yellow ≥ {_settings.GpuTempWarm}°, red ≥ {_settings.GpuTempHot}°)");
            }
        }

        TitleText.Inlines.Add(new Run("📈 Performance") { Foreground = TitleNameBrush });

        string stats = $"   CPU {cpu:0}%  {NormalizeDrive(_settings.DiskDrive)}: {disk:0}%";
        if (_settings.ShowNetwork) stats += $"  Net {net:0}%";
        TitleText.Inlines.Add(new Run(stats) { Foreground = TitleStatsBrush });

        if (!string.IsNullOrEmpty(_topProcessText))
            TitleText.Inlines.Add(new Run("   " + _topProcessText) { Foreground = TitleProcBrush });

        var tips = tempTip.Count > 0 ? string.Join("\n", tempTip) : null;
        TitleText.ToolTip = (tips, _topProcessToolTip) switch
        {
            (null, var p) => p,
            (var t, null) => t,
            (var t, var p) => t + "\n\n" + p,
        };
    }

    /// <summary>Fixed-width "nnn%" (space-padded, clamped to 0–999) so the monospace legend doesn't shift.</summary>
    private static string Pct3(double value) =>
        $"{Math.Clamp(Math.Round(value), 0, 999),3:0}%";

    private static string FormatDuration(int seconds)
    {
        int s = Math.Max(1, seconds);
        return s % 60 == 0 ? $"{s / 60} min" : $"{s / 60}m {s % 60}s";
    }

    /// <summary>Formats a byte/sec rate as KB/s, MB/s, or GB/s.</summary>
    private static string FormatRate(double bytesPerSec)
    {
        if (bytesPerSec >= 1_000_000_000) return $"{bytesPerSec / 1_000_000_000:0.0} GB/s";
        if (bytesPerSec >= 1_000_000)     return $"{bytesPerSec / 1_000_000:0.0} MB/s";
        return $"{bytesPerSec / 1_000:0} KB/s";
    }

    private void DrawChart()
    {
        double w = ChartCanvas.ActualWidth;
        double h = ChartCanvas.ActualHeight;
        ChartCanvas.Children.Clear();
        if (w <= 0 || h <= 0) return;

        // Horizontal grid lines at 25/50/75%
        if (_settings.ShowGrid)
        {
            var gridBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF));
            gridBrush.Freeze();
            for (int p = 1; p <= 3; p++)
            {
                double y = h * p / 4.0;
                ChartCanvas.Children.Add(new Line
                {
                    X1 = 0, Y1 = y, X2 = w, Y2 = y,
                    Stroke = gridBrush, StrokeThickness = 1
                });
            }
        }

        int n = _history.Count;
        if (n < 2) return;

        int cap = Capacity;
        var samples = _history.ToArray();

        bool showNet = _settings.ShowNetwork;
        var cpuPts = new PointCollection(n);
        var diskPts = new PointCollection(n);
        var netPts = showNet ? new PointCollection(n) : null;

        for (int j = 0; j < n; j++)
        {
            double x = cap <= 1 ? w : w * (j + (cap - n)) / (double)(cap - 1);
            double cpuY = h * (1 - Math.Clamp(samples[j].Cpu / 100.0, 0, 1));
            double diskY = h * (1 - Math.Clamp(samples[j].DiskActive / 100.0, 0, 1));
            cpuPts.Add(new System.Windows.Point(x, cpuY));
            diskPts.Add(new System.Windows.Point(x, diskY));
            netPts?.Add(new System.Windows.Point(x, h * (1 - Math.Clamp(samples[j].Network / 100.0, 0, 1))));
        }

        // Disk and network drawn first so CPU sits on top
        AddSeries(diskPts, _diskBrush, _settings.DiskFilled, h);
        if (netPts != null)
            AddSeries(netPts, _netBrush, _settings.NetFilled, h);
        AddSeries(cpuPts, _cpuBrush, _settings.CpuFilled, h);
    }

    /// <summary>
    /// Adds one series to the chart: always the line, plus (when filled) a 50%-alpha
    /// area closed down to the chart baseline beneath it.
    /// </summary>
    private void AddSeries(PointCollection pts, SolidColorBrush brush, bool filled, double h)
    {
        if (filled && pts.Count >= 2)
        {
            var area = new PointCollection(pts.Count + 2);
            foreach (var p in pts) area.Add(p);
            area.Add(new System.Windows.Point(pts[^1].X, h));
            area.Add(new System.Windows.Point(pts[0].X, h));
            ChartCanvas.Children.Add(new Polygon { Points = area, Fill = HalfAlpha(brush) });
        }
        ChartCanvas.Children.Add(new Polyline
        {
            Points = pts, Stroke = brush,
            StrokeThickness = 1.5, StrokeLineJoin = PenLineJoin.Round
        });
    }

    private static SolidColorBrush HalfAlpha(SolidColorBrush brush)
    {
        var c = brush.Color;
        var b = new SolidColorBrush(System.Windows.Media.Color.FromArgb((byte)(c.A / 2), c.R, c.G, c.B));
        b.Freeze();
        return b;
    }

    // ── Legend toggles (line ↔ filled area) ──────────────────────────────────

    private void LegendItem_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string series }) return;
        switch (series)
        {
            case "Cpu":  _settings.CpuFilled  = !_settings.CpuFilled;  break;
            case "Disk": _settings.DiskFilled = !_settings.DiskFilled; break;
            case "Net":  _settings.NetFilled  = !_settings.NetFilled;  break;
            default: return;
        }
        e.Handled = true;   // don't start a widget drag
        SettingsService.Save(App.Settings);
        UpdateLegendDots();
        DrawChart();
    }

    /// <summary>Solid dot = line mode; half-alpha dot with solid ring = filled-area mode.</summary>
    private void UpdateLegendDots()
    {
        StyleDot(CpuDot, _cpuBrush, _settings.CpuFilled);
        StyleDot(DiskDot, _diskBrush, _settings.DiskFilled);
        StyleDot(NetDot, _netBrush, _settings.NetFilled);
    }

    private static void StyleDot(Ellipse dot, SolidColorBrush brush, bool filled)
    {
        dot.Fill = filled ? HalfAlpha(brush) : brush;
        dot.Stroke = filled ? brush : null;
    }

    private void ChartCanvas_SizeChanged(object sender, SizeChangedEventArgs e) => DrawChart();

    private void MemoryCanvas_SizeChanged(object sender, SizeChangedEventArgs e) => DrawMemoryBar();

    private void DrawMemoryBar()
    {
        double w = MemoryCanvas.ActualWidth;
        double h = MemoryCanvas.ActualHeight;
        MemoryCanvas.Children.Clear();
        if (w <= 0 || h <= 0) return;

        double barH = h * Math.Clamp(_memUsagePercent / 100.0, 0, 1);
        var rect = new System.Windows.Shapes.Rectangle
        {
            Width = w,
            Height = Math.Max(1, barH),
            Fill = _memBrush
        };
        System.Windows.Controls.Canvas.SetLeft(rect, 0);
        System.Windows.Controls.Canvas.SetTop(rect, h - barH);
        MemoryCanvas.Children.Add(rect);

        const double gb = 1024.0 * 1024 * 1024;
        MemoryBarBorder.ToolTip =
            $"Memory: {_memUsedBytes / gb:0.0} / {_memTotalBytes / gb:0.0} GB ({_memUsagePercent:0}%)";
    }

    private void DiskSpaceCanvas_SizeChanged(object sender, SizeChangedEventArgs e) => DrawDiskSpaceBar();

    private void DrawDiskSpaceBar()
    {
        double w = DiskSpaceCanvas.ActualWidth;
        double h = DiskSpaceCanvas.ActualHeight;
        DiskSpaceCanvas.Children.Clear();
        if (w <= 0 || h <= 0) return;

        double fraction = Math.Clamp(_diskUsagePercent / 100.0, 0, 1);
        double barH = h * fraction;

        var rect = new System.Windows.Shapes.Rectangle
        {
            Width = w,
            Height = Math.Max(1, barH),
            Fill = _diskBrush   // same color as the disk activity graph line
        };
        System.Windows.Controls.Canvas.SetLeft(rect, 0);
        System.Windows.Controls.Canvas.SetTop(rect, h - barH);
        DiskSpaceCanvas.Children.Add(rect);

        DiskSpaceBarBorder.ToolTip = $"{NormalizeDrive(_settings.DiskDrive)}: {_diskUsagePercent:0.#}% used";
    }

    // ── Drive selector ───────────────────────────────────────────────────────

    private void BuildDriveSelector()
    {
        DriveSelectorPanel.Children.Clear();
        var drives = PerfService.GetFixedDrives();
        string active = NormalizeDrive(_settings.DiskDrive);

        var activeBg   = _diskBrush;   // same color as the disk activity graph line
        var activeFg   = System.Windows.Media.Brushes.Black;   // contrast against the disk color
        var inactiveBg = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF));

        foreach (var drive in drives)
        {
            var btn = new System.Windows.Controls.Button
            {
                Content    = $"{drive}:",
                Tag        = drive,
                Style      = (Style)FindResource("DriveButtonStyle"),
                Background = drive == active ? activeBg : inactiveBg,
            };
            if (drive == active) btn.Foreground = activeFg;
            btn.Click += DriveButton_Click;
            DriveSelectorPanel.Children.Add(btn);
        }
    }

    private void DriveButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button btn || btn.Tag is not string drive) return;
        _settings.DiskDrive = drive;
        SettingsService.Save(App.Settings);
        BuildDriveSelector();
        Sample();
    }

    private static string NormalizeDrive(string drive) =>
        string.IsNullOrWhiteSpace(drive) ? "C" : char.ToUpperInvariant(drive[0]).ToString();

    // ── Appearance helpers ───────────────────────────────────────────────────

    private void RebuildBrushes()
    {
        _cpuBrush = MakeBrush(_settings.CpuColor, System.Windows.Media.Colors.CornflowerBlue);
        _diskBrush = MakeBrush(_settings.DiskColor, System.Windows.Media.Colors.Orange);
        _netBrush = MakeBrush(_settings.NetworkColor, System.Windows.Media.Colors.LightGreen);
        UpdateLegendDots();
        _memBrush = MakeBrush(_settings.MemoryColor, System.Windows.Media.Color.FromRgb(0xCB, 0xA6, 0xF7));
    }

    private static SolidColorBrush MakeFrozenBrush(byte a, byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(a, r, g, b));
        brush.Freeze();
        return brush;
    }

    private static SolidColorBrush MakeBrush(string hex, System.Windows.Media.Color fallback)
    {
        try
        {
            var brush = new SolidColorBrush((System.Windows.Media.Color)ColorConverter.ConvertFromString(hex));
            brush.Freeze();
            return brush;
        }
        catch
        {
            var brush = new SolidColorBrush(fallback);
            brush.Freeze();
            return brush;
        }
    }

    private void ApplyBackgroundInternal(double opacity)
    {
        try
        {
            var color = (System.Windows.Media.Color)ColorConverter.ConvertFromString(_bgColorHex);
            WidgetBorder.Background = new SolidColorBrush(color);
        }
        catch { }
        // Fade the whole widget (frame, chart, text, bars), not just the background brush.
        WidgetBorder.Opacity = opacity;
    }

    private void ApplyChartBackgroundInternal()
    {
        try
        {
            var color = (System.Windows.Media.Color)ColorConverter.ConvertFromString(_settings.ChartBackgroundColor);
            ChartBorder.Background = new SolidColorBrush(color);
        }
        catch { }
    }

    public void ApplyBackground(string hexColor, double opacity)
    {
        _bgColorHex = hexColor;
        _bgOpacity = opacity;
        ApplyBackgroundInternal(opacity);
    }

    public void ApplyChartColors(string chartBgHex, string cpuHex, string diskHex, string netHex, string memHex)
    {
        _settings.ChartBackgroundColor = chartBgHex;
        _settings.CpuColor = cpuHex;
        _settings.DiskColor = diskHex;
        _settings.NetworkColor = netHex;
        _settings.MemoryColor = memHex;
        ApplyChartBackgroundInternal();
        RebuildBrushes();
        DrawChart();
        if (_settings.ShowDiskSpaceBar) DrawDiskSpaceBar();
        if (_settings.ShowMemoryBar) DrawMemoryBar();
        if (_settings.ShowDriveSelector) BuildDriveSelector();
    }

    public void ApplyFontScale(int percent)
    {
        double factor = Math.Max(0.25, percent / 100.0);
        TitleText.FontSize = Math.Max(6, 11 * factor);
        CpuLegend.FontSize = Math.Max(6, 10 * factor);
        DiskLegend.FontSize = Math.Max(6, 10 * factor);
        NetLegend.FontSize = Math.Max(6, 10 * factor);
    }

    public void ApplyVisibility(bool showTitle, bool showLegend, bool showGrid, bool showTopProcess,
                                bool showNetwork, bool showDriveSelector, bool showDiskSpaceBar,
                                bool showMemoryBar, bool showTemperature)
    {
        _settings.ShowTemperature   = showTemperature;
        _settings.ShowMemoryBar     = showMemoryBar;
        _settings.ShowTitle         = showTitle;
        _settings.ShowLegend        = showLegend;
        _settings.ShowGrid          = showGrid;
        _settings.ShowTopProcess    = showTopProcess;
        _settings.ShowNetwork       = showNetwork;
        _settings.ShowDriveSelector = showDriveSelector;
        _settings.ShowDiskSpaceBar  = showDiskSpaceBar;

        // Keep the process-service ref count in sync with the toggle.
        if (showTopProcess && !_processCpuServiceStarted) {
            _processCpuService.Start();
            _processCpuServiceStarted = true;
        } else if (!showTopProcess && _processCpuServiceStarted) {
            _processCpuService.Stop();
            _processCpuServiceStarted = false;
        }

        ApplyVisibilityInternal();
        UpdateTopProcess();
        UpdateTemperatures();
        DrawChart();
    }

    private void ApplyVisibilityInternal()
    {
        TitleSection.Visibility = _settings.ShowTitle ? Visibility.Visible : Visibility.Collapsed;
        LegendPanel.Visibility  = _settings.ShowLegend ? Visibility.Visible : Visibility.Collapsed;

        var netVisibility = _settings.ShowNetwork ? Visibility.Visible : Visibility.Collapsed;
        NetLegendItem.Visibility = netVisibility;

        DiskSpaceBarBorder.Visibility = _settings.ShowDiskSpaceBar
            ? Visibility.Visible : Visibility.Collapsed;
        MemoryBarBorder.Visibility = _settings.ShowMemoryBar
            ? Visibility.Visible : Visibility.Collapsed;

        if (_settings.ShowDriveSelector)
        {
            DriveSelectorPanel.Visibility = Visibility.Visible;
            BuildDriveSelector();
        }
        else
        {
            DriveSelectorPanel.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>Applies sampling-related settings and rebuilds the history buffer/timer.</summary>
    public void ApplyChartSettings(string drive, int durationSeconds, int updateInterval)
    {
        _settings.DiskDrive = drive;
        _settings.DurationSeconds = durationSeconds;
        _settings.UpdateInterval = updateInterval;

        if (_updateTimer != null)
            _updateTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(100, updateInterval));

        int cap = Capacity;
        while (_history.Count > cap) _history.Dequeue();
        DrawChart();
    }

    public void ApplySize(double width, double height)
    {
        Width = Math.Max(MinWidth, width);
        Height = Math.Max(MinHeight, height);
        if (_isEmbedded)
            MoveTo(_posX, _posY);   // child windows need an explicit MoveWindow to pick up the new size
    }

    // ── Hover ────────────────────────────────────────────────────────────────

    private void Widget_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        IconPanel.Visibility = Visibility.Visible;
        ApplyBackgroundInternal(Math.Min(1.0, _bgOpacity + 0.07));
    }

    private void Widget_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        IconPanel.Visibility = Visibility.Collapsed;
        ApplyBackgroundInternal(_bgOpacity);
    }

    // ── Dragging (move) ──────────────────────────────────────────────────────

    private void Widget_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (IsClickOnInteractiveElement(e.OriginalSource))
            return;

        var zone = GetResizeZone(e.GetPosition(WidgetBorder));
        if (zone != ResizeZone.None)
        {
            StartResize(zone);
            e.Handled = true;
            return;
        }

        // Manual drag (DragMove() fails on Windows 10 with AllowsTransparency=True + WindowStyle=None,
        // and doesn't work at all for a window embedded in WorkerW). Everything is in screen pixels.
        SnapshotStart();
        _isDragging = true;
        SetCapturedCursor(System.Windows.Input.Cursors.SizeAll);
        WidgetBorder.CaptureMouse();
        e.Handled = true;
    }

    private void SnapshotStart()
    {
        _startCursor = DesktopService.GetCursorPosition();
        _startX = _posX;
        _startY = _posY;
        _startW = Width;
        _startH = Height;
    }

    private void Widget_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_isDragging)
        {
            var c = DesktopService.GetCursorPosition();
            var (x, y) = WidgetGeometry.DragTo(_startX, _startY, _startCursor.X, _startCursor.Y, c.X, c.Y);
            MoveTo(x, y);
            return;
        }

        if (_isResizing)
        {
            UpdateResize();
            return;
        }

        UpdateHoverCursor(GetResizeZone(e.GetPosition(WidgetBorder)));
    }

    /// <summary>
    /// The move cursor comes from ChartBorder (XAML) and the hand cursor from the legend
    /// items, so everything else (padding/edges, title, side bars) shows the default arrow.
    /// Resize corners overlap the chart's corners (CornerHitSize &gt; padding), so while
    /// in a corner the border forces its resize cursor over any child cursor.
    /// </summary>
    private void UpdateHoverCursor(ResizeZone zone)
    {
        bool inCorner = zone != ResizeZone.None;
        WidgetBorder.Cursor = inCorner ? CursorFor(zone) : null;
        WidgetBorder.ForceCursor = inCorner;
    }

    /// <summary>
    /// While the border has mouse capture (drag/resize), it's the element queried for the
    /// cursor, so pin the appropriate cursor for the duration of the operation.
    /// </summary>
    private void SetCapturedCursor(System.Windows.Input.Cursor cursor)
    {
        WidgetBorder.Cursor = cursor;
        WidgetBorder.ForceCursor = true;
    }

    private void Widget_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isDragging && !_isResizing) return;
        EndDragOrResize();
        UpdateHoverCursor(GetResizeZone(e.GetPosition(WidgetBorder)));
        e.Handled = true;
    }

    /// <summary>Capture can be lost mid-drag (Alt-Tab, Win key, UAC prompt); finish cleanly instead of sticking.</summary>
    private void Widget_LostMouseCapture(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_isDragging || _isResizing)
            EndDragOrResize();
    }

    private void EndDragOrResize()
    {
        bool wasResizing = _isResizing;
        _isDragging = false;
        _isResizing = false;
        _resizeZone = ResizeZone.None;
        if (WidgetBorder.IsMouseCaptured)
            WidgetBorder.ReleaseMouseCapture();
        UpdateHoverCursor(ResizeZone.None);

        if (wasResizing)
        {
            _settings.Width = Width;
            _settings.Height = Height;
        }
        EnsureOnScreen();
        SavePosition();
    }

    private bool IsClickOnInteractiveElement(object? source)
    {
        if (source is System.Windows.Controls.Button) return true;
        if (source is System.Windows.FrameworkElement fe)
        {
            var parent = System.Windows.Media.VisualTreeHelper.GetParent(fe);
            while (parent != null)
            {
                if (parent == IconPanel) return true;
                parent = System.Windows.Media.VisualTreeHelper.GetParent(parent);
            }
        }
        return false;
    }

    // ── Resizing ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Bottom corners always resize. Top corners only resize when the title bar is
    /// hidden — while it's shown, the top of the widget is the drag/move zone instead.
    /// </summary>
    private ResizeZone GetResizeZone(System.Windows.Point pos) =>
        WidgetGeometry.HitTest(pos.X, pos.Y, WidgetBorder.ActualWidth, WidgetBorder.ActualHeight,
                               allowTopCorners: !_settings.ShowTitle);

    private static System.Windows.Input.Cursor CursorFor(ResizeZone zone) => zone switch
    {
        ResizeZone.TopLeft or ResizeZone.BottomRight => System.Windows.Input.Cursors.SizeNWSE,
        ResizeZone.TopRight or ResizeZone.BottomLeft => System.Windows.Input.Cursors.SizeNESW,
        _ => System.Windows.Input.Cursors.SizeAll
    };

    private void StartResize(ResizeZone zone)
    {
        SnapshotStart();
        _isResizing = true;
        _resizeZone = zone;
        SetCapturedCursor(CursorFor(zone));
        WidgetBorder.CaptureMouse();
    }

    private void UpdateResize()
    {
        var c = DesktopService.GetCursorPosition();
        double dpiX = DpiX, dpiY = DpiY;

        // Never let the widget grow beyond the virtual screen.
        double maxW = _currentDisplayConfiguration.TotalWidth  > 0 ? _currentDisplayConfiguration.TotalWidth  / dpiX : double.PositiveInfinity;
        double maxH = _currentDisplayConfiguration.TotalHeight > 0 ? _currentDisplayConfiguration.TotalHeight / dpiY : double.PositiveInfinity;

        var r = WidgetGeometry.Resize(_resizeZone, _startW, _startH, _startX, _startY,
                                      c.X - _startCursor.X, c.Y - _startCursor.Y,
                                      dpiX, dpiY, MinWidth, MinHeight, maxW, maxH);
        Width = r.Width;
        Height = r.Height;
        MoveTo(r.X, r.Y);
    }

    // ── Settings / About ─────────────────────────────────────────────────────

    public void OpenSettings()
    {
        // Tray double-click / tray menu / widget menu can all request this while it's already open.
        if (_settingsDialog != null)
        {
            _settingsDialog.Activate();
            return;
        }

        bool ok;
        try
        {
            _settingsDialog = new SettingsWindow(_settings, livePreviewTarget: this);
            ok = _settingsDialog.ShowDialog() == true;
        }
        finally
        {
            _settingsDialog = null;
        }

        if (ok)
        {
            _bgColorHex = _settings.BackgroundColor;
            _bgOpacity = _settings.BackgroundOpacity;
            ApplyBackgroundInternal(_bgOpacity);
            ApplyChartBackgroundInternal();
            RebuildBrushes();
            ApplyFontScale(_settings.FontScalePercent);
            ApplyVisibilityInternal();
            ApplySize(_settings.Width, _settings.Height);

            if (_updateTimer != null)
                _updateTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(100, _settings.UpdateInterval));
            int cap = Capacity;
            while (_history.Count > cap) _history.Dequeue();
            DrawChart();
        }
        else
        {
            // Restore live-preview changes from the working settings
            _bgColorHex = _settings.BackgroundColor;
            _bgOpacity = _settings.BackgroundOpacity;
            ApplyBackgroundInternal(_bgOpacity);
            ApplyChartBackgroundInternal();
            RebuildBrushes();
            ApplyFontScale(_settings.FontScalePercent);
            ApplyVisibilityInternal();
            ApplySize(_settings.Width, _settings.Height);
            DrawChart();
        }
    }

    private void ResourceMonitor_Click(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo("resmon.exe") { UseShellExecute = true }); }
        catch { }
    }

    private void Settings_Click(object sender, RoutedEventArgs e) => OpenSettings();

    private void About_Click(object sender, RoutedEventArgs e)
        => ((App)System.Windows.Application.Current).OpenAbout(this);

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (App.Settings.Widgets.Count <= 1)
        {
            System.Windows.MessageBox.Show(
                "This is the only widget. Use Exit to close the app instead.",
                "Remove Widget", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var result = System.Windows.MessageBox.Show(
            "Remove this widget?",
            "Confirm Remove",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
            ((App)System.Windows.Application.Current).RemoveWidget(_settings.Id);
    }

    private void Exit_Click(object sender, RoutedEventArgs e)
        => System.Windows.Application.Current.Shutdown();

    protected override void OnClosed(EventArgs e)
    {
        _updateTimer?.Stop();
        _updateTimer = null;
        _displayCheckTimer?.Stop();
        _displayCheckTimer = null;

        if (_processCpuServiceStarted) {
            _processCpuService.Stop();
            _processCpuServiceStarted = false;
        }

        base.OnClosed(e);
    }
}
