// Copyright (c) 2026 LanDen Labs - Dennis Lang
namespace WinWidgetPerf.Models;

public class AppSettings
{
    public List<WidgetSettings> Widgets { get; set; } = [];
    public bool AutoStart { get; set; } = false;
}

public class WidgetSettings
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public int X { get; set; } = 100;
    public int Y { get; set; } = 100;

    /// <summary>Widget size in device-independent pixels (resizable via the corner grip).</summary>
    public double Width { get; set; } = 340;
    public double Height { get; set; } = 160;

    /// <summary>Milliseconds between performance samples.</summary>
    public int UpdateInterval { get; set; } = 1000;

    /// <summary>Total span of data shown across the chart, in seconds (default 2 minutes).</summary>
    public int DurationSeconds { get; set; } = 120;

    /// <summary>Drive letter whose disk queue is charted (e.g. "C").</summary>
    public string DiskDrive { get; set; } = "C";

    /// <summary>Disk queue length that maps to the full chart height.</summary>
    public double DiskQueueScale { get; set; } = 4.0;

    public bool ShowTitle { get; set; } = true;
    public bool ShowLegend { get; set; } = true;
    public bool ShowGrid { get; set; } = true;

    /// <summary>Show the network-load series (learned peak-relative byte rate).</summary>
    public bool ShowNetwork { get; set; } = true;

    /// <summary>Show the busiest process (rolling CPU average) in the title bar.</summary>
    public bool ShowTopProcess { get; set; } = true;

    /// <summary>Show a row of drive-letter buttons below the chart for quick drive switching.</summary>
    public bool ShowDriveSelector { get; set; } = false;

    /// <summary>Show a narrow right-side bar indicating percent disk space used on the tracked drive.</summary>
    public bool ShowDiskSpaceBar { get; set; } = false;

    public bool EmbedInWallpaper { get; set; } = true;

    public string BackgroundColor { get; set; } = "#1E1E2E";
    public double BackgroundOpacity { get; set; } = 0.80;
    public int FontScalePercent { get; set; } = 100;

    public string ChartBackgroundColor { get; set; } = "#0D0D1A";
    public string CpuColor { get; set; } = "#FF89B4FA";   // blue
    public string DiskColor { get; set; } = "#FFF9A825";  // amber
    public string NetworkColor { get; set; } = "#FFA6E3A1";  // green

    /// <summary>
    /// Stores positions for different display configurations.
    /// Key: ConfigurationHash, Value: DisplayPosition
    /// </summary>
    public Dictionary<string, DisplayPosition> DisplayPositions { get; set; } = [];

    /// <summary>
    /// Hash of the last known display configuration.
    /// Used to determine if display setup has changed.
    /// </summary>
    public string LastDisplayConfigurationHash { get; set; } = string.Empty;
}
