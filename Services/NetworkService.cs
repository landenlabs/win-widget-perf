// Copyright (c) 2026 LanDen Labs - Dennis Lang
using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;

namespace WinWidgetPerf.Services;

/// <summary>
/// Reports network load as a percent of a personalized maximum.
///
/// Once a second on a background thread, the active connection's cumulative byte
/// counters are read; the delta over the measured interval gives transmit and
/// receive rates, and the larger of the two is the instantaneous load (bytes/sec).
/// That rate is smoothed and compared against a <b>learned maximum</b> for the
/// current connection — so the gauge reads "how busy is this link relative to the
/// most it has ever sustained here." The learned maxima are stored per connection
/// (type + router) in <see cref="NetworkMaxStore"/> and persist across runs.
///
/// The active connection is re-evaluated each sample, so switching between wifi and
/// wired (or between networks) automatically re-targets the gauge and its learned max.
/// All access is guarded and failures yield 0 rather than throwing.
/// </summary>
public sealed class NetworkService : IDisposable
{
    private readonly object _gate = new();
    private System.Threading.Timer? _timer;

    // Per-interface cumulative counters from the previous tick (keyed by NIC Id).
    private readonly Dictionary<string, (long Rx, long Tx)> _prev = new();
    private DateTime _lastSampleUtc = DateTime.UtcNow;

    // Learned per-connection maxima (shared, persisted).
    private readonly Dictionary<string, ConnectionStat> _store;
    private string _activeKey = string.Empty;
    private double _smoothed;        // EMA of the raw byte rate, bytes/sec
    private double _currentPercent;  // latest load, 0..100
    private double _currentRate;     // latest raw rate, bytes/sec (for tooltip)
    private double _currentMax;      // effective max used for the percent (for tooltip)
    private string _currentLabel = string.Empty;

    private bool _dirty;
    private DateTime _lastSaveUtc = DateTime.UtcNow;

    private const int SampleMs = 1000;
    private const double Alpha = 0.4;              // EMA smoothing factor
    private const double DecayPerTick = 0.9995;    // slow relaxation of the learned max
    private const double FloorBytesPerSec = 125_000;  // 1 Mbit/s — keeps early percentages sane
    private const int SaveEverySeconds = 15;

    public NetworkService()
    {
        _store = NetworkMaxStore.Load();
        SafeSample(); // prime previous counters
        _timer = new System.Threading.Timer(_ => SafeSample(), null, SampleMs, SampleMs);
    }

    /// <summary>Latest network load as a percent of the learned maximum, 0..100.</summary>
    public double GetLoadPercent()
    {
        lock (_gate) { return _currentPercent; }
    }

    /// <summary>Latest raw rate (bytes/sec), the effective max, and a connection label.</summary>
    public (double RateBytesPerSec, double MaxBytesPerSec, string Label) GetDetail()
    {
        lock (_gate) { return (_currentRate, _currentMax, _currentLabel); }
    }

    private void SafeSample()
    {
        try { Sample(); }
        catch { /* never let the timer thread die */ }
    }

    private void Sample()
    {
        var now = DateTime.UtcNow;
        double elapsed = (now - _lastSampleUtc).TotalSeconds;
        _lastSampleUtc = now;
        if (elapsed <= 0) elapsed = SampleMs / 1000.0;

        // Read every candidate NIC's cumulative counters and pick the busiest as "active".
        NetworkInterface? active = null;
        double activeRate = 0;       // max(rx,tx) bytes/sec on the active NIC
        double activeRxTxSum = -1;   // throughput used to choose the active NIC
        var current = new Dictionary<string, (long Rx, long Tx)>();

        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (!IsCandidate(nic)) continue;

            long rx, tx;
            try
            {
                var s = nic.GetIPStatistics();
                rx = s.BytesReceived;
                tx = s.BytesSent;
            }
            catch { continue; }

            current[nic.Id] = (rx, tx);

            double rxRate = 0, txRate = 0;
            if (_prev.TryGetValue(nic.Id, out var p))
            {
                rxRate = Math.Max(0, (rx - p.Rx) / elapsed);
                txRate = Math.Max(0, (tx - p.Tx) / elapsed);
            }

            double sum = rxRate + txRate;
            // Prefer the busiest NIC; if all idle, fall back to the fastest link.
            double rank = sum > 0 ? sum : LinkBytesPerSec(nic) * 1e-9;
            if (rank > activeRxTxSum)
            {
                activeRxTxSum = rank;
                active = nic;
                activeRate = Math.Max(rxRate, txRate);
            }
        }

