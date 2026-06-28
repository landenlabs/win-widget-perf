// Copyright (c) 2026 LanDen Labs - Dennis Lang

using System.Diagnostics;

namespace WinWidgetPerf.Services;

/// <summary>
/// Tracks per-process CPU usage over a rolling window and reports the busiest
/// processes as their average share of total CPU capacity.
///
/// Each process's cumulative <see cref="Process.TotalProcessorTime"/> is sampled
/// on a background thread; the delta between samples is the CPU-time that process
/// consumed in that interval. Deltas are aggregated by process name (so all
/// "chrome" children sum together) and retained in a time-stamped window.
///
/// Running non-elevated, reading <see cref="Process.TotalProcessorTime"/> throws
/// for some protected/system processes — those are simply skipped, so a few system
/// processes may go unattributed.
/// </summary>
public sealed class ProcessCpuService : IDisposable {
    private sealed record Snapshot(DateTime Time, Dictionary<string, double> CpuSeconds);

    private readonly object _gate = new();
    private Dictionary<int, TimeSpan> _prevTotals = new();         // FIX: non-readonly for O(1) swap
    private readonly Queue<Snapshot> _window = new();
    private readonly int _coreCount = Math.Max(1, Environment.ProcessorCount);
    private System.Threading.Timer? _timer;
    private readonly int _sampleMs;                                 // FIX: configurable interval

    private const int MaxWindowSeconds = 600;
    private const int ProcessEnumEvery = 3;                        // FIX: only enumerate processes every Nth tick
    private int _sampleSkip = 0;

    public ProcessCpuService(int sampleMs = 1000) {
        _sampleMs = Math.Max(1000, sampleMs);                      // FIX: never faster than 1s
        SafeSample(); // prime previous totals
        _timer = new System.Threading.Timer(_ => SafeSample(), null, _sampleMs, _sampleMs);
    }

    private void SafeSample() {
        try { Sample(); } catch { /* never let the timer thread die */ }
    }

    private void Sample() {
        var now = DateTime.UtcNow;

        // FIX: skip process enumeration on most ticks — rolling average
        // display is unaffected since it averages over many seconds anyway.
        if (++_sampleSkip < ProcessEnumEvery) {
            return;
        }
        _sampleSkip = 0;

        var current = new Dictionary<int, TimeSpan>();
        var perName = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

        foreach (var p in Process.GetProcesses()) {
            try {
                int pid = p.Id;
                if (pid == 0) continue; // System Idle Process

                TimeSpan total = p.TotalProcessorTime;
                current[pid] = total;

                if (_prevTotals.TryGetValue(pid, out var prev)) {
                    double sec = (total - prev).TotalSeconds;
                    if (sec > 0) {
                        string name = string.IsNullOrEmpty(p.ProcessName) ? $"pid {pid}" : p.ProcessName;
                        perName.TryGetValue(name, out var acc);
                        perName[name] = acc + sec;
                    }
                }
            } catch { /* access denied, or process exited mid-read */ } finally { p.Dispose(); }
        }

        lock (_gate) {
            _prevTotals = current;                                  // FIX: O(1) swap instead of Clear()+fill

            _window.Enqueue(new Snapshot(now, perName));
            var cutoff = now - TimeSpan.FromSeconds(MaxWindowSeconds);
            while (_window.Count > 0 && _window.Peek().Time < cutoff)
                _window.Dequeue();
        }
    }

    /// <summary>
    /// Returns up to <paramref name="count"/> processes ranked by their average
    /// percentage of total CPU capacity over the most recent
    /// <paramref name="windowSeconds"/> seconds. Empty until the window has warmed up.
    /// </summary>
    public List<(string Name, double Percent)> GetTopProcesses(int windowSeconds, int count) {
        windowSeconds = Math.Clamp(windowSeconds, 1, MaxWindowSeconds);
        var cutoff = DateTime.UtcNow - TimeSpan.FromSeconds(windowSeconds);

        var agg = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        DateTime earliest = DateTime.UtcNow;
        bool any = false;

        lock (_gate) {
            foreach (var snap in _window) {
                if (snap.Time < cutoff) continue;
                if (!any) { earliest = snap.Time; any = true; }
                foreach (var kv in snap.CpuSeconds) {
                    agg.TryGetValue(kv.Key, out var acc);
                    agg[kv.Key] = acc + kv.Value;
                }
            }
        }

        if (!any) return [];

        // Average over the actual covered span × core count = share of total capacity.
        double spanSec = Math.Max(1.0, (DateTime.UtcNow - earliest).TotalSeconds);
        double denom = spanSec * _coreCount;

        return agg
            .Select(kv => (Name: kv.Key, Percent: Math.Min(100.0, kv.Value / denom * 100.0)))
            .OrderByDescending(t => t.Percent)
            .Take(Math.Max(1, count))
            .ToList();
    }

    public void Dispose() {
        _timer?.Dispose();
        _timer = null;
    }
}