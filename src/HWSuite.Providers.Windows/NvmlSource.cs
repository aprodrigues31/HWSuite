using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using HWSuite.Core.Gpu;
using HWSuite.Core.Model;
using HWSuite.Core.Sensors;

namespace HWSuite.Providers.Windows;

/// <summary>
/// NVIDIA telemetry through the NVIDIA Management Library (nvml.dll, installed with every GeForce / RTX driver). It adds
/// what the general sensor library does not read: the power limit in effect, the performance state, the video
/// encoder and decoder load, and why the card is holding its clock back (power, temperature, hardware slowdown).
/// <see cref="Cards"/> gives the inventory facts (architecture, CUDA cores, bus width, power limits, temperature
/// limits, VBIOS). Without an NVIDIA card or driver it reports nothing. Works without administrator rights.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed unsafe class NvmlSource : ISensorSource
{
    private const int Success = 0;
    private const uint ClockGraphics = 0;
    private const uint ClockMemory = 2;
    private const uint ThresholdShutdown = 0;
    private const uint ThresholdSlowdown = 1;

    private readonly Api? _api;
    private readonly List<(nint Handle, string Name, int Index)> _devices = [];
    private bool _disposed;

    public NvmlSource()
    {
        _api = Api.Open();
        if (_api is null)
        {
            return;
        }

        uint count = 0;
        if (_api.GetCount(&count) != Success)
        {
            return;
        }

        for (uint i = 0; i < count; i++)
        {
            nint handle = 0;
            if (_api.GetHandle(i, &handle) == Success)
            {
                _devices.Add((handle, _api.Name(handle) ?? $"NVIDIA GPU {i}", (int)i));
            }
        }
    }

    public string Name => "NVIDIA NVML";

    public IReadOnlyList<Diagnostic> Diagnostics => [];

    public SensorSnapshot Read()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var readings = new List<SensorReading>();
        if (_api is null)
        {
            return new SensorSnapshot(DateTimeOffset.Now, readings);
        }

        foreach (var (handle, name, index) in _devices)
        {
            void Add(string id, string sensor, SensorKind kind, double? value)
            {
                if (value is { } v)
                {
                    readings.Add(new SensorReading($"/nvml/{index}/{id}", name, "GPU", sensor, kind, v, null, null));
                }
            }

            uint value = 0;
            Add("power-limit", "GPU Power Limit", SensorKind.Power, _api.EnforcedPowerLimit(handle, &value) == Success ? value / 1000.0 : null);
            int state = 0;
            Add("pstate", "GPU Performance State", SensorKind.Factor, _api.PerformanceState(handle, &state) == Success && state is >= 0 and <= 15 ? state : null);
            uint period = 0;
            Add("encoder", "GPU Video Encoder", SensorKind.Load, _api.EncoderUtilization(handle, &value, &period) == Success ? value : null);
            Add("decoder", "GPU Video Decoder", SensorKind.Load, _api.DecoderUtilization(handle, &value, &period) == Success ? value : null);
            if (_api.ClockReasons(handle) is { } reasons)
            {
                var limits = (NvidiaClockLimit)reasons;
                foreach (var (flag, label) in Nvml.LimitSensors)
                {
                    Add("limit-" + flag.ToString().ToLowerInvariant(), label, SensorKind.Load, limits.HasFlag(flag) ? 100 : 0);
                }
            }
        }

        return new SensorSnapshot(DateTimeOffset.Now, readings);
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _api?.Dispose();
            _disposed = true;
        }
    }

    /// <summary>Inventory facts of every NVIDIA card, keyed by PCI "VVVV:DDDD:SSSSSSSS" (vendor, device, subsystem).</summary>
    public static IReadOnlyList<NvidiaCard> Cards()
    {
        using var api = Api.Open();
        if (api is null)
        {
            return [];
        }

        var cards = new List<NvidiaCard>();
        uint count = 0;
        if (api.GetCount(&count) != Success)
        {
            return cards;
        }

        int cuda = 0;
        var cudaVersion = api.CudaDriverVersion(&cuda) == Success ? Nvml.CudaVersion(cuda) : null;
        for (uint i = 0; i < count; i++)
        {
            nint handle = 0;
            if (api.GetHandle(i, &handle) != Success)
            {
                continue;
            }

            PciInfo pci = default;
            if (api.GetPciInfo(handle, &pci) != Success)
            {
                continue;
            }

            uint value = 0, min = 0, max = 0;
            var (vendor, device) = Nvml.PciIds(pci.PciDeviceId);
            cards.Add(new NvidiaCard(
                vendor,
                device,
                pci.PciSubSystemId.ToString("X8", System.Globalization.CultureInfo.InvariantCulture),
                api.Name(handle))
            {
                Architecture = api.Architecture(handle, &value) == Success ? Nvml.Architecture(value) : null,
                CudaCores = api.Cores(handle, &value) == Success && value > 0 ? (int)value : null,
                MemoryBusBits = api.BusWidth(handle, &value) == Success && value > 0 ? (int)value : null,
                MaxGpuClockMhz = api.MaxClock(handle, ClockGraphics, &value) == Success && value > 0 ? (int)value : null,
                MaxMemoryClockMhz = api.MaxClock(handle, ClockMemory, &value) == Success && value > 0 ? (int)value : null,
                PowerLimitW = api.EnforcedPowerLimit(handle, &value) == Success ? value / 1000.0 : null,
                PowerLimitDefaultW = api.DefaultPowerLimit(handle, &value) == Success ? value / 1000.0 : null,
                PowerLimitMinW = api.PowerLimitRange(handle, &min, &max) == Success ? min / 1000.0 : null,
                PowerLimitMaxW = api.PowerLimitRange(handle, &min, &max) == Success ? max / 1000.0 : null,
                SlowdownTemperatureC = api.Threshold(handle, ThresholdSlowdown, &value) == Success && value > 0 ? (int)value : null,
                ShutdownTemperatureC = api.Threshold(handle, ThresholdShutdown, &value) == Success && value > 0 ? (int)value : null,
                VbiosVersion = api.Vbios(handle),
                CudaVersion = cudaVersion,
            });
        }

        return cards;
    }

    /// <summary>nvmlPciInfo_t (v3): legacy bus id, domain, bus, device, pciDeviceId, pciSubSystemId, bus id.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct PciInfo
    {
        public fixed byte BusIdLegacy[16];
        public uint Domain;
        public uint Bus;
        public uint Device;
        public uint PciDeviceId;
        public uint PciSubSystemId;
        public fixed byte BusId[32];
    }

    /// <summary>nvml.dll loaded at run time (no NVIDIA driver = no library = nothing to report), initialised once per instance.</summary>
    private sealed class Api : IDisposable
    {
        private readonly nint _library;
        private readonly delegate* unmanaged<int> _shutdown;
        private readonly delegate* unmanaged<uint*, int> _getCount;
        private readonly delegate* unmanaged<uint, nint*, int> _getHandle;
        private readonly delegate* unmanaged<nint, byte*, uint, int> _name;
        private readonly delegate* unmanaged<nint, byte*, uint, int> _vbios;
        private readonly delegate* unmanaged<nint, PciInfo*, int> _pci;
        private readonly delegate* unmanaged<nint, uint*, int> _enforcedLimit;
        private readonly delegate* unmanaged<nint, uint*, int> _defaultLimit;
        private readonly delegate* unmanaged<nint, uint*, uint*, int> _limitRange;
        private readonly delegate* unmanaged<nint, int*, int> _pstate;
        private readonly delegate* unmanaged<nint, uint*, uint*, int> _encoder;
        private readonly delegate* unmanaged<nint, uint*, uint*, int> _decoder;
        private readonly delegate* unmanaged<nint, ulong*, int> _reasons;
        private readonly delegate* unmanaged<nint, uint, uint*, int> _maxClock;
        private readonly delegate* unmanaged<nint, uint*, int> _busWidth;
        private readonly delegate* unmanaged<nint, uint*, int> _cores;
        private readonly delegate* unmanaged<nint, uint*, int> _architecture;
        private readonly delegate* unmanaged<nint, uint, uint*, int> _threshold;
        private readonly delegate* unmanaged<int*, int> _cuda;

        private Api(nint library)
        {
            _library = library;
            _shutdown = (delegate* unmanaged<int>)Export("nvmlShutdown");
            _getCount = (delegate* unmanaged<uint*, int>)Export("nvmlDeviceGetCount_v2");
            _getHandle = (delegate* unmanaged<uint, nint*, int>)Export("nvmlDeviceGetHandleByIndex_v2");
            _name = (delegate* unmanaged<nint, byte*, uint, int>)Export("nvmlDeviceGetName");
            _vbios = (delegate* unmanaged<nint, byte*, uint, int>)Export("nvmlDeviceGetVbiosVersion");
            _pci = (delegate* unmanaged<nint, PciInfo*, int>)Export("nvmlDeviceGetPciInfo_v3");
            _enforcedLimit = (delegate* unmanaged<nint, uint*, int>)Export("nvmlDeviceGetEnforcedPowerLimit");
            _defaultLimit = (delegate* unmanaged<nint, uint*, int>)Export("nvmlDeviceGetPowerManagementDefaultLimit");
            _limitRange = (delegate* unmanaged<nint, uint*, uint*, int>)Export("nvmlDeviceGetPowerManagementLimitConstraints");
            _pstate = (delegate* unmanaged<nint, int*, int>)Export("nvmlDeviceGetPerformanceState");
            _encoder = (delegate* unmanaged<nint, uint*, uint*, int>)Export("nvmlDeviceGetEncoderUtilization");
            _decoder = (delegate* unmanaged<nint, uint*, uint*, int>)Export("nvmlDeviceGetDecoderUtilization");

            // Renamed in driver R535 ("clock event reasons"); same call, older drivers only have the old name.
            var reasons = Export("nvmlDeviceGetCurrentClocksEventReasons");
            _reasons = (delegate* unmanaged<nint, ulong*, int>)(reasons != 0 ? reasons : Export("nvmlDeviceGetCurrentClocksThrottleReasons"));
            _maxClock = (delegate* unmanaged<nint, uint, uint*, int>)Export("nvmlDeviceGetMaxClockInfo");
            _busWidth = (delegate* unmanaged<nint, uint*, int>)Export("nvmlDeviceGetMemoryBusWidth");
            _cores = (delegate* unmanaged<nint, uint*, int>)Export("nvmlDeviceGetNumGpuCores");
            _architecture = (delegate* unmanaged<nint, uint*, int>)Export("nvmlDeviceGetArchitecture");
            _threshold = (delegate* unmanaged<nint, uint, uint*, int>)Export("nvmlDeviceGetTemperatureThreshold");
            _cuda = (delegate* unmanaged<int*, int>)Export("nvmlSystemGetCudaDriverVersion_v2");
        }

        /// <summary>Loads and initialises NVML, or null when there is no NVIDIA driver (or it fails to start).</summary>
        public static Api? Open()
        {
            string[] candidates =
            [
                Path.Combine(Environment.SystemDirectory, "nvml.dll"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "NVIDIA Corporation", "NVSMI", "nvml.dll"),
            ];
            foreach (var path in candidates)
            {
                if (!File.Exists(path) || !NativeLibrary.TryLoad(path, out var library))
                {
                    continue;
                }

                if (NativeLibrary.TryGetExport(library, "nvmlInit_v2", out var init) && ((delegate* unmanaged<int>)init)() == Success)
                {
                    var api = new Api(library);
                    if (api._getCount != null && api._getHandle != null)
                    {
                        return api;
                    }

                    api.Dispose();
                    return null;
                }

                NativeLibrary.Free(library);
            }

            return null;
        }

        private nint Export(string name) => NativeLibrary.TryGetExport(_library, name, out var address) ? address : 0;

        private const int NotSupported = 3; // NVML_ERROR_NOT_SUPPORTED: also used for missing entry points

        public int GetCount(uint* count) => _getCount(count);

        public int GetHandle(uint index, nint* handle) => _getHandle(index, handle);

        public string? Name(nint device) => Text(_name, device);

        public string? Vbios(nint device) => Text(_vbios, device);

        public int GetPciInfo(nint device, PciInfo* info) => _pci == null ? NotSupported : _pci(device, info);

        public int EnforcedPowerLimit(nint device, uint* milliwatts) => _enforcedLimit == null ? NotSupported : _enforcedLimit(device, milliwatts);

        public int DefaultPowerLimit(nint device, uint* milliwatts) => _defaultLimit == null ? NotSupported : _defaultLimit(device, milliwatts);

        public int PowerLimitRange(nint device, uint* min, uint* max) => _limitRange == null ? NotSupported : _limitRange(device, min, max);

        public int PerformanceState(nint device, int* state) => _pstate == null ? NotSupported : _pstate(device, state);

        public int EncoderUtilization(nint device, uint* percent, uint* period) => _encoder == null ? NotSupported : _encoder(device, percent, period);

        public int DecoderUtilization(nint device, uint* percent, uint* period) => _decoder == null ? NotSupported : _decoder(device, percent, period);

        public ulong? ClockReasons(nint device)
        {
            ulong reasons = 0;
            return _reasons != null && _reasons(device, &reasons) == Success ? reasons : null;
        }

        public int MaxClock(nint device, uint type, uint* mhz) => _maxClock == null ? NotSupported : _maxClock(device, type, mhz);

        public int BusWidth(nint device, uint* bits) => _busWidth == null ? NotSupported : _busWidth(device, bits);

        public int Cores(nint device, uint* cores) => _cores == null ? NotSupported : _cores(device, cores);

        public int Architecture(nint device, uint* code) => _architecture == null ? NotSupported : _architecture(device, code);

        public int Threshold(nint device, uint type, uint* celsius) => _threshold == null ? NotSupported : _threshold(device, type, celsius);

        public int CudaDriverVersion(int* version) => _cuda == null ? NotSupported : _cuda(version);

        private static string? Text(delegate* unmanaged<nint, byte*, uint, int> call, nint device)
        {
            if (call == null)
            {
                return null;
            }

            var buffer = stackalloc byte[96];
            if (call(device, buffer, 96) != Success)
            {
                return null;
            }

            var text = Encoding.UTF8.GetString(buffer, new ReadOnlySpan<byte>(buffer, 96).IndexOf((byte)0) is var end and >= 0 ? end : 96).Trim();
            return text.Length == 0 ? null : text;
        }

        public void Dispose()
        {
            if (_shutdown != null)
            {
                _ = _shutdown();
            }

            NativeLibrary.Free(_library);
        }
    }
}

