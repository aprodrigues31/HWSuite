using System.Buffers.Binary;

namespace HWSuite.Core.Storage;

/// <summary>
/// Request buffers for reading ATA S.M.A.R.T. pages on Windows, as plain byte layouts:
/// SMART_RCV_DRIVE_DATA for SATA/ATA disks on the inbox drivers, and SCSI pass-through with an
/// ATA PASS-THROUGH CDB (SAT) for disks behind USB bridges.
/// </summary>
public static class AtaCommands
{
    public const byte SmartCommand = 0xB0;
    public const byte SmartReadData = 0xD0;
    public const byte SmartReadThresholds = 0xD1;
    private const byte SmartLbaMid = 0x4F; // the SMART signature the drive checks
    private const byte SmartLbaHigh = 0xC2;

    /// <summary>sizeof(SENDCMDINPARAMS) - 1: the request without its 1-byte buffer placeholder.</summary>
    public const int SendCmdInSize = 32;

    /// <summary>SENDCMDOUTPARAMS header (cBufferSize + DRIVERSTATUS) before the 512-byte page.</summary>
    public const int SendCmdOutHeader = 16;

    /// <summary>SENDCMDINPARAMS (pack 1): cBufferSize, IDEREGS {features, count, number, cylLow, cylHigh, driveHead, command}, bDriveNumber.</summary>
    public static byte[] SendCmdIn(byte feature, int driveNumber)
    {
        var b = new byte[SendCmdInSize];
        BinaryPrimitives.WriteInt32LittleEndian(b, AtaSmart.PageSize);
        b[4] = feature;
        b[5] = 1; // sector count
        b[6] = 1; // sector number
        b[7] = SmartLbaMid;
        b[8] = SmartLbaHigh;
        b[9] = (byte)(0xA0 | ((driveNumber & 1) << 4));
        b[10] = SmartCommand;
        b[12] = (byte)driveNumber;
        return b;
    }

    // SCSI_PASS_THROUGH (x64): Length(2) ScsiStatus PathId TargetId Lun CdbLength SenseInfoLength DataIn(1)
    // DataTransferLength(4)@12 TimeOutValue(4)@16 DataBufferOffset(8)@24 SenseInfoOffset(4)@32 Cdb[16]@36 -> 56 bytes.
    public const int PassThroughSize = 56;
    public const int SenseOffset = PassThroughSize;
    public const int SenseSize = 32;
    public const int DataOffset = SenseOffset + SenseSize; // 88, 8-byte aligned
    public const int PassThroughBufferSize = DataOffset + AtaSmart.PageSize;

    /// <summary>
    /// SCSI_PASS_THROUGH + sense + data buffer carrying a SMART command. <paramref name="sixteen"/> uses
    /// ATA PASS-THROUGH(16) (85h); otherwise the 12-byte form (A1h) some older USB bridges need.
    /// </summary>
    public static byte[] SatPassThrough(byte feature, bool sixteen)
    {
        var b = new byte[PassThroughBufferSize];
        BinaryPrimitives.WriteUInt16LittleEndian(b, PassThroughSize);
        b[6] = (byte)(sixteen ? 16 : 12); // CdbLength
        b[7] = SenseSize;
        b[8] = 1; // SCSI_IOCTL_DATA_IN
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(12), AtaSmart.PageSize);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(16), 5); // seconds
        BinaryPrimitives.WriteUInt64LittleEndian(b.AsSpan(24), DataOffset);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(32), SenseOffset);

        var cdb = b.AsSpan(36, 16);
        // byte 1: protocol 4 (PIO data-in) << 1; byte 2: T_DIR=1 (in), BYT_BLOK=1, T_LENGTH=2 (sector count) -> 0x0E
        if (sixteen)
        {
            cdb[0] = 0x85;
            cdb[1] = 4 << 1;
            cdb[2] = 0x0E;
            cdb[4] = feature;
            cdb[6] = 1; // sector count
            cdb[8] = 1; // LBA low
            cdb[10] = SmartLbaMid;
            cdb[12] = SmartLbaHigh;
            cdb[13] = 0xA0; // device
            cdb[14] = SmartCommand;
        }
        else
        {
            cdb[0] = 0xA1;
            cdb[1] = 4 << 1;
            cdb[2] = 0x0E;
            cdb[3] = feature;
            cdb[4] = 1;
            cdb[5] = 1;
            cdb[6] = SmartLbaMid;
            cdb[7] = SmartLbaHigh;
            cdb[8] = 0xA0;
            cdb[9] = SmartCommand;
        }

        return b;
    }
}
