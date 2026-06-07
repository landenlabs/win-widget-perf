// Copyright (c) 2026 LanDen Labs - Dennis Lang
namespace WinWidgetPerf.Models;

/// <summary>
/// A single point in time on the stripchart: total CPU load and the
/// average disk queue length for the monitored drive.
/// </summary>
public readonly struct PerfSample
{
    /// <summary>Total CPU load, 0..100.</summary>
    public double Cpu { get; init; }

    /// <summary>Average disk queue length (raw, unscaled).</summary>
    public double DiskQueue { get; init; }
}
