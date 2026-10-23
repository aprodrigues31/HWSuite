using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using HWSuite.Core.Gpu;
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
    private const int Od8Count = 77; // OD8_COUNT in adl_defines.h (display-library SDK)
    private const int Od8BufferEntries = 256;

    private readonly bool _raw;
    private readonly nint _context;
    private readonly List<Adapter> _adapters = [];
    private readonly Dictionary<string, int> _status = [];
    private bool _disposed;
    private Dictionary<int, List<Od8Setting>>? _overdrive; // raw mode only

    private sealed class Adapter(int index, string name, string id)
    {
        public int Index { get; } = index;
        public string Name { get; } = name;
        public string Id { get; } = id;
        public uint Device { get; set; }
        public nint LogData { get; set; } // ADLPMLogData written by the driver while the session runs
    }

    public AdlTelemetrySource(bool raw = false)
        : this(raw, startLog: true)
    {
    }

    private AdlTelemetrySource(bool raw, bool startLog)
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
            foreach (var adapter in startLog ? _adapters : [])
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

    /// <summary>What the inventory needs from the AMD driver about one card.</summary>
    public sealed record CardDetails(int? MemoryVendor, IReadOnlyList<Od8Setting> Overdrive);

    /// <summary>
    /// Video memory chip maker (ADL2_Adapter_VRAMVendor_Get) and Overdrive 8 table per AMD adapter name,
    /// for the inventory. Opens and closes its own ADL context; empty without an AMD driver.
    /// </summary>
    public static Dictionary<string, CardDetails> Cards()
    {
        var result = new Dictionary<string, CardDetails>(StringComparer.OrdinalIgnoreCase);
        using var adl = new AdlTelemetrySource(raw: false, startLog: false);
        if (adl._context == 0)
        {
            return result;
        }

        foreach (var adapter in adl._adapters)
        {
            int? memoryVendor = null;
            try
            {
                var vendor = 0;
                if (ADL2_Adapter_VRAMVendor_Get(adl._context, adapter.Index, &vendor) == AdlOk && vendor > 0)
                {
                    memoryVendor = vendor;
                }
            }
            catch (EntryPointNotFoundException)
            {
                // older driver without this call
            }

            result[adapter.Name] = new CardDetails(memoryVendor, adl.Overdrive(adapter));
        }

        return result;
    }

    /// <summary>
    /// The Overdrive 8 table (tuning ranges and the card's defaults), when the card supports Overdrive 8.
    /// The driver allocates the list through our callback; we free it.
    /// </summary>
    private List<Od8Setting> Overdrive(Adapter adapter)
    {
        var settings = new List<Od8Setting>();
        try
        {
            int supported = 0, enabled = 0, version = 0;
            var caps = ADL2_Overdrive_Caps(_context, adapter.Index, &supported, &enabled, &version);
            _status[$"Overdrive_Caps #{adapter.Index}"] = caps;
            _status[$"Overdrive version #{adapter.Index}"] = version;
            if (caps != AdlOk || version < 8)
            {
                return settings;
            }

            // The count is an input too: the size of the table we understand (the SDK sample passes
            // OD8_COUNT; with 0 the driver answers ADL_ERR_NULL_POINTER).
            int capabilities = 0, count = Od8Count;
            Od8InitSetting* list = null;
            var result = ADL2_Overdrive8_Init_SettingX2_Get(_context, adapter.Index, &capabilities, &count, &list);
            _status[$"Overdrive8_Init_SettingX2_Get #{adapter.Index}"] = result;
            _status[$"Overdrive8 X2 count #{adapter.Index}"] = count;
            if (list != null)
            {
                try
                {
                    if (result == AdlOk)
                    {
                        Collect(settings, list, count);
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal((nint)list);
                }
            }

            if (settings.Count == 0)
            {
                // Older entry point: ADLOD8InitSetting { int count; int capabilities; ADLOD8SingleInitSetting[OD8_COUNT] },
                // with spare room in case the driver knows more settings than this SDK.
                var buffer = new int[2 + (4 * Od8BufferEntries)];
                buffer[0] = Od8Count;
                fixed (int* p = buffer)
                {
                    var fixedResult = ADL2_Overdrive8_Init_Setting_Get(_context, adapter.Index, p);
                    _status[$"Overdrive8_Init_Setting_Get #{adapter.Index}"] = fixedResult;
                    if (fixedResult == AdlOk)
                    {
                        Collect(settings, (Od8InitSetting*)(p + 2), Math.Min(buffer[0], Od8Count));
                    }
                }
            }

            _status[$"Overdrive8 settings #{adapter.Index}"] = settings.Count;
        }
        catch (EntryPointNotFoundException)
        {
            _status[$"Overdrive8 not exported #{adapter.Index}"] = -1;
        }

        return settings;
    }

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
            foreach (var adapter in _adapters)
            {
                _overdrive ??= _adapters.ToDictionary(a => a.Index, Overdrive);
                foreach (var s in _overdrive[adapter.Index])
                {
                    var name = string.Create(CultureInfo.InvariantCulture, $"OD8 {s.Id} {Overdrive8.Name(s.Id)} (default; min {s.Min}, max {s.Max})");
                    readings.Add(new SensorReading($"{adapter.Id}/od8/{s.Id}", $"{adapter.Name} (Overdrive 8)", "GPU", name, SensorKind.Other, s.Default, s.Min, s.Max));
                }
            }

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
        _status["ADL adapters (all outputs)"] = count;
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
            var udid = AnsiString(info.Slice(8, AdlMaxPath));
            var vendor = AdlTelemetry.VendorFromUdid(udid) ?? BitConverter.ToInt32(info[276..]);
            if (vendor is not (AmdVendorId or 1002) || !seen.Add((bus, device)))
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

    private static void Collect(List<Od8Setting> settings, Od8InitSetting* list, int count)
    {
        for (var i = 0; i < Math.Min(count, Od8BufferEntries); i++)
        {
            var s = list[i];
            if (s.FeatureId != 0 || s.MinValue != 0 || s.MaxValue != 0 || s.DefaultValue != 0)
            {
                settings.Add(new Od8Setting(i, s.MinValue, s.MaxValue, s.DefaultValue));
            }
        }
    }

    /// <summary>ADLOD8SingleInitSetting: featureID, minValue, maxValue, defaultValue.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct Od8InitSetting
    {
        public int FeatureId;
        public int MinValue;
        public int MaxValue;
        public int DefaultValue;
    }

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
    private static partial int ADL2_Overdrive_Caps(nint context, int adapterIndex, int* supported, int* enabled, int* version);

    [LibraryImport(Dll)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private static partial int ADL2_Overdrive8_Init_SettingX2_Get(nint context, int adapterIndex, int* capabilities, int* count, Od8InitSetting** list);

    [LibraryImport(Dll)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private static partial int ADL2_Overdrive8_Init_Setting_Get(nint context, int adapterIndex, int* initSetting);

    [LibraryImport(Dll)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private static partial int ADL2_Adapter_VRAMVendor_Get(nint context, int adapterIndex, int* vendor);

    [LibraryImport(Dll)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private static partial int ADL2_Adapter_PMLog_Stop(nint context, int adapterIndex, uint device);
}
