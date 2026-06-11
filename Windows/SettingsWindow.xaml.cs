// Copyright (c) 2026 LanDen Labs - Dennis Lang
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using WinWidgetPerf.Models;
using WinWidgetPerf.Services;

namespace WinWidgetPerf.Windows;

public partial class SettingsWindow : Window, INotifyPropertyChanged
{
    private readonly WidgetSettings _widget;
    private readonly WidgetWindow? _livePreviewTarget;

    public ObservableCollection<string> Drives { get; } = [];

    /// <summary>Full path to the learned network-max store, shown as the button tooltip.</summary>
    public string NetworkStorePath => NetworkMaxStore.StorePath;

    // ── Colors ───────────────────────────────────────────────────────────────

    private string _bgColorHex = "#1E1E2E";
    public string BgColorHex
    {
        get => _bgColorHex;
        set { _bgColorHex = value; _bgColorBrush = null; OnPropertyChanged(); OnPropertyChanged(nameof(BgColorBrush)); LivePreviewBackground(); }
    }
    private SolidColorBrush? _bgColorBrush;
    public SolidColorBrush BgColorBrush => _bgColorBrush ??= ToBrush(_bgColorHex);

    private string _chartBgColorHex = "#0D0D1A";
    public string ChartBgColorHex
    {
        get => _chartBgColorHex;
        set { _chartBgColorHex = value; _chartBgColorBrush = null; OnPropertyChanged(); OnPropertyChanged(nameof(ChartBgColorBrush)); LivePreviewChartColors(); }
    }
    private SolidColorBrush? _chartBgColorBrush;
    public SolidColorBrush ChartBgColorBrush => _chartBgColorBrush ??= ToBrush(_chartBgColorHex);

    private string _cpuColorHex = "#FF89B4FA";
    public string CpuColorHex
    {
        get => _cpuColorHex;
        set { _cpuColorHex = value; _cpuColorBrush = null; OnPropertyChanged(); OnPropertyChanged(nameof(CpuColorBrush)); LivePreviewChartColors(); }
    }
    private SolidColorBrush? _cpuColorBrush;
    public SolidColorBrush CpuColorBrush => _cpuColorBrush ??= ToBrush(_cpuColorHex);

    private string _diskColorHex = "#FFF9A825";
    public string DiskColorHex
    {
        get => _diskColorHex;
        set { _diskColorHex = value; _diskColorBrush = null; OnPropertyChanged(); OnPropertyChanged(nameof(DiskColorBrush)); LivePreviewChartColors(); }
    }
    private SolidColorBrush? _diskColorBrush;
    public SolidColorBrush DiskColorBrush => _diskColorBrush ??= ToBrush(_diskColorHex);

    private string _netColorHex = "#FFA6E3A1";
    public string NetColorHex
    {
        get => _netColorHex;
        set { _netColorHex = value; _netColorBrush = null; OnPropertyChanged(); OnPropertyChanged(nameof(NetColorBrush)); LivePreviewChartColors(); }
    }
    private SolidColorBrush? _netColorBrush;
    public SolidColorBrush NetColorBrush => _netColorBrush ??= ToBrush(_netColorHex);

    // ── Sliders / numerics ──────────────────────────────────────────────────

    private int _bgOpacityPercent;
    public int BgOpacityPercent
    {
        get => _bgOpacityPercent;
        set { _bgOpacityPercent = value; OnPropertyChanged(); LivePreviewBackground(); }
    }

    private int _fontScalePercent;
    public int FontScalePercent
    {
        get => _fontScalePercent;
        set { _fontScalePercent = value; OnPropertyChanged(); _livePreviewTarget?.ApplyFontScale(value); }
    }

    private int _updateInterval;
    public int UpdateInterval
    {
        get => _updateInterval;
        set { _updateInterval = value; OnPropertyChanged(); LivePreviewChartSettings(); }
    }

    private int _durationSeconds;
    public int DurationSeconds
    {
        get => _durationSeconds;
        set { _durationSeconds = value; OnPropertyChanged(); OnPropertyChanged(nameof(DurationHint)); LivePreviewChartSettings(); }
    }

    public string DurationHint
    {
        get
        {
            int s = Math.Max(1, _durationSeconds);
            return s % 60 == 0 ? $"= {s / 60} min" : $"= {s / 60}m {s % 60}s";
        }
    }

