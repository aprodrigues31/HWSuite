namespace HWSuite.Core.Model;

/// <summary>Removes values that identify one specific machine before a report is shared.</summary>
public static class InventoryRedactor
{
    public static void Redact(HardwareInventory inventory)
    {
        ArgumentNullException.ThrowIfNull(inventory);

        inventory.MachineName = null;
        if (inventory.System is { } system)
        {
            system.SerialNumber = null;
            system.Uuid = null;
        }

        if (inventory.Motherboard is { } board)
        {
            board.SerialNumber = null;
        }

        foreach (var module in inventory.Memory?.Modules ?? [])
        {
            module.SerialNumber = null;
            if (module.Spd is { } spd)
            {
                spd.SerialNumber = null;
            }
        }

        foreach (var disk in inventory.Disks)
        {
            disk.SerialNumber = null;
        }
    }
}
