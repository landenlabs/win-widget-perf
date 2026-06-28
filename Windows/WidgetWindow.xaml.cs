// Copyright (c) 2026 LanDen Labs - Dennis Lang
using System.Diagnostics;
using System.Windows;
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
    private readonly Queue<PerfSample> _history = new();

    private System.Windows.Threading.DispatcherTimer? _updateTimer;
    private System.Windows.Threading.DispatcherTimer? _displayCheckTimer;
    private DisplayConfiguration _currentDisplayConfiguration;

    private string _bgColorHex = "#1E1E2E";
    private double _bgOpacity = 0.80;
    private SolidColorBrush _cpuBrush = new(System.Windows.Media.Colors.CornflowerBlue);
    private SolidColorBrush _diskBrush = new(System.Windows.Media.Colors.Orange);
    private SolidColorBrush _netBrush = new(System.Windows.Media.Colors.LightGreen);

    private double _diskUsagePercent;

    private bool _isEmbedded;
    private int _embeddedX;
    private int _embeddedY;

    // Drag (move)
    private bool _isDragging;
    private System.Windows.Point _dragOffset;

    // Resize
    private bool _isResizing;
    private DesktopService.POINT _resizeStartCursor;
    private double _resizeStartW;
    private double _resizeStartH;
    private double _dpiScaleX = 1.0;
    private double _dpiScaleY = 1.0;

    public string WidgetId => _settings.Id;

    public WidgetWindow(WidgetSettings settings, PerfService perfService, ProcessCpuService processCpuService, NetworkService networkService)
    {
        InitializeComponent();

        _settings = settings;
        _perfService = perfService;
        _processCpuService = processCpuService;
        _networkService = networkService;

        _bgColorHex = string.IsNullOrEmpty(settings.BackgroundColor) ? "#1E1E2E" : settings.BackgroundColor;
        _bgOpacity = settings.BackgroundOpacity > 0 ? settings.BackgroundOpacity : 0.80;

        Width = settings.Width > MinWidth ? settings.Width : 340;
        Height = settings.Height > MinHeight ? settings.Height : 160;

        _currentDisplayConfiguration = DisplayService.GetCurrentDisplayConfiguration();
        var (x, y) = DisplayService.GetDisplayPosition(settings, _currentDisplayConfiguration);
        Left = x;
        Top = y;
        _embeddedX = x;
        _embeddedY = y;

        RebuildBrushes();
        InitializeUpdateTimer();
        InitializeDisplayCheckTimer();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        var source = PresentationSource.FromVisual(this);
        _dpiScaleX = source?.CompositionTarget.TransformToDevice.M11 ?? 1.0;
        _dpiScaleY = source?.CompositionTarget.TransformToDevice.M22 ?? 1.0;

        EnsureOnScreen();
        ApplyBackgroundInternal(_bgOpacity);
        ApplyChartBackgroundInternal();
        ApplyFontScale(_settings.FontScalePercent > 0 ? _settings.FontScalePercent : 100);
        ApplyVisibilityInternal();

        if (_settings.EmbedInWallpaper)
        {
            _isEmbedded = DesktopService.EmbedInWallpaper(this);
            if (_isEmbedded)
                DesktopService.MoveEmbeddedWindow(this, _embeddedX, _embeddedY);
            else
                DesktopService.SetAlwaysOnBottom(this);
        }
        else
        {
            DesktopService.SetAlwaysOnBottom(this);
        }

        Sample();
    }

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

    private void EnsureOnScreen()
    {
        int physX = (int)(Left   * _dpiScaleX);
        int physY = (int)(Top    * _dpiScaleY);
        int physW = (int)(Width  * _dpiScaleX);
        int physH = (int)(Height * _dpiScaleY);

        const int minVisible = 50;
        bool visible = System.Windows.Forms.Screen.AllScreens.Any(s =>
            physX + physW > s.Bounds.Left + minVisible &&
            physX         < s.Bounds.Right  - minVisible &&
            physY + physH > s.Bounds.Top    + minVisible &&
            physY         < s.Bounds.Bottom - minVisible);

        if (!visible)
        {
            var area = System.Windows.Forms.Screen.PrimaryScreen?.WorkingArea
                       ?? new System.Drawing.Rectangle(0, 0, 1920, 1040);
            Left = (area.Right - physW - 20) / _dpiScaleX;
            Top  = (area.Top   + 20)         / _dpiScaleY;
            _embeddedX = (int)Left;
            _embeddedY = (int)Top;
            DisplayService.SaveDisplayPosition(_settings, _currentDisplayConfiguration, _embeddedX, _embeddedY);
            SettingsService.Save(App.Settings);
        }
    }

    private void CheckDisplayConfigurationChanged()
    {
        var newConfig = DisplayService.GetCurrentDisplayConfiguration();
        if (newConfig.ConfigurationHash != _currentDisplayConfiguration.ConfigurationHash)
        {
            _currentDisplayConfiguration = newConfig;
            var (x, y) = DisplayService.GetDisplayPosition(_settings, _currentDisplayConfiguration);
            _embeddedX = x;
            _embeddedY = y;
            if (_isEmbedded)
                DesktopService.MoveEmbeddedWindow(this, x, y);
            else
            {
                Left = x;
                Top = y;
            }
        }
    }

    // ── Sampling + chart ─────────────────────────────────────────────────────

    /// <summary>Maximum number of points retained for the configured duration.</summary>
    private int Capacity =>
        Math.Max(2, (int)Math.Ceiling(Math.Max(1, _settings.DurationSeconds) * 1000.0 /
                                       Math.Max(100, _settings.UpdateInterval)) + 1);

    private void Sample()
    {
        double cpu = _perfService.GetCpuLoad();
        double disk = _perfService.GetDiskQueue(_settings.DiskDrive);
        double net = _settings.ShowNetwork ? _networkService.GetLoadPercent() : 0;

        _history.Enqueue(new PerfSample { Cpu = cpu, DiskQueue = disk, Network = net });
        int cap = Capacity;
        while (_history.Count > cap) _history.Dequeue();

        if (_settings.ShowDiskSpaceBar)
        {
            _diskUsagePercent = PerfService.GetDiskUsagePercent(_settings.DiskDrive);
            DrawDiskSpaceBar();
        }

        CpuLegend.Text = $"CPU {cpu:0}%";
        DiskLegend.Text = $"{NormalizeDrive(_settings.DiskDrive)}: Q {disk:0.0}";
        NetLegend.Text = $"Net {net:0}%";
        if (_settings.ShowNetwork)
        {
            var (rate, max, label) = _networkService.GetDetail();
            NetLegend.ToolTip = $"{label}\n{FormatRate(rate)} of {FormatRate(max)} learned max";
        }

        UpdateTopProcess();
        DrawChart();
    }

    private void UpdateTopProcess()
    {
        if (!_settings.ShowTopProcess)
        {
            ProcText.Text = "";
            ProcText.ToolTip = null;
            return;
        }

        var tops = _processCpuService.GetTopProcesses(Math.Max(1, _settings.DurationSeconds), 3);
        if (tops.Count == 0)
        {
            ProcText.Text = "";
            ProcText.ToolTip = null;
            return;
        }

        ProcText.Text = $"🔥 {tops[0].Name} {tops[0].Percent:0}%";
        ProcText.ToolTip =
            $"Top CPU — rolling {FormatDuration(_settings.DurationSeconds)}\n" +
            string.Join("\n", tops.Select((t, i) => $"{i + 1}. {t.Name}  {t.Percent:0.#}%"));
    }

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
        double scale = _settings.DiskQueueScale <= 0 ? 1.0 : _settings.DiskQueueScale;

        bool showNet = _settings.ShowNetwork;
        var cpuPts = new PointCollection(n);
        var diskPts = new PointCollection(n);
        var netPts = showNet ? new PointCollection(n) : null;

        for (int j = 0; j < n; j++)
        {
            double x = cap <= 1 ? w : w * (j + (cap - n)) / (double)(cap - 1);
            double cpuY = h * (1 - Math.Clamp(samples[j].Cpu / 100.0, 0, 1));
            double diskY = h * (1 - Math.Clamp(samples[j].DiskQueue / scale, 0, 1));
            cpuPts.Add(new System.Windows.Point(x, cpuY));
            diskPts.Add(new System.Windows.Point(x, diskY));
            netPts?.Add(new System.Windows.Point(x, h * (1 - Math.Clamp(samples[j].Network / 100.0, 0, 1))));
        }

        // Disk and network drawn first so CPU sits on top
        ChartCanvas.Children.Add(new Polyline
        {
            Points = diskPts, Stroke = _diskBrush,
            StrokeThickness = 1.5, StrokeLineJoin = PenLineJoin.Round
        });
        if (netPts != null)
        {
            ChartCanvas.Children.Add(new Polyline
            {
                Points = netPts, Stroke = _netBrush,
                StrokeThickness = 1.5, StrokeLineJoin = PenLineJoin.Round
            });
        }
        ChartCanvas.Children.Add(new Polyline
        {
            Points = cpuPts, Stroke = _cpuBrush,
            StrokeThickness = 1.5, StrokeLineJoin = PenLineJoin.Round
        });
    }

    private void ChartCanvas_SizeChanged(object sender, SizeChangedEventArgs e) => DrawChart();

    private void DiskSpaceCanvas_SizeChanged(object sender, SizeChangedEventArgs e) => DrawDiskSpaceBar();

    private void DrawDiskSpaceBar()
    {
        double w = DiskSpaceCanvas.ActualWidth;
        double h = DiskSpaceCanvas.ActualHeight;
        DiskSpaceCanvas.Children.Clear();
        if (w <= 0 || h <= 0) return;

        double fraction = Math.Clamp(_diskUsagePercent / 100.0, 0, 1);
        double barH = h * fraction;

        var color = fraction < 0.70
            ? System.Windows.Media.Color.FromRgb(0xA6, 0xE3, 0xA1)   // green
            : fraction < 0.90
                ? System.Windows.Media.Color.FromRgb(0xF9, 0xA8, 0x25) // amber
                : System.Windows.Media.Color.FromRgb(0xF3, 0x8B, 0xA8); // red

        var rect = new System.Windows.Shapes.Rectangle
        {
            Width = w,
            Height = Math.Max(1, barH),
            Fill = new SolidColorBrush(color)
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

        var activeBg   = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0xAA, 0x89, 0xB4, 0xFA));
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
        CpuDot.Fill = _cpuBrush;
        DiskDot.Fill = _diskBrush;
        NetDot.Fill = _netBrush;
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
            WidgetBorder.Background = new SolidColorBrush(color) { Opacity = opacity };
        }
        catch { }
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

    public void ApplyChartColors(string chartBgHex, string cpuHex, string diskHex, string netHex)
    {
        _settings.ChartBackgroundColor = chartBgHex;
        _settings.CpuColor = cpuHex;
        _settings.DiskColor = diskHex;
        _settings.NetworkColor = netHex;
        ApplyChartBackgroundInternal();
        RebuildBrushes();
        DrawChart();
    }

    public void ApplyFontScale(int percent)
    {
        double factor = Math.Max(0.25, percent / 100.0);
        TitleText.FontSize = Math.Max(6, 11 * factor);
        ProcText.FontSize = Math.Max(6, 11 * factor);
        CpuLegend.FontSize = Math.Max(6, 10 * factor);
        DiskLegend.FontSize = Math.Max(6, 10 * factor);
    }

    public void ApplyVisibility(bool showTitle, bool showLegend, bool showGrid, bool showTopProcess,
                                bool showNetwork, bool showDriveSelector, bool showDiskSpaceBar)
    {
        _settings.ShowTitle         = showTitle;
        _settings.ShowLegend        = showLegend;
        _settings.ShowGrid          = showGrid;
        _settings.ShowTopProcess    = showTopProcess;
        _settings.ShowNetwork       = showNetwork;
        _settings.ShowDriveSelector = showDriveSelector;
        _settings.ShowDiskSpaceBar  = showDiskSpaceBar;
        ApplyVisibilityInternal();
        UpdateTopProcess();
        DrawChart();
    }

    private void ApplyVisibilityInternal()
    {
        TitleSection.Visibility = _settings.ShowTitle ? Visibility.Visible : Visibility.Collapsed;
        LegendPanel.Visibility  = _settings.ShowLegend ? Visibility.Visible : Visibility.Collapsed;
        ProcText.Visibility     = _settings.ShowTopProcess ? Visibility.Visible : Visibility.Collapsed;

        var netVisibility = _settings.ShowNetwork ? Visibility.Visible : Visibility.Collapsed;
        NetDot.Visibility    = netVisibility;
        NetLegend.Visibility = netVisibility;

        DiskSpaceBarBorder.Visibility = _settings.ShowDiskSpaceBar
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
    public void ApplyChartSettings(string drive, int durationSeconds, double queueScale, int updateInterval)
    {
        _settings.DiskDrive = drive;
        _settings.DurationSeconds = durationSeconds;
        _settings.DiskQueueScale = queueScale;
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
            DesktopService.MoveEmbeddedWindow(this, _embeddedX, _embeddedY);
    }

    // ── Hover ────────────────────────────────────────────────────────────────

    private void Widget_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        IconPanel.Visibility = Visibility.Visible;
        ResizeGrip.Opacity = 1.0;
        ApplyBackgroundInternal(Math.Min(1.0, _bgOpacity + 0.07));
    }

    private void Widget_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        IconPanel.Visibility = Visibility.Collapsed;
        ResizeGrip.Opacity = 0.4;
        ApplyBackgroundInternal(_bgOpacity);
    }

    // ── Dragging (move) ──────────────────────────────────────────────────────

    private void Widget_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (IsClickOnInteractiveElement(e.OriginalSource))
            return;

        var cursor = DesktopService.GetCursorPosition();
        if (_isEmbedded)
        {
            var bounds = DesktopService.GetWindowBounds(this);
            _dragOffset = new System.Windows.Point(cursor.X - bounds.Left, cursor.Y - bounds.Top);
        }
        else
        {
            // DragMove() fails on Windows 10 with AllowsTransparency=True + WindowStyle=None
            _dragOffset = e.GetPosition(this);
        }
        _isDragging = true;
        WidgetBorder.CaptureMouse();
        e.Handled = true;
    }

    private void Widget_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_isDragging) return;
        var cursor = DesktopService.GetCursorPosition();
        if (_isEmbedded)
        {
            _embeddedX = cursor.X - (int)_dragOffset.X;
            _embeddedY = cursor.Y - (int)_dragOffset.Y;
            DesktopService.MoveEmbeddedWindow(this, _embeddedX, _embeddedY);
        }
        else
        {
            Left = cursor.X / _dpiScaleX - _dragOffset.X;
            Top = cursor.Y / _dpiScaleY - _dragOffset.Y;
        }
    }

    private void Widget_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isDragging) return;
        _isDragging = false;
        WidgetBorder.ReleaseMouseCapture();

        if (_isEmbedded)
        {
            var bounds = DesktopService.GetWindowBounds(this);
            DisplayService.SaveDisplayPosition(_settings, _currentDisplayConfiguration, bounds.Left, bounds.Top);
        }
        else
        {
            DisplayService.SaveDisplayPosition(_settings, _currentDisplayConfiguration, (int)Left, (int)Top);
        }
        SettingsService.Save(App.Settings);
    }

    private bool IsClickOnInteractiveElement(object? source)
    {
        if (source is System.Windows.Controls.Button) return true;
        if (source is Path) return true; // resize grip
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

    private void ResizeGrip_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _isResizing = true;
        _resizeStartCursor = DesktopService.GetCursorPosition();
        _resizeStartW = Width;
        _resizeStartH = Height;
        ResizeGrip.CaptureMouse();
        e.Handled = true;
    }

    private void ResizeGrip_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_isResizing) return;
        var cursor = DesktopService.GetCursorPosition();
        double dxDip = (cursor.X - _resizeStartCursor.X) / _dpiScaleX;
        double dyDip = (cursor.Y - _resizeStartCursor.Y) / _dpiScaleY;

        Width = Math.Max(MinWidth, _resizeStartW + dxDip);
        Height = Math.Max(MinHeight, _resizeStartH + dyDip);

        if (_isEmbedded)
            DesktopService.MoveEmbeddedWindow(this, _embeddedX, _embeddedY);
        e.Handled = true;
    }

    private void ResizeGrip_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isResizing) return;
        _isResizing = false;
        ResizeGrip.ReleaseMouseCapture();

        _settings.Width = Width;
        _settings.Height = Height;
        SettingsService.Save(App.Settings);
        e.Handled = true;
    }

    // ── Settings / About ─────────────────────────────────────────────────────

    public void OpenSettings()
    {
        var dlg = new SettingsWindow(_settings, livePreviewTarget: this);
        if (dlg.ShowDialog() == true)
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
        => new AboutWindow() { Owner = this }.ShowDialog();

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
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
        base.OnClosed(e);
    }
}