    private double _diskQueueScale;
    public double DiskQueueScale
    {
        get => _diskQueueScale;
        set { _diskQueueScale = value; OnPropertyChanged(); LivePreviewChartSettings(); }
    }

    private string _selectedDrive = "C";
    public string SelectedDrive
    {
        get => _selectedDrive;
        set { _selectedDrive = value ?? "C"; OnPropertyChanged(); LivePreviewChartSettings(); }
    }

    private int _widgetWidth;
    public int WidgetWidth
    {
        get => _widgetWidth;
        set { _widgetWidth = value; OnPropertyChanged(); LivePreviewSize(); }
    }

    private int _widgetHeight;
    public int WidgetHeight
    {
        get => _widgetHeight;
        set { _widgetHeight = value; OnPropertyChanged(); LivePreviewSize(); }
    }

    // ── Toggles ──────────────────────────────────────────────────────────────

    private bool _embedInWallpaper;
    public bool EmbedInWallpaper { get => _embedInWallpaper; set { _embedInWallpaper = value; OnPropertyChanged(); } }

    private bool _autoStartEnabled;
    public bool AutoStartEnabled { get => _autoStartEnabled; set { _autoStartEnabled = value; OnPropertyChanged(); } }

    private bool _showTitle;
    public bool ShowTitle { get => _showTitle; set { _showTitle = value; OnPropertyChanged(); LivePreviewVisibility(); } }

    private bool _showLegend;
    public bool ShowLegend { get => _showLegend; set { _showLegend = value; OnPropertyChanged(); LivePreviewVisibility(); } }

    private bool _showGrid;
    public bool ShowGrid { get => _showGrid; set { _showGrid = value; OnPropertyChanged(); LivePreviewVisibility(); } }

    private bool _showTopProcess;
    public bool ShowTopProcess { get => _showTopProcess; set { _showTopProcess = value; OnPropertyChanged(); LivePreviewVisibility(); } }

    private bool _showNetwork;
    public bool ShowNetwork { get => _showNetwork; set { _showNetwork = value; OnPropertyChanged(); LivePreviewVisibility(); } }

    // ── Originals for Cancel restore ────────────────────────────────────────

    private readonly string _origBgColor;
    private readonly string _origChartBgColor;
    private readonly string _origCpuColor;
    private readonly string _origDiskColor;
    private readonly string _origNetColor;
    private readonly int _origBgOpacityPercent;
    private readonly int _origFontScalePercent;
    private readonly int _origUpdateInterval;
    private readonly int _origDurationSeconds;
    private readonly double _origDiskQueueScale;
    private readonly string _origDrive;
    private readonly double _origWidth;
    private readonly double _origHeight;
    private readonly bool _origShowTitle;
    private readonly bool _origShowLegend;
    private readonly bool _origShowGrid;
    private readonly bool _origShowTopProcess;
    private readonly bool _origShowNetwork;
    private readonly double _origPosX;
    private readonly double _origPosY;

    // ── Position picker state ────────────────────────────────────────────────

    private double _mapScale, _mapLeft, _mapTop, _mapOffsetX, _mapOffsetY;
    private double _pdpiScaleX = 1.0, _pdpiScaleY = 1.0;
    private System.Windows.Controls.Border? _widgetMarker;
    private bool _markerDragging;
    private System.Windows.Point _markerDragStart;
    private double _markerDragOrigLeft, _markerDragOrigTop;
    private double _editPosX, _editPosY;

    public string WidgetPositionText => $"X: {(int)_editPosX}  Y: {(int)_editPosY}";

    // ── Constructor ──────────────────────────────────────────────────────────

