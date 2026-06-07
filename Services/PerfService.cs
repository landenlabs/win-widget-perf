// Copyright (c) 2026 LanDen Labs - Dennis Lang
using System.Diagnostics;
using System.IO;

namespace WinWidgetPerf.Services;

/// <summary>
/// Collects live CPU load and per-drive disk queue length using Windows
/// performance counters. The CPU counter is shared; disk counters are created
/// per drive on demand and cached. All access is guarded — a missing or failing
/// counter simply yields 0 rather than throwing.
/// </summary>
public sealed class PerfService : IDisposable
{
    private readonly object _gate = new();
    private PerformanceCounter? _cpuCounter;
    private readonly Dictionary<string, PerformanceCounter> _diskCounters =
        new(StringComparer.OrdinalIgnoreCase);

    public PerfService()
    {
        try
        {
            _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
            _cpuCounter.NextValue(); // prime — first read is always 0
        }
        catch
        {
            _cpuCounter = null;
        }
    }

    /// <summary>Instantaneous total CPU load, clamped to 0..100.</summary>
    public double GetCpuLoad()
    {
        lock (_gate)
        {
            if (_cpuCounter == null) return 0;
            try { return Math.Clamp(_cpuCounter.NextValue(), 0, 100); }
            catch { return 0; }
        }
    }

    /// <summary>
    /// Average disk queue length for the given drive letter (e.g. "C").
    /// Returns 0 if the counter is unavailable.
    /// </summary>
    public double GetDiskQueue(string driveLetter)
    {
        string instance = NormalizeInstance(driveLetter);
        lock (_gate)
        {
            if (!_diskCounters.TryGetValue(instance, out var counter))
            {
                try
                {
                    counter = new PerformanceCounter("LogicalDisk", "Avg. Disk Queue Length", instance);
                    counter.NextValue(); // prime
                    _diskCounters[instance] = counter;
                }
                catch
                {
                    return 0;
                }
            }

            try { return Math.Max(0, counter.NextValue()); }
            catch { return 0; }
        }
    }

    /// <summary>Fixed-drive letters available on this machine, e.g. ["C","D"].</summary>
    public static List<string> GetFixedDrives()
    {
        var list = new List<string>();
        try
        {
            foreach (var d in DriveInfo.GetDrives())
            {
                if (d.DriveType == DriveType.Fixed && d.Name.Length >= 1)
                {
                    string letter = char.ToUpperInvariant(d.Name[0]).ToString();
                    if (!list.Contains(letter))
                        list.Add(letter);
                }
            }
        }
        catch { /* fall through to default */ }

        if (list.Count == 0) list.Add("C");
        return list;
    }

    // LogicalDisk instance names are the drive letter plus a colon, e.g. "C:".
    private static string NormalizeInstance(string driveLetter)
    {
        if (string.IsNullOrWhiteSpace(driveLetter)) return "C:";
        return $"{char.ToUpperInvariant(driveLetter[0])}:";
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _cpuCounter?.Dispose();
            _cpuCounter = null;
            foreach (var c in _diskCounters.Values) c.Dispose();
            _diskCounters.Clear();
        }
    }
}
