using System.Globalization;

namespace HWSuite.Core.Localization;

/// <summary>
/// User-facing text in Portuguese (Brazil) and English. Kept as plain dictionaries so the
/// core has no resource-file tooling; a missing key falls back to English, then to the key.
/// </summary>
public sealed class Strings
{
    private static readonly Dictionary<string, string> English = new(StringComparer.Ordinal)
    {
        ["report.title"] = "HWSuite hardware report",
        ["report.generated"] = "Generated",
        ["report.machine"] = "Machine",
        ["section.os"] = "Operating system",
        ["section.system"] = "System",
        ["section.motherboard"] = "Motherboard",
        ["section.bios"] = "BIOS",
        ["section.cpu"] = "Processor",
        ["section.caches"] = "Caches",
        ["section.memory"] = "Memory",
        ["section.gpu"] = "Graphics",
        ["section.disks"] = "Storage",
        ["section.diagnostics"] = "Diagnostics",
        ["field.name"] = "Name",
        ["field.version"] = "Version",
        ["field.build"] = "Build",
        ["field.architecture"] = "Architecture",
        ["field.manufacturer"] = "Manufacturer",
        ["field.product"] = "Model",
        ["field.family"] = "Family",
        ["field.sku"] = "SKU",
        ["field.serial"] = "Serial number",
        ["field.uuid"] = "UUID",
        ["field.chassis"] = "Chassis",
        ["field.vendor"] = "Vendor",
        ["field.releaseDate"] = "Release date",
        ["field.smbios"] = "SMBIOS version",
        ["field.socket"] = "Socket",
        ["field.signature"] = "Signature",
        ["field.familyModelStepping"] = "Family / model / stepping",
        ["field.coresThreads"] = "Cores / threads",
        ["field.clocks"] = "Clock (bus / max / current)",
        ["field.instructions"] = "Instructions",
        ["field.hypervisor"] = "Running under a hypervisor",
        ["field.total"] = "Total installed",
        ["field.slots"] = "Slots (used / total)",
        ["field.maxCapacity"] = "Maximum capacity",
        ["field.slot"] = "Slot",
        ["field.size"] = "Size",
        ["field.type"] = "Type",
        ["field.speed"] = "Speed (rated / configured)",
        ["field.ranks"] = "Ranks",
        ["field.partNumber"] = "Part number",
        ["field.pciId"] = "PCI ID (vendor:device / subsystem)",
        ["field.dedicatedMemory"] = "Dedicated memory",
        ["field.sharedMemory"] = "Shared memory",
        ["field.driver"] = "Driver (version / date)",
        ["field.vbios"] = "Video BIOS",
        ["field.model"] = "Model",
        ["field.firmware"] = "Firmware",
        ["field.bus"] = "Interface",
        ["field.media"] = "Media",
        ["field.removable"] = "Removable",
        ["field.temperature"] = "Temperature",
        ["field.wear"] = "Wear (used / spare)",
        ["field.powerOn"] = "Power-on hours / cycles",
        ["field.unsafeShutdowns"] = "Unsafe shutdowns",
        ["field.mediaErrors"] = "Media errors",
        ["field.dataRw"] = "Data read / written",
        ["field.criticalWarning"] = "Critical warning flags",
        ["cache.shared"] = "shared by {0} threads",
        ["cache.sharedOne"] = "one per thread",
        ["cache.detail"] = "{0}, {1}-way, {2} B line, {3}",
        ["value.yes"] = "Yes",
        ["value.no"] = "No",
        ["value.none"] = "None detected",
        ["severity.Info"] = "info",
        ["severity.Warning"] = "warning",
        ["severity.Error"] = "error",
        ["cli.usage"] = """
            Usage:
              hwsuite                       Print a text summary of this machine.
              hwsuite report [options]      Write a full report.

            Options for 'report':
              -f, --format <txt|html|json>  Output format (default: txt).
              -o, --out <path>              Write to a file instead of the console.
                  --redact                  Remove serial numbers and UUIDs.
                  --lang <pt-BR|en>         Report language (default: system language).

            General:
              -h, --help                    Show this help.
              -v, --version                 Show the version.
            """,
        ["cli.unknownArgument"] = "Unknown argument: {0}",
        ["cli.missingValue"] = "Missing value for {0}",
        ["cli.badFormat"] = "Unsupported format '{0}'. Use txt, html or json.",
        ["cli.written"] = "Report written to {0}",
    };

