using System.Runtime.Intrinsics.X86;
using HWSuite.Core.Collection;
using HWSuite.Core.Cpuid;
using HWSuite.Core.Model;

namespace HWSuite.Providers.Common;

/// <summary>Executes the CPUID instruction directly. Works in user mode, no driver needed.</summary>
public sealed class X86CpuidReader : ICpuidReader
{
    public static bool IsAvailable => X86Base.IsSupported;

    public CpuidRegisters Read(uint leaf, uint subleaf = 0)
    {
        var (eax, ebx, ecx, edx) = X86Base.CpuId(unchecked((int)leaf), unchecked((int)subleaf));
        return new CpuidRegisters(unchecked((uint)eax), unchecked((uint)ebx), unchecked((uint)ecx), unchecked((uint)edx));
    }
}

/// <summary>
/// Fills processor identity, instruction sets and caches from CPUID. Runs after SMBIOS so it
/// can overwrite the often generic SMBIOS processor strings with the CPU's own brand string.
/// </summary>
public sealed class CpuidProvider : IInventoryProvider
{
    private readonly ICpuidReader _reader;

    public CpuidProvider()
        : this(new X86CpuidReader())
    {
    }

    public CpuidProvider(ICpuidReader reader) => _reader = reader;

    public string Name => "CPUID";

    public bool IsSupported => _reader is not X86CpuidReader || X86CpuidReader.IsAvailable;

    public void Contribute(HardwareInventory inventory, ProviderContext context)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(context);

        var result = CpuidDecoder.Decode(_reader);
        if (inventory.Processors.Count == 0)
        {
            inventory.Processors.Add(new ProcessorInfo());
        }

        // CPUID only sees the package the thread runs on; multi-socket systems use identical CPUs.
        foreach (var cpu in inventory.Processors)
        {
            CpuidDecoder.Apply(result, cpu);
        }

        if (result.Caches.Count == 0)
        {
            context.Info("CPU does not report deterministic cache parameters");
        }
    }
}
