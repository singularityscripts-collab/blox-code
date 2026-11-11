# Log de atualizações do Blox Code

Todas as versões do Blox Code, da mais nova para a mais antiga. Quem tem o Blox Code instalado
recebe cada versão automaticamente na próxima vez que abrir.

## 1.13.0 — Treze funções novas

### Novidades (Windows)

- **Trazer seu setup do Bloxstrap, Fishstrap, Voidstrap ou Froststrap:** Fast Flags (viram um perfil
  com o nome do launcher), mods e as escolhas de Discord, localização e confirmação. O outro
  launcher não é alterado. A Home oferece uma vez; em Settings fica sempre à mão.
- **Escolher o servidor:** no menu de um jogo (botão direito), "Choose a server…" lista os
  servidores públicos com jogadores, ping e FPS, e entra no que você escolher.
- **Gráficos por jogo:** "Graphics for this game" faz aquele jogo sempre abrir com um preset ou um
  perfil de flags, sem mexer nas flags que você usa nos outros.
- **Fixar jogos na Home** e **criar um atalho na área de trabalho** para um jogo, com o ícone dele.
- **Copiar o convite do servidor:** o aviso "Connected to a server" ganhou um botão que copia o
  link daquele servidor para mandar a um amigo.
- **Stats:** uma aba nova com o tempo jogado, sessões, jogos, dias seguidos, um gráfico por dia e os
  jogos mais jogados (semana, mês ou desde sempre). Fica só no seu PC.
- **Configurações do Roblox** em Flags: sensibilidade da câmera, volume, nível gráfico, tela cheia,
  estatísticas de desempenho, câmera invertida e menos movimento, mudadas antes de o jogo abrir
  (com o Roblox fechado, já que ele grava as próprias configurações ao sair).
- **Abrir programas junto com o Roblox** (OBS, contador de FPS…), que podem fechar sozinhos quando
  a última janela do Roblox fecha. Settings → Open with Roblox.
- **Baixar a atualização do Roblox enquanto você joga:** depois de uns minutos num jogo, a versão
  nova (se houver) é baixada em segundo plano, e a próxima abertura só precisa descompactar.
- **Lembrete de pausa** depois de 1 h, 1,5 h ou 2 h jogando sem parar (desligado por padrão).
- **Temas da tela de abertura:** Bit, Minimal ou Terminal (uma barra de progresso em texto), em Look.
- Quando uma abertura falha, o Blox Code consulta a página de status do Roblox e diz se o problema
  é do próprio Roblox.

### Novidades (Linux)

- **Jogos recentes na Home** e o aviso de onde fica o servidor (como notificação do sistema),
  lidos do log do Sober enquanto ele roda.

## 1.12.1 — Correções da caça aos bugs

### Correções

- **Links na barra da Home:** um link de servidor privado entrava num servidor público; agora abre
  o servidor privado. O link "Join server" que o Blox Code põe no Discord passou a funcionar na
  barra (e leva àquele servidor), e os links de compartilhamento do Roblox (`roblox.com/share?…`)
  também são aceitos.
- **Histórico de jogos:** um arquivo de histórico danificado impedia o Blox Code de abrir; agora ele
  abre normalmente. Um histórico que não dá para ler não é mais apagado na partida seguinte: fica
  guardado como `History.json.corrupt`, e um arquivo preso por outro programa (como um antivírus)
  não é sobrescrito.
- Uma linha estranha no log do Roblox podia desligar o Discord, o aviso de servidor e o histórico
  daquela janela até o fim da sessão.
- O nome da versão do Roblox agora é conferido antes de virar pasta: só nomes de versão de verdade,
  sempre dentro da pasta `Versions`.
- Nomes de jogo longos com emoji não aparecem mais com "�" no Discord.
- **Perfis de Fast Flags:** importar um arquivo traz só as flags válidas (e diz quantas ficaram de
  fora); importar ou renomear para um nome que já existe pergunta antes de substituir.
- Flags com valor vazio (`null`) num Settings.json editado à mão são descartadas ao abrir.

## 1.12.0 — Visual novo, minimalista e transparente

### Novidades

- **Visual novo:** a barra lateral virou abas no topo (Home, Flags, Mods, Look, Settings, About; no
  Linux também Sober), e embaixo fica só uma linha com o estado do Roblox e a versão do Blox Code.
- **Home:** só uma barra de jogo no estilo da pesquisa do Windows. Cole o link e aperte Enter, ou
  clique no Play redondo com ela vazia para abrir o Roblox. Os jogos recentes ficam logo abaixo.
