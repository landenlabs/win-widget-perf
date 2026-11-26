// Copyright (c) 2026 LanDen Labs - Dennis Lang
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Navigation;

namespace WinWidgetPerf.Windows;

public partial class AboutWindow : Window
{
    private bool _closing;

    public AboutWindow()
    {
        InitializeComponent();
        Topmost = true;

        VersionText.Text = $"v{AppVersion()}  ·  CPU, disk, network & memory desktop widget";

        // "(update available)" is hidden until the Store confirms a newer package exists.
        StoreLine.Inlines.Remove(UpdateAvailableLink);
        Loaded += async (_, _) =>
        {
            if (await IsStoreUpdateAvailableAsync() && !_closing)
                StoreLine.Inlines.Add(UpdateAvailableLink);
        };

        var mp4 = Path.Combine(AppContext.BaseDirectory, "Assets", "landenlabs.mp4");
        if (File.Exists(mp4))
        {
            LogoPlayer.MediaEnded += (_, _) =>
            {
                if (_closing) return;
                try { LogoPlayer.Position = TimeSpan.Zero; LogoPlayer.Play(); }
                catch { }
            };
            LogoPlayer.Visibility = System.Windows.Visibility.Visible;
            Loaded += (_, _) =>
            {
                if (_closing) return;
                try { LogoPlayer.Source = new Uri(mp4); LogoPlayer.Play(); }
                catch { }
            };
        }
        else
        {
            var png = Path.Combine(AppContext.BaseDirectory, "Assets", "landenlabs.png");
            if (File.Exists(png))
            {
                LogoFallback.Source = new System.Windows.Media.Imaging.BitmapImage(new Uri(png));
                LogoFallback.Visibility = System.Windows.Visibility.Visible;
            }
        }
    }

    /// <summary>
    /// Three-part version (major.minor.patch). When installed as an MSIX (Store or sideload) the
    /// package version is authoritative — it's what the Store and "Apps &amp; features" show.
    /// </summary>
    internal static string AppVersion()
    {
        if (Services.AutoStartService.IsPackaged)
        {
            try
            {
                var v = global::Windows.ApplicationModel.Package.Current.Id.Version;
                return $"{v.Major}.{v.Minor}.{v.Build}";
            }
            catch { /* fall back to the assembly version */ }
        }
        var a = Assembly.GetExecutingAssembly().GetName().Version;
        return a is null ? "?" : $"{a.Major}.{a.Minor}.{a.Build}";
    }

    /// <summary>
    /// Asks the Microsoft Store whether a newer version of this app is published. Only meaningful
    /// for Store installs; the portable exe, sideloaded MSIX, or no network all report false.
    /// </summary>
    private static async Task<bool> IsStoreUpdateAvailableAsync()
    {
        if (!Services.AutoStartService.IsPackaged) return false;
        try
        {
            var ctx = global::Windows.Services.Store.StoreContext.GetDefault();
            var updates = await ctx.GetAppAndOptionalStorePackageUpdatesAsync();
            return updates.Count > 0;
        }
        catch { return false; }
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        _closing = true;
        try { LogoPlayer.Stop(); } catch { }
        LogoPlayer.Source = null;
        base.OnClosing(e);
    }

    private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
