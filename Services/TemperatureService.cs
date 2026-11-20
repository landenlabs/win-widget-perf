// Copyright (c) 2026 LanDen Labs - Dennis Lang
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace WinWidgetPerf.Services;

/// <summary>
/// Reads CPU and GPU temperatures without admin rights or third-party drivers.
/// <list type="bullet">
/// <item>CPU: the "Thermal Zone Information" performance counters (ACPI thermal zones).
///   Accuracy depends on the firmware — some desktops only expose a fixed or
///   motherboard-level zone rather than the CPU package sensor.</item>
/// <item>GPU: D3DKMTQueryAdapterInfo(KMTQAITYPE_ADAPTERPERFDATA), the same source Task
///   Manager uses. Requires a WDDM 2.4+ driver that reports temperature (most discrete GPUs).</item>
/// </list>
/// Sources are discovered lazily on first use; a missing source yields null.
/// </summary>
public sealed class TemperatureService : IDisposable
{
    private readonly object _gate = new();
    private bool _initialized;
    private readonly List<PerformanceCounter> _cpuZones = [];
    private bool _cpuZonesHighPrecision;
    private uint[] _gpuAdapters = [];

    /// <summary>Hottest thermal-zone temperature in °C, or null if unavailable.</summary>
    public double? GetCpuTemperature()
    {
        lock (_gate)
        {
            EnsureInitialized();
            double? max = null;
            foreach (var zone in _cpuZones)
            {
                try
                {
                    // "Temperature" is Kelvin; "High Precision Temperature" is tenths of Kelvin.
                    double kelvin = zone.NextValue() / (_cpuZonesHighPrecision ? 10.0 : 1.0);
                    double c = kelvin - 273.15;
                    if (c is > 0 and < 150 && (max == null || c > max)) max = c;
                }
                catch { /* zone disappeared — skip */ }
            }
            return max;
        }
    }

    /// <summary>Hottest GPU temperature in °C across adapters, or null if unavailable.</summary>
    public double? GetGpuTemperature()
    {
        lock (_gate)
        {
            EnsureInitialized();
            double? max = null;
            foreach (var hAdapter in _gpuAdapters)
            {
                var perf = new D3DKMT_ADAPTER_PERFDATA { PhysicalAdapterIndex = 0 };
                var handle = GCHandle.Alloc(perf, GCHandleType.Pinned);
                try
                {
                    var query = new D3DKMT_QUERYADAPTERINFO
                    {
                        hAdapter = hAdapter,
                        Type = KMTQAITYPE_ADAPTERPERFDATA,
                        pPrivateDriverData = handle.AddrOfPinnedObject(),
                        PrivateDriverDataSize = (uint)Marshal.SizeOf<D3DKMT_ADAPTER_PERFDATA>(),
                    };
                    if (D3DKMTQueryAdapterInfo(ref query) != 0) continue;
                    perf = Marshal.PtrToStructure<D3DKMT_ADAPTER_PERFDATA>(handle.AddrOfPinnedObject());
                }
                catch { continue; }
                finally { handle.Free(); }

                double c = perf.Temperature / 10.0;
                if (c is > 0 and < 150 && (max == null || c > max)) max = c;
            }
            return max;
        }
    }

    private void EnsureInitialized()
    {
        if (_initialized) return;
        _initialized = true;
        InitCpuZones();
        InitGpuAdapters();
    }

    private void InitCpuZones()
    {
        try
        {
            const string category = "Thermal Zone Information";
            if (!PerformanceCounterCategory.Exists(category)) return;
            var cat = new PerformanceCounterCategory(category);
            _cpuZonesHighPrecision = cat.CounterExists("High Precision Temperature");
            string counter = _cpuZonesHighPrecision ? "High Precision Temperature" : "Temperature";
            foreach (var instance in cat.GetInstanceNames())
            {
                try { _cpuZones.Add(new PerformanceCounter(category, counter, instance, readOnly: true)); }
                catch { }
            }
        }
        catch { /* counters unavailable */ }
    }

    private void InitGpuAdapters()
    {
        try
        {
            var enumArgs = new D3DKMT_ENUMADAPTERS2();
            if (D3DKMTEnumAdapters2(ref enumArgs) != 0 || enumArgs.NumAdapters == 0) return;

            var infos = new D3DKMT_ADAPTERINFO[enumArgs.NumAdapters];
            var handle = GCHandle.Alloc(infos, GCHandleType.Pinned);
            try
            {
                enumArgs.pAdapters = handle.AddrOfPinnedObject();
                if (D3DKMTEnumAdapters2(ref enumArgs) != 0) return;
            }
            finally { handle.Free(); }

            _gpuAdapters = infos.Take((int)enumArgs.NumAdapters).Select(i => i.hAdapter).ToArray();
        }
        catch { _gpuAdapters = []; }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            foreach (var zone in _cpuZones) zone.Dispose();
            _cpuZones.Clear();
            foreach (var hAdapter in _gpuAdapters)
            {
                var close = new D3DKMT_CLOSEADAPTER { hAdapter = hAdapter };
                try { D3DKMTCloseAdapter(ref close); } catch { }
            }
            _gpuAdapters = [];
        }
    }

    // ── D3DKMT interop (layouts from d3dkmthk.h) ─────────────────────────────

    private const int KMTQAITYPE_ADAPTERPERFDATA = 62;

    [StructLayout(LayoutKind.Sequential)]
    private struct D3DKMT_ADAPTERINFO
    {
        public uint hAdapter;
        public uint AdapterLuidLow;
        public int AdapterLuidHigh;
        public uint NumOfSources;
        public int bPrecisePresentRegionsPreferred;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct D3DKMT_ENUMADAPTERS2
    {
        public uint NumAdapters;
        public IntPtr pAdapters;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct D3DKMT_QUERYADAPTERINFO
    {
        public uint hAdapter;
        public int Type;
        public IntPtr pPrivateDriverData;
        public uint PrivateDriverDataSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct D3DKMT_ADAPTER_PERFDATA
    {
        public uint PhysicalAdapterIndex;
        public ulong MemoryFrequency;
        public ulong MaxMemoryFrequency;
        public ulong MaxMemoryFrequencyOC;
        public ulong MemoryBandwidth;
        public ulong PCIEBandwidth;
        public uint FanRPM;
        public uint Power;
        public uint Temperature;
        public byte PowerStateOverride;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct D3DKMT_CLOSEADAPTER
    {
        public uint hAdapter;
    }

    [DllImport("gdi32.dll")] private static extern int D3DKMTEnumAdapters2(ref D3DKMT_ENUMADAPTERS2 args);
    [DllImport("gdi32.dll")] private static extern int D3DKMTQueryAdapterInfo(ref D3DKMT_QUERYADAPTERINFO args);
    [DllImport("gdi32.dll")] private static extern int D3DKMTCloseAdapter(ref D3DKMT_CLOSEADAPTER args);
}
