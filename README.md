# HWSuite

Monitoramento e inventário de hardware para Windows 11 (CPU, GPU, sensores) em C#/.NET, open source (MPL-2.0).

*Hardware inventory and monitoring for Windows 11 in C#/.NET. One tool covering what CPU-Z, GPU-Z and HWMonitor do separately.*

> **Status: 0.11.0 lançada: inventário e sensores em tempo real na linha de comando e no aplicativo de desktop, com área de notificação, alertas, log, perfis de placa-mãe, SPD/XMP da memória, link PCIe, telemetria extra de placas AMD, S.M.A.R.T. de discos SATA/USB, banco de dados de GPUs no estilo GPU-Z , clocks da placa lidos do driver AMD e detecção de GPU falsificada.** Identifica o hardware sem instalar nenhum driver, gera relatórios em texto, HTML e JSON, mostra os sensores ao vivo e instala o driver PawnIO quando necessário.

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

## Aplicativo de desktop

O `HWSuite.Gui.exe` (WinUI 3) mostra os sensores ao vivo, com gráfico do sensor selecionado, e o inventário completo do hardware. Não precisa instalar: baixe `hwsuite-gui-<versão>-win-x64.zip` em [Releases](https://github.com/aprodrigues31/HWSuite/releases), extraia a pasta inteira e execute o `HWSuite.Gui.exe`. Se faltarem privilégio de administrador ou o driver PawnIO, uma barra no topo oferece resolver com um clique.

Ao fechar a janela, o HWSuite continua na área de notificação mostrando as temperaturas e avisa quando um sensor passa do limite configurado. Em **Configurações** ficam o intervalo de atualização, o tema, os alertas, o log CSV e a opção de iniciar com o Windows (cria uma tarefa agendada apontando para a pasta atual do app; se mover a pasta, desligue e ligue a opção de novo).

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

1. Instale o driver PawnIO uma vez: `hwsuite driver install` (ou aceite a oferta que o `hwsuite sensors` faz quando ele falta).
2. Rode o terminal como administrador.

```text
hwsuite driver status                     Mostra se o PawnIO está instalado e se o terminal é administrador
hwsuite driver install [--yes]            Instala o driver (pede confirmação e permissão de administrador)
hwsuite driver uninstall [--yes]          Remove o driver
```

O HWSuite não distribui o driver de kernel. A instalação usa o pacote oficial do PawnIO pelo winget (`namazso.PawnIO`); sem winget, baixa o instalador do [GitHub do autor](https://github.com/namazso/PawnIO.Setup) e só o executa se a assinatura digital for válida e do autor. Sem o driver, `hwsuite sensors` avisa o que está faltando e mostra o que conseguir ler.
Código de saída: `0` sucesso, `2` argumento inválido, `3` relatório gerado mas algum provedor falhou.

## Como obter

- **Versões estáveis:** em [Releases](https://github.com/aprodrigues31/HWSuite/releases), dois `.zip` por versão: `hwsuite-<versão>-win-x64.zip` com a linha de comando `hwsuite.exe` e `hwsuite-gui-<versão>-win-x64.zip` com o aplicativo (win-x64, autocontidos, não precisam de .NET instalado).
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
  HWSuite.Hosting              Montagem dos provedores, compartilhada pelos front-ends
  HWSuite.Cli                  Linha de comando "hwsuite"
  HWSuite.Gui                  Aplicativo de desktop (WinUI 3)
tests/
  HWSuite.Tests                xUnit com dumps de registradores e tabelas montadas byte a byte
```

Cada provedor implementa `IInventoryProvider` e só preenche o modelo comum; os decodificadores não fazem P/Invoke, então são testados com dados gravados em qualquer sistema operacional.

## Roadmap

1. **Inventário sem driver** — esta versão.
2. **Sensores (MVP público):** ✅ sensores em tempo real, mínimo/máximo e log CSV na linha de comando (0.3.0); ✅ aplicativo de desktop com sensores, gráfico e inventário (0.4.0). ✅ área de notificação, alertas e configurações (0.5.0).
3. **Detalhe:** ✅ perfis de placa-mãe, SPD/XMP da memória e link PCIe (0.6.0); ✅ telemetria AMD pela ADL: link PCIe ao vivo e limites do driver (0.7.0). ✅ S.M.A.R.T. de SATA/USB com avaliação de saúde (0.8.0). A seguir: baterias e nobreaks, controlador de tensão (VRM), EXPO.
4. **Paridade:** ✅ banco de dados de GPUs e campos do GPU-Z (0.9.0), clocks da placa pelo driver AMD (0.10.0), detecção de GPU falsificada (0.11.0). A seguir: backup do BIOS de vídeo, benchmark, fontes/AIOs, NPU.

## Licença

[Mozilla Public License 2.0](LICENSE). HWSuite não usa código, dados nem marcas do CPU-Z, GPU-Z ou HWMonitor; eles servem apenas como referência de funcionalidade.
