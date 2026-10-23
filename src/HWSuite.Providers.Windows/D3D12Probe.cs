using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace HWSuite.Providers.Windows;

/// <summary>
/// What Direct3D 12 says about an adapter: highest feature level (e.g. 12_2, "DirectX 12 Ultimate"),
/// highest shader model and the DirectX Raytracing tier. Creates a device on the adapter and asks
/// ID3D12Device::CheckFeatureSupport; no window or rendering is involved.
/// </summary>
[SupportedOSPlatform("windows")]
internal static unsafe partial class D3D12Probe
{
    private static readonly Guid IidD3D12Device = new("189819f1-1db6-4b57-be54-1821339b85f7");

    private const int FeatureLevel11_0 = 0xB000;
    private static readonly int[] FeatureLevels = [0xB000, 0xB100, 0xC000, 0xC100, 0xC200];
    private const int FeatureFeatureLevels = 2; // D3D12_FEATURE_FEATURE_LEVELS
    private const int FeatureShaderModel = 7; // D3D12_FEATURE_SHADER_MODEL
    private const int FeatureOptions5 = 27; // D3D12_FEATURE_D3D12_OPTIONS5
    private const int CheckFeatureSupportSlot = 13; // IUnknown 0-2, ID3D12Object 3-6, GetNodeCount 7 ... CheckFeatureSupport 13

    public sealed record Result(string FeatureLevel, string? ShaderModel, string? RaytracingTier);

    /// <param name="adapter">An IDXGIAdapter pointer (IUnknown).</param>
    public static Result? Probe(nint adapter)
    {
        nint device;
        try
        {
            if (D3D12CreateDevice(adapter, FeatureLevel11_0, IidD3D12Device, out device) < 0 || device == 0)
            {
                return null;
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return null; // Windows without Direct3D 12
        }

        try
        {
            var vtbl = *(nint**)device;
            var check = (delegate* unmanaged[Stdcall]<nint, int, void*, uint, int>)vtbl[CheckFeatureSupportSlot];

            // { UINT NumFeatureLevels; const D3D_FEATURE_LEVEL* pFeatureLevelsRequested; D3D_FEATURE_LEVEL MaxSupportedFeatureLevel; }
            var level = 0;
            fixed (int* requested = FeatureLevels)
            {
                var levels = stackalloc byte[24];
                *(uint*)levels = (uint)FeatureLevels.Length;
                *(int**)(levels + 8) = requested;
                if (check(device, FeatureFeatureLevels, levels, 24) >= 0)
                {
                    level = *(int*)(levels + 16);
                }
            }

            if (level == 0)
            {
                return null;
            }

            // The driver lowers the requested model to the highest it supports; unknown models fail, so step down.
            string? shaderModel = null;
            for (var model = 0x69; model >= 0x50; model--)
            {
                var sm = model;
                if (check(device, FeatureShaderModel, &sm, sizeof(int)) >= 0)
                {
                    shaderModel = $"{sm >> 4}.{sm & 0xF}";
                    break;
                }
            }

            // { BOOL SRVOnlyTiledResourceTier3; D3D12_RENDER_PASS_TIER; D3D12_RAYTRACING_TIER }: tier 10 = 1.0, 11 = 1.1.
            string? raytracing = null;
            var options5 = stackalloc int[3];
            if (check(device, FeatureOptions5, options5, 12) >= 0 && options5[2] >= 10)
            {
                raytracing = $"{options5[2] / 10}.{options5[2] % 10}";
            }

            return new Result($"{(level >> 12) & 0xF}_{(level >> 8) & 0xF}", shaderModel, raytracing);
        }
        finally
        {
            var vtbl = *(nint**)device;
            ((delegate* unmanaged[Stdcall]<nint, uint>)vtbl[2])(device);
        }
    }

    [LibraryImport("d3d12.dll")]
    private static partial int D3D12CreateDevice(nint adapter, int minimumFeatureLevel, in Guid riid, out nint device);
}
