using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using HWSuite.Core.Benchmark;

namespace HWSuite.Providers.Windows;

/// <summary>A GPU the benchmark can run on: its DXGI index and name.</summary>
public sealed record GpuAdapter(int Index, string Name, long DedicatedMemoryBytes);

/// <summary>
/// The GPU workload as a Direct3D 11 compute shader (cs_5_0, compiled at run time with the
/// d3dcompiler_47.dll that ships with Windows). Every thread runs 16 independent fused multiply-add chains;
/// the results go to a buffer that is read back after each dispatch, which both ends the batch and gives
/// the checksum the stress test compares. Works on any Direct3D 11 GPU and driver, no extra install.
/// COM calls go through the vtables directly (indices from d3d11.h).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed unsafe partial class D3D11ComputeRunner : IGpuComputeRunner
{
    private const int ThreadsPerGroup = 256;
    private const int FlopsPerIteration = 4 * 4 * 4 * 2; // 4 unrolled steps x 4 float4 chains x 4 lanes x FMA

    private const string Shader = """
        cbuffer Params : register(b0) { uint Iterations; uint Seed; uint Pad0; uint Pad1; };
        RWStructuredBuffer<uint> Results : register(u0);

        [numthreads(256, 1, 1)]
        void main(uint3 id : SV_DispatchThreadID)
        {
            float base = 1.0f + (float)((id.x ^ Seed) & 1023) * (1.0f / 1024.0f);
            float4 a = float4(base, base + 0.25f, base + 0.5f, base + 0.75f);
            float4 b = a * 0.5f;
            float4 c = a * 0.25f;
            float4 d = a * 0.125f;
            const float4 m = float4(0.999f, 0.9991f, 0.9992f, 0.9993f);
            const float4 k = float4(0.001f, 0.0011f, 0.0012f, 0.0013f);
            [loop]
            for (uint i = 0; i < Iterations; i++)
            {
                [unroll]
                for (uint j = 0; j < 4; j++)
                {
                    a = mad(a, m, k);
                    b = mad(b, m, k);
                    c = mad(c, m, k);
                    d = mad(d, m, k);
                }
            }

            Results[id.x] = asuint(a.x + b.y + c.z + d.w) ^ asuint(a.w * b.x);
        }
        """;

    private static readonly Guid IidFactory1 = new("770aae78-f26f-4dba-a829-253c83d1b387");

    private readonly int _groups;
    private nint _device;
    private nint _context;
    private nint _shader;
    private nint _output;
    private nint _staging;
    private nint _view;
    private nint _constants;
    private uint _iterations = 16;
    private bool _constantsDirty = true;

    private D3D11ComputeRunner(string adapterName, int groups)
    {
        AdapterName = adapterName;
        _groups = groups;
    }

    public string AdapterName { get; }

    public double FlopsPerBatch => (double)_groups * ThreadsPerGroup * _iterations * FlopsPerIteration;

    /// <summary>Hardware GPUs, largest dedicated memory first (the discrete card before the integrated one).</summary>
    public static IReadOnlyList<GpuAdapter> Adapters()
    {
        var result = new List<GpuAdapter>();
        ForEachAdapter((index, adapter, desc) =>
        {
            if ((desc->Flags & 2) == 0) // DXGI_ADAPTER_FLAG_SOFTWARE
            {
                result.Add(new GpuAdapter(index, new string(desc->Description).TrimEnd('\0').Trim(), (long)desc->DedicatedVideoMemory));
            }

            return false;
        });
        return [.. result.OrderByDescending(a => a.DedicatedMemoryBytes)];
    }

    /// <summary>A runner on the DXGI adapter with this index.</summary>
    public static D3D11ComputeRunner Create(GpuAdapter adapter, int groups = 4096)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        var runner = new D3D11ComputeRunner(adapter.Name, groups);
        var created = false;
        ForEachAdapter((index, pointer, _) =>
        {
            if (index != adapter.Index)
            {
                return false;
            }

            runner.Initialize(pointer, driverType: 0);
            created = true;
            return true;
        });

        if (!created)
        {
            runner.Dispose();
            throw new InvalidOperationException($"GPU adapter {adapter.Index} not found.");
        }

        return runner;
    }

    /// <summary>A runner on WARP, Direct3D's software rasterizer (used by the automated tests: CI machines have no GPU).</summary>
    public static D3D11ComputeRunner CreateWarp(int groups)
    {
        var runner = new D3D11ComputeRunner("Microsoft Basic Render Driver (WARP)", groups);
        runner.Initialize(0, driverType: 5);
        return runner;
    }

    public void Calibrate(TimeSpan target)
    {
        _iterations = 16;
        _constantsDirty = true;
        for (var attempt = 0; attempt < 12; attempt++)
        {
            var (elapsed, _) = RunBatch();
            if (elapsed < target / 4 && _iterations < 1 << 20)
            {
                _iterations *= 4;
                _constantsDirty = true;
                continue;
            }

            var scaled = _iterations * Math.Min(4, target.TotalMilliseconds / Math.Max(elapsed.TotalMilliseconds, 0.01));
            _iterations = (uint)Math.Clamp(scaled, 1, 1 << 22);
            _constantsDirty = true;
            break;
        }
    }

    public (TimeSpan Elapsed, ulong Checksum) RunBatch()
    {
        ObjectDisposedException.ThrowIf(_device == 0, this);
        var ctx = *(nint**)_context;
        if (_constantsDirty)
        {
            var values = stackalloc uint[4] { _iterations, 0x5EED, 0, 0 };
            ((delegate* unmanaged[Stdcall]<nint, nint, uint, nint, void*, uint, uint, void>)ctx[48])(_context, _constants, 0, 0, values, 0, 0); // UpdateSubresource
            _constantsDirty = false;
        }

        var clock = Stopwatch.StartNew();
        var shader = _shader;
        var view = _view;
        var constants = _constants;
        ((delegate* unmanaged[Stdcall]<nint, nint, nint*, uint, void>)ctx[69])(_context, shader, null, 0); // CSSetShader
        ((delegate* unmanaged[Stdcall]<nint, uint, uint, nint*, uint*, void>)ctx[68])(_context, 0, 1, &view, null); // CSSetUnorderedAccessViews
        ((delegate* unmanaged[Stdcall]<nint, uint, uint, nint*, void>)ctx[71])(_context, 0, 1, &constants); // CSSetConstantBuffers
        ((delegate* unmanaged[Stdcall]<nint, uint, uint, uint, void>)ctx[41])(_context, (uint)_groups, 1, 1); // Dispatch
        ((delegate* unmanaged[Stdcall]<nint, nint, nint, void>)ctx[47])(_context, _staging, _output); // CopyResource

        MappedSubresource mapped;
        Check(((delegate* unmanaged[Stdcall]<nint, nint, uint, int, uint, MappedSubresource*, int>)ctx[14])(_context, _staging, 0, 1, 0, &mapped), "Map"); // waits for the GPU
        var elapsed = clock.Elapsed;
        ulong checksum = 14695981039346656037UL;
        var data = (uint*)mapped.Data;
        var count = _groups * ThreadsPerGroup;
        for (var i = 0; i < count; i++)
        {
            checksum = (checksum ^ data[i]) * 1099511628211UL;
        }

        ((delegate* unmanaged[Stdcall]<nint, nint, uint, void>)ctx[15])(_context, _staging, 0); // Unmap
        return (elapsed, checksum);
    }

    public void Dispose()
    {
        foreach (var p in new[] { _view, _staging, _output, _constants, _shader, _context, _device })
        {
            if (p != 0)
            {
                Release(p);
            }
        }

        _view = _staging = _output = _constants = _shader = _context = _device = 0;
    }

    private void Initialize(nint adapter, int driverType)
    {
        try
        {
            var level = 0xB000; // D3D_FEATURE_LEVEL_11_0
            nint device, context;
            int obtained;
            Check(D3D11CreateDevice(adapter, driverType, 0, 0, &level, 1, 7, &device, &obtained, &context), "D3D11CreateDevice");
            (_device, _context) = (device, context);
            var dev = *(nint**)_device;

            // Compute shader.
            var bytecode = Compile();
            try
            {
                var blob = *(nint**)bytecode;
                var code = ((delegate* unmanaged[Stdcall]<nint, void*>)blob[3])(bytecode);
                var size = ((delegate* unmanaged[Stdcall]<nint, nuint>)blob[4])(bytecode);
                nint shader;
                Check(((delegate* unmanaged[Stdcall]<nint, void*, nuint, nint, nint*, int>)dev[18])(_device, code, size, 0, &shader), "CreateComputeShader");
                _shader = shader;
            }
            finally
            {
                Release(bytecode);
            }

            var bytes = (uint)(_groups * ThreadsPerGroup * sizeof(uint));
            var createBuffer = (delegate* unmanaged[Stdcall]<nint, BufferDesc*, nint, nint*, int>)dev[3];

            // Output: structured buffer the shader writes (UAV); staging copy the CPU reads; constants.
            var outputDesc = new BufferDesc { ByteWidth = bytes, Usage = 0, BindFlags = 0x80, MiscFlags = 0x40, StructureByteStride = 4 };
            nint output, staging, constants, view;
            Check(createBuffer(_device, &outputDesc, 0, &output), "CreateBuffer (output)");
            _output = output;
            var stagingDesc = new BufferDesc { ByteWidth = bytes, Usage = 3, CpuAccessFlags = 0x20000 };
            Check(createBuffer(_device, &stagingDesc, 0, &staging), "CreateBuffer (staging)");
            _staging = staging;
            var constantDesc = new BufferDesc { ByteWidth = 16, Usage = 0, BindFlags = 0x4 };
            Check(createBuffer(_device, &constantDesc, 0, &constants), "CreateBuffer (constants)");
            _constants = constants;

            var viewDesc = new UavDesc { Format = 0, ViewDimension = 1, FirstElement = 0, NumElements = (uint)(_groups * ThreadsPerGroup) };
            Check(((delegate* unmanaged[Stdcall]<nint, nint, UavDesc*, nint*, int>)dev[8])(_device, _output, &viewDesc, &view), "CreateUnorderedAccessView");
            _view = view;
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    private static nint Compile()
    {
        var source = Encoding.ASCII.GetBytes(Shader);
        nint code = 0, errors = 0;
        int hr;
        fixed (byte* p = source)
        {
            hr = D3DCompile(p, (nuint)source.Length, "hwsuite-gpu-bench", 0, 0, "main", "cs_5_0", 1 << 15, 0, &code, &errors); // D3DCOMPILE_OPTIMIZATION_LEVEL3
        }

        if (hr < 0)
        {
            var message = errors != 0 ? Marshal.PtrToStringAnsi((nint)((delegate* unmanaged[Stdcall]<nint, void*>)(*(nint**)errors)[3])(errors)) : null;
            if (errors != 0)
            {
                Release(errors);
            }

            throw new InvalidOperationException($"D3DCompile failed (0x{hr:X8}): {message}");
        }

        if (errors != 0)
        {
            Release(errors); // warnings
        }

        return code;
    }

    /// <summary>Calls <paramref name="visit"/> for each DXGI adapter until it returns true.</summary>
    private static void ForEachAdapter(AdapterVisitor visit)
    {
        Marshal.ThrowExceptionForHR(Native.CreateDXGIFactory1(IidFactory1, out var factory));
        try
        {
            var enumAdapters1 = (delegate* unmanaged[Stdcall]<nint, uint, nint*, int>)(*(nint**)factory)[12];
            for (uint i = 0; ; i++)
            {
                nint adapter;
                if (enumAdapters1(factory, i, &adapter) < 0)
                {
                    break; // DXGI_ERROR_NOT_FOUND: no more adapters
                }

                try
                {
                    AdapterDesc1 desc;
                    var getDesc1 = (delegate* unmanaged[Stdcall]<nint, AdapterDesc1*, int>)(*(nint**)adapter)[10];
                    if (getDesc1(adapter, &desc) >= 0 && visit((int)i, adapter, &desc))
                    {
                        return;
                    }
                }
                finally
                {
                    Release(adapter);
                }
            }
        }
        finally
        {
            Release(factory);
        }
    }

    private static void Release(nint comObject) => ((delegate* unmanaged[Stdcall]<nint, uint>)(*(nint**)comObject)[2])(comObject);

    private static void Check(int hr, string call)
    {
        if (hr < 0)
        {
            throw new InvalidOperationException($"{call} failed (0x{hr:X8}).");
        }
    }

    private delegate bool AdapterVisitor(int index, nint adapter, AdapterDesc1* desc);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct AdapterDesc1
    {
        public fixed char Description[128];
        public uint VendorId;
        public uint DeviceId;
        public uint SubSysId;
        public uint Revision;
        public nuint DedicatedVideoMemory;
        public nuint DedicatedSystemMemory;
        public nuint SharedSystemMemory;
        public long AdapterLuid;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BufferDesc
    {
        public uint ByteWidth;
        public int Usage;
        public uint BindFlags;
        public uint CpuAccessFlags;
        public uint MiscFlags;
        public uint StructureByteStride;
    }

    /// <summary>D3D11_UNORDERED_ACCESS_VIEW_DESC for a buffer: format, dimension, then D3D11_BUFFER_UAV (padded to the union's 12 bytes).</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct UavDesc
    {
        public int Format;
        public int ViewDimension;
        public uint FirstElement;
        public uint NumElements;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MappedSubresource
    {
        public void* Data;
        public uint RowPitch;
        public uint DepthPitch;
    }

    [LibraryImport("d3d11.dll")]
    private static partial int D3D11CreateDevice(nint adapter, int driverType, nint software, uint flags, int* featureLevels, uint levels, uint sdkVersion, nint* device, int* obtainedLevel, nint* context);

    [LibraryImport("d3dcompiler_47.dll", StringMarshalling = StringMarshalling.Utf8)]
    private static partial int D3DCompile(void* source, nuint size, string sourceName, nint defines, nint include, string entryPoint, string target, uint flags1, uint flags2, nint* code, nint* errors);
}
