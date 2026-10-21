using HWSuite.Core.Model;

namespace HWSuite.Core.Collection;

/// <summary>
/// A source of hardware information. Providers never talk to each other: each one reads
/// its source and merges what it found into the shared <see cref="HardwareInventory"/>.
/// </summary>
public interface IInventoryProvider
{
    /// <summary>Short, stable name shown in diagnostics (e.g. "SMBIOS", "CPUID").</summary>
    string Name { get; }

    /// <summary>False when the provider cannot run on this OS or CPU; it is then skipped.</summary>
    bool IsSupported { get; }

    /// <summary>Reads the source and merges the result into <paramref name="inventory"/>.</summary>
    void Contribute(HardwareInventory inventory, ProviderContext context);
}

/// <summary>Lets a provider record a non-fatal note (partial data, missing permission).</summary>
public sealed class ProviderContext
{
    private readonly HardwareInventory _inventory;
    private readonly string _source;

    internal ProviderContext(HardwareInventory inventory, string source)
    {
        _inventory = inventory;
        _source = source;
    }

    public void Info(string message) => Add(DiagnosticSeverity.Info, message);

    public void Warn(string message) => Add(DiagnosticSeverity.Warning, message);

    private void Add(DiagnosticSeverity severity, string message) =>
        _inventory.Diagnostics.Add(new Diagnostic(_source, severity, message));
}

/// <summary>
/// Runs providers in order. A provider that throws does not stop the others: its error
/// becomes a diagnostic, so the inventory degrades gracefully instead of failing.
/// </summary>
public sealed class InventoryCollector
{
    private readonly IReadOnlyList<IInventoryProvider> _providers;
    private readonly TimeProvider _time;

    public InventoryCollector(IEnumerable<IInventoryProvider> providers, TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(providers);
        _providers = [.. providers];
        _time = time ?? TimeProvider.System;
    }

    public HardwareInventory Collect()
    {
        var inventory = new HardwareInventory
        {
            CollectedAt = _time.GetLocalNow(),
            MachineName = Environment.MachineName,
        };

        foreach (var provider in _providers)
        {
            if (!provider.IsSupported)
            {
                continue; // platform-specific providers (Windows vs. Linux) simply do not apply
            }

            try
            {
                provider.Contribute(inventory, new ProviderContext(inventory, provider.Name));
            }
#pragma warning disable CA1031 // A faulty provider must never take the whole collection down.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                inventory.Diagnostics.Add(new Diagnostic(provider.Name, DiagnosticSeverity.Error, $"{ex.GetType().Name}: {ex.Message}"));
            }
        }

        Finalize(inventory);
        return inventory;
    }

    private static void Finalize(HardwareInventory inventory)
    {
        if (inventory.Memory is { TotalInstalledBytes: null, Modules.Count: > 0 } memory)
        {
            memory.TotalInstalledBytes = memory.Modules.Sum(m => m.SizeBytes);
        }

        if (inventory.Processors.Count == 1 && inventory.Processors[0].Threads is null)
        {
            inventory.Processors[0].Threads = Environment.ProcessorCount;
        }
    }
}