- **Páginas mais limpas:** sem cartões; cada grupo de opções é uma lista separada por linhas finas,
  com títulos maiores, botões arredondados e textos mais curtos. Appearance agora se chama Look, e
  Behaviour se chama Settings.
- **Transparência (Windows 11):** em Look → Transparency, escolha Acrylic (o que está atrás da
  janela aparece desfocado, o padrão), Mica (as cores do papel de parede) ou Off. Vale também para a
  janela de abertura do Roblox. Precisa do Windows 11 22H2 ou mais novo e de "Efeitos de
  transparência" ligado em Configurações → Personalização → Cores; sem isso a janela fica sólida, e
  a página diz o motivo.

## 1.11.2 — Várias janelas do Roblox funcionando de verdade

A 1.11.1 não chegou a ser publicada (o teste automático no contêiner do Arch Linux falhou antes de
o Blox Code rodar); esta é a mesma versão.

### Correções (Windows)

- **Várias janelas do Roblox:** a segunda janela fechava sozinha. O Roblox atual tem uma segunda
  trava ("NoReload") que entrega cada abertura para a janela que já está aberta a partir da mesma
  pasta. Agora cada janela extra abre por uma pasta própria (um atalho de pasta, sem copiar nada), e
  as duas travas ficam vencidas. Testado com três janelas ao mesmo tempo, e também com um Roblox que
  já estava aberto antes do Blox Code.
- O Blox Code lia o log do "CrashHandler" do Roblox em vez do log do jogo. Agora acompanha todas as
  janelas abertas: o Discord mostra o jogo mais recente e passa para outro jogo aberto quando você sai
  de um, e o histórico registra cada janela.
- Behaviour mostra quantas janelas do Roblox estão abertas.

Para jogar em várias janelas, use uma conta diferente em cada: com a mesma conta em dois jogos, o
próprio Roblox derruba uma das sessões (erro 273).

## 1.11.0 — Várias janelas, jogos recentes, perfis e Roblox Studio

### Novidades (Windows)

- **Várias janelas do Roblox ao mesmo tempo:** em Behaviour → "Run several Roblox windows at once",
  cada Play abre uma janela nova em vez de substituir a aberta. Tem um botão "Close all" para fechar
  todas. O jeito de fazer vem do MultiBloxy, estudado e reescrito.
- **Jogos recentes na Home:** os últimos jogos aparecem com ícone, há quanto tempo e por quanto tempo
  você jogou. Clique para jogar de novo; com o botão direito dá para voltar ao mesmo servidor, abrir a
  página do jogo ou tirar da lista. Ao lado fica o tempo jogado na semana e o jogo mais jogado.
- **Perfis de Fast Flags:** salve o conjunto atual com um nome ("Max FPS", "Bonito"…) e troque com um
  clique. Também dá para renomear, atualizar, exportar e importar como arquivo .json.
- **Roblox Studio:** "Launch Roblox Studio" no menu de abertura e "Open Studio" na Home. O Blox Code
  baixa, atualiza e abre o Studio com as suas Fast Flags.
- **Fixar a versão do Roblox:** em Behaviour, para ficar numa versão em vez de atualizar.
- **Liberar espaço:** em Behaviour, apaga versões antigas do Roblox, downloads em cache e logs com
  mais de uma semana.
- **Cor de destaque:** menta, azul, violeta, rosa ou âmbar, no app inteiro e na janela de
  inicialização (Appearance).
- **Ícone dos atalhos:** escolha um .ico para os atalhos do Blox Code (Appearance).

## 1.10.0 — Discord e localização do servidor

### Novidades (Windows)

- **Jogo no Discord:** enquanto você joga, o seu status no Discord mostra o jogo, o ícone, quem fez e
  há quanto tempo, com um botão para a página do jogo. Some quando você sai do jogo.
- **"Join server" no Discord (opcional):** deixa os amigos entrarem no mesmo servidor pelo seu
  status. Vem desligado e nunca aparece em servidor privado.
- **Onde fica o servidor:** depois de entrar num jogo, um aviso pequeno com o Bit aparece no canto da
  tela, por exemplo "Location: São Paulo, BR". Ele não tira o foco do jogo e some sozinho.
- As três opções ficam em Behaviour → **While you play**.
- Para isso, o Blox Code fica aberto em segundo plano, sem janela, enquanto o Roblox roda, e lê o log
  do Roblox. Fecha sozinho quando o Roblox fecha.

