# Changelog

Todas as mudanças relevantes do HWSuite ficam registradas aqui.
O formato segue [Keep a Changelog](https://keepachangelog.com/pt-BR/1.1.0/) e as versões seguem [SemVer](https://semver.org/lang/pt-BR/).

Cada executável publicado pelo CI se identifica como `versão (build N, commit)`, por exemplo `0.2.0 (build 5, a3b8c29)`:
o número do build é a execução do GitHub Actions que o gerou e o commit é o código exato que foi compilado.

## [Não lançado]

### Adicionado
- **Benchmark → Internet:** teste de velocidade da Internet e do Wi-Fi, com três abas: **Resultado** (download, upload, latência sem carga e com download/upload, jitter, perda de pacotes sem carga, servidor, provedor e IP público), **Detalhamento** (gráficos de vazão a cada 100 ms e de latência e jitter, com mediana, média, mínimo e máximo) e **Melhorias** (dicas a partir do que foi medido: sinal Wi-Fi fraco, Wi-Fi ou cabo como gargalo, faixa de 2,4 GHz, cabo a 100 Mbps, *bufferbloat*, perda, jitter e latência altos).
- Dois servidores: **Cloudflare** (data center mais próximo, padrão) e **M-Lab** (protocolo aberto NDT7). Como o M-Lab publica cada teste com o IP público num banco de dados aberto, o app pede consentimento antes (com opção de não perguntar de novo). Escolha IPv4 ou IPv6; cada teste usa só o protocolo escolhido.
- A perda de pacotes é medida sem carga, como no SIMET e no Speedtest: 100 sondagens (conexões TCP) ao servidor de teste, contando as que ficam sem resposta em 1 segundo. Não usa ping, que muitos servidores limitam ou descartam.
- Cartão **Conexão**: adaptador em uso, tipo (Wi-Fi ou cabo), velocidade do link e, no Wi-Fi, rede, sinal (% e dBm), padrão (Wi-Fi 4/5/6/7), faixa e canal e taxa do link.
- Resultados em `Documentos\HWSuite\net-benchmark.csv` (sem o IP público), na nova aba **Internet** de Benchmark → Resultados e na limpeza seletiva; linha de comando `hwsuite net [--ipv6] [--mlab --accept-mlab-policy]`.

## [0.16.0] - 2026-10-28

### Adicionado
- **Benchmark → Resultados:** o Benchmark ganhou dois subitens no menu, **Testes** (onde se rodam os testes) e **Resultados**, uma página com abas Processador, Placa de vídeo e Disco. Cada aba mostra uma tabela com cabeçalho, uma linha por execução (da mais recente para a mais antiga), números alinhados, uma barra que compara cada execução com a melhor e a melhor destacada. Os botões "Limpar resultados…" e "Abrir pasta" ficam nessa página; o histórico em texto corrido saiu da página de testes.
- **Benchmark de disco** (estilo CrystalDiskMark) na página Benchmark e em `hwsuite bench --disk C: [--size GB]`: SEQ1M Q8T1, SEQ1M Q1T1, RND4K Q32T1 e RND4K Q1T1, leitura e gravação em MB/s e IOPS nos testes aleatórios. Ignora o cache do Windows (mede o disco, não a memória), usa um arquivo temporário de 1 ou 4 GB com dados aleatórios que é apagado ao final, e mostra o modelo do disco por trás de cada letra. Resultados em `Documentos\HWSuite\disk-benchmark.csv`.
- **Limpar resultados:** botão na página Benchmark que abre uma janela para escolher o que apagar (processador, placa de vídeo, disco, em qualquer combinação); nada vem marcado.

## [0.15.0] - 2026-10-28

### Alterado
- **Sensores:** os nomes das colunas (Sensor, Tipo, Valor, Mín., Máx.) ficam abaixo do título de cada dispositivo, em vez de uma única linha no topo da página.

### Adicionado
- **Idioma:** em Configurações, escolha entre Português (Brasil), English ou o idioma do Windows (padrão); vale ao reabrir o app. Todos os textos existem nos dois idiomas, conferidos por teste.
- **Atualização automática:** quando o HWSuite foi instalado pelo instalador, a barra de nova versão oferece "Atualizar agora". O app baixa o instalador, confere o SHA-256 publicado pelo GitHub (se não conferir, nada é instalado), instala em modo silencioso, fecha e reabre já na versão nova. A versão portátil continua com o botão de download.
- **Winget:** manifestos do pacote `aprodrigues31.HWSuite`, um workflow para o primeiro envio ao repositório da comunidade do Windows Package Manager e o envio automático de cada nova versão no workflow de release (com o *secret* `WINGET_TOKEN`).
- Botão **"Verificar atualizações"** em Configurações e em Sobre: consulta o GitHub na hora e diz se há versão nova (abrindo a barra com o botão de download) ou se você já está na mais recente.

## [0.14.0] - 2026-10-28

### Adicionado
- **Instalador para Windows** (`hwsuite-setup-<versão>.exe`): instala por usuário, sem pedir administrador, em `%LOCALAPPDATA%\Programs\HWSuite`, com atalho no Menu Iniciar (e na área de trabalho, se marcado), o aplicativo e o `hwsuite.exe` de linha de comando. Desinstala por "Adicionar ou remover programas", removendo também a tarefa de iniciar com o Windows. Os zips portáteis continuam disponíveis.
- **Aviso de nova versão:** ao abrir, o aplicativo faz uma consulta ao GitHub e, se houver versão mais nova, mostra uma barra com o botão "Baixar instalador". Pode ser desligado em Configurações.
- **AMD EXPO:** módulos DDR5 com perfis EXPO mostram cada perfil (velocidade, temporizações e tensão VDD) na memória, ao lado do JEDEC e do XMP 3.0. Em módulos híbridos XMP+EXPO, o bloco EXPO ocupa o lugar do terceiro perfil XMP, que deixa de ser lido como XMP. Conferido com dumps reais de módulos XMP+EXPO.
- **Bateria e nobreak:** nova seção no relatório e na página Hardware, lida pelo driver de bateria do Windows (sem administrador). Em notebooks mostra fabricante, número de série, data de fabricação, química, carga e situação (na tomada, carregando, na bateria), autonomia estimada, **saúde** (capacidade atual em relação à original e o desgaste), capacidades de projeto/carga total/atual, ciclos de carga, tensão, potência e temperatura. Nobreaks ligados por USB aparecem como "Nobreak (USB)", com carga, situação e autonomia. O `--redact` remove o número de série.
- **NPU (acelerador de IA):** nova seção no relatório e na página Hardware com nome, fabricante, geração, ID PCI, driver e o desempenho de IA informado pelo fabricante (TOPS INT8). Reconhece AMD XDNA (Ryzen 7040: 10 TOPS; 8040: 16) e XDNA 2 (Ryzen AI 300, Krackan, Strix Halo: 50), Intel NPU 3 (Meteor Lake: 11; Arrow Lake: 13), NPU 4 (Lunar Lake: 48) e NPU 5 (Panther Lake: 50), e Qualcomm Hexagon (Snapdragon X: 45). Lê a categoria "Processadores neurais" do Windows, sem administrador; em PCs sem NPU a seção não aparece.
- **Saúde da fonte pelas tensões da placa-mãe:** alertas quando +12 V, +5 V ou +3,3 V saem da faixa ATX (±5%: 11,40–12,60 V, 4,75–5,25 V, 3,135–3,465 V), ligados por padrão e com chave em Configurações → Fonte e water cooler. Nos stress tests de processador e de placa de vídeo, o painel mostra cada linha com o valor atual, o mínimo e o máximo durante o teste, e o resultado final avisa se alguma linha saiu da faixa. Serve para qualquer fonte, já que a maioria não tem conexão de dados; usa as linhas que o perfil da placa identifica.
- **Bomba do water cooler:** em Configurações, escolha o conector de ventoinha em que a bomba está ligada (a lista mostra a rotação atual de cada um) e a rotação mínima. A bomba aparece como "Bomba (water cooler)" nos sensores, no log e nos alertas, e um alerta avisa se ela desacelerar ou parar.

## [0.13.0] - 2026-10-28

### Adicionado
- **Benchmark do processador** (estilo CPU-Z): a mesma carga em um núcleo (1 thread) e depois em todos os núcleos (todas as threads), com nota em pontos e o ganho com todas as threads. A carga fica no cache de cada núcleo (inteiros, leituras e gravações dependentes, ponto flutuante), então mede os núcleos e não a memória. Fica na nova página **Benchmark** do aplicativo (5, 10 ou 30 s por teste, com cancelamento) e em `hwsuite bench [--duration s]`. Os resultados vão para `Documentos\HWSuite\benchmark.csv` e as execuções anteriores aparecem para comparação.
- **Stress test** (estabilidade): o botão "Stress test" da página Benchmark (ou `hwsuite bench --stress`) roda a carga em todas as threads sem limite de tempo, até clicar em Parar. Cada lote de trabalho é conferido com um resultado de referência, e qualquer diferença conta como erro de cálculo (sinal de overclock, undervolt, memória ou temperatura instáveis). A tela mostra tempo, pontos por segundo (uma queda indica redução de clock por calor), erros e a temperatura do processador com a máxima. O Windows não entra em suspensão durante o teste, e a página continua rodando enquanto se olha a página Sensores.
- **Benchmark e stress test da placa de vídeo**: a página Benchmark ganhou a seção "Placa de vídeo". O teste roda um compute shader do Direct3D 11 (cadeias de multiplicação-soma em ponto flutuante de 32 bits) e mede o desempenho em GFLOPS, sem instalar nada, em placas AMD, NVIDIA e Intel. O stress test roda sem limite de tempo e confere cada lote com um resultado de referência, mostrando GFLOPS, erros e a temperatura da GPU com a máxima. Também por `hwsuite bench --gpu [--stress]`. Resultados em `Documentos\HWSuite\gpu-benchmark.csv`.
- **Comparação com outros processadores:** depois do benchmark, a página mostra o resultado numa lista com outros processadores medidos com o mesmo teste, com barras e notas de um núcleo e de todos os núcleos. Se houver resultados do mesmo modelo, aparece o veredito: dentro do esperado (95% da mediana ou mais), um pouco abaixo ou abaixo, com o que verificar. A lista vem de duas fontes: uma tabela embutida com resultados reais, alimentada pelo botão "Enviar resultado" (abre uma issue pré-preenchida no GitHub, sem dados pessoais, para revisar e enviar), e os arquivos `benchmark.csv` de outros PCs copiados para `Documentos\HWSuite\referencias`. O `hwsuite bench` mostra a mesma comparação, e o CI mede o processador do runner do GitHub como mais uma referência.

### Corrigido
- **BIOS de vídeo:** a data de compilação no formato da AMD ("08/02/23,00:05:24", mês/dia/ano) agora é lida corretamente; o part number é localizado como faz o driver amdgpu do Linux.

### Adicionado
- A linha "Cópia da BIOS de vídeo" também mostra o nome que o fabricante gravou na BIOS (por exemplo "ASRock R23D-E3 SL 16GB 228W") e a versão da ATOMBIOS.

## [0.12.0] - 2026-10-28

### Adicionado
- **Backup da BIOS de vídeo** (placas AMD): a cópia que o firmware UEFI publica na tabela ACPI VFCT é lida sem driver e sem administrador, conferida (assinatura 55 AA, soma de verificação, tabelas ATOM) e salva em `.rom` pelo botão "Salvar BIOS de vídeo" da página Hardware (em Documentos\HWSuite) ou por `hwsuite vbios [-o pasta|arquivo.rom]`. O relatório mostra a linha "Cópia da BIOS de vídeo" com tamanho, soma de verificação, part number e data.

## [0.11.0] - 2026-10-28

### Adicionado
- **Detecção de GPU falsificada** (estilo GPU-Z): cada placa reconhecida é conferida com o modelo que o nome diz. O programa compara o fabricante e o ID PCI com os IDs dos chips verdadeiros (tabela gerada da pci.ids, com variantes de notebook e workstation), o nível de recursos DirectX 12 e o ray tracing (todos os modelos do banco são DirectX 12 Ultimate), a geração PCIe que a placa suporta, a quantidade de memória e, nas AMD, a velocidade da memória. A linha "Autenticidade" mostra "nenhum sinal de falsificação" ou os sinais encontrados. Com o driver genérico da Microsoft, a linha "Modelo pelo ID PCI" diz que modelo é a placa.

## [0.10.0] - 2026-10-22

### Adicionado
- **Clocks da placa lidos do driver AMD** (Overdrive 8): clock real da memória com a velocidade por pino e a banda calculadas a partir dele (na RX 7800 XT da ASRock: 2.438 MHz, 19,5 Gbps, 624,1 GB/s, iguais ao GPU-Z), e o limite de frequência da GPU no Adrenalin. O modo `--raw` mostra a tabela completa do Overdrive 8.

## [0.9.0] - 2026-10-22

### Adicionado
- Fase 4a: **banco de dados de GPUs** (estilo GPU-Z) com as especificações de referência de 64 modelos de mesa: Radeon RX 6000/7000/9000, GeForce RTX 30/40/50 e Intel Arc A/B. Na página Hardware e no relatório, cada placa conhecida mostra chip, arquitetura e processo, unidades (CU/SM/Xe), shaders, TMUs, ROPs, clocks base/boost, memória (tipo, barramento, velocidade e banda), consumo e data de lançamento. O modelo é identificado pelo nome (a AMD usa o mesmo ID para modelos diferentes) e a memória escolhe entre variantes (RTX 3060 8/12 GB, RTX 4060 Ti 8/16 GB etc.).
- **Vídeo no estilo GPU-Z:** fabricante da placa (ASRock, ASUS, Sapphire...), revisão do chip, área do die e transistores, taxas de preenchimento (pixel e textura), fabricante da memória de vídeo (AMD), pacote do driver (por exemplo "AMD Software: Adrenalin Edition 26.10.1" ou "NVIDIA 560.94"), suporte a DirectX 12 (nível de recursos e Shader Model) e ray tracing (DXR), APIs do driver (OpenCL, CUDA, DirectCompute, DirectML, Vulkan, OpenGL) e Resizable BAR (pela janela de memória que o Windows atribuiu à placa).
- **Conferência de memória:** se a placa informa bem menos memória do que o modelo tem, o relatório avisa (outra variante, ou placa remarcada/falsificada).

## [0.8.0] - 2026-10-22

### Adicionado
- Fase 3e: **S.M.A.R.T. de discos SATA e USB** (com administrador): tabela completa de atributos (atual, pior, limite do fabricante, valor bruto), como no CrystalDiskInfo, com nomes em português. Discos SATA internos pela interface padrão do Windows; HDs e SSDs em gavetas USB pelo comando ATA embutido no padrão SAT, que a maioria dos adaptadores aceita. Páginas inválidas (adaptadores que não repassam o comando) são descartadas pelo checksum.
- **Saúde** de cada disco no estilo CrystalDiskInfo: *Ruim* quando um atributo chega ao limite do fabricante, *Atenção* com setores realocados, pendentes ou incorrigíveis, *Boa* caso contrário. NVMe ganha a mesma avaliação pelo aviso crítico, reserva disponível e desgaste.
- Discos SATA passam a mostrar temperatura, horas ligado, ciclos, desgaste (SSD), dados gravados e setores realocados/pendentes.

## [0.7.0] - 2026-10-22

### Corrigido
- Autoteste do aplicativo no CI falhava de forma intermitente: os temporizadores do teste eram recolhidos pelo coletor de lixo antes de disparar (o aplicativo em si não era afetado). O CI agora também captura as pilhas gerenciada e nativa se a interface parar.

### Adicionado
- Fase 3d: **telemetria extra de placas AMD** pela AMD Display Library (sem administrador), lida da mesma sessão de telemetria que o driver usa: **link PCIe ao vivo** (velocidade e linhas; numa RX 7800 XT, 16 GT/s x16, igual ao HWiNFO) e os **limites** que o driver usa para reduzir o clock (temperaturas de borda, hot spot, GCD/MCD, memória e VR, TDC e PPT, em %). Temperaturas dos reguladores de tensão e dos chiplets aparecem quando o driver as informa (o da RX 7800 XT não informa).
- `hwsuite sensors --raw` lista todos os sensores que o driver AMD informa, numerados, para comparar com outras ferramentas.

## [0.6.0] - 2026-10-21

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

[Não lançado]: https://github.com/aprodrigues31/HWSuite/compare/v0.16.0...HEAD
[0.16.0]: https://github.com/aprodrigues31/HWSuite/releases/tag/v0.16.0
[0.15.0]: https://github.com/aprodrigues31/HWSuite/releases/tag/v0.15.0
[0.14.0]: https://github.com/aprodrigues31/HWSuite/releases/tag/v0.14.0
[0.13.0]: https://github.com/aprodrigues31/HWSuite/releases/tag/v0.13.0
[0.12.0]: https://github.com/aprodrigues31/HWSuite/releases/tag/v0.12.0
[0.11.0]: https://github.com/aprodrigues31/HWSuite/releases/tag/v0.11.0
[0.10.0]: https://github.com/aprodrigues31/HWSuite/releases/tag/v0.10.0
[0.9.0]: https://github.com/aprodrigues31/HWSuite/releases/tag/v0.9.0
[0.8.0]: https://github.com/aprodrigues31/HWSuite/releases/tag/v0.8.0
[0.7.0]: https://github.com/aprodrigues31/HWSuite/releases/tag/v0.7.0
[0.6.0]: https://github.com/aprodrigues31/HWSuite/releases/tag/v0.6.0
[0.5.0]: https://github.com/aprodrigues31/HWSuite/releases/tag/v0.5.0
[0.4.0]: https://github.com/aprodrigues31/HWSuite/releases/tag/v0.4.0
[0.3.0]: https://github.com/aprodrigues31/HWSuite/releases/tag/v0.3.0
[0.2.0]: https://github.com/aprodrigues31/HWSuite/releases/tag/v0.2.0
[0.1.0]: https://github.com/aprodrigues31/HWSuite/commit/1d2d84c