    private static readonly Dictionary<string, string> Portuguese = new(StringComparer.Ordinal)
    {
        ["report.title"] = "Relatório de hardware HWSuite",
        ["report.generated"] = "Gerado em",
        ["report.machine"] = "Máquina",
        ["section.os"] = "Sistema operacional",
        ["section.system"] = "Sistema",
        ["section.motherboard"] = "Placa-mãe",
        ["section.bios"] = "BIOS",
        ["section.cpu"] = "Processador",
        ["section.caches"] = "Caches",
        ["section.memory"] = "Memória",
        ["section.gpu"] = "Vídeo",
        ["section.disks"] = "Armazenamento",
        ["section.diagnostics"] = "Diagnóstico",
        ["field.name"] = "Nome",
        ["field.version"] = "Versão",
        ["field.build"] = "Build",
        ["field.architecture"] = "Arquitetura",
        ["field.manufacturer"] = "Fabricante",
        ["field.product"] = "Modelo",
        ["field.family"] = "Família",
        ["field.sku"] = "SKU",
        ["field.serial"] = "Número de série",
        ["field.uuid"] = "UUID",
        ["field.chassis"] = "Gabinete",
        ["field.vendor"] = "Fornecedor",
        ["field.releaseDate"] = "Data de lançamento",
        ["field.smbios"] = "Versão SMBIOS",
        ["field.socket"] = "Soquete",
        ["field.signature"] = "Assinatura",
        ["field.familyModelStepping"] = "Família / modelo / stepping",
        ["field.coresThreads"] = "Núcleos / threads",
        ["field.clocks"] = "Clock (barramento / máx. / atual)",
        ["field.instructions"] = "Instruções",
        ["field.hypervisor"] = "Executando sob hipervisor",
        ["field.total"] = "Total instalado",
        ["field.slots"] = "Slots (em uso / total)",
        ["field.maxCapacity"] = "Capacidade máxima",
        ["field.slot"] = "Slot",
        ["field.size"] = "Tamanho",
        ["field.type"] = "Tipo",
        ["field.speed"] = "Velocidade (nominal / configurada)",
        ["field.ranks"] = "Ranks",
        ["field.partNumber"] = "Part number",
        ["field.pciId"] = "ID PCI (fornecedor:dispositivo / subsistema)",
        ["field.dedicatedMemory"] = "Memória dedicada",
        ["field.sharedMemory"] = "Memória compartilhada",
        ["field.driver"] = "Driver (versão / data)",
        ["field.vbios"] = "BIOS de vídeo",
        ["field.model"] = "Modelo",
        ["field.firmware"] = "Firmware",
        ["field.bus"] = "Interface",
        ["field.media"] = "Mídia",
        ["field.removable"] = "Removível",
        ["field.temperature"] = "Temperatura",
        ["field.wear"] = "Desgaste (usado / reserva)",
        ["field.powerOn"] = "Horas ligado / ciclos",
        ["field.unsafeShutdowns"] = "Desligamentos inseguros",
        ["field.mediaErrors"] = "Erros de mídia",
        ["field.dataRw"] = "Dados lidos / gravados",
        ["field.criticalWarning"] = "Alertas críticos",
        ["cache.shared"] = "compartilhado por {0} threads",
        ["cache.sharedOne"] = "um por thread",
        ["cache.detail"] = "{0}, {1} vias, linha de {2} B, {3}",
        ["value.yes"] = "Sim",
        ["value.no"] = "Não",
        ["value.none"] = "Nenhum detectado",
        ["severity.Info"] = "info",
        ["severity.Warning"] = "aviso",
        ["severity.Error"] = "erro",
        ["cli.usage"] = """
            Uso:
              hwsuite                       Mostra um resumo em texto desta máquina.
              hwsuite report [opções]       Gera o relatório completo.

            Opções de 'report':
              -f, --format <txt|html|json>  Formato de saída (padrão: txt).
              -o, --out <caminho>           Grava em arquivo em vez de mostrar no console.
                  --redact                  Remove números de série e UUIDs.
                  --lang <pt-BR|en>         Idioma do relatório (padrão: idioma do sistema).

            Geral:
              -h, --help                    Mostra esta ajuda.
              -v, --version                 Mostra a versão.
            """,
        ["cli.unknownArgument"] = "Argumento desconhecido: {0}",
        ["cli.missingValue"] = "Falta o valor de {0}",
        ["cli.badFormat"] = "Formato '{0}' não suportado. Use txt, html ou json.",
        ["cli.written"] = "Relatório gravado em {0}",
    };

    private readonly Dictionary<string, string> _table;

    private Strings(CultureInfo culture, Dictionary<string, string> table)
    {
        Culture = culture;
        _table = table;
    }

    public CultureInfo Culture { get; }

    /// <summary>Portuguese for any "pt" culture, English otherwise.</summary>
    public static Strings For(CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        var portuguese = culture.TwoLetterISOLanguageName == "pt";
        return new Strings(culture, portuguese ? Portuguese : English);
    }

    public static Strings For(string? cultureName) =>
        For(string.IsNullOrWhiteSpace(cultureName) ? CultureInfo.CurrentUICulture : CultureInfo.GetCultureInfo(cultureName));

    public string this[string key] =>
        _table.TryGetValue(key, out var value) ? value
        : English.TryGetValue(key, out var fallback) ? fallback
        : key;

    public string Format(string key, params object?[] args) => string.Format(Culture, this[key], args);

    internal static IReadOnlyCollection<string> EnglishKeys => English.Keys;

    internal static IReadOnlyCollection<string> PortugueseKeys => Portuguese.Keys;
}