## 1.9.1 — Janela de inicialização sem espaço sobrando

### Visual

- A janela que aparece enquanto o Roblox abre ficou mais baixa, e o Bit, a barra e o texto agora
  ficam centralizados, sem o espaço vazio que sobrava antes do rodapé. Se der erro, a janela cresce
  um pouco para a mensagem caber.

## 1.9.0 — Visual "Midnight terminal" e links do Roblox

### Correções

- **"Roblox links don't open Blox Code" voltava sozinho:** o Roblox oficial, quando instala ou se
  atualiza, registra de novo os links `roblox://` para ele mesmo. Agora o Blox Code instalado pega
  os links de volta toda vez que abre e antes de iniciar o Roblox, como o Bloxstrap faz. Se outro
  launcher (Bloxstrap, Fishstrap…) estiver com os links, ele não mexe e o aviso continua perguntando.
- No Linux, o mesmo vale para o Sober: quando uma atualização dele toma os links, o Blox Code pega de
  volta.
- Uma cópia do Blox Code aberta com outra pasta de configurações (portátil ou de teste) não impede
  mais de abrir o menu da cópia instalada.

### Visual

- Novo visual **Midnight terminal**, nos dois sistemas: fundo quase preto, barra lateral e cartões
  um tom acima, bordas finas e o menta `#4ADE9A` como única cor de destaque.
- Títulos das páginas e rótulos em JetBrains Mono; rótulos de grupo em maiúsculas acima dos cartões.
- Barra lateral: marca em mono, botão Play maior, ícone da página aberta em menta e um cartão de
  status do Roblox no rodapé, com a versão do Blox Code embaixo.
- Home: saudação em mono, busca com lupa, botão de enviar com seta, atalhos de Mods e Fast Flags com
  ícone em menta, e Open Roblox / Reinstall Roblox no rodapé.
- Mods: cartão da pasta de mods no topo, presets com ícone, todas as seções de arquivos (cursor,
  sons, fonte) e a lista "Your files" com um aviso quando está vazia.
- Interruptores e seletores novos, e a janela de inicialização com a porcentagem em menta.

## 1.8.0 — Visual minimalista e tela inicial

### Tela inicial

- Ao abrir o Blox Code aparece uma tela como a do Bloxstrap: **Launch Roblox**, que já mostra o
  preset gráfico, os mods e as Fast Flags que vão ser usados, ou **Configure Blox Code**. Fechar a
  tela fecha o Blox Code. Links do Roblox e o atalho "Play Roblox" continuam abrindo o jogo direto, e
  `-menu` abre as configurações sem passar por ela.

### Visual

- Visual minimalista nos dois sistemas: fundo grafite (ou quase branco no tema claro), cartões
  chapados com bordas finas, sem brilho de fundo, sem gradientes e sem sombras.
- O menta fica só no que importa: o botão principal e os interruptores. Opções escolhidas e a página
  aberta na barra lateral usam um destaque neutro.
- Títulos um pouco menores e mais leves, seções da barra lateral sem letras maiúsculas e rodapé da
  barra lateral sem moldura.

## 1.7.1 — Atualizações só pelo GitHub

- O Blox Code agora busca atualizações só no GitHub. O endereço de reserva do GitLab foi removido.

## 1.7.0 — Presets de mods do Bloxstrap

### Mods

- **Presets com um clique**, iguais aos do Bloxstrap (lidos do próprio Bloxstrap com engenharia
  reversa):
  - **Cursor do mouse** de 2006 ou de 2013;
  - **Sons antigos do personagem**: passos, pulo e levantar clássicos; queda, aterrissagem, nado e
    splash ficam em silêncio;
  - **Fundo antigo do editor de avatar** (só no Windows);
  - **Estilo de emoji**: Catmoji, Windows 11, Windows 10 ou Windows 8. A fonte é baixada quando você
    escolhe e conferida antes de ser usada.
- Desligar um preset apaga só os arquivos dele. Se você trocou algum por um arquivo seu, ele fica.
- Os arquivos que vieram de um preset aparecem como "From the … preset" na lista de mods.
- Nova aba **Presets** e **Reset all** também desliga os presets e o emoji.

### Linux

- O AppImage volta a ser gerado e testado automaticamente (Ubuntu 22.04 e 24.04, Debian 12 e Arch),
  agora pelo GitLab.

## 1.6.0 — Atualizações que não dependem do GitHub

### Atualizações