    public SettingsWindow(WidgetSettings widget, WidgetWindow? livePreviewTarget = null)
    {
        _widget = widget;
        _livePreviewTarget = livePreviewTarget;

        InitializeComponent();
        Topmost = true;

        foreach (var d in PerfService.GetFixedDrives())
            Drives.Add(d);

        // Snapshot originals
        _origBgColor          = Fallback(widget.BackgroundColor, "#1E1E2E");
        _origChartBgColor     = Fallback(widget.ChartBackgroundColor, "#0D0D1A");
        _origCpuColor         = Fallback(widget.CpuColor, "#FF89B4FA");
        _origDiskColor        = Fallback(widget.DiskColor, "#FFF9A825");
        _origNetColor         = Fallback(widget.NetworkColor, "#FFA6E3A1");
        _origBgOpacityPercent = (int)Math.Round(widget.BackgroundOpacity * 100);
        if (_origBgOpacityPercent == 0) _origBgOpacityPercent = 80;
        _origFontScalePercent = widget.FontScalePercent > 0 ? widget.FontScalePercent : 100;
        _origUpdateInterval   = widget.UpdateInterval > 0 ? widget.UpdateInterval : 1000;
        _origDurationSeconds  = widget.DurationSeconds > 0 ? widget.DurationSeconds : 120;
        _origDiskQueueScale   = widget.DiskQueueScale > 0 ? widget.DiskQueueScale : 4.0;
        _origDrive            = string.IsNullOrEmpty(widget.DiskDrive) ? "C" : widget.DiskDrive;
        _origWidth            = widget.Width > 0 ? widget.Width : 340;
        _origHeight           = widget.Height > 0 ? widget.Height : 160;
        _origShowTitle        = widget.ShowTitle;
        _origShowLegend       = widget.ShowLegend;
        _origShowGrid         = widget.ShowGrid;
        _origShowTopProcess   = widget.ShowTopProcess;
        _origShowNetwork      = widget.ShowNetwork;
        _origPosX             = livePreviewTarget?.Left ?? widget.X;
        _origPosY             = livePreviewTarget?.Top  ?? widget.Y;
        _editPosX             = _origPosX;
        _editPosY             = _origPosY;

        // Load working copies
        _bgColorHex       = _origBgColor;
        _chartBgColorHex  = _origChartBgColor;
        _cpuColorHex      = _origCpuColor;
        _diskColorHex     = _origDiskColor;
        _netColorHex      = _origNetColor;
        _bgOpacityPercent = _origBgOpacityPercent;
        _fontScalePercent = _origFontScalePercent;
        _updateInterval   = _origUpdateInterval;
        _durationSeconds  = _origDurationSeconds;
        _diskQueueScale   = _origDiskQueueScale;
        _selectedDrive    = Drives.Contains(_origDrive) ? _origDrive : (Drives.FirstOrDefault() ?? "C");
        _widgetWidth      = (int)Math.Round(_origWidth);
        _widgetHeight     = (int)Math.Round(_origHeight);
        _embedInWallpaper = widget.EmbedInWallpaper;
        _autoStartEnabled = AutoStartService.IsEnabled();
        _showTitle        = widget.ShowTitle;
        _showLegend       = widget.ShowLegend;
        _showGrid         = widget.ShowGrid;
        _showTopProcess   = widget.ShowTopProcess;
        _showNetwork      = widget.ShowNetwork;

        // Fields were assigned after InitializeComponent() established the bindings,
        // so push every bound property to the UI now.
        foreach (var name in new[]
        {
            nameof(BgColorHex), nameof(BgColorBrush),
            nameof(ChartBgColorHex), nameof(ChartBgColorBrush),
            nameof(CpuColorHex), nameof(CpuColorBrush),
            nameof(DiskColorHex), nameof(DiskColorBrush),
            nameof(NetColorHex), nameof(NetColorBrush),
            nameof(BgOpacityPercent), nameof(FontScalePercent),
            nameof(UpdateInterval), nameof(DurationSeconds), nameof(DurationHint),
            nameof(DiskQueueScale), nameof(SelectedDrive),
            nameof(WidgetWidth), nameof(WidgetHeight),
            nameof(EmbedInWallpaper), nameof(AutoStartEnabled),
            nameof(ShowTitle), nameof(ShowLegend), nameof(ShowGrid), nameof(ShowTopProcess), nameof(ShowNetwork),
            nameof(WidgetPositionText),
        })
        {
            OnPropertyChanged(name);
        }
    }

    // ── Color pickers ─────────────────────────────────────────────────────────

