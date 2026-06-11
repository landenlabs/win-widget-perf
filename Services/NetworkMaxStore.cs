// Copyright (c) 2026 LanDen Labs - Dennis Lang
using System.IO;
using System.Text.Json;

namespace WinWidgetPerf.Services;

/// <summary>
/// One learned network connection: the maximum sustained byte rate ever observed
/// on it. The rate is the larger of transmit/receive (bytes/sec) and is what the
/// network gauge measures "percent busy" against.
/// </summary>
public sealed class ConnectionStat
{
    /// <summary>Highest learned byte rate for this connection (bytes/sec).</summary>
    public double MaxBytesPerSec { get; set; }

    /// <summary>Last-seen NIC link speed (bytes/sec); used as a sanity ceiling.</summary>
    public double LinkBytesPerSec { get; set; }

    /// <summary>Human-readable description (e.g. "wifi · Home") for diagnostics.</summary>
    public string Label { get; set; } = string.Empty;
}

/// <summary>
/// Persists the learned per-connection maximum byte rates to a small JSON file
/// alongside the app settings. Keyed by a connection signature (type + router id).
/// Mirrors <see cref="SettingsService"/>: AppData folder, System.Text.Json, silent-fail.
/// </summary>
public static class NetworkMaxStore
{
    private static readonly string AppDataPath = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "WinWidgetPerf"
    );

    private static readonly string StoreFile = System.IO.Path.Combine(AppDataPath, "network-max.json");

    static NetworkMaxStore()
    {
        if (!Directory.Exists(AppDataPath))
            Directory.CreateDirectory(AppDataPath);
    }

    public static Dictionary<string, ConnectionStat> Load()
    {
        try
        {
            if (File.Exists(StoreFile))
            {
                var json = File.ReadAllText(StoreFile);
                return JsonSerializer.Deserialize<Dictionary<string, ConnectionStat>>(json)
                       ?? new Dictionary<string, ConnectionStat>();
            }
        }
        catch
        {
            // Silently fail and return an empty store
        }

        return new Dictionary<string, ConnectionStat>();
    }

    public static void Save(Dictionary<string, ConnectionStat> store)
    {
        try
        {
            var json = JsonSerializer.Serialize(store, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(StoreFile, json);
        }
        catch
        {
            // Silently fail
        }
    }
}