- **Fonte de atualização própria:** o Blox Code não pergunta mais ao GitHub se tem versão nova.
  Ele lê um arquivo `latest.json` assinado, que pode ficar em qualquer hospedagem, e tenta os
  endereços de reserva na ordem quando um sai do ar.
- **Atualizações assinadas:** o app só aceita uma versão nova se o `latest.json` estiver assinado
  com a chave do Blox Code, e só instala o arquivo baixado se o SHA-256 bater. Se alguém invadir a
  hospedagem, não consegue empurrar um programa falso.
- Se o download por um endereço falhar, o Blox Code tenta o próximo da lista.

### Outras mudanças

- **What's new** mostra as notas da versão dentro do próprio app, sem abrir site nenhum.
- Janelas com texto longo agora rolam em vez de passar do tamanho da tela.

## 1.5.0 — Presets que conhecem seu PC, visual novo

### Gráficos

- **Preset sugerido para o seu PC:** o Blox Code identifica a placa de vídeo (Windows pelo registro,
  Linux pelo sistema) e sugere um preset: Lowest para Intel HD/UHD antigas, Low para Iris Xe e APUs
  da AMD, e Low/Medium/High para placas dedicadas conforme a memória de vídeo. Um botão **Use it**
  aplica a sugestão. A Home também mostra qual é a sugestão.
- **Medium mais bonito:** antes forçava texturas no nível 2. Agora deixa o Roblox escolher as
  texturas mais nítidas que a placa aguenta. Quem estava no Medium antigo passa para o novo sozinho.
- **Mais FPS opcional:** dois novos interruptores, só com flags da allowlist: **Pause lighting
  updates** (luz e sombra de objetos em movimento param de atualizar) e **Plain gray sky** (sem
  skybox).

### Visual

- Cartões com aparência de vidro (brilho no topo, gradiente suave e, no Linux, sombra), botão
  principal em gradiente com sombra, abas e seletores em formato de pílula com a opção escolhida em
  menta, cantos mais arredondados e um brilho menta discreto no fundo.
- Títulos das páginas em Inter, grandes e em negrito; a fonte de terminal fica no nome, na
  saudação e no código. Seções da sidebar em maiúsculas, e a página aberta fica com fundo menta.

### Correções

- Dois pacotes do Roblox com o mesmo conteúdo podiam ser baixados ao mesmo tempo no mesmo arquivo.
  Agora o segundo espera o primeiro.
- Sem fonte personalizada, o Blox Code não abre mais os arquivos do Roblox a cada launch só para
  conferir as fontes (mais rápido, principalmente no Linux).
- As prévias de fonte e de som na tela Mods deixavam cópias acumulando na pasta temporária. Agora
  elas são apagadas.

## 1.4.0 — Fontes novas, animações e Mods melhores

### Visual

- **Fontes próprias:** o Blox Code agora traz a **Inter** (texto) e a **JetBrains Mono** (títulos e
  código) dentro do programa, então fica igual em todo PC e em toda distro. Antes dependia das fontes
  instaladas (Segoe UI, Cascadia, DejaVu…).
- **Animações:** as páginas entram deslizando e os cartões aparecem um depois do outro; botões,
  abas e itens da sidebar mudam de cor suavemente; botões dão uma leve "apertada" no clique; a barra
  menta da página aberta cresce do meio. Dá para desligar em **Appearance → Interface animations**
  (no Windows também respeita a opção de animações do sistema).
- O cartão do rodapé da sidebar ganhou uma bolinha: verde instalado, âmbar rodando portátil.

### Mods

- **Abas:** All, Cursor, Sounds, Font e Advanced, para ir direto ao que você quer trocar.
- **Ouvir antes:** cada som tem um botão ▶ que toca o seu arquivo ou o som original do Roblox.
- **Reset all** traz de volta o cursor, os sons e a fonte originais de uma vez.

## 1.3.2 — Atualizações do Roblox mais rápidas

- **Windows:** o Blox Code escolhia o servidor que respondia primeiro a um teste pequeno, e muitas
  vezes era o da Amazon S3, que baixa mais devagar. Agora os servidores de CDN do Roblox têm
  preferência (Akamai primeiro) e o S3 fica de reserva.
- O servidor `roblox-setup.cachefly.net` saiu da lista: ele passava no teste, mas não entrega mais
  os arquivos do Roblox.
- Os pacotes agora baixam 3 de cada vez, o maior primeiro, e cada um é conferido e descompactado
  enquanto os outros ainda baixam. Na mesma versão do Roblox, a instalação caiu de cerca de 86 s
  para 45 s.

