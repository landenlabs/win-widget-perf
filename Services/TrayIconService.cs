// Copyright (c) 2026 LanDen Labs - Dennis Lang
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using WinWidgetPerf.Models;

namespace WinWidgetPerf.Services;

public class TrayIconService : IDisposable
{
    private NotifyIcon? _notifyIcon;
    private readonly Action _onAddWidget;
    private readonly Func<List<WidgetSettings>> _getWidgets;
    private readonly Action<string> _onWidgetSettings;
    private readonly Action<string> _onWidgetRemove;
    private readonly Action _onAbout;
    private readonly Action _onExit;
    private readonly Func<double> _getCpuLoad;
    private System.Windows.Threading.DispatcherTimer? _tooltipTimer;

    public TrayIconService(
        Action onAddWidget,
        Func<List<WidgetSettings>> getWidgets,
        Action<string> onWidgetSettings,
        Action<string> onWidgetRemove,
        Action onAbout,
        Action onExit,
        Func<double> getCpuLoad)
    {
        _onAddWidget      = onAddWidget;
        _getWidgets       = getWidgets;
        _onWidgetSettings = onWidgetSettings;
        _onWidgetRemove   = onWidgetRemove;
        _onAbout          = onAbout;
        _onExit           = onExit;
        _getCpuLoad       = getCpuLoad;

        InitializeTrayIcon();
        InitializeTooltipTimer();
    }

    private static Icon CreatePerfIcon()
    {
        try
        {
            using var bmp = new Bitmap(16, 16, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using var g = Graphics.FromImage(bmp);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(System.Drawing.Color.Transparent);

            // Axes
            using var axisPen = new Pen(System.Drawing.Color.FromArgb(150, 150, 150), 1f);
            g.DrawLine(axisPen, 2, 2, 2, 14);   // y-axis
            g.DrawLine(axisPen, 2, 14, 15, 14);  // x-axis

            // CPU trace (blue)
            using var cpuPen = new Pen(System.Drawing.Color.FromArgb(137, 180, 250), 1.4f);
            g.DrawLines(cpuPen, new[]
            {
                new PointF(3, 11), new PointF(6, 6), new PointF(9, 9), new PointF(12, 4), new PointF(15, 7)
            });

            // Disk trace (amber)
            using var diskPen = new Pen(System.Drawing.Color.FromArgb(249, 168, 37), 1.4f);
            g.DrawLines(diskPen, new[]
            {
                new PointF(3, 13), new PointF(6, 11), new PointF(9, 12), new PointF(12, 9), new PointF(15, 11)
            });

            return Icon.FromHandle(bmp.GetHicon());
        }
        catch
        {
            return SystemIcons.Application;
        }
    }

    private void InitializeTrayIcon()
    {
        _notifyIcon = new NotifyIcon
        {
            Icon    = CreatePerfIcon(),
            Visible = true,
            Text    = "Performance Widget"
        };
        _notifyIcon.DoubleClick += (_, _) => Invoke(() =>
            _onWidgetSettings(_getWidgets().FirstOrDefault()?.Id ?? ""));
        BuildMenu();
    }

    private void InitializeTooltipTimer()
    {
        _tooltipTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(3)
        };
        _tooltipTimer.Tick += (_, _) => UpdateTooltip();
        _tooltipTimer.Start();
    }

    private void UpdateTooltip()
    {
        if (_notifyIcon == null) return;
        try
        {
            double cpu = _getCpuLoad();
            _notifyIcon.Text = $"Performance Widget — CPU {cpu:0}%";
        }
        catch { /* leave previous text */ }
    }

    public void RebuildMenu() => BuildMenu();

    private void BuildMenu()
    {
        if (_notifyIcon == null) return;

        var menu = new ContextMenuStrip();

        var widgets = _getWidgets();
        bool canRemove = widgets.Count > 1;

        for (int i = 0; i < widgets.Count; i++)
        {
            string id    = widgets[i].Id;
            string label = widgets.Count == 1 ? "Performance Widget" : $"Performance Widget {i + 1}";

            var sub = new ToolStripMenuItem(label);
            sub.DropDownItems.Add("Settings", null, (_, _) => Invoke(() => _onWidgetSettings(id)));

            var removeItem = new ToolStripMenuItem("Remove Widget", null,
                (_, _) => Invoke(() => _onWidgetRemove(id)));
            removeItem.Enabled = canRemove;
            sub.DropDownItems.Add(removeItem);

            menu.Items.Add(sub);
        }

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("+ Add Widget", null, (_, _) => Invoke(_onAddWidget));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("About", null, (_, _) => Invoke(_onAbout));
        menu.Items.Add("Exit",  null, (_, _) => Invoke(_onExit));

        _notifyIcon.ContextMenuStrip = menu;
    }

    private static void Invoke(Action action)
    {
        if (System.Windows.Application.Current?.Dispatcher.CheckAccess() == true)
            action();
        else
            System.Windows.Application.Current?.Dispatcher.Invoke(action);
    }

    public void Dispose()
    {
        _tooltipTimer?.Stop();
        _notifyIcon?.Dispose();
    }
}
