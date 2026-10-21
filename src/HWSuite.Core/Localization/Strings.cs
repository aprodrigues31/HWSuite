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
        ["report.tool"] = "Tool",
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
        ["field.virtualization"] = "Virtualization",
        ["value.vbsHost"] = "Physical machine with Hyper-V enabled (VBS / memory integrity)",
        ["value.virtualMachine"] = "Virtual machine",
        ["value.virtualMachineOf"] = "Virtual machine ({0})",
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
        ["sensorKind.Temperature"] = "Temperatures",
        ["sensorKind.Voltage"] = "Voltages",
        ["sensorKind.Current"] = "Currents",
        ["sensorKind.Power"] = "Power",
        ["sensorKind.Clock"] = "Clocks",
        ["sensorKind.Load"] = "Load",
        ["sensorKind.Fan"] = "Fans",
        ["sensorKind.Control"] = "Fan control",
        ["sensorKind.Level"] = "Levels",
        ["sensorKind.Frequency"] = "Frequencies",
        ["sensorKind.Flow"] = "Flow",
        ["sensorKind.Factor"] = "Factors",
        ["sensorKind.Data"] = "Data",
        ["sensorKind.SmallData"] = "Data",
        ["sensorKind.Throughput"] = "Throughput",
        ["sensorKind.TimeSpan"] = "Time",
        ["sensorKind.Energy"] = "Energy",
        ["sensorKind.Noise"] = "Noise",
        ["sensorKind.Timing"] = "Timings",
        ["sensorKind.Humidity"] = "Humidity",
        ["sensorKind.Conductivity"] = "Conductivity",
        ["sensorKind.Other"] = "Other",
        ["sensors.sensor"] = "Sensor",
        ["sensors.value"] = "Value",
        ["sensors.min"] = "Min",
        ["sensors.max"] = "Max",
        ["sensors.none"] = "No sensors could be read.",
        ["sensors.title"] = "HWSuite sensors",
        ["sensors.watchHint"] = "Refreshing every {0} s. Press Ctrl+C to stop.",
        ["sensors.logging"] = "Logging to {0} every {1} s. Press Ctrl+C to stop.",
        ["sensors.logDone"] = "{0} samples written to {1}",
        ["driver.statusInstalled"] = "PawnIO driver: installed",
        ["driver.statusMissing"] = "PawnIO driver: not installed",
        ["driver.statusPendingRemoval"] = "PawnIO driver: removed, pending reboot (still active until Windows restarts)",
        ["driver.rebootToFinish"] = "Restart Windows to finish removing the driver.",
        ["driver.statusElevated"] = "Administrator: yes",
        ["driver.statusNotElevated"] = "Administrator: no",
        ["driver.about"] = "PawnIO is a signed kernel driver by namazso ({0}). HWSuite installs the official package; it does not ship the driver.",
        ["driver.confirmInstall"] = "Install the PawnIO driver now? Windows will ask for administrator permission. [y/N] ",
        ["driver.confirmUninstall"] = "Remove the PawnIO driver? Other tools that use it (OpenRGB, FanControl, LibreHardwareMonitor) will stop reading sensors. [y/N] ",
        ["driver.needYes"] = "Confirmation required. Run again with --yes to proceed without a prompt.",
        ["driver.cancelled"] = "Cancelled. Nothing was changed.",
        ["driver.elevating"] = "Requesting administrator permission...",
        ["driver.uacDenied"] = "Administrator permission was not granted. Nothing was changed.",
        ["driver.step.UsingWinget"] = "Installing through winget ({0})...",
        ["driver.step.WingetFailed"] = "winget did not install the driver ({0}); trying the official installer from GitHub.",
        ["driver.step.Downloading"] = "Downloading {0}",
        ["driver.step.VerifyingSignature"] = "Digital signature: {0}",
        ["driver.step.RunningInstaller"] = "Running the official installer silently...",
        ["driver.step.Uninstalling"] = "Removing through winget ({0})...",
        ["driver.outcome.Installed"] = "PawnIO installed ({0}).",
        ["driver.outcome.AlreadyInstalled"] = "PawnIO is already installed.",
        ["driver.outcome.Uninstalled"] = "PawnIO removed.",
        ["driver.outcome.NotInstalled"] = "PawnIO is not installed.",
        ["driver.outcome.NotElevated"] = "This operation requires administrator rights.",
        ["driver.outcome.DownloadFailed"] = "Could not download the installer: {0}",
        ["driver.outcome.SignatureRejected"] = "Installer rejected: its signature is not from the PawnIO author ({0}). Install manually from the official site.",
        ["driver.outcome.InstallerFailed"] = "Installation failed: {0}",
        ["driver.nextStep"] = "Now open the terminal as administrator and run: hwsuite sensors",
        ["sensors.offerDriver"] = "The PawnIO driver is missing, so CPU and motherboard sensors cannot be read. Install it now? Windows will ask for administrator permission. [y/N] ",
        ["sensors.unsupported"] = "Real-time sensors are available on Windows only.",
        ["sensors.offerElevate"] = "This terminal is not running as administrator, so CPU, motherboard and disk sensors cannot be read. Reopen as administrator? [y/N] ",
        ["sensors.elevatedWindow"] = "Opened in a new administrator window.",
        ["sensors.lowLevelZero"] = "The processor returned only zeros even with administrator rights and PawnIO. If PawnIO was just installed, restart Windows and try again.",
        ["sensors.notElevated"] = "Not running as administrator: CPU, motherboard and memory sensors are limited. Open the terminal with \"Run as administrator\".",
        ["sensors.noPawnIo"] = "PawnIO driver not found: CPU and motherboard sensors need it. Install with: hwsuite driver install",
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
              hwsuite sensors [options]     Show live sensors (temperatures, voltages, fans, power).
              hwsuite driver <status|install|uninstall> [--yes]
                                            Check, install or remove the PawnIO driver.

            Options for 'report':
              -f, --format <txt|html|json>  Output format (default: txt).
              -o, --out <path>              Write to a file instead of the console.
                  --redact                  Remove serial numbers and UUIDs.
                  --lang <pt-BR|en>         Report language (default: system language).

            Options for 'sensors':
              -w, --watch                   Keep refreshing the table until Ctrl+C.
                  --log <file.csv>          Record samples to a CSV file until Ctrl+C.
              -i, --interval <seconds>      Refresh / sampling interval (default: 1).
                  --duration <seconds>      Stop automatically after this time.

            Most motherboard and CPU sensors need administrator rights and the PawnIO driver
            (hwsuite driver install). GPU and disk sensors work without them.

            General:
              -h, --help                    Show this help.
              -v, --version                 Show the version.
            """,
        ["cli.unknownArgument"] = "Unknown argument: {0}",
        ["cli.missingValue"] = "Missing value for {0}",
        ["cli.badFormat"] = "Unsupported format '{0}'. Use txt, html or json.",
        ["cli.badNumber"] = "Invalid number for {0}: {1}",
        ["cli.written"] = "Report written to {0}",
    };

    private static readonly Dictionary<string, string> Portuguese = new(StringComparer.Ordinal)
    {
        ["report.title"] = "Relatório de hardware HWSuite",
        ["report.generated"] = "Gerado em",
        ["report.machine"] = "Máquina",
        ["report.tool"] = "Ferramenta",
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
        ["field.virtualization"] = "Virtualização",
        ["value.vbsHost"] = "Máquina física com Hyper-V ativo (VBS / integridade de memória)",
        ["value.virtualMachine"] = "Máquina virtual",
        ["value.virtualMachineOf"] = "Máquina virtual ({0})",
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
        ["sensorKind.Temperature"] = "Temperaturas",
        ["sensorKind.Voltage"] = "Tensões",
        ["sensorKind.Current"] = "Correntes",
        ["sensorKind.Power"] = "Consumo",
        ["sensorKind.Clock"] = "Clocks",
        ["sensorKind.Load"] = "Utilização",
        ["sensorKind.Fan"] = "Ventoinhas",
        ["sensorKind.Control"] = "Controle de ventoinhas",
        ["sensorKind.Level"] = "Níveis",
        ["sensorKind.Frequency"] = "Frequências",
        ["sensorKind.Flow"] = "Vazão",
        ["sensorKind.Factor"] = "Fatores",
        ["sensorKind.Data"] = "Dados",
        ["sensorKind.SmallData"] = "Dados",
        ["sensorKind.Throughput"] = "Taxa de transferência",
        ["sensorKind.TimeSpan"] = "Tempo",
        ["sensorKind.Energy"] = "Energia",
        ["sensorKind.Noise"] = "Ruído",
        ["sensorKind.Timing"] = "Temporizações",
        ["sensorKind.Humidity"] = "Umidade",
        ["sensorKind.Conductivity"] = "Condutividade",
        ["sensorKind.Other"] = "Outros",
        ["sensors.sensor"] = "Sensor",
        ["sensors.value"] = "Atual",
        ["sensors.min"] = "Mín.",
        ["sensors.max"] = "Máx.",
        ["sensors.none"] = "Nenhum sensor pôde ser lido.",
        ["sensors.title"] = "Sensores HWSuite",
        ["sensors.watchHint"] = "Atualizando a cada {0} s. Pressione Ctrl+C para parar.",
        ["sensors.logging"] = "Gravando em {0} a cada {1} s. Pressione Ctrl+C para parar.",
        ["sensors.logDone"] = "{0} amostras gravadas em {1}",
        ["driver.statusInstalled"] = "Driver PawnIO: instalado",
        ["driver.statusMissing"] = "Driver PawnIO: não instalado",
        ["driver.statusPendingRemoval"] = "Driver PawnIO: removido, pendente de reinício (continua ativo até reiniciar o Windows)",
        ["driver.rebootToFinish"] = "Reinicie o Windows para concluir a remoção do driver.",
        ["driver.statusElevated"] = "Administrador: sim",
        ["driver.statusNotElevated"] = "Administrador: não",
        ["driver.about"] = "O PawnIO é um driver de kernel assinado, de namazso ({0}). O HWSuite instala o pacote oficial; não distribui o driver.",
        ["driver.confirmInstall"] = "Instalar o driver PawnIO agora? O Windows vai pedir permissão de administrador. [s/N] ",
        ["driver.confirmUninstall"] = "Remover o driver PawnIO? Outras ferramentas que o usam (OpenRGB, FanControl, LibreHardwareMonitor) deixarão de ler sensores. [s/N] ",
        ["driver.needYes"] = "É preciso confirmar. Rode de novo com --yes para seguir sem pergunta.",
        ["driver.cancelled"] = "Cancelado. Nada foi alterado.",
        ["driver.elevating"] = "Solicitando permissão de administrador...",
        ["driver.uacDenied"] = "A permissão de administrador não foi concedida. Nada foi alterado.",
        ["driver.step.UsingWinget"] = "Instalando pelo winget ({0})...",
        ["driver.step.WingetFailed"] = "O winget não instalou o driver ({0}); tentando o instalador oficial do GitHub.",
        ["driver.step.Downloading"] = "Baixando {0}",
        ["driver.step.VerifyingSignature"] = "Assinatura digital: {0}",
        ["driver.step.RunningInstaller"] = "Executando o instalador oficial em modo silencioso...",
        ["driver.step.Uninstalling"] = "Removendo pelo winget ({0})...",
        ["driver.outcome.Installed"] = "PawnIO instalado ({0}).",
        ["driver.outcome.AlreadyInstalled"] = "O PawnIO já está instalado.",
        ["driver.outcome.Uninstalled"] = "PawnIO removido.",
        ["driver.outcome.NotInstalled"] = "O PawnIO não está instalado.",
        ["driver.outcome.NotElevated"] = "Esta operação exige privilégio de administrador.",
        ["driver.outcome.DownloadFailed"] = "Não foi possível baixar o instalador: {0}",
        ["driver.outcome.SignatureRejected"] = "Instalador recusado: a assinatura não é do autor do PawnIO ({0}). Instale manualmente pelo site oficial.",
        ["driver.outcome.InstallerFailed"] = "A instalação falhou: {0}",
        ["driver.nextStep"] = "Agora abra o terminal como administrador e rode: hwsuite sensors",
        ["sensors.offerDriver"] = "O driver PawnIO não está instalado, então os sensores de processador e placa-mãe não podem ser lidos. Instalar agora? O Windows vai pedir permissão de administrador. [s/N] ",
        ["sensors.unsupported"] = "Os sensores em tempo real estão disponíveis apenas no Windows.",
        ["sensors.offerElevate"] = "Este terminal não está como administrador, então os sensores de processador, placa-mãe e discos não podem ser lidos. Reabrir como administrador? [s/N] ",
        ["sensors.elevatedWindow"] = "Aberto em uma nova janela de administrador.",
        ["sensors.lowLevelZero"] = "O processador retornou apenas zeros mesmo com administrador e PawnIO. Se o PawnIO acabou de ser instalado, reinicie o Windows e tente de novo.",
        ["sensors.notElevated"] = "Sem privilégio de administrador: sensores de processador, placa-mãe e memória ficam limitados. Abra o terminal com \"Executar como administrador\".",
        ["sensors.noPawnIo"] = "Driver PawnIO não encontrado: os sensores de processador e placa-mãe precisam dele. Instale com: hwsuite driver install",
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
              hwsuite sensors [opções]      Mostra os sensores ao vivo (temperaturas, tensões, ventoinhas, consumo).
              hwsuite driver <status|install|uninstall> [--yes]
                                            Verifica, instala ou remove o driver PawnIO.

            Opções de 'report':
              -f, --format <txt|html|json>  Formato de saída (padrão: txt).
              -o, --out <caminho>           Grava em arquivo em vez de mostrar no console.
                  --redact                  Remove números de série e UUIDs.
                  --lang <pt-BR|en>         Idioma do relatório (padrão: idioma do sistema).

            Opções de 'sensors':
              -w, --watch                   Atualiza a tabela continuamente até Ctrl+C.
                  --log <arquivo.csv>       Grava as leituras em CSV até Ctrl+C.
              -i, --interval <segundos>     Intervalo de atualização / amostragem (padrão: 1).
                  --duration <segundos>     Para automaticamente após esse tempo.

            A maioria dos sensores da placa-mãe e do processador exige administrador e o driver
            PawnIO (hwsuite driver install). Sensores de vídeo e de disco funcionam sem eles.

            Geral:
              -h, --help                    Mostra esta ajuda.
              -v, --version                 Mostra a versão.
            """,
        ["cli.unknownArgument"] = "Argumento desconhecido: {0}",
        ["cli.missingValue"] = "Falta o valor de {0}",
        ["cli.badFormat"] = "Formato '{0}' não suportado. Use txt, html ou json.",
        ["cli.badNumber"] = "Número inválido para {0}: {1}",
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