## 1.3.1 — Roblox nítido de novo depois de um preset

- **Windows:** enquanto um preset gráfico fixa a qualidade, o Roblox guarda esse nível para cada jogo
  em `%LocalAppData%\Roblox\frm.cfg`. Depois de voltar para o Auto, os jogos continuavam começando na
  qualidade antiga (no Lowest antigo, a 1, borrada), até no Roblox aberto sem o Blox Code. Agora,
  quando a qualidade fixa sai, o Blox Code apaga esse arquivo antes do próximo jogo e o Roblox o
  recria do zero.

## 1.3.0 — Mods sem fuçar pasta, layout novo

### Mods

- **Escolha um arquivo e pronto:** a tela Mods agora lista o que dá para trocar (cursor do mouse,
  cursor de texto, sons de morte, pulo, aterrissagem, passos, queda, natação, mergulho, levantar e
  explosão, e a fonte do jogo). Clique em **Choose file…** ou arraste o arquivo para a linha, e o
  Blox Code coloca no lugar certo. **Reset** traz o original do Roblox de volta.
- **Fonte do jogo:** troca todas as fontes do Roblox pela sua (TTF ou OTF), do jeito que o Bloxstrap
  faz: cada família de fonte do Roblox passa a apontar para ela, e isso é refeito a cada versão nova
  do Roblox.
- A pasta `Modifications` e (no Linux) a lista de todos os arquivos do Roblox continuam lá, como
  opção avançada.

### Layout

- **Home:** o preset gráfico fica direto na tela inicial, com atalhos para Mods e Fast Flags que
  mostram o que você já mudou.
- **Sidebar:** botão **Play Roblox** destacado e páginas separadas em *Customize* e *App*.

### Fast Flags

- **Lowest sem borrão:** o Lowest usava qualidade 1 e texturas no nível 0, o que deixava a imagem
  toda borrada. Agora usa qualidade 4 e texturas no nível 1 (o Low foi para qualidade 6 e texturas
  2). Quem estava no Lowest ou Low antigo passa para o novo sozinho.

## 1.2.0 — Tema próprio e presets gráficos

### Visual novo

- **Tema próprio do Blox Code:** o layout continua o mesmo (sidebar, composer, cartões), mas as
  cores agora são nossas. **Midnight** (escuro) é um azul-marinho profundo, como a tela do Bit, com
  um acento verde-menta; **Daylight** (claro) é um papel azulado com menta escura. O Bit continua
  cor de argila.
- Títulos e o nome `blox code_` em fonte de terminal, e a página aberta na sidebar ganha uma barra
  de cursor menta.

### Fast Flags

- **Preset gráfico:** Auto, Lowest, Low, Medium e High mudam qualidade, texturas, grama e detalhe
  de uma vez. Usam só flags da allowlist do Roblox (as outras o Roblox ignora). Se você mexer numa
  das flags à mão, o preset aparece como personalizado.
- **Rendering API (Windows):** escolha Direct3D 11, Vulkan ou OpenGL.
- Os presets seguem o modelo do gerenciador de Fast Flags do Bloxstrap: cada preset é dono de uma
  flag, opções que se excluem nunca ficam ligadas juntas e toda mudança vai para o log.
- **Editor:** flags fora da allowlist ganham o aviso "Roblox ignora esta flag".

## 1.1.2 — Só o Bit, e mais estabilidade

### O Bit em todo lugar

- **Atalhos do Windows:** quem tinha o Blox Code desde a 1.0.x ainda via o Clawd no atalho da área
  de trabalho e do menu Iniciar. O programa já tinha o ícone do Bit, mas o Windows guardava a imagem
  antiga. Agora, depois de cada atualização (e ao instalar), o Blox Code regrava os próprios atalhos
  e avisa o Windows para buscar o ícone de novo. Atalhos que você apagou continuam apagados.

### Linux

- **Sem FUSE:** quem abre o AppImage com `--appimage-extract-and-run` (porque o sistema não tem
  FUSE) perderia o Blox Code na primeira atualização: a versão nova era aberta sem esse modo e
  fechava na hora. O atalho do menu de aplicativos tinha o mesmo problema. Agora os dois usam o
  mesmo modo da cópia atual.
- **Busca nos arquivos do Roblox (Mods):** a lista tem dezenas de milhares de arquivos e era
  refiltrada a cada tecla. Agora filtra quando você para de digitar, sem travar a digitação.
- **About:** clicar no Bit fazia ele pular para sempre. Agora é um pulo só, como no Windows.

### Windows

