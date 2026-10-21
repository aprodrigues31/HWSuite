# Changelog

Todas as mudanças relevantes do HWSuite ficam registradas aqui.
O formato segue [Keep a Changelog](https://keepachangelog.com/pt-BR/1.1.0/) e as versões seguem [SemVer](https://semver.org/lang/pt-BR/).

Cada executável publicado pelo CI se identifica como `versão (build N, commit)`, por exemplo `0.2.0 (build 5, a3b8c29)`:
o número do build é a execução do GitHub Actions que o gerou e o commit é o código exato que foi compilado.

## [Não lançado]

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

[Não lançado]: https://github.com/aprodrigues31/HWSuite/compare/v0.2.0...HEAD
[0.2.0]: https://github.com/aprodrigues31/HWSuite/releases/tag/v0.2.0
[0.1.0]: https://github.com/aprodrigues31/HWSuite/commit/1d2d84c