/// <summary>What NVML tells about one NVIDIA card, for the inventory.</summary>
public sealed record NvidiaCard(string VendorId, string DeviceId, string SubsystemId, string? Name)
{
    public string? Architecture { get; init; }

    public int? CudaCores { get; init; }

    public int? MemoryBusBits { get; init; }

    public int? MaxGpuClockMhz { get; init; }

    public int? MaxMemoryClockMhz { get; init; }

    public double? PowerLimitW { get; init; }

    public double? PowerLimitDefaultW { get; init; }

    public double? PowerLimitMinW { get; init; }

    public double? PowerLimitMaxW { get; init; }

    public int? SlowdownTemperatureC { get; init; }

    public int? ShutdownTemperatureC { get; init; }

    public string? VbiosVersion { get; init; }

    public string? CudaVersion { get; init; }

    /// <summary>Copies the facts onto the DXGI adapter they belong to.</summary>
    public void ApplyTo(GpuInfo gpu)
    {
        ArgumentNullException.ThrowIfNull(gpu);
        gpu.Architecture = Architecture;
        gpu.ShaderUnits = CudaCores;
        gpu.MemoryBusBits = MemoryBusBits;
        gpu.MaxGpuClockMhz = MaxGpuClockMhz;
        gpu.MaxMemoryClockMhz = MaxMemoryClockMhz;
        gpu.PowerLimitW = PowerLimitW;
        gpu.PowerLimitDefaultW = PowerLimitDefaultW;
        gpu.PowerLimitMinW = PowerLimitMinW;
        gpu.PowerLimitMaxW = PowerLimitMaxW;
        gpu.SlowdownTemperatureC = SlowdownTemperatureC;
        gpu.ShutdownTemperatureC = ShutdownTemperatureC;
        gpu.BiosVersion ??= VbiosVersion;
        gpu.ComputeVersion = CudaVersion;
    }
}
