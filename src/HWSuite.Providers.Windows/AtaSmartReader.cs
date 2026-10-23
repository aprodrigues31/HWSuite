using System.Globalization;
using System.Runtime.Versioning;
using HWSuite.Core.Storage;

namespace HWSuite.Providers.Windows;

/// <summary>
/// Reads the ATA S.M.A.R.T. data and threshold pages of a physical drive. SATA/ATA disks go through
/// SMART_RCV_DRIVE_DATA; USB disks through SCSI pass-through with a SAT ATA PASS-THROUGH command,
/// which most USB-SATA bridges understand. Needs administrator rights (read/write device handle).
/// </summary>
[SupportedOSPlatform("windows")]
internal static class AtaSmartReader
{
    private const uint IoctlSmartReceiveDriveData = 0x0007_C088;
    private const uint IoctlScsiPassThrough = 0x0004_D004;
    private const uint GenericReadWrite = 0x8000_0000 | 0x4000_0000;

    public enum Result
    {
        Ok,
        AccessDenied,
        NotSupported,
    }

    public static (Result Result, byte[]? Data, byte[]? Thresholds) Read(int driveIndex, bool usb)
    {
        using var handle = Native.CreateFile(
            string.Create(CultureInfo.InvariantCulture, $@"\\.\PhysicalDrive{driveIndex}"),
            GenericReadWrite, Native.FileShareReadWrite, 0, Native.OpenExisting, 0, 0);
        if (handle.IsInvalid)
        {
            return (Result.AccessDenied, null, null);
        }

        byte[]? Page(byte feature)
        {
            if (!usb)
            {
                var output = Native.Ioctl(handle, IoctlSmartReceiveDriveData, AtaCommands.SendCmdIn(feature, driveIndex),
                    AtaCommands.SendCmdOutHeader + AtaSmart.PageSize);
                return output is { Length: >= AtaCommands.SendCmdOutHeader + AtaSmart.PageSize } ? output[AtaCommands.SendCmdOutHeader..] : null;
            }

            foreach (var sixteen in new[] { true, false })
            {
                var request = AtaCommands.SatPassThrough(feature, sixteen);
                var output = Native.Ioctl(handle, IoctlScsiPassThrough, request, request.Length);
                if (output is { Length: AtaCommands.PassThroughBufferSize } && output[2] == 0) // ScsiStatus GOOD
                {
                    var page = output[AtaCommands.DataOffset..];
                    if (AtaSmart.IsValidPage(page))
                    {
                        return page;
                    }
                }
            }

            return null;
        }

        var data = Page(AtaCommands.SmartReadData);
        if (data is null || !AtaSmart.IsValidPage(data))
        {
            return (Result.NotSupported, null, null);
        }

        var thresholds = Page(AtaCommands.SmartReadThresholds);
        return (Result.Ok, data, thresholds is not null && AtaSmart.IsValidPage(thresholds) ? thresholds : null);
    }
}
