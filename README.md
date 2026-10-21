# HWSuite

Monitoramento e inventário de hardware para Windows 11 (CPU, GPU, sensores) em C#/.NET, open source (MPL-2.0).

*Hardware inventory and monitoring for Windows 11 in C#/.NET. One tool covering what CPU-Z, GPU-Z and HWMonitor do separately.*

> **Status: 0.2.0 lançada (inventário); 0.3.0 em desenvolvimento (sensores em tempo real).** Identifica o hardware sem instalar nenhum driver, gera relatórios em texto, HTML e JSON, e mostra os sensores ao vivo pela linha de comando. A interface gráfica vem na sequência da fase 2.

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

### Sensores em tempo real

```text
hwsuite sensors                           Tabela única com valor atual, mínimo e máximo
hwsuite sensors --watch                   Atualiza a cada segundo até Ctrl+C
hwsuite sensors --log temps.csv --interval 2 --duration 600
```

Sensores de vídeo e de disco funcionam em qualquer conta. Para os sensores de processador e placa-mãe (temperaturas, tensões, ventoinhas, consumo):

1. Instale o driver PawnIO uma vez: `winget install namazso.PawnIO`.
2. Rode o terminal como administrador.

O HWSuite não embarca driver de kernel; o PawnIO é assinado e instalado separadamente. Sem ele, o comando avisa o que está faltando e mostra o que conseguir ler.
Código de saída: `0` sucesso, `2` argumento inválido, `3` relatório gerado mas algum provedor falhou.

## Como obter

- **Versões estáveis:** em [Releases](https://github.com/aprodrigues31/HWSuite/releases), um `.zip` por versão com `hwsuite.exe` (win-x64, autocontido, não precisa instalar .NET).
- **Builds de desenvolvimento:** cada execução do GitHub Actions publica o artefato `hwsuite-<versão>-build.<N>-win-x64`, com o executável e relatórios de exemplo gerados no runner Windows.
- **Compilar:** requer o SDK do .NET 10.

```powershell
dotnet build HWSuite.slnx -c Release
dotnet test HWSuite.slnx
dotnet run --project src/HWSuite.Cli -- report --format html --out pc.html
```

## Versões

Cada executável sabe exatamente de onde veio:

```text
> hwsuite --version
hwsuite 0.2.0 (build 5, a3b8c29)
```

- **0.2.0**: versão SemVer, definida em `Directory.Build.props` (`VersionPrefix`).
- **build 5**: número da execução do GitHub Actions que gerou o arquivo.
- **a3b8c29**: commit do código compilado.

A mesma identificação aparece nas propriedades do arquivo no Windows (versão `0.2.0.5`), no cabeçalho dos relatórios e no campo `toolVersion` do JSON. O que mudou em cada versão está no [CHANGELOG](CHANGELOG.md).

Para lançar uma versão: atualize `VersionPrefix` e o `CHANGELOG.md`, faça o commit e rode o workflow **Release** na aba Actions ("Run workflow" em `main`). Ele compila, testa, cria a tag `v<versão>` e publica o release com o `.zip` e as notas do changelog. Também dispara se alguém enviar a tag `v<versão>` manualmente.

## Estrutura

```text
src/
  HWSuite.Core                 Modelo, coleta, decodificadores (CPUID, SMBIOS, storage, PCI), relatórios, idiomas
  HWSuite.Providers.Common     CPUID (x86/x64) e SMBIOS via sysfs no Linux (apoio ao desenvolvimento)
  HWSuite.Providers.Windows    SMBIOS do firmware, Windows, DXGI, IOCTLs de disco, detecção de admin/PawnIO
  HWSuite.Providers.Lhm        Sensores em tempo real via LibreHardwareMonitorLib (MPL-2.0)
  HWSuite.Cli                  Linha de comando "hwsuite"
tests/
  HWSuite.Tests                xUnit com dumps de registradores e tabelas montadas byte a byte
```

Cada provedor implementa `IInventoryProvider` e só preenche o modelo comum; os decodificadores não fazem P/Invoke, então são testados com dados gravados em qualquer sistema operacional.

## Roadmap

1. **Inventário sem driver** — esta versão.
2. **Sensores (MVP público):** ✅ sensores em tempo real, mínimo/máximo e log CSV na linha de comando (0.3.0). A seguir: interface gráfica com árvore de sensores e gráficos, alertas e ícone na bandeja.
3. **Detalhe:** SPD/XMP/EXPO, temperatura dos módulos, teste de carga PCIe, baterias e nobreaks, S.M.A.R.T. de SATA.
4. **Paridade:** banco de dados de GPUs, backup do BIOS de vídeo, detecção de GPU falsificada, benchmark, fontes/AIOs, NPU.

## Licença

[Mozilla Public License 2.0](LICENSE). HWSuite não usa código, dados nem marcas do CPU-Z, GPU-Z ou HWMonitor; eles servem apenas como referência de funcionalidade.