- **Mods:** uma pasta dentro de `Modifications` que não pudesse ser lida (permissão, arquivo em uso)
  fazia a tela Mods dar erro e impedia o Roblox de abrir. Agora ela é pulada.
- A tela Behaviour apontava para o lugar antigo da configuração de atualização.

### Atualização automática (Windows e Linux)

- Se um passo depois da troca do arquivo falhasse (atalhos, registro, menu do Linux), a atualização
  inteira era tratada como falha, mesmo com a versão nova já instalada. Agora isso fica só no log.
- Se a cópia nova não conseguisse abrir, o Blox Code mostrava um erro genérico de inicialização.
  Agora registra o problema e segue, como já fazia nos outros casos.

## 1.1.1 — Versão certa em Aplicativos

- **Windows:** depois de se atualizar sozinho para a 1.1.0, o Blox Code continuava aparecendo como
  1.0.3 em Configurações → Aplicativos (o programa em si já era o novo). Agora a versão mostrada
  acompanha cada atualização. Nada muda no Linux.

## 1.1.0 — Blox Code no Linux

O Blox Code agora roda no Linux, nas distros baseadas no **Arch** (Arch, CachyOS, EndeavourOS,
Manjaro) e no **Debian/Ubuntu** (Ubuntu, Linux Mint, Pop!_OS, Zorin, Debian). Vem num único arquivo,
o `BloxCode-x86_64.AppImage`, com o mesmo tema do Claude desktop e o Bit.

> **Primeira versão para Linux.** O AppImage passa nos testes automáticos no Ubuntu, no Debian e no
> Arch, mas jogar de verdade pelo Sober ainda está sendo testado num PC real. Se algo não funcionar,
> use **Behaviour → Export diagnostics** e abra uma issue com o zip.

### Como funciona no Linux

