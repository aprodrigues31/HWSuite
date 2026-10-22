using System.Runtime.InteropServices;
using HWSuite.Core.Collection;
using HWSuite.Core.Model;
using HWSuite.Core.Spd;
using HWSuite.Providers.Windows;
using LibreHardwareMonitor.Hardware;
using RAMSPDToolkit.I2CSMBus;
using RAMSPDToolkit.SPD;
using RAMSPDToolkit.SPD.Interop.Shared;

namespace HWSuite.Providers.Lhm;

/// <summary>
/// Reads every memory module's SPD EEPROM over SMBus and attaches the decoded data (chip maker,
/// manufacturing date, JEDEC and XMP profiles) to the modules from SMBIOS. SMBus access goes through
/// the PawnIO driver, so this needs administrator rights; without them it only leaves a note.
/// </summary>
public sealed partial class LhmSpdProvider : IInventoryProvider
{
    public string Name => "SPD";

    public bool IsSupported => OperatingSystem.IsWindows();

    public void Contribute(HardwareInventory inventory, ProviderContext context)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(context);

        var access = AccessStatus.Detect();
        if (!access.IsElevated || !access.PawnIoInstalled)
        {
            context.Info("module details (SPD, XMP) need administrator rights and the PawnIO driver");
            return;
        }

        List<SpdInfo> spds;
        try
        {
            spds = [.. ReadAll().Select(d => SpdDecoder.Decode(d.Data, d.ModuleManufacturer, d.DramManufacturer)).OfType<SpdInfo>()];
        }
#pragma warning disable CA1031 // SMBus access depends on chipset and firmware; failure must not cost the rest of the report.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            context.Warn($"could not read the memory SPD: {ex.Message}");
            return;
        }

        if (spds.Count == 0)
        {
            context.Info("no memory SPD found on the SMBus (virtual machine, or chipset not supported)");
            return;
        }

        inventory.Memory ??= new MemoryInfo();
        var attached = SpdMerger.Attach(inventory.Memory, spds);
        if (attached < spds.Count)
        {
            context.Info($"{spds.Count - attached} SPD(s) could not be matched to a memory slot");
        }
    }

    private sealed record SpdDump(byte Address, byte[] Data, string? ModuleManufacturer, string? DramManufacturer);

    /// <summary>
    /// Uses the SMBus that live sensors already opened (the desktop app); otherwise opens
    /// LibreHardwareMonitor's memory group just for this read, which installs its PawnIO SMBus
    /// driver and detects the buses. The toolkit's global SMBus mutex keeps reads from colliding.
    /// </summary>
    private static List<SpdDump> ReadAll()
    {
        if (LhmRuntime.AnyOpen && SMBusManager.RegisteredSMBuses.Count > 0)
        {
            return ReadBuses();
        }

        var computer = new Computer { IsMemoryEnabled = true };
        LhmRuntime.Open(computer);
        try
        {
            return ReadBuses();
        }
        finally
        {
            LhmRuntime.Close(computer);
        }
    }

    private static List<SpdDump> ReadBuses()
    {
        var dumps = new List<SpdDump>();
        var seen = new HashSet<(SMBusInterface, byte)>();

        // Each SPD byte read is followed by a 1 ms sleep, which lasts ~15 ms at Windows' default timer
        // resolution: ask for 1 ms while reading.
        _ = TimeBeginPeriod(1);
        try
        {
            foreach (var bus in SMBusManager.RegisteredSMBuses)
            {
                for (var address = SPDConstants.SPD_BEGIN; address <= SPDConstants.SPD_END; address++)
                {
                    if (!seen.Add((bus, address)) || new SPDDetector(bus, address).Accessor is not { } spd)
                    {
                        continue;
                    }

                    var type = spd.At(2);
                    var length = SpdDecoder.Length(type);
                    if (length == 0)
                    {
                        continue; // DDR3 or older: SMBIOS already has the basics
                    }

                    var data = new byte[length];
                    foreach (var (from, to) in SpdDecoder.RangesToRead(type, i => spd.At((ushort)i)))
                    {
                        for (var i = from; i <= to; i++)
                        {
                            data[i] = spd.At((ushort)i);
                        }
                    }

                    dumps.Add(new SpdDump(address, data, spd.GetModuleManufacturerString(), spd.GetDRAMManufacturerString()));
                }
            }
        }
        finally
        {
            _ = TimeEndPeriod(1);
        }

        return dumps;
    }

    [LibraryImport("winmm.dll", EntryPoint = "timeBeginPeriod")]
    private static partial uint TimeBeginPeriod(uint milliseconds);

    [LibraryImport("winmm.dll", EntryPoint = "timeEndPeriod")]
    private static partial uint TimeEndPeriod(uint milliseconds);
}