    private void BgColorSwatch_Click(object sender, MouseButtonEventArgs e) => PickColor(_bgColorHex, hex => BgColorHex = hex);
    private void BgColorSwatch_Click(object sender, RoutedEventArgs e) => PickColor(_bgColorHex, hex => BgColorHex = hex);
    private void ChartBgColorSwatch_Click(object sender, MouseButtonEventArgs e) => PickColor(_chartBgColorHex, hex => ChartBgColorHex = hex);
    private void ChartBgColorSwatch_Click(object sender, RoutedEventArgs e) => PickColor(_chartBgColorHex, hex => ChartBgColorHex = hex);
    private void CpuColorSwatch_Click(object sender, MouseButtonEventArgs e) => PickColor(_cpuColorHex, hex => CpuColorHex = hex);
    private void CpuColorSwatch_Click(object sender, RoutedEventArgs e) => PickColor(_cpuColorHex, hex => CpuColorHex = hex);
    private void DiskColorSwatch_Click(object sender, MouseButtonEventArgs e) => PickColor(_diskColorHex, hex => DiskColorHex = hex);
    private void DiskColorSwatch_Click(object sender, RoutedEventArgs e) => PickColor(_diskColorHex, hex => DiskColorHex = hex);
    private void NetColorSwatch_Click(object sender, MouseButtonEventArgs e) => PickColor(_netColorHex, hex => NetColorHex = hex);
    private void NetColorSwatch_Click(object sender, RoutedEventArgs e) => PickColor(_netColorHex, hex => NetColorHex = hex);

    private void PickColor(string current, Action<string> apply)
    {
        var picker = new ColorPickerWindow(current) { Owner = this };
        if (picker.ShowDialog() == true)
        {
            var c = picker.SelectedColor;
            apply($"#FF{c.R:X2}{c.G:X2}{c.B:X2}");
        }
    }

    // ── Live preview ──────────────────────────────────────────────────────────

    private void LivePreviewBackground() => _livePreviewTarget?.ApplyBackground(_bgColorHex, _bgOpacityPercent / 100.0);
    private void LivePreviewChartColors() => _livePreviewTarget?.ApplyChartColors(_chartBgColorHex, _cpuColorHex, _diskColorHex, _netColorHex);
    private void LivePreviewVisibility() => _livePreviewTarget?.ApplyVisibility(_showTitle, _showLegend, _showGrid, _showTopProcess, _showNetwork);
    private void LivePreviewChartSettings() =>
        _livePreviewTarget?.ApplyChartSettings(_selectedDrive, Math.Max(1, _durationSeconds),
            _diskQueueScale > 0 ? _diskQueueScale : 1.0, Math.Max(100, _updateInterval));
    private void LivePreviewSize()
    {
        if (_widgetWidth >= 160 && _widgetHeight >= 80)
            _livePreviewTarget?.ApplySize(_widgetWidth, _widgetHeight);
    }

    // ── Screen-map position picker (mirrors the other widgets) ───────────────

    private void Window_Loaded(object sender, RoutedEventArgs e) => BuildScreenMap();

