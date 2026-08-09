// Copyright (c) 2026 LanDen Labs - Dennis Lang
namespace WinWidgetPerf.Models;

/// <summary>
/// A single point in time on the stripchart: total CPU load, the
/// disk active-time percentage for the monitored drive, and network load.
/// </summary>
public readonly struct PerfSample
{
    /// <summary>Total CPU load, 0..100.</summary>
    public double Cpu { get; init; }

    /// <summary>Disk active-time percentage, 0..100.</summary>
    public double DiskActive { get; init; }

    /// <summary>Network load as a percent of the learned per-connection maximum, 0..100.</summary>
    public double Network { get; init; }
}
