# HWSuite

Monitoramento e inventário de hardware para Windows 11 (CPU, GPU, sensores) em C#/.NET, open source (MPL-2.0).

*Hardware inventory and monitoring for Windows 11 in C#/.NET. One tool covering what CPU-Z, GPU-Z and HWMonitor do separately.*

> **Status: fase 1 (MVP de inventário).** Identifica o hardware sem instalar nenhum driver e gera relatórios em texto, HTML e JSON pela linha de comando. Sensores em tempo real, interface gráfica e o driver PawnIO chegam na fase 2.

## O que a fase 1 já faz

| Área | Dados | Fonte |
| --- | --- | --- |
| Processador | Nome, fabricante, família/modelo/stepping, soquete, núcleos/threads, clocks nominais, instruções (SSE…AVX-512, AMX), caches L1–L3 | Instrução CPUID + SMBIOS |
| Placa-mãe e BIOS | Fabricante, modelo, revisão, série, versão e data da BIOS, versão SMBIOS | Tabela SMBIOS do firmware |
| Sistema | Fabricante, modelo, família, SKU, tipo de gabinete, UUID | SMBIOS |
| Memória | Total, slots usados/totais, capacidade máxima e, por módulo: tipo (DDR4/DDR5/LPDDR5…), velocidade nominal e configurada, ranks, fabricante, part number | SMBIOS |
| Vídeo | Placas presentes, IDs PCI, memória dedicada/compartilhada, versão e data do driver, BIOS de vídeo | DXGI + registro |
| Armazenamento | Modelo, firmware, série, interface (NVMe/SATA/USB…), tamanho, SSD/HDD e, em NVMe, saúde: temperatura, desgaste, horas ligado, dados lidos/gravados, erros | IOCTLs de armazenamento |
| Sistema operacional | Edição (detecta Windows 11), versão, build | Registro |

Tudo funciona **sem privilégio de administrador**. O que um provedor não conseguir ler aparece na seção *Diagnóstico* do relatório, sem interromper o resto.

## Uso

```text
hwsuite                                   Resumo em texto no console
hwsuite report --format html --out pc.html
hwsuite report --format json --out pc.json --redact
hwsuite report --lang en                  Relatório em inglês
```

`--redact` remove números de série, UUID e nome da máquina antes de compartilhar o relatório.
Código de saída: `0` sucesso, `2` argumento inválido, `3` relatório gerado mas algum provedor falhou.

## Como obter

- **Executável pronto:** cada build do GitHub Actions publica `hwsuite.exe` (win-x64, autocontido, não precisa instalar .NET) no artefato `hwsuite-win-x64`, junto com relatórios de exemplo gerados no runner Windows.
- **Compilar:** requer o SDK do .NET 10.

```powershell
dotnet build HWSuite.slnx -c Release
dotnet test HWSuite.slnx
dotnet run --project src/HWSuite.Cli -- report --format html --out pc.html
```

## Estrutura

```text
src/
  HWSuite.Core                 Modelo, coleta, decodificadores (CPUID, SMBIOS, storage, PCI), relatórios, idiomas
  HWSuite.Providers.Common     CPUID (x86/x64) e SMBIOS via sysfs no Linux (apoio ao desenvolvimento)
  HWSuite.Providers.Windows    SMBIOS do firmware, Windows, DXGI, IOCTLs de disco
  HWSuite.Cli                  Linha de comando "hwsuite"
tests/
  HWSuite.Tests                xUnit com dumps de registradores e tabelas montadas byte a byte
```

Cada provedor implementa `IInventoryProvider` e só preenche o modelo comum; os decodificadores não fazem P/Invoke, então são testados com dados gravados em qualquer sistema operacional.

## Roadmap

1. **Inventário sem driver** — esta versão.
2. **Sensores (MVP público):** LibreHardwareMonitorLib + PawnIO, APIs de GPU (NVML/ADLX/IGCL), árvore de sensores, gráficos, log CSV, alertas, ícone na bandeja, interface WinUI 3.
3. **Detalhe:** SPD/XMP/EXPO, temperatura dos módulos, teste de carga PCIe, baterias e nobreaks, S.M.A.R.T. de SATA.
4. **Paridade:** banco de dados de GPUs, backup do BIOS de vídeo, detecção de GPU falsificada, benchmark, fontes/AIOs, NPU.

## Licença

[Mozilla Public License 2.0](LICENSE). HWSuite não usa código, dados nem marcas do CPU-Z, GPU-Z ou HWMonitor; eles servem apenas como referência de funcionalidade.