        lock (_gate)
        {
            _prev.Clear();
            foreach (var kv in current) _prev[kv.Key] = kv.Value;

            if (active == null)
            {
                _currentPercent = 0;
                _currentRate = 0;
                return;
            }

            string key = ConnectionKey(active, out string label);
            if (key != _activeKey)
            {
                _activeKey = key;
                _smoothed = activeRate; // reset smoothing on connection switch
            }
            else
            {
                _smoothed = _smoothed * (1 - Alpha) + activeRate * Alpha;
            }

            if (!_store.TryGetValue(key, out var stat))
            {
                stat = new ConnectionStat { MaxBytesPerSec = FloorBytesPerSec, Label = label };
                _store[key] = stat;
            }
            stat.Label = label;

            double link = LinkBytesPerSec(active);
            if (link > 0) stat.LinkBytesPerSec = link;

            // Relax the learned max slowly so a one-off spike doesn't pin it forever,
            // then raise it to the current smoothed rate and cap it at the link speed.
            stat.MaxBytesPerSec *= DecayPerTick;
            if (_smoothed > stat.MaxBytesPerSec) { stat.MaxBytesPerSec = _smoothed; _dirty = true; }
            if (link > 0 && stat.MaxBytesPerSec > link) stat.MaxBytesPerSec = link;

            double effectiveMax = Math.Max(stat.MaxBytesPerSec, FloorBytesPerSec);
            _currentRate = activeRate;
            _currentMax = effectiveMax;
            _currentLabel = label;
            _currentPercent = Math.Clamp(_smoothed / effectiveMax, 0, 1) * 100.0;

            if (_dirty && (now - _lastSaveUtc).TotalSeconds >= SaveEverySeconds)
            {
                NetworkMaxStore.Save(_store);
                _dirty = false;
                _lastSaveUtc = now;
            }
        }
    }

    /// <summary>An operational, non-loopback/tunnel interface that has a usable IPv4 gateway.</summary>
    private static bool IsCandidate(NetworkInterface nic)
    {
        if (nic.OperationalStatus != OperationalStatus.Up) return false;
        if (nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
            return false;
        try { return GatewayIPv4(nic) != null; }
        catch { return false; }
    }

    private static IPAddress? GatewayIPv4(NetworkInterface nic)
    {
        foreach (var g in nic.GetIPProperties().GatewayAddresses)
        {
            if (g.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork &&
                !g.Address.Equals(IPAddress.Any))
            {
                return g.Address;
            }
        }
        return null;
    }

    // Speed is bits/sec; convert to bytes/sec. Returns 0 when unknown.
    private static double LinkBytesPerSec(NetworkInterface nic)
    {
        try { return nic.Speed > 0 ? nic.Speed / 8.0 : 0; }
        catch { return 0; }
    }

    /// <summary>
    /// Connection signature: "{type}|{routerId}". Type is wifi/wired/other; routerId
    /// is the default gateway's MAC (via ARP), falling back to the gateway IP + NIC id.
    /// </summary>
    private static string ConnectionKey(NetworkInterface nic, out string label)
    {
        string type = nic.NetworkInterfaceType switch
        {
            NetworkInterfaceType.Wireless80211 => "wifi",
            NetworkInterfaceType.Ethernet or NetworkInterfaceType.GigabitEthernet
                or NetworkInterfaceType.FastEthernetT or NetworkInterfaceType.FastEthernetFx => "wired",
            _ => nic.NetworkInterfaceType.ToString().ToLowerInvariant()
        };

        var gateway = GatewayIPv4(nic);
        string routerId;
        if (gateway != null)
        {
            routerId = GatewayMac(gateway) ?? $"{gateway}#{nic.Id}";
        }
        else
        {
            routerId = nic.Id;
        }

        label = gateway != null ? $"{type} · {gateway}" : $"{type} · {nic.Name}";
        return $"{type}|{routerId}";
    }

    [DllImport("iphlpapi.dll", ExactSpelling = true)]
    private static extern int SendARP(uint destIp, uint srcIp, byte[] macAddr, ref uint macAddrLen);

    private static string? GatewayMac(IPAddress gateway)
    {
        try
        {
            uint dest = BitConverter.ToUInt32(gateway.GetAddressBytes(), 0);
            var mac = new byte[6];
            uint len = (uint)mac.Length;
            if (SendARP(dest, 0, mac, ref len) == 0 && len >= 6)
            {
                // All-zero means no resolution.
                if (mac[0] == 0 && mac[1] == 0 && mac[2] == 0 &&
                    mac[3] == 0 && mac[4] == 0 && mac[5] == 0) return null;
                return string.Join(":", mac.Take(6).Select(b => b.ToString("X2")));
            }
        }
        catch { /* fall through to null */ }
        return null;
    }

    public void Dispose()
    {
        _timer?.Dispose();
        _timer = null;
        lock (_gate)
        {
            if (_dirty)
            {
                NetworkMaxStore.Save(_store);
                _dirty = false;
            }
        }
    }
}
