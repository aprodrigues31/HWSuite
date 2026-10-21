# Changelog

Todas as mudanças relevantes do HWSuite ficam registradas aqui.
O formato segue [Keep a Changelog](https://keepachangelog.com/pt-BR/1.1.0/) e as versões seguem [SemVer](https://semver.org/lang/pt-BR/).

Cada executável publicado pelo CI se identifica como `versão (build N, commit)`, por exemplo `0.2.0 (build 5, a3b8c29)`:
o número do build é a execução do GitHub Actions que o gerou e o commit é o código exato que foi compilado.

## [Não lançado]

## [0.3.0] - 2026-10-21

### Adicionado
- Fase 2a: comando `hwsuite sensors` com os sensores em tempo real via LibreHardwareMonitorLib 0.9.6 — temperaturas, tensões, ventoinhas, consumo, clocks e utilização de CPU, placa-mãe (Super I/O), memória, GPUs NVIDIA/AMD/Intel, discos, fontes e baterias, com valores mínimo e máximo.
- `hwsuite sensors --watch` atualiza a tabela continuamente; `--log arquivo.csv` grava as leituras em CSV; `--interval` e `--duration` controlam a amostragem.
- Aviso quando faltam privilégio de administrador ou o driver PawnIO, que os sensores de processador e placa-mãe exigem.
- `hwsuite driver status|install|uninstall`: instala o driver PawnIO oficial pelo winget ou, sem winget, pelo instalador do GitHub do autor com verificação da assinatura digital; pede confirmação (ou `--yes`) e permissão de administrador via UAC.
- `hwsuite sensors` oferece instalar o PawnIO quando ele está faltando e reabrir como administrador quando o terminal não está elevado (o resultado volta para o mesmo terminal; `--watch` e `--log` abrem em nova janela).
- Aviso quando o processador devolve apenas zeros mesmo com administrador e PawnIO (indica reinício pendente após instalar o driver).

### Alterado
- Avisos de administrador/driver saem junto com a tabela de sensores (antes iam só para a saída de erro e se perdiam ao redirecionar para arquivo).
- Sensores sem nenhuma leitura deixam de aparecer na tabela.
- Dados em GB/MB exibidos em unidades legíveis (21029 GB → 20,5 TB; 16368 MB → 16 GB); contadores sem decimais inúteis.
- Temporizações da memória (SPD) aparecem em "Temporizações", em nanossegundos.

### Corrigido
- Mínimos falsos em 0 (temperatura de SSD, consumo da GPU): o HWSuite calcula mínimo e máximo por conta própria, ignorando a leitura inicial que ainda não tem dados.
- Perguntas de confirmação (instalar driver, reabrir como administrador) saem na tela mesmo quando a saída é redirecionada para arquivo; antes iam para dentro do arquivo e o comando parecia travado.

## [0.2.0] - 2026-10-21

### Adicionado
- Versão rastreável: `hwsuite --version`, propriedades do `.exe` (FileVersion `0.2.0.<build>`), cabeçalho dos relatórios e campo `toolVersion` no JSON.
- Artefato do CI nomeado com a versão (`hwsuite-0.2.0-build.N-win-x64`) e workflow **Release** que cria a tag e publica o release no GitHub.
- NVMe: número de série, modelo e firmware lidos do Identify Controller.
- Virtualização: distingue máquina física com Hyper-V/VBS ativo de máquina virtual, com o nome do hipervisor.

### Corrigido
- Número de série dos NVMe mostrava o identificador EUI-64/NGUID do Windows em vez do serial de fábrica.
- Datas da BIOS e do driver de vídeo apareciam no formato americano; agora seguem o idioma do relatório.
- Memória de vídeo mostrava o valor descontado pelo DXGI (15,8 GB em placa de 16 GB).
- Textos de preenchimento do fabricante ("SKU", "None", "To Be Filled By O.E.M.", seriais zerados) deixam de aparecer.
- Família do processador exibida em hexadecimal, como modelo e stepping.

## [0.1.0] - 2026-10-21

### Adicionado
- Fase 1 (inventário sem driver): processador e caches (CPUID), sistema, placa-mãe, BIOS e memória (SMBIOS), placas de vídeo (DXGI), discos e saúde NVMe (IOCTLs), versão do Windows.
- Linha de comando `hwsuite` com relatórios TXT, HTML e JSON em português e inglês, e `--redact`.
- Testes xUnit e CI no GitHub Actions (Windows) com relatório de exemplo e `hwsuite.exe` autocontido.

[Não lançado]: https://github.com/aprodrigues31/HWSuite/compare/v0.3.0...HEAD
[0.3.0]: https://github.com/aprodrigues31/HWSuite/releases/tag/v0.3.0
[0.2.0]: https://github.com/aprodrigues31/HWSuite/releases/tag/v0.2.0
[0.1.0]: https://github.com/aprodrigues31/HWSuite/commit/1d2d84c