    private void BuildScreenMap()
    {
        var screens = System.Windows.Forms.Screen.AllScreens;
        int minX = screens.Min(s => s.Bounds.Left);
        int minY = screens.Min(s => s.Bounds.Top);
        int maxX = screens.Max(s => s.Bounds.Right);
        int maxY = screens.Max(s => s.Bounds.Bottom);
        _mapOffsetX = minX;
        _mapOffsetY = minY;

        double cW = ScreenMapCanvas.ActualWidth;
        double cH = ScreenMapCanvas.ActualHeight;
        if (cW <= 0 || cH <= 0) return;

        double vdW = maxX - minX;
        double vdH = maxY - minY;
        _mapScale = Math.Min(cW / vdW, cH / vdH);
        _mapLeft = (cW - vdW * _mapScale) / 2.0;
        _mapTop  = (cH - vdH * _mapScale) / 2.0;

        var source = PresentationSource.FromVisual(this);
        _pdpiScaleX = source?.CompositionTarget.TransformToDevice.M11 ?? 1.0;
        _pdpiScaleY = source?.CompositionTarget.TransformToDevice.M22 ?? 1.0;

        ScreenMapCanvas.Children.Clear();

        foreach (var screen in screens)
        {
            double left = _mapLeft + (screen.Bounds.Left - minX) * _mapScale;
            double top  = _mapTop  + (screen.Bounds.Top  - minY) * _mapScale;
            double w    = screen.Bounds.Width  * _mapScale;
            double h    = screen.Bounds.Height * _mapScale;

            var monitorRect = new System.Windows.Controls.Border
            {
                Width = w, Height = h,
                Background       = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x1E, 0x1E, 0x30)),
                BorderBrush      = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x45, 0x45, 0x70)),
                BorderThickness  = new Thickness(1),
                CornerRadius     = new CornerRadius(2),
                IsHitTestVisible = false
            };
            System.Windows.Controls.Canvas.SetLeft(monitorRect, left);
            System.Windows.Controls.Canvas.SetTop(monitorRect, top);
            ScreenMapCanvas.Children.Add(monitorRect);

            var lbl = new System.Windows.Controls.TextBlock
            {
                Text       = screen.Primary ? "Primary" : $"{screen.Bounds.Width}×{screen.Bounds.Height}",
                FontSize   = 9,
                Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x58, 0x5B, 0x70)),
                IsHitTestVisible = false
            };
            System.Windows.Controls.Canvas.SetLeft(lbl, left + 3);
            System.Windows.Controls.Canvas.SetTop(lbl,  top  + 2);
            ScreenMapCanvas.Children.Add(lbl);
        }

        double widgetWpx = (_livePreviewTarget?.ActualWidth  ?? _origWidth)  * _pdpiScaleX;
        double widgetHpx = (_livePreviewTarget?.ActualHeight ?? _origHeight) * _pdpiScaleY;
        double markerW   = Math.Max(widgetWpx * _mapScale, 14);
        double markerH   = Math.Max(widgetHpx * _mapScale, 8);

        double markerLeft = _mapLeft + (_editPosX * _pdpiScaleX - minX) * _mapScale;
        double markerTop  = _mapTop  + (_editPosY * _pdpiScaleY - minY) * _mapScale;

        _widgetMarker = new System.Windows.Controls.Border
        {
            Width = markerW, Height = markerH,
            Background      = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0xCC, 0x89, 0xB4, 0xFA)),
            BorderBrush     = System.Windows.Media.Brushes.White,
            BorderThickness = new Thickness(1),
            CornerRadius    = new CornerRadius(2),
            Cursor          = System.Windows.Input.Cursors.SizeAll,
            ToolTip         = "Drag to reposition the widget"
        };
        _widgetMarker.MouseLeftButtonDown += WidgetMarker_MouseLeftButtonDown;
        _widgetMarker.MouseMove           += WidgetMarker_MouseMove;
        _widgetMarker.MouseLeftButtonUp   += WidgetMarker_MouseLeftButtonUp;

        System.Windows.Controls.Canvas.SetLeft(_widgetMarker, markerLeft);
        System.Windows.Controls.Canvas.SetTop(_widgetMarker, markerTop);
        System.Windows.Controls.Panel.SetZIndex(_widgetMarker, 10);
        ScreenMapCanvas.Children.Add(_widgetMarker);
    }

    private void WidgetMarker_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _markerDragging     = true;
        _markerDragStart    = e.GetPosition(ScreenMapCanvas);
        _markerDragOrigLeft = System.Windows.Controls.Canvas.GetLeft(_widgetMarker!);
        _markerDragOrigTop  = System.Windows.Controls.Canvas.GetTop(_widgetMarker!);
        _widgetMarker!.CaptureMouse();
        e.Handled = true;
    }

    private void WidgetMarker_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_markerDragging || _widgetMarker == null) return;

        var pos     = e.GetPosition(ScreenMapCanvas);
        double newL = _markerDragOrigLeft + (pos.X - _markerDragStart.X);
        double newT = _markerDragOrigTop  + (pos.Y - _markerDragStart.Y);

        newL = Math.Max(0, Math.Min(newL, ScreenMapCanvas.ActualWidth  - _widgetMarker.Width));
        newT = Math.Max(0, Math.Min(newT, ScreenMapCanvas.ActualHeight - _widgetMarker.Height));

        System.Windows.Controls.Canvas.SetLeft(_widgetMarker, newL);
        System.Windows.Controls.Canvas.SetTop(_widgetMarker, newT);

        _editPosX = ((newL - _mapLeft) / _mapScale + _mapOffsetX) / _pdpiScaleX;
        _editPosY = ((newT - _mapTop)  / _mapScale + _mapOffsetY) / _pdpiScaleY;

        OnPropertyChanged(nameof(WidgetPositionText));

        if (_livePreviewTarget != null)
        {
            _livePreviewTarget.Left = _editPosX;
            _livePreviewTarget.Top  = _editPosY;
        }
        e.Handled = true;
    }

    private void WidgetMarker_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_markerDragging) return;
        _markerDragging = false;
        _widgetMarker?.ReleaseMouseCapture();
        e.Handled = true;
    }

    // ── Dialog buttons ───────────────────────────────────────────────────────

    /// <summary>Opens Explorer with network-max.json selected (or the data folder if it doesn't exist yet).</summary>
    private void OpenNetworkDataFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (System.IO.File.Exists(NetworkMaxStore.StorePath))
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe")
                {
                    Arguments = $"/select,\"{NetworkMaxStore.StorePath}\"",
                    UseShellExecute = true
                });
            }
            else
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(NetworkMaxStore.FolderPath)
                {
                    UseShellExecute = true
                });
            }
        }
        catch { /* ignore — opening Explorer is best-effort */ }
    }

    private void OK_Click(object sender, RoutedEventArgs e)
    {
        _widget.BackgroundColor      = _bgColorHex;
        _widget.BackgroundOpacity    = _bgOpacityPercent / 100.0;
        _widget.ChartBackgroundColor = _chartBgColorHex;
        _widget.CpuColor             = _cpuColorHex;
        _widget.DiskColor            = _diskColorHex;
        _widget.NetworkColor         = _netColorHex;
        _widget.FontScalePercent     = _fontScalePercent;
        _widget.UpdateInterval       = Math.Max(100, _updateInterval);
        _widget.DurationSeconds      = Math.Max(1, _durationSeconds);
        _widget.DiskQueueScale       = _diskQueueScale > 0 ? _diskQueueScale : 4.0;
        _widget.DiskDrive            = _selectedDrive;
        _widget.Width                = Math.Max(160, _widgetWidth);
        _widget.Height               = Math.Max(80, _widgetHeight);
        _widget.EmbedInWallpaper     = _embedInWallpaper;
        _widget.ShowTitle            = _showTitle;
        _widget.ShowLegend           = _showLegend;
        _widget.ShowGrid             = _showGrid;
        _widget.ShowTopProcess       = _showTopProcess;
        _widget.ShowNetwork          = _showNetwork;

        var config = DisplayService.GetCurrentDisplayConfiguration();
        DisplayService.SaveDisplayPosition(_widget, config, (int)_editPosX, (int)_editPosY);
        if (_livePreviewTarget != null)
        {
            _livePreviewTarget.Left = _editPosX;
            _livePreviewTarget.Top  = _editPosY;
        }

        AutoStartService.SetEnabled(_autoStartEnabled);
        App.Settings.AutoStart = _autoStartEnabled;

        SettingsService.Save(App.Settings);
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        // Restore the persistent settings object and the live widget
        _widget.ChartBackgroundColor = _origChartBgColor;
        _widget.CpuColor             = _origCpuColor;
        _widget.DiskColor            = _origDiskColor;
        _widget.NetworkColor         = _origNetColor;
        _widget.ShowTitle            = _origShowTitle;
        _widget.ShowLegend           = _origShowLegend;
        _widget.ShowGrid             = _origShowGrid;
        _widget.ShowTopProcess       = _origShowTopProcess;
        _widget.ShowNetwork          = _origShowNetwork;
        _widget.DiskDrive            = _origDrive;
        _widget.DurationSeconds      = _origDurationSeconds;
        _widget.DiskQueueScale       = _origDiskQueueScale;
        _widget.UpdateInterval       = _origUpdateInterval;

        _livePreviewTarget?.ApplyBackground(_origBgColor, _origBgOpacityPercent / 100.0);
        _livePreviewTarget?.ApplyChartColors(_origChartBgColor, _origCpuColor, _origDiskColor, _origNetColor);
        _livePreviewTarget?.ApplyFontScale(_origFontScalePercent);
        _livePreviewTarget?.ApplyVisibility(_origShowTitle, _origShowLegend, _origShowGrid, _origShowTopProcess, _origShowNetwork);
        _livePreviewTarget?.ApplyChartSettings(_origDrive, _origDurationSeconds,
            _origDiskQueueScale, _origUpdateInterval);
        _livePreviewTarget?.ApplySize(_origWidth, _origHeight);
        if (_livePreviewTarget != null)
        {
            _livePreviewTarget.Left = _origPosX;
            _livePreviewTarget.Top  = _origPosY;
        }

        DialogResult = false;
        Close();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static string Fallback(string value, string fallback) => string.IsNullOrEmpty(value) ? fallback : value;

    private static SolidColorBrush ToBrush(string hex)
    {
        try { return new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex)); }
        catch { return System.Windows.Media.Brushes.Black; }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
