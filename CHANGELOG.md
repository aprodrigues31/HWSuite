# Changelog

Todas as mudanças relevantes do HWSuite ficam registradas aqui.
O formato segue [Keep a Changelog](https://keepachangelog.com/pt-BR/1.1.0/) e as versões seguem [SemVer](https://semver.org/lang/pt-BR/).

Cada executável publicado pelo CI se identifica como `versão (build N, commit)`, por exemplo `0.2.0 (build 5, a3b8c29)`:
o número do build é a execução do GitHub Actions que o gerou e o commit é o código exato que foi compilado.

## [Não lançado]

### Corrigido
- Sensores paravam com "Object reference not set to an instance of an object" depois de abrir a página Hardware: a leitura do SPD fechava a biblioteca de sensores, que libera recursos globais ainda em uso. A biblioteca agora só é fechada quando ninguém mais a usa, e o SPD aproveita a conexão dos sensores.
- Página Hardware levava cerca de 1 minuto com administrador: o SPD era lido byte a byte, com uma pausa que dura ~15 ms no relógio padrão do Windows. Agora só os bytes usados são lidos e com relógio de 1 ms.

### Adicionado
- Fase 3a: **perfis por placa-mãe**. O chip de sensores da placa (Super I/O) só numera as entradas; o perfil diz o que cada uma é naquela placa, aplica o divisor de tensão e oculta entradas sem nada ligado. Vale para a linha de comando e para o aplicativo, e os alertas e o log usam os valores corrigidos.
- Primeiro perfil: ASUS TUF GAMING B550M-PLUS (e a versão Wi-Fi), conferido com o HWiNFO numa placa real: ventoinhas com o nome do conector (CPU_FAN, CPU_OPT, CHA_FAN1, CHA_FAN2), tensões nas escalas corretas (Vcore, +5 V, +12 V, VTT e demais entradas com o nome do pino), temperatura da CPU pelo sensor da placa e entradas sem nada ligado ocultas (o "Temperature #3" de 81 °C). Conectores de ventoinha vazios aparecem sozinhos quando algo passa a girar neles.
- Fase 3b: **memória em detalhe** pelo SPD de cada pente (com administrador e PawnIO): fabricante dos chips de memória, semana e ano de fabricação, organização (ranks, largura e densidade dos chips, ECC), sensor de temperatura, perfil JEDEC e perfis **XMP** (2.0 em DDR4, 3.0 em DDR5) com velocidade, latências e tensão. Aparece no relatório e na página Hardware do aplicativo. EXPO (AMD, DDR5) fica para uma próxima versão.
- Fase 3c: **link PCIe da placa de vídeo** (estilo GPU-Z): versão e linhas que a placa suporta e as que está usando, por exemplo "PCIe 4.0 x16, rodando em PCIe 4.0 x16". Nas Radeon RX 6000/7000, que têm uma ponte PCIe interna, mostra o link com o slot, não o interno. Explica quando o link está abaixo do máximo (economia de energia em repouso ou slot com menos linhas).
- `hwsuite sensors --raw` mostra as entradas sem o perfil, para comparar com a BIOS ao criar perfis novos.

## [0.5.0] - 2026-10-21

### Adicionado
- Fase 2c no aplicativo de desktop:
  - **Área de notificação:** ícone com a temperatura da CPU e da GPU na dica; fechar a janela mantém o monitoramento rodando (configurável); menu com Abrir e Sair.
  - **Alertas:** notificação do Windows quando um sensor passa do limite. Começa com CPU acima de 90 °C, GPU acima de 95 °C e discos acima de 70 °C; o botão **Criar alerta** na página Sensores vigia o sensor selecionado. Só dispara após 3 leituras seguidas e só volta ao normal com 3 °C de folga, para não repetir avisos.
  - **Configurações:** intervalo de atualização (1 a 60 s), tema claro/escuro/do Windows, fechar para a bandeja, limites dos alertas, gravação contínua de log CSV em Documentos\HWSuite\logs e **iniciar com o Windows** (tarefa agendada que abre minimizado e como administrador, sem pedir UAC a cada logon).
  - Valores além do limite aparecem em vermelho na lista de sensores.
  - Instância única: abrir o app de novo traz a janela que já está rodando.
  - Ícone próprio do aplicativo.

## [0.4.0] - 2026-10-21

### Adicionado
- Fase 2b: aplicativo de desktop `HWSuite.Gui` (WinUI 3, autocontido, sem instalação):
  - **Sensores:** lista ao vivo agrupada por dispositivo com valor, mínimo e máximo, atualizada a cada segundo, e gráfico dos últimos 5 minutos do sensor selecionado.
  - **Hardware:** o inventário completo (mesmo conteúdo do `hwsuite report`) e botão para salvar o relatório HTML em Documentos\HWSuite.
  - Barra de aviso que oferece **reabrir como administrador** ou **instalar o driver PawnIO** com um clique, e reinicia os sensores após a instalação.
- Biblioteca `HWSuite.Hosting`, compartilhada pela linha de comando e pelo aplicativo.
- O release passa a ter dois pacotes: `hwsuite-<versão>-win-x64.zip` (linha de comando) e `hwsuite-gui-<versão>-win-x64.zip` (aplicativo).

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

[Não lançado]: https://github.com/aprodrigues31/HWSuite/compare/v0.5.0...HEAD
[0.5.0]: https://github.com/aprodrigues31/HWSuite/releases/tag/v0.5.0
[0.4.0]: https://github.com/aprodrigues31/HWSuite/releases/tag/v0.4.0
[0.3.0]: https://github.com/aprodrigues31/HWSuite/releases/tag/v0.3.0
[0.2.0]: https://github.com/aprodrigues31/HWSuite/releases/tag/v0.2.0
[0.1.0]: https://github.com/aprodrigues31/HWSuite/commit/1d2d84c
