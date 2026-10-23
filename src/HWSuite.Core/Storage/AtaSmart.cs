using System.Buffers.Binary;
using System.Globalization;
using HWSuite.Core.Model;

namespace HWSuite.Core.Storage;

/// <summary>One ATA S.M.A.R.T. attribute: normalized values (higher is better) and the vendor's raw counter.</summary>
public sealed record SmartEntry(int Id, int Current, int Worst, int? Threshold, long Raw)
{
    /// <summary>The drive's own failure criterion: the normalized value fell to its threshold.</summary>
    public bool BelowThreshold => Threshold is > 0 and < 0xFE && Current > 0 && Current <= Threshold;
}

/// <summary>
/// Decodes the ATA SMART READ DATA (B0h/D0h) and READ THRESHOLDS (B0h/D1h) pages: 512 bytes each,
/// 30 entries of 12 bytes from offset 2, byte 511 a checksum (all bytes add up to 0 mod 256).
/// Also summarizes them into <see cref="DiskHealth"/> and a CrystalDiskInfo-style assessment.
/// </summary>
public static class AtaSmart
{
    public const int PageSize = 512;
    private const int Entries = 30;
    private const int EntrySize = 12;

    // Attributes whose raw value counts damaged sectors: any non-zero value deserves attention.
    private static readonly int[] SectorProblems = [5, 197, 198];

    /// <summary>True when the page passes its checksum (USB bridges that ignore the command return zeros or junk).</summary>
    public static bool IsValidPage(ReadOnlySpan<byte> page)
    {
        if (page.Length < PageSize || page[..PageSize].IndexOfAnyExcept((byte)0) < 0)
        {
            return false;
        }

        byte sum = 0;
        foreach (var b in page[..PageSize])
        {
            sum += b;
        }

        return sum == 0;
    }

    public static IReadOnlyList<SmartEntry> Parse(ReadOnlySpan<byte> data, ReadOnlySpan<byte> thresholds = default)
    {
        var limits = new Dictionary<int, int>();
        if (thresholds.Length >= PageSize)
        {
            for (var i = 0; i < Entries; i++)
            {
                var entry = thresholds.Slice(2 + (i * EntrySize), EntrySize);
                if (entry[0] != 0)
                {
                    limits[entry[0]] = entry[1];
                }
            }
        }

        var attributes = new List<SmartEntry>();
        if (data.Length < PageSize)
        {
            return attributes;
        }

        for (var i = 0; i < Entries; i++)
        {
            var entry = data.Slice(2 + (i * EntrySize), EntrySize);
            var id = entry[0];
            if (id == 0)
            {
                continue;
            }

            // Raw value: 6 bytes little-endian at offset 5.
            var raw = (long)BinaryPrimitives.ReadUInt32LittleEndian(entry[5..]) | ((long)BinaryPrimitives.ReadUInt16LittleEndian(entry[9..]) << 32);
            attributes.Add(new SmartEntry(id, entry[3], entry[4], limits.TryGetValue(id, out var t) ? t : null, raw));
        }

        return attributes;
    }

