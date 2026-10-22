using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using HWSuite.Core.Model;
using HWSuite.Core.Sensors;

namespace HWSuite.Providers.Windows;

/// <summary>
/// Extra AMD GPU telemetry through the AMD Display Library (atiadlxx.dll, installed with the Radeon
/// driver): see <see cref="AdlTelemetry"/>. Two ways to read, as LibreHardwareMonitor does:
/// the PMLog session (the driver writes every supported sensor into shared memory; needed on RDNA 3)
/// and the one-shot ADL2_New_QueryPMLogData_Get query as a fallback. Works without administrator
/// rights; without an AMD GPU or driver it reports nothing. Raw mode also reports each ADL call's
/// status code, so a missing value can be traced to the call that failed.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed unsafe partial class AdlTelemetrySource : ISensorSource
{
    private const int AdlOk = 0;
    private const int AdlMaxPath = 256;
    private const int AdapterInfoSize = 1572; // sizeof(AdapterInfo), adl_structures.h
    private const int MaxSensors = 256; // ADL_PMLOG_MAX_SENSORS
    private const int AmdVendorId = 0x1002;
    private const uint SampleRateMs = 1000;

    private readonly bool _raw;
    private readonly nint _context;
    private readonly List<Adapter> _adapters = [];
    private readonly Dictionary<string, int> _status = [];
    private bool _disposed;

    private sealed class Adapter(int index, string name, string id)
    {
        public int Index { get; } = index;
        public string Name { get; } = name;
        public string Id { get; } = id;
        public uint Device { get; set; }
        public nint LogData { get; set; } // ADLPMLogData written by the driver while the session runs
    }

    public AdlTelemetrySource(bool raw = false)
    {
        _raw = raw;
        try
        {
            nint context = 0;
            var created = ADL2_Main_Control_Create(&Allocate, 1, &context);
            _status["Main_Control_Create"] = created;
            if (created != AdlOk)
            {
                return;
            }

            _context = context;
            FindAdapters();
            foreach (var adapter in _adapters)
            {
                StartLog(adapter);
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            _status["atiadlxx.dll not available"] = -1; // no AMD driver: nothing to add
        }
    }

    public string Name => "AMD ADL";

    public IReadOnlyList<Diagnostic> Diagnostics => [];

    public SensorSnapshot Read()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var readings = new List<SensorReading>();

        foreach (var adapter in _adapters)
        {
            var values = ReadLog(adapter) ?? Query(adapter) ?? [];
            readings.AddRange(AdlTelemetry.Readings(adapter.Name, adapter.Id, values, _raw));
        }

        if (_raw)
        {
            foreach (var (call, code) in _status)
            {
                readings.Add(new SensorReading($"/adl/status/{call}", "AMD ADL (status)", "GPU", call, SensorKind.Other, code, code, code));
            }
        }

        return new SensorSnapshot(DateTimeOffset.Now, readings);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_context == 0)
        {
            return;
        }

        foreach (var adapter in _adapters.Where(a => a.Device != 0))
        {
            if (adapter.LogData != 0)
            {
                _ = ADL2_Adapter_PMLog_Stop(_context, adapter.Index, adapter.Device);
            }

            _ = ADL2_Device_PMLog_Device_Destroy(_context, adapter.Device);
        }

        _ = ADL2_Main_Control_Destroy(_context);
    }

    /// <summary>Starts a PMLog session with every sensor the driver supports (LibreHardwareMonitor's sequence).</summary>
    private void StartLog(Adapter adapter)
    {
        uint device = 0;
        var created = ADL2_Device_PMLog_Device_Create(_context, adapter.Index, &device);
        _status[$"PMLog_Device_Create #{adapter.Index}"] = created;
        if (created != AdlOk)
        {
            return;
        }

        adapter.Device = device;
        var support = new PmLogSupportInfo();
        var supported = ADL2_Adapter_PMLog_Support_Get(_context, adapter.Index, &support);
        _status[$"PMLog_Support_Get #{adapter.Index}"] = supported;
        if (supported != AdlOk)
        {
            return;
        }

        var input = new PmLogStartInput { SampleRate = SampleRateMs };
        var count = 0;
        while (count < MaxSensors - 1 && support.Sensors[count] != 0)
        {
            input.Sensors[count] = support.Sensors[count];
            count++;
        }

        _status[$"PMLog sensors #{adapter.Index}"] = count;
        var output = new PmLogStartOutput();
        var started = ADL2_Adapter_PMLog_Start(_context, adapter.Index, &input, &output, device);
        _status[$"PMLog_Start #{adapter.Index}"] = started;
        if (started == AdlOk && output.LoggingAddress != 0)
        {
            adapter.LogData = output.LoggingAddress;
        }
    }

    /// <summary>Values from the shared PMLog buffer: { version, rate, lastUpdated, [sensor, value] x 256 }.</summary>
    private static Dictionary<int, int>? ReadLog(Adapter adapter)
    {
        if (adapter.LogData == 0)
        {
            return null;
        }

        var lastUpdated = *(ulong*)(adapter.LogData + 8);
        if (lastUpdated == 0)
        {
            return null; // the driver has not written a sample yet
        }

        var pairs = (uint*)(adapter.LogData + 16);
        var values = new Dictionary<int, int>();
        for (var k = 0; k < MaxSensors; k++)
        {
            var sensor = pairs[k * 2];
            if (sensor == 0)
            {
                break;
            }

            values[(int)sensor] = unchecked((int)pairs[(k * 2) + 1]);
        }

        return values.Count > 0 ? values : null;
    }

    private Dictionary<int, int>? Query(Adapter adapter)
    {
        var output = default(PmLogDataOutput); // size left 0, as LibreHardwareMonitor calls it
        var result = ADL2_New_QueryPMLogData_Get(_context, adapter.Index, &output);
        _status[$"New_QueryPMLogData_Get #{adapter.Index}"] = result;
        if (result != AdlOk)
        {
            return null;
        }

        var values = new Dictionary<int, int>();
        for (var i = 1; i < MaxSensors; i++)
        {
            if (output.Sensors[i * 2] != 0) // { int supported; int value; }
            {
                values[i] = output.Sensors[(i * 2) + 1];
            }
        }

        return values;
    }

    /// <summary>One ADL adapter index per physical AMD GPU (ADL lists one per display output).</summary>
    private void FindAdapters()
    {
        int count = 0;
        var counted = ADL2_Adapter_NumberOfAdapters_Get(_context, &count);
        _status["Adapter_NumberOfAdapters_Get"] = counted;
        if (counted != AdlOk || count <= 0)
        {
            return;
        }

        var buffer = new byte[count * AdapterInfoSize];
        fixed (byte* p = buffer)
        {
            var info = ADL2_Adapter_AdapterInfo_Get(_context, p, buffer.Length);
            _status["Adapter_AdapterInfo_Get"] = info;
            if (info != AdlOk)
            {
                return;
            }
        }

        var seen = new HashSet<(int, int)>();
        for (var i = 0; i < count; i++)
        {
            var info = buffer.AsSpan(i * AdapterInfoSize, AdapterInfoSize);
            var index = BitConverter.ToInt32(info[4..]);
            var bus = BitConverter.ToInt32(info[264..]);
            var device = BitConverter.ToInt32(info[268..]);
            var vendor = BitConverter.ToInt32(info[276..]);
            if (vendor != AmdVendorId || !seen.Add((bus, device)))
            {
                continue;
            }

            _adapters.Add(new Adapter(index, AnsiString(info.Slice(280, AdlMaxPath)), $"/adl/{bus}-{device}"));
        }

        _status["AMD adapters"] = _adapters.Count;
    }

    private static string AnsiString(ReadOnlySpan<byte> bytes)
    {
        var end = bytes.IndexOf((byte)0);
        return Encoding.Latin1.GetString(end < 0 ? bytes : bytes[..end]).Trim();
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static nint Allocate(int size) => Marshal.AllocHGlobal(size);

    [StructLayout(LayoutKind.Sequential)]
    private struct PmLogDataOutput
    {
        public int Size;
        public fixed int Sensors[MaxSensors * 2];
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PmLogSupportInfo
    {
        public fixed ushort Sensors[MaxSensors];
        public fixed int Reserved[16];
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PmLogStartInput
    {
        public fixed ushort Sensors[MaxSensors];
        public uint SampleRate;
        public fixed int Reserved[15];
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PmLogStartOutput
    {
        public nint LoggingAddress;
        public fixed int Reserved[14];
    }

    private const string Dll = "atiadlxx.dll";

    [LibraryImport(Dll)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private static partial int ADL2_Main_Control_Create(delegate* unmanaged[Cdecl]<int, nint> callback, int connectedAdapters, nint* context);

    [LibraryImport(Dll)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private static partial int ADL2_Main_Control_Destroy(nint context);

    [LibraryImport(Dll)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private static partial int ADL2_Adapter_NumberOfAdapters_Get(nint context, int* count);

    [LibraryImport(Dll)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private static partial int ADL2_Adapter_AdapterInfo_Get(nint context, byte* info, int size);

    [LibraryImport(Dll)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private static partial int ADL2_New_QueryPMLogData_Get(nint context, int adapterIndex, PmLogDataOutput* output);

    [LibraryImport(Dll)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private static partial int ADL2_Device_PMLog_Device_Create(nint context, int adapterIndex, uint* device);

    [LibraryImport(Dll)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private static partial int ADL2_Device_PMLog_Device_Destroy(nint context, uint device);

    [LibraryImport(Dll)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private static partial int ADL2_Adapter_PMLog_Support_Get(nint context, int adapterIndex, PmLogSupportInfo* info);

    [LibraryImport(Dll)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private static partial int ADL2_Adapter_PMLog_Start(nint context, int adapterIndex, PmLogStartInput* input, PmLogStartOutput* output, uint device);

    [LibraryImport(Dll)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private static partial int ADL2_Adapter_PMLog_Stop(nint context, int adapterIndex, uint device);
}
