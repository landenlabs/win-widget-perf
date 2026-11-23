// Copyright (c) 2026 LanDen Labs - Dennis Lang
using System.IO;
using System.Text.Json;
using WinWidgetPerf.Models;

namespace WinWidgetPerf.Services;

public static class SettingsService
{
    private static readonly string AppDataPath = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "WinWidgetPerf"
    );

    private static readonly string SettingsFile = System.IO.Path.Combine(AppDataPath, "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static AppSettings Load()
    {
        AppSettings settings;
        try
        {
            settings = File.Exists(SettingsFile)
                ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsFile)) ?? new AppSettings()
                : new AppSettings();
        }
        catch
        {
            // Corrupt or unreadable file: start from defaults rather than failing to launch.
            settings = new AppSettings();
        }

        foreach (var w in settings.Widgets)
            DisplayService.PruneStalePositions(w);
        return settings;
    }

    /// <summary>
    /// Writes to a temp file then swaps it in, so a crash or power loss mid-write can't leave a
    /// truncated settings.json behind. Failures are swallowed (saving is called on every drag/resize).
    /// </summary>
    public static void Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(AppDataPath);
            var tmp = SettingsFile + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(settings, JsonOptions));
            File.Move(tmp, SettingsFile, overwrite: true);
        }
        catch
        {
            // Best effort.
        }
    }
}
