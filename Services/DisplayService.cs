// Copyright (c) 2026 LanDen Labs - Dennis Lang
using System.Collections.ObjectModel;
using System.Windows;
using WinWidgetPerf.Models;

namespace WinWidgetPerf.Services;

/// <summary>
/// Service for managing display/monitor configurations and positions
/// </summary>
public class DisplayService
{
    /// <summary>
    /// Gets the current display configuration
    /// </summary>
    public static DisplayConfiguration GetCurrentDisplayConfiguration()
    {
        var screens = GetAllScreens();

        int monitorCount = screens.Count;
        int totalWidth = 0;
        int totalHeight = 0;

        // Calculate total virtual screen dimensions
        int minX = 0, minY = 0, maxX = 0, maxY = 0;

        foreach (var screen in screens)
        {
            minX = Math.Min(minX, screen.Bounds.X);
            minY = Math.Min(minY, screen.Bounds.Y);
            maxX = Math.Max(maxX, screen.Bounds.X + screen.Bounds.Width);
            maxY = Math.Max(maxY, screen.Bounds.Y + screen.Bounds.Height);
        }

        totalWidth = maxX - minX;
        totalHeight = maxY - minY;

        // Create a unique hash based on monitor configuration
        var hash = GenerateConfigurationHash(screens);

        var description = monitorCount == 1
            ? $"1 Monitor: {screens[0].Bounds.Width}x{screens[0].Bounds.Height}"
            : $"{monitorCount} Monitors: {totalWidth}x{totalHeight}";

        return new DisplayConfiguration
        {
            ConfigurationHash = hash,
            MonitorCount = monitorCount,
            TotalWidth = totalWidth,
            TotalHeight = totalHeight,
            Description = description,
            LastUsed = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Gets a list of all available screens
    /// </summary>
    private static List<DisplayInfo> GetAllScreens()
    {
        var screens = new List<DisplayInfo>();

        try
        {
            var allScreens = System.Windows.Forms.Screen.AllScreens;

            foreach (var screen in allScreens)
            {
                screens.Add(new DisplayInfo
                {
                    Bounds = new System.Drawing.Rectangle(
                        screen.Bounds.X,
                        screen.Bounds.Y,
                        screen.Bounds.Width,
                        screen.Bounds.Height),
                    WorkingArea = new System.Drawing.Rectangle(
                        screen.WorkingArea.X,
                        screen.WorkingArea.Y,
                        screen.WorkingArea.Width,
                        screen.WorkingArea.Height),
                    IsPrimary = screen.Primary,
                    DeviceName = screen.DeviceName
                });
            }
        }
        catch
        {
            // Fallback if screen enumeration fails
            screens.Add(new DisplayInfo
            {
                Bounds = new System.Drawing.Rectangle(0, 0, 1920, 1080),
                WorkingArea = new System.Drawing.Rectangle(0, 0, 1920, 1080),
                IsPrimary = true,
                DeviceName = "Primary Display"
            });
        }

        return screens;
    }

    /// <summary>
    /// Generates a unique hash for the display configuration
    /// </summary>
    private static string GenerateConfigurationHash(List<DisplayInfo> screens)
    {
        if (screens.Count == 0)
            return "DEFAULT";

        // Sort screens by position to ensure consistent hashing
        var sortedScreens = screens.OrderBy(s => s.Bounds.X).ThenBy(s => s.Bounds.Y).ToList();

        var hashParts = new List<string>();

        foreach (var screen in sortedScreens)
        {
            // Include monitor position, size, and primary flag
            hashParts.Add($"{screen.Bounds.X}_{screen.Bounds.Y}_{screen.Bounds.Width}x{screen.Bounds.Height}_{screen.IsPrimary}");
        }

        var combined = string.Join("|", hashParts);

        // Use simple hash to make it readable
        int hash = combined.GetHashCode();
        return $"DISP_{Math.Abs(hash):X8}";
    }

    /// <summary>
    /// Gets the position for a widget on the current display configuration
    /// Falls back to default position if no position saved for this configuration
    /// </summary>
    public static (int X, int Y) GetDisplayPosition(WidgetSettings settings, DisplayConfiguration currentConfig)
    {
        // Check if we have a saved position for this display configuration
        if (settings.DisplayPositions.TryGetValue(currentConfig.ConfigurationHash, out var position))
        {
            return (position.X, position.Y);
        }

        // Fallback to stored X, Y (for backward compatibility)
        return (settings.X, settings.Y);
    }

    /// <summary>
    /// Saves the widget position for the current display configuration
    /// </summary>
    public static void SaveDisplayPosition(WidgetSettings settings, DisplayConfiguration currentConfig, int x, int y)
    {
        var displayPosition = new DisplayPosition
        {
            ConfigurationHash = currentConfig.ConfigurationHash,
            X = x,
            Y = y,
            LastSet = DateTime.UtcNow
        };

        settings.DisplayPositions[currentConfig.ConfigurationHash] = displayPosition;
        settings.LastDisplayConfigurationHash = currentConfig.ConfigurationHash;

        // Also update the default X, Y for backward compatibility
        settings.X = x;
        settings.Y = y;
    }

    /// <summary>
    /// Gets the primary screen bounds
    /// </summary>
    public static System.Drawing.Rectangle GetPrimaryScreenBounds()
    {
        try
        {
            var primaryScreen = System.Windows.Forms.Screen.PrimaryScreen;
            return primaryScreen.Bounds;
        }
        catch
        {
            return new System.Drawing.Rectangle(0, 0, 1920, 1080);
        }
    }

    /// <summary>
    /// Validates that a position is within reasonable bounds of the display configuration
    /// </summary>
    public static bool IsPositionValidForDisplay(int x, int y, DisplayConfiguration config)
    {
        // Allow some off-screen positioning (up to 100 pixels)
        const int buffer = 100;

        return x >= -buffer && y >= -buffer &&
               x <= config.TotalWidth + buffer &&
               y <= config.TotalHeight + buffer;
    }

    /// <summary>
    /// Gets a list of all known display configurations from settings
    /// </summary>
    public static List<DisplayConfiguration> GetKnownDisplayConfigurations(List<WidgetSettings> widgets)
    {
        var configurations = new Dictionary<string, DisplayConfiguration>();

        foreach (var widget in widgets)
        {
            foreach (var positionEntry in widget.DisplayPositions)
            {
                if (!configurations.ContainsKey(positionEntry.Key))
                {
                    configurations[positionEntry.Key] = new DisplayConfiguration
                    {
                        ConfigurationHash = positionEntry.Key,
                        MonitorCount = 0, // Unknown
                        Description = $"Saved configuration: {positionEntry.Key}"
                    };
                }
            }
        }

        return configurations.Values.ToList();
    }
}

/// <summary>
/// Internal class to represent display information
/// </summary>
internal class DisplayInfo
{
    public System.Drawing.Rectangle Bounds { get; set; }
    public System.Drawing.Rectangle WorkingArea { get; set; }
    public bool IsPrimary { get; set; }
    public string DeviceName { get; set; } = string.Empty;
}
