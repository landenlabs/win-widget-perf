// Copyright (c) 2026 LanDen Labs - Dennis Lang
using System.Windows;
using WinWidgetPerf.Models;
using WinWidgetPerf.Services;
using WinWidgetPerf.Windows;

namespace WinWidgetPerf;

public partial class App : System.Windows.Application
{
    private static Mutex? _mutex;
    private static bool _ownsMutex;
    private AboutWindow? _aboutWindow;
    private TrayIconService? _trayIcon;
    private readonly List<WidgetWindow> _widgetWindows = [];
    private readonly PerfService _perfService = new();
    private readonly ProcessCpuService _processCpuService = new();
    private readonly NetworkService _networkService = new();
    private readonly TemperatureService _temperatureService = new();

    public static AppSettings Settings { get; private set; } = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _mutex = new Mutex(true, "WinWidgetPerf_UniqueInstance_v1", out _ownsMutex);
        if (!_ownsMutex)
        {
            System.Windows.MessageBox.Show("WinWidgetPerf is already running.",
                "WinWidgetPerf", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        Settings = SettingsService.Load();

        // Ensure at least one widget exists
        if (Settings.Widgets.Count == 0)
        {
            Settings.Widgets.Add(new WidgetSettings());
        }

        foreach (var widget in Settings.Widgets)
            CreateAndShowWidget(widget);

        _trayIcon = new TrayIconService(
            onAddWidget:      AddWidget,
            getWidgets:       () => Settings.Widgets,
            onWidgetSettings: id => _widgetWindows.FirstOrDefault(w => w.WidgetId == id)?.OpenSettings(),
            onWidgetRemove:   RemoveWidget,
            onAbout:          () => OpenAbout(),
            onExit:           Shutdown,
            getCpuLoad:       () => _perfService.GetCpuLoad()
        );
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _trayIcon?.Dispose();
        _perfService.Dispose();
        _processCpuService.Dispose();
        _networkService.Dispose();
        _temperatureService.Dispose();
        // Only the instance that acquired the mutex may release it (ReleaseMutex throws otherwise).
        if (_ownsMutex)
            _mutex?.ReleaseMutex();
        _mutex?.Dispose();
        base.OnExit(e);
    }

    private void CreateAndShowWidget(WidgetSettings settings)
    {
        var window = new WidgetWindow(settings, _perfService, _processCpuService, _networkService, _temperatureService);
        _widgetWindows.Add(window);
        window.Show();
    }

    public void AddWidget()
    {
        var newWidget = new WidgetSettings
        {
            X = 100 + (_widgetWindows.Count * 20),
            Y = 100 + (_widgetWindows.Count * 20)
        };
        Settings.Widgets.Add(newWidget);
        SettingsService.Save(Settings);
        CreateAndShowWidget(newWidget);
        _trayIcon?.RebuildMenu();
    }

    public void RemoveWidget(string widgetId)
    {
        if (Settings.Widgets.Count <= 1) return;   // always keep at least one widget

        var window = _widgetWindows.FirstOrDefault(w => w.WidgetId == widgetId);
        if (window != null)
        {
            _widgetWindows.Remove(window);
            window.Close();
        }

        var settings = Settings.Widgets.FirstOrDefault(w => w.Id == widgetId);
        if (settings != null)
        {
            Settings.Widgets.Remove(settings);
            SettingsService.Save(Settings);
        }
        _trayIcon?.RebuildMenu();
    }

    /// <summary>Shows the About dialog, or activates it if it's already open.</summary>
    public void OpenAbout(Window? owner = null)
    {
        if (_aboutWindow != null)
        {
            _aboutWindow.Activate();
            return;
        }
        try
        {
            _aboutWindow = new AboutWindow();
            if (owner != null) _aboutWindow.Owner = owner;
            _aboutWindow.ShowDialog();
        }
        finally
        {
            _aboutWindow = null;
        }
    }
}