- **Via Sober.** O Roblox para Windows não roda mais no Wine (o anti-cheat bloqueia), então o
  caminho no Linux é o [Sober](https://sober.vinegarhq.org/), que roda a versão Android do Roblox
  nativamente. O Sober baixa e atualiza o Roblox sozinho; o Blox Code cuida do resto.
- **Instala o Sober para você.** Se ele faltar, a tela inicial mostra **Install Sober**: o Blox Code
  adiciona o Flathub e instala o Sober só para o seu usuário, sem pedir senha, com o Bit andando na
  barra de progresso. Se faltar o próprio Flatpak, aparece o comando certo para a sua distro
  (`pacman` ou `apt`) com um botão de copiar. O Blox Code nunca roda `sudo` sozinho.
- **Links do navegador.** Instalado, o Blox Code entra no menu de aplicativos e passa a abrir os links
  `roblox://`: aplica suas Fast Flags e mods e entrega o link ao Sober.

### O que dá para fazer

- **Fast Flags:** o mesmo editor do Windows (busca, validação, chave para booleanas, Import JSON,
  Export JSON). As flags vão direto para o `config.json` do Sober assim que você muda, como valores
  JSON de verdade (`true`, `144`), então valem até se você abrir o Sober direto. As flags que você já
  tinha colocado no Sober são importadas na primeira vez, não apagadas. Presets de anti-aliasing e
  qualidade de textura.
- **Mods com a lista dos arquivos do Roblox:** a tela Mods lê o pacote do Roblox que o Sober baixou e
  lista cursores, sons, fontes e texturas, com busca e filtros. Escolha um arquivo, clique em
  **Replace with file…** e ele vai para o lugar certo do `asset_overlay` do Sober na hora.
  **Restore original** desfaz. Se você já tinha colocado um arquivo no overlay na mão, ele é guardado
  e volta quando o mod sai.
- **Configurações do Sober explicadas:** qualidade gráfica (Quality, Balanced, Performance), OpenGL
  ou Vulkan, HiDPI, GameMode, fechar ao sair do jogo, localização do servidor, Discord e controles,
  cada uma com uma frase dizendo para que serve.
- **Restaurar o Sober:** volta o `config.json` do backup que o Blox Code faz antes da primeira mudança,
  reseta o Sober para o padrão ou tira todos os mods do Blox Code de dentro dele.
- **Aviso de NVIDIA:** se o driver da NVIDIA não bate com a extensão GL do Flatpak (o motivo mais
  comum do Sober rodar sem a placa de vídeo), a tela inicial avisa e mostra `flatpak update`.
- **Exportar diagnóstico:** em Behaviour, um `.zip` com versões (distro, Flatpak, Sober, driver),
  configurações e logs para mandar a quem estiver ajudando. Sem senhas nem dados da conta do Roblox.

### Robustez

- O `config.json` do Sober é gravado num arquivo temporário, validado e trocado de uma vez: um JSON
  quebrado impediria o Sober de abrir. As chaves que o Blox Code não conhece ficam intactas. Se o
  arquivo já estiver quebrado, o Blox Code não mexe nele e explica o que fazer.
- A saída do Sober vai para `sober.log`, e não para o Blox Code, então fechar o Blox Code nunca
  derruba o jogo. Se o Sober fechar logo ao abrir, a tela de launch mostra o erro.
- A atualização automática troca o AppImage sem estragar a cópia que está rodando, e baixa para
  `~/.cache` (o `/tmp` pode não deixar executar programas em algumas distros).
- Uma janela de menu e um launch por vez, como no Windows.
- O AppImage é testado automaticamente a cada versão no Ubuntu 22.04 e 24.04, no Debian 12 e no Arch,
  com um autoteste (`BloxCode-x86_64.AppImage --self-test`) que roda numa pasta temporária, sem mexer
  nas suas configurações.

### Windows

- Nada muda no uso. Por dentro, a parte comum (configurações, Fast Flags, atualizador) virou uma
  biblioteca compartilhada com a versão Linux.
- As versões agora são compiladas e publicadas pelo GitHub Actions, com o `.exe` e o `.AppImage` no
  mesmo release.

## 1.0.3 — Conheça o Bit

O Blox Code ganhou um mascote próprio. O **Bit** substitui o Clawd, que era o mascote do Claude Code
(da Anthropic). Agora a identidade visual do Blox Code é nossa, e a versão para Linux já nasce com ela.

- **Quem é o Bit:** um bloco de montar, como o "Blox" do nome, com um terminal no lugar do rosto,
  como o "Code". Na cabeça, uma antena com uma faísca que pisca como sinal. É pixel art, igual ao
  estilo do resto do app.
- **Animações:**
  - **Parado:** respira, pisca, e a boca é um cursor de terminal piscando.
  - **Baixando o Roblox:** anda em cima da barra de progresso.
  - **Extraindo:** digita com os bracinhos enquanto a tela rola linhas de código.
  - **Terminou:** dá um pulinho com os olhos em ^ ^ e a antena acesa.
  - **Deu erro:** antena apagada, olhar baixo e boca triste.
  - Clicar nele na tela About também faz ele pular.
- **Ícone novo:** o Bit no lugar do Clawd, na barra de tarefas, no menu Iniciar e nos atalhos. Os
  atalhos que já existem passam a mostrar o ícone novo depois da atualização (o Windows pode demorar
  um pouco para atualizar o cache de ícones).
- **Indicador de carregamento novo:** o asterisco que piscava na tela de launch (que também vinha do
  Claude Code) virou um bloco girando em volta de um quadrado (▖ ▌ ▘ ▀ ▝ ▐ ▗ ▄). O ✻ da barra de título
  e da lista de mods também saiu.
- O tema continua o mesmo: as cores e a tipografia no estilo do Claude desktop não mudam.

## 1.0.2 — Robustez antes do port para Linux

Última varredura no código antes de começar a versão para Linux. Nada muda na aparência; o foco é
o Blox Code não travar nem fechar sozinho em situações comuns.

### Erros que podiam fechar o app

- **Área de transferência ocupada.** Se outro programa estivesse usando a área de transferência
  (gerenciadores de clipboard, o próprio navegador), o **Export JSON** do Fast Flag Editor dava erro.
  Agora ele tenta de novo por um instante e, se não der, avisa "The clipboard is busy — try again".
- **Abrir pastas e links.** "Mods folder", "Open logs", "Open install folder" e os links do app
  davam erro se a pasta não existisse ou não houvesse navegador padrão. Agora a pasta é criada se
  faltar, e qualquer outro problema vira uma mensagem.
- **Erro logo na abertura.** Se a pasta de dados (`%LocalAppData%\BloxCode`) não pudesse ser criada,
  o app fechava antes de conseguir mostrar ou registrar o erro. Agora o tratamento de erros entra
  primeiro, e você vê a mensagem.
- **Erros fora da janela principal.** Falhas em tarefas de fundo não eram registradas em lugar
  nenhum. Agora vão para o log, o que facilita achar a causa de qualquer problema.
- **Mensagem de erro que falha.** Se a própria janela de erro falhasse ao abrir, o app podia entrar
  num ciclo de erros. Agora isso é registrado e o ciclo é interrompido.

### Abrir o Roblox

- **Links com espaços ou aspas.** O link do jogo agora é repassado ao Roblox como um único argumento
  entre aspas quando precisa, então não pode ser quebrado em pedaços.
- **Canal inválido.** Um canal digitado com espaço, barra ou outro caractere estranho em
  Behaviour virava um endereço errado. Agora é ignorado (fica registrado no log) e o Blox Code usa
  o `production`.
- **Instalador do WebView2 travado.** Se o instalador do WebView2 (que o Roblox usa) travasse, o
  launch ficava esperando para sempre. Agora ele espera no máximo 3 minutos e segue; o Roblox abre
  mesmo assim.
- **Conexão que não responde.** Um servidor que não aceita a conexão agora falha em 10 segundos, em
  vez dos ~21 segundos do Windows, e o Blox Code passa para o próximo.

### Instalação e Fast Flags

- **Atalhos.** Se um atalho do menu Iniciar ou da área de trabalho não pudesse ser criado, a
  instalação parava no meio. Agora o atalho com problema é pulado (fica no log) e a instalação termina.
- **Importar arquivo JSON.** Abrir um arquivo que não pode ser lido, ou um arquivo enorme por engano,
  dava erro ou travava o editor. Agora aparece uma mensagem; arquivos acima de 2 MB são recusados.

### Novidade pequena

- O aviso **"Updated to vX"** na tela inicial agora tem o botão **What's new**, que abre as notas
  daquela versão no GitHub.

## 1.0.1 — Estabilidade

### Rede e downloads

- **Travamentos de até 10 minutos.** A escolha do servidor de download, a consulta da versão do
  Roblox e o manifesto de pacotes agora têm tempo-limite curto (8, 15 e 30 segundos).
- **Download parado.** Um download que parasse de receber dados ficava travado para sempre. Agora,
  sem dados por 30 segundos, ele é tentado de novo, até 3 vezes.
- **Sem internet.** Aparecia como "Channel 'production' is not available". Agora aparece como erro
  de rede.

### Abertura e atualização

- **Processo invisível.** Um erro antes da primeira janela deixava o Blox Code rodando sem janela.
  Agora ele mostra o erro e fecha.
- **Atualização que falha.** Se o Blox Code não conseguisse se substituir, o que você pediu (entrar
  no jogo) se perdia. Agora acontece mesmo assim, na versão atual, e a atualização tenta de novo depois.
- **Uma janela de menu por vez.** Abrir o Blox Code de novo traz o menu já aberto para a frente,
  como no Bloxstrap, em vez de abrir um segundo que sobrescreveria as configurações do primeiro.
- **Launch na fila.** Um segundo launch ao mesmo tempo mostra "Waiting for another Blox Code launch
  to finish".
- **Tema do sistema.** No tema "System", o Blox Code troca de claro para escuro junto com o Windows.

### Arquivos e configurações

- **Configurações sem corromper.** Settings e estado são gravados num arquivo temporário e trocados
  de uma vez, então desligar o PC no meio não estraga nada. Um arquivo já estragado é guardado como
  `.corrupt` em vez de ser substituído pelas configurações padrão.
- **Desinstalação.** O comando de desinstalação não apagava a pasta de instalação (o `timeout` do
  Windows falha sem console). Agora apaga.
- **Roblox aberto.** Reinstalar com o Roblox aberto quebrava a instalação pela metade. Agora o Blox
  Code avisa "Close Roblox and try again" antes de apagar qualquer coisa.
- **Mods em uso.** Um mod que o Roblox estivesse usando impedia o jogo de abrir. Agora é pulado e
  aplicado no próximo launch.

### Fast Flag Editor

- **Flags sumindo.** Com duas linhas de mesmo nome, as duas saíam das configurações salvas,
  inclusive a original. Agora só a cópia repetida fica marcada.
- **Mais leve.** Marcar caixas não salva mais a lista inteira a cada clique.

## 1.0.0 — Primeira versão

- Bootstrapper do Roblox com a cara do Claude desktop, estrelando o Clawd.
- Fluxo de atualização e launch obtido por engenharia reversa do Bloxstrap 2.11.4 com o REA e
  reescrito do zero em C#/WPF.
- Fast Flag Editor no estilo do Bloxstrap (Add single / Import JSON, validação, presets).
- Mods com backup e restauração dos arquivos originais.
- Instalação por usuário, sem precisar de administrador.
- Atualização automática pelo GitHub Releases.