    /// <summary>Health summary from the attributes. <paramref name="ssd"/> enables wear estimation.</summary>
    public static DiskHealth Summarize(IReadOnlyList<SmartEntry> attributes, bool ssd)
    {
        ArgumentNullException.ThrowIfNull(attributes);
        var byId = attributes.GroupBy(a => a.Id).ToDictionary(g => g.Key, g => g.First());
        long? Raw(int id) => byId.TryGetValue(id, out var a) ? a.Raw : null;

        var health = new DiskHealth
        {
            SmartAttributes = attributes,
            Assessment = Assess(attributes),
            PowerOnHours = Raw(9) is { } hours ? hours & 0xFFFFFFFF : null, // some vendors pack minutes in the high bytes
            PowerCycles = Raw(12),
            UnsafeShutdowns = Raw(192) ?? Raw(174),
            ReallocatedSectors = Raw(5),
            PendingSectors = Raw(197),
        };

        // Temperature: low byte of 194 (or 190, "airflow"); the other bytes often hold min/max.
        foreach (var id in new[] { 194, 190 })
        {
            if (Raw(id) is { } t && (t & 0xFF) is > 0 and < 120)
            {
                health.TemperatureCelsius = (int)(t & 0xFF);
                break;
            }
        }

        if (ssd)
        {
            // Normalized "life left" counters, in the order vendors most commonly use them.
            foreach (var id in new[] { 231, 233, 177, 202, 169 })
            {
                if (byId.TryGetValue(id, out var life) && life.Current is > 0 and <= 100)
                {
                    health.PercentageUsed = 100 - life.Current;
                    break;
                }
            }
        }

        // 241/242 count logical blocks of 512 bytes on most drives; implausible values are left out.
        health.DataWrittenBytes = Lbas(Raw(241));
        health.DataReadBytes = Lbas(Raw(242));
        return health;
    }

    /// <summary>Bad: a value at its threshold. Caution: damaged, pending or uncorrectable sectors. Good: neither.</summary>
    public static DiskHealthStatus Assess(IReadOnlyList<SmartEntry> attributes)
    {
        ArgumentNullException.ThrowIfNull(attributes);
        if (attributes.Any(a => a.BelowThreshold))
        {
            return DiskHealthStatus.Bad;
        }

        return attributes.Any(a => SectorProblems.Contains(a.Id) && a.Raw > 0) ? DiskHealthStatus.Caution : DiskHealthStatus.Good;
    }

    /// <summary>NVMe: any critical warning bit is bad; little spare left or 90 % of the rated life used is a caution.</summary>
    public static DiskHealthStatus AssessNvme(DiskHealth health)
    {
        ArgumentNullException.ThrowIfNull(health);
        if (health.CriticalWarning is > 0)
        {
            return DiskHealthStatus.Bad;
        }

        return health.PercentageUsed >= 90 || health.AvailableSparePercent is < 10 ? DiskHealthStatus.Caution : DiskHealthStatus.Good;
    }

    private static long? Lbas(long? count) => count is > 0 and < (1L << 45) ? count * 512 : null; // < 16 PiB

    /// <summary>Attribute name, Portuguese or English; null for vendor-specific ids without a common meaning.</summary>
    public static string? Name(int id, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        if (!Names.TryGetValue(id, out var name))
        {
            return null;
        }

        return culture.TwoLetterISOLanguageName == "pt" ? name.Pt : name.En;
    }

