// Copyright (c) 2026 LanDen Labs - Dennis Lang
namespace WinWidgetPerf.Models;

/// <summary>
/// Represents a unique display configuration identified by monitor layout
/// </summary>
public class DisplayConfiguration
{
    /// <summary>
    /// Unique hash of the display configuration (based on monitors count, resolution, DPI)
    /// </summary>
    public string ConfigurationHash { get; set; } = string.Empty;

    /// <summary>
    /// Number of monitors in this configuration
    /// </summary>
    public int MonitorCount { get; set; }

    /// <summary>
    /// Total virtual screen width (all monitors combined)
    /// </summary>
    public int TotalWidth { get; set; }

    /// <summary>
    /// Total virtual screen height (all monitors combined)
    /// </summary>
    public int TotalHeight { get; set; }

    /// <summary>
    /// Description of the configuration (e.g., "1 Monitor: 1920x1080", "2 Monitors: 3840x2160")
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Timestamp when this configuration was last active
    /// </summary>
    public DateTime LastUsed { get; set; }

    public override bool Equals(object? obj)
    {
        return obj is DisplayConfiguration config && config.ConfigurationHash == ConfigurationHash;
    }

    public override int GetHashCode()
    {
        return ConfigurationHash.GetHashCode();
    }

    public override string ToString()
    {
        return $"{Description} [Hash: {ConfigurationHash}]";
    }
}

/// <summary>
/// Stores widget position for a specific display configuration
/// </summary>
public class DisplayPosition
{
    /// <summary>
    /// Configuration hash that this position applies to
    /// </summary>
    public string ConfigurationHash { get; set; } = string.Empty;

    /// <summary>
    /// X coordinate on this display
    /// </summary>
    public int X { get; set; }

    /// <summary>
    /// Y coordinate on this display
    /// </summary>
    public int Y { get; set; }

    /// <summary>
    /// When this position was last set
    /// </summary>
    public DateTime LastSet { get; set; }
}
