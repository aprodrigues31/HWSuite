using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using HWSuite.Core.Model;
using HWSuite.Core.Sensors;

namespace HWSuite.Providers.Windows;

/// <summary>
/// Extra AMD GPU telemetry through the AMD Display Library (atiadlxx.dll, installed with the Radeon
/// driver): see <see cref="AdlTelemetry"/>. Works without administrator rights. On machines without
/// an AMD GPU or driver it reports nothing.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed unsafe partial class AdlTelemetrySource : ISensorSource
{
    private const int AdlOk = 0;
    private const int AdlMaxPath = 256;
    private const int AdapterInfoSize = 1572; // sizeof(AdapterInfo) in adl_structures.h
    private const int MaxSensors = 256; // ADL_PMLOG_MAX_SENSORS
    private const int AmdVendorId = 0x1002;

    private readonly bool _raw;
    private readonly nint _context;
    private readonly List<(int Index, string Name, string Id)> _adapters = [];
    private bool _disposed;

    public AdlTelemetrySource(bool raw = false)
    {
        _raw = raw;
        try
        {
            nint context = 0;
            if (ADL2_Main_Control_Create(&Allocate, 1, &context) != AdlOk)
            {
                return;
            }

            _context = context;
            FindAdapters();
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            // No AMD driver: nothing to add.
        }
    }

    public string Name => "AMD ADL";

    public IReadOnlyList<Diagnostic> Diagnostics => [];

    public SensorSnapshot Read()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var readings = new List<SensorReading>();
        foreach (var (index, name, id) in _adapters)
        {
            var output = default(PmLogDataOutput); // size left 0, as LibreHardwareMonitor calls it
            if (ADL2_New_QueryPMLogData_Get(_context, index, &output) != AdlOk)
            {
                continue;
            }

            var values = new Dictionary<int, int>();
            for (var i = 1; i < MaxSensors; i++)
            {
                if (output.Sensors[i * 2] != 0) // { int supported; int value; }
                {
                    values[i] = output.Sensors[(i * 2) + 1];
                }
            }

            readings.AddRange(AdlTelemetry.Readings(name, id, values, _raw));
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
        if (_context != 0)
        {
            _ = ADL2_Main_Control_Destroy(_context);
        }
    }

    /// <summary>One ADL adapter index per physical AMD GPU (ADL lists one per display output).</summary>
    private void FindAdapters()
    {
        int count = 0;
        if (ADL2_Adapter_NumberOfAdapters_Get(_context, &count) != AdlOk || count <= 0)
        {
            return;
        }

        var buffer = new byte[count * AdapterInfoSize];
        fixed (byte* p = buffer)
        {
            if (ADL2_Adapter_AdapterInfo_Get(_context, p, buffer.Length) != AdlOk)
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

            var name = AnsiString(info.Slice(280, AdlMaxPath));
            _adapters.Add((index, name, $"/adl/{bus}-{device}"));
        }
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
}