    private static readonly Dictionary<int, (string En, string Pt)> Names = new()
    {
        [1] = ("Read Error Rate", "Taxa de erros de leitura"),
        [2] = ("Throughput Performance", "Desempenho de transferência"),
        [3] = ("Spin-Up Time", "Tempo de partida do motor"),
        [4] = ("Start/Stop Count", "Partidas/paradas do motor"),
        [5] = ("Reallocated Sectors Count", "Setores realocados"),
        [7] = ("Seek Error Rate", "Taxa de erros de posicionamento"),
        [8] = ("Seek Time Performance", "Desempenho de posicionamento"),
        [9] = ("Power-On Hours", "Horas ligado"),
        [10] = ("Spin Retry Count", "Tentativas de partida do motor"),
        [11] = ("Recalibration Retries", "Tentativas de recalibração"),
        [12] = ("Power Cycle Count", "Ciclos de liga/desliga"),
        [13] = ("Soft Read Error Rate", "Erros de leitura recuperáveis"),
        [167] = ("SSD Protect Mode", "Modo de proteção do SSD"),
        [168] = ("SATA PHY Error Count", "Erros na interface SATA"),
        [169] = ("Remaining Life", "Vida restante"),
        [170] = ("Available Reserved Space", "Espaço reserva disponível"),
        [171] = ("Program Fail Count", "Falhas de gravação"),
        [172] = ("Erase Fail Count", "Falhas de apagamento"),
        [173] = ("Average Erase Count", "Média de apagamentos por bloco"),
        [174] = ("Unexpected Power Loss Count", "Quedas de energia inesperadas"),
        [175] = ("Power Loss Protection Failure", "Falha na proteção contra queda de energia"),
        [177] = ("Wear Leveling Count", "Nivelamento de desgaste"),
        [179] = ("Used Reserved Block Count", "Blocos reserva usados"),
        [180] = ("Unused Reserved Block Count", "Blocos reserva livres"),
        [181] = ("Program Fail Count", "Falhas de gravação"),
        [182] = ("Erase Fail Count", "Falhas de apagamento"),
        [183] = ("SATA Downshift Error Count", "Reduções de velocidade SATA"),
        [184] = ("End-to-End Error", "Erros de integridade ponta a ponta"),
        [187] = ("Reported Uncorrectable Errors", "Erros incorrigíveis informados"),
        [188] = ("Command Timeout", "Comandos expirados"),
        [189] = ("High Fly Writes", "Gravações com cabeça alta"),
        [190] = ("Airflow Temperature", "Temperatura do fluxo de ar"),
        [191] = ("G-Sense Error Rate", "Impactos detectados"),
        [192] = ("Power-Off Retract Count", "Desligamentos de emergência"),
        [193] = ("Load/Unload Cycle Count", "Ciclos de carga/descarga da cabeça"),
        [194] = ("Temperature", "Temperatura"),
        [195] = ("Hardware ECC Recovered", "Erros corrigidos por ECC"),
        [196] = ("Reallocation Event Count", "Eventos de realocação"),
        [197] = ("Current Pending Sector Count", "Setores pendentes"),
        [198] = ("Uncorrectable Sector Count", "Setores incorrigíveis"),
        [199] = ("UltraDMA CRC Error Count", "Erros de CRC (cabo/conexão)"),
        [200] = ("Write Error Rate", "Taxa de erros de gravação"),
        [202] = ("Percent Lifetime Remaining", "Vida útil restante"),
        [206] = ("Flying Height", "Altura de voo da cabeça"),
        [210] = ("RAIN Successful Recovery", "Recuperações por RAIN"),
        [220] = ("Disk Shift", "Deslocamento do disco"),
        [222] = ("Loaded Hours", "Horas com cabeça carregada"),
        [223] = ("Load/Unload Retry Count", "Tentativas de carga/descarga"),
        [224] = ("Load Friction", "Atrito na carga"),
        [225] = ("Load/Unload Cycle Count", "Ciclos de carga/descarga"),
        [226] = ("Load-in Time", "Tempo de carga"),
        [230] = ("Drive Life Protection Status", "Proteção de vida útil"),
        [231] = ("SSD Life Left", "Vida restante do SSD"),
        [232] = ("Endurance Remaining", "Resistência restante"),
        [233] = ("Media Wearout Indicator", "Indicador de desgaste da mídia"),
        [234] = ("Average/Maximum Erase Count", "Apagamentos médios/máximos"),
        [235] = ("Good Block Count", "Blocos bons"),
        [240] = ("Head Flying Hours", "Horas de voo da cabeça"),
        [241] = ("Total Host Writes", "Total gravado pelo sistema"),
        [242] = ("Total Host Reads", "Total lido pelo sistema"),
        [243] = ("Total Host Writes (Expanded)", "Total gravado (estendido)"),
        [244] = ("Average Erase Count", "Média de apagamentos"),
        [245] = ("Maximum Erase Count", "Máximo de apagamentos"),
        [246] = ("Total Host Sector Writes", "Setores gravados pelo sistema"),
        [247] = ("Host Program Page Count", "Páginas gravadas pelo sistema"),
        [248] = ("Background Program Page Count", "Páginas gravadas internamente"),
        [249] = ("NAND Writes (1 GiB)", "Gravações na NAND (GiB)"),
        [250] = ("Read Error Retry Rate", "Tentativas por erro de leitura"),
        [254] = ("Free Fall Protection", "Proteção contra queda"),
    };
}
