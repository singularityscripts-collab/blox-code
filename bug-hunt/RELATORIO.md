# Caça-bugs do Blox Code — rodada 2: as funções do launcher (1.13.1, commit ced0d7d)

Feita em 19/11/2026, só no projeto local, com arquivos de teste numa pasta temporária. Nenhum
servidor de terceiros foi atacado; o teste de download usa um servidor falso no próprio PC
(127.0.0.1). Nada foi corrigido ainda.

Áreas atacadas: instalação e atualização do Roblox (mirrors, pacotes, extração, cache, versões
antigas), mods e fonte, Fast Flags e gráficos por jogo, multi-instância, links e a barra da Home,
atalhos de jogo, "Free up space", "Reinstall Roblox", desinstalar, auto-update do próprio Blox Code,
programas que abrem com o Roblox e o estado salvo (`State.json`) entre processos.

Testes em `bug-hunt/Program.cs` (seção "Rodada 2"). `dotnet run --project bug-hunt`: 5 bugs
reproduzidos, 33 verificações ok. Cada bug falhou nas duas execuções.

**Status: os 5 bugs e as 4 suspeitas foram corrigidos (ainda sem commit).** `dotnet run --project
bug-hunt`: 41 de 41 verificações passam (`L6`, `L7` e `L8` são testes novos, um para cada
suspeita corrigida que dava para testar). Builds do Windows e do Linux: 0 avisos. Autoteste
do Linux: todas as verificações passam. O app real abre, mostra Settings e fecha sem erro no log.

| # | Correção |
| --- | --- |
| L1 | `Paths.InstallExecutable`: se o `BloxCode.exe` estiver em uso, ele é renomeado para `BloxCode.exe.<id>.old` (o Windows permite) e o novo é copiado. As cópias antigas são apagadas numa próxima abertura, quando ninguém mais roda delas (`Paths.DeleteOldExecutables`). A instalação a partir da versão portátil usa o mesmo caminho. |
| L2, L2b | `Bootstrapper.IsVersionInUse` confere o Roblox e o Studio rodando da pasta, inclusive pelos atalhos `Instances\N`. "Free up space" e a limpeza de versões antigas pulam a versão inteira enquanto ela está em uso e a apagam depois que o jogo fecha. |
| L3 | `Store.SaveState` grava só os campos que este processo mudou desde a última leitura ou gravação, por cima do que está no disco, e traz para a memória o que os outros processos salvaram. A gravação passa por um mutex nomeado (`BloxCode-State`). |
| L4 | Cada download da atualização tem um nome próprio (`BloxCode-X-<id>.exe`); as sobras são apagadas numa próxima abertura. |
| Suspeita: dois launches juntos | `LaunchGate`: mutex nomeado num thread próprio. O segundo launch espera sem limite de tempo, o Cancel interrompe a espera, e um launch que caiu libera a vez (mutex abandonado). Antes ele desistia depois de 3 minutos e rodava junto com o primeiro. |
| Suspeita: versão fixada fora do ar | Um 403/404 no manifesto da versão fixada vira `PinnedVersionGoneException`, com uma mensagem que fala da versão fixada e de onde limpá-la. A consulta à página de status do Roblox é pulada nesse caso. |
| Suspeita: desinstalar | O aviso (no Settings e no `-uninstall`) diz que configurações, Fast Flags e mods também vão embora, e mostra a pasta dos mods. |
| Suspeita: Studio apagava o Prefetch | A limpeza do cache de downloads mantém os pacotes baixados nos últimos 2 dias. |

## Bugs confirmados (do mais grave para o menos grave)

### L1 — O Blox Code não se atualiza enquanto o Roblox está aberto, e baixa a atualização toda vez

- **Gravidade:** Alta
- **Onde:** `shared/Updater.cs` (`ApplyUpgrade`), `shared/Paths.cs:82` (`InstallExecutable`),
  `windows/App.xaml.cs` (`FindUpdateAsync`)
- **Como reproduzir:**
  1. Abra um jogo pelo Blox Code. O processo que abriu o Roblox continua aberto em segundo plano
     (Discord, histórico) até o Roblox fechar. Ele roda a partir de `BloxCode.exe` instalado.
  2. Com uma versão nova do Blox Code publicada, clique num link do Roblox ou abra o Blox Code.
- **Esperado:** a atualização é instalada, ou fica para quando o Roblox fechar, sem erro.
- **Aconteceu:** a versão nova (~60 MB) é baixada, a troca do `BloxCode.exe` falha porque o arquivo
  está em uso ("being used by another process") e aparece "Update didn't finish". Isso se repete
  a cada link ou abertura enquanto o jogo roda: são 60 MB baixados de novo a cada vez, e uma janela
  de erro antes de cada jogo.
- **Causa provável:** o Windows não deixa sobrescrever um `.exe` em execução. `ApplyUpgrade` só
  espera 5 s (10 tentativas), que é o tempo de o processo anterior fechar, mas o processo em segundo
  plano fica aberto por horas.
- **Sugestão:** o Windows deixa *renomear* um `.exe` em execução. Renomear o instalado para
  `BloxCode.exe.old`, copiar o novo e apagar o `.old` na próxima abertura. Outra opção é não
  procurar atualização enquanto houver um Blox Code em segundo plano.
- **Teste:** `L1` (um programa de verdade rodando a partir do caminho instalado).

### L2 / L2b — Apagar uma versão antiga do Roblox que ainda está aberta deixa a pasta pela metade

- **Gravidade:** Média
- **Onde:** `windows/Core/DiskCleanup.cs:22` (Settings → Free up space) e
  `windows/Core/Bootstrapper.cs:639` (`CleanupOldVersions`, ligado por padrão)
- **Como reproduzir (L2):** jogue numa versão do Roblox; enquanto o jogo roda, o Roblox lança uma
  versão nova e outro launch instala essa versão; abra Settings → Free up space.
- **Como reproduzir (L2b):** com multi-instância ligada e um jogo aberto, o Roblox lança uma versão
  nova e você abre outra janela: a instalação da versão nova apaga a pasta da antiga.
- **Esperado:** o próprio código diz "Anything in use is skipped, never forced": uma versão em uso
  fica intacta.
- **Aconteceu:** `Directory.Delete(recursive)` apaga tudo o que não está preso (sons, texturas,
  fontes, `AppSettings.xml`) e só para no `.exe` em uso. O jogo aberto perde os arquivos que ainda
  vai carregar, e a pasta fica quebrada no disco. No "Free up space", o espaço liberado também não
  é contado.
- **Causa provável:** nada confere se um `RobloxPlayerBeta.exe` está rodando daquela pasta antes de
  apagar. O `Bootstrapper` já tem `IsRunningFrom` para isso, mas só usa na reinstalação.
- **Sugestão:** pular a pasta quando `IsRunningFrom(dir, "RobloxPlayerBeta.exe")` for verdadeiro,
  incluindo as janelas abertas pelos atalhos `Instances\N`, e tentar de novo na próxima limpeza.
- **Testes:** `L2`, `L2b`.

### L3 — Fechar o Roblox desfaz o que outro Blox Code salvou enquanto o jogo rodava

- **Gravidade:** Média
- **Onde:** `windows/Core/Integrations.cs:59` (`CloseAll` → `Store.SaveState`), `shared/Settings.cs:169`
- **Como reproduzir:**
  1. Em Settings → Open with Roblox, configure um programa (ex.: OBS) com "fecha junto".
  2. Abra um jogo. O processo em segundo plano guarda uma cópia do `State.json` na memória.
  3. Com o jogo aberto, abra as configurações e clique em "Reinstall Roblox" (agendado) ou dispense
     o banner de importar.
  4. Feche o Roblox.
- **Esperado:** o que foi salvo no passo 3 continua salvo.
- **Aconteceu:** ao fechar o OBS, o processo em segundo plano grava a cópia antiga inteira do
  `State.json` por cima. A reinstalação agendada some e o banner volta. Pelo mesmo caminho podem
  voltar a versão do Roblox e a lista de mods aplicados, o que força uma reinstalação desnecessária.
- **Causa provável:** `Store.SaveState` grava o objeto inteiro que está na memória, que foi lido
  quando aquele processo abriu.
- **Sugestão:** em `CloseAll` (e em quem grava estado num processo que vive muito), reler o
  `State.json` do disco, mudar só o campo necessário e gravar, de preferência com um lock entre
  processos (um mutex nomeado).
- **Teste:** `L3`.

### L4 — Dois links abertos juntos: um deles falha ao baixar a atualização

- **Gravidade:** Baixa
- **Onde:** `shared/Updater.cs:206` (`DownloadAsync`, sempre o mesmo arquivo `BloxCode-X.exe`)
- **Como reproduzir:** com uma atualização publicada, clique em dois links do Roblox (ou em dois
  atalhos de jogo) em poucos segundos.
- **Esperado:** os dois seguem; o segundo espera ou reaproveita o download do primeiro.
- **Aconteceu:** o segundo falha na hora com "being used by another process" e mostra "Couldn't
  update Blox Code".
- **Sugestão:** baixar para um arquivo temporário único e renomear no fim, ou segurar o mutex
  `BloxCode-AutoUpdater` durante o download.
- **Teste:** `L4` (servidor falso local, download lento de 3 MB).

## Suspeitas encontradas lendo o código (não reproduzidas)

- **Dois launches instalando o Roblox ao mesmo tempo** (`BootstrapperWindow.RunAsync`): o segundo
  launch espera o primeiro por no máximo 3 minutos e depois segue sem a trava. Numa primeira
  instalação lenta (mais de 3 min), se a pessoa clicar em Play de novo, o segundo pode apagar a
  pasta que o primeiro ainda está extraindo (`Directory.Delete(versionDir)`). Precisaria de uma
  conexão lenta de verdade para reproduzir.
- **Versão fixada que o Roblox tirou do ar** (Settings → Pin): o launch falha com "Network error:
  … 403 (Forbidden)", sem dizer que o problema é a versão fixada. Até a pessoa limpar o campo, o
  Roblox não abre. Não testei para não fazer pedidos de propósito ao CDN do Roblox.
- **Desinstalar apaga mods e configurações:** `rmdir /S /Q` em `%LocalAppData%\BloxCode` leva
  junto a pasta `Modifications` (sons, cursor e fonte que a pessoa escolheu) e o `Settings.json`.
  O aviso fala só em "Blox Code, os atalhos e os arquivos do Roblox".
- **Abrir o Studio apaga o que foi baixado em segundo plano:** a instalação do Studio limpa o cache
  de downloads e mantém só os pacotes já instalados, então os pacotes da versão nova do Roblox
  baixados durante o jogo (Prefetch) são apagados.

## O que testei e não quebrou

- Atalho de jogo com nome reservado do Windows ("CON"): criado normalmente (`L5`).
- Extração de pacotes: um caminho `../` dentro do zip é recusado (`ExtractPackage`, lendo o código).
- Nome de versão e canal: só `version-` com 16 hex, e canal só `[a-z0-9_-]` (rodada 1, B9).
- Links e barra da Home: links de jogo, servidor privado, share e `roblox://` (rodada 1).
- Fast Flags: importação, perfis, valores nulos, JSON malformado (rodada 1, B8/B10/X4).
- Download de pacotes do Roblox: tentativa de novo, detector de travamento (30 s sem dados) e
  checksum MD5, conferidos lendo o código.
- Auto-update: manifesto sem assinatura válida é recusado (X1); tamanho e SHA-256 conferidos.
- Mods: remover um mod restaura o original pelo backup; mod preso pelo Roblox é tentado de novo no
  próximo launch, sem derrubar o launch (lendo o código).
- Programas que abrem com o Roblox: abre uma vez só e fecha só o que o Blox Code abriu (F5).
- Menu e links (corrigidos na 1.13.1): B11 e B12 continuam passando.

---

# Caça-bugs do Blox Code — 1.12.0 (commit ecba0ce)

Feito em 11/11/2026, só no projeto local, com dados de teste numa pasta temporária.

**Status: os 10 bugs foram corrigidos (ainda sem commit).**

- `dotnet run --project bug-hunt`: 20 de 20 verificações passam.
- Autoteste (`--self-test`): todas as verificações passam.
- Builds do Windows e do Linux: 0 avisos.
- O B3 foi conferido no app de verdade: com o mesmo `History.json` quebrado, o Blox Code abre
  normalmente.

O que mudou em cada bug:

| # | Correção |
| --- | --- |
| B3 | O histórico descarta entradas nulas, e um jogo sem nome vira "Place N". Erros nos recentes não impedem mais a Home de abrir (`GameHistory.Parse`, `HomePage.RefreshRecent`). |
| B2 | Um arquivo inválido é guardado como `History.json.corrupt` antes de ser regravado. Se o arquivo estiver preso, a leitura é tentada de novo e a gravação é pulada; ela nunca grava uma lista vazia por cima (`GameHistory.LoadForChange`). Teste extra: `B2b`. |
| B1 | `long.TryParse` no `ActivityParser`, e cada linha do log é lida dentro de um try/catch no watcher. |
| B4, B5, B6 | `RobloxLink.ToLaunchArgs` agora mantém `linkCode` (servidor privado) e `gameInstanceId`, aceita `games/start?placeId=` e `roblox.com/share?code=…&type=…`. Os valores são validados antes de entrar no link. Teste extra: `X6`, com os links que já funcionavam. |
| B9 | `Deployment.IsValidVersionGuid` exige `^version-[0-9a-f]{16}$`, e `Deployment.VersionFolder` só devolve pastas dentro de `Versions`. |
| B7 | `Clip` nunca corta um emoji ao meio. |
| B8 | `FastFlagValidator.ValidOnly` é usado ao importar um perfil e diz quantas entradas foram puladas. Importar ou renomear para um nome que já existe agora pergunta antes de substituir. |
| B10 | `Store.Load` remove as flags com valor `null`, inclusive dentro dos perfis. |

Ainda não testado com o Roblox de verdade: os links de compartilhamento (`roblox://navigation/share_links`) e
de servidor privado (`linkCode`) abrindo o servidor certo.

**Como rodar os testes:** `dotnet run --project bug-hunt`. O código de saída é o número de bugs
reproduzidos. Cada teste descreve o comportamento **certo**, então falha enquanto o bug existir e
vira teste de regressão quando for corrigido. Todos os testes rodam numa pasta temporária
(`BLOXCODE_LAYOUT=xdg`, `HOME` próprio) e nunca tocam `%LocalAppData%\BloxCode`.

Cada bug abaixo foi reproduzido **duas vezes**. O B3 também foi conferido no app de verdade (o Blox
Code nem abre).

| # | Bug | Gravidade |
| --- | --- | --- |
| B3 | Histórico com entrada vazia ou jogo sem nome impede o Blox Code de abrir | Alto |
| B2 | Histórico ilegível é apagado na próxima partida, sem cópia | Médio |
| B1 | Um número grande demais no log do Roblox desliga o acompanhamento daquela janela | Médio |
| B4 | Link de servidor privado entra num servidor público | Médio |
| B5 | A barra recusa o próprio link "Join server" que o Blox Code gera | Médio |
| B9 | O nome da versão do Roblox não é validado antes de virar pasta apagada | Médio (segurança, difícil de explorar) |
| B7 | Nome de jogo longo com emoji vira "�" no Discord | Baixo |
| B8 | Importar perfil de Fast Flags não valida as flags | Baixo |
| B6 | Links de compartilhamento (`roblox.com/share?...`) não são aceitos | Baixo |
| B10 | Valor `null` de flag no Settings.json chega ao app | Baixo |

---

## B3 — Histórico com entrada vazia ou jogo sem nome impede o Blox Code de abrir

- **Gravidade:** Alto (o app não abre e o usuário não tem como resolver sozinho)
- **Onde:**
  - `shared/GameHistory.cs:97` (`Recent`) e `:105` (`Stats`);
  - `windows/UI/Pages/HomePage.xaml.cs:150` (`PlainName(game.Name)`), que roda no construtor da Home.
- **Como reproduzir:**
  1. Ponha em `History.json` (fica em `%LocalAppData%\BloxCode`) o conteúdo
     `[null, {"PlaceId": 5, "Name": "x", "JoinedAt": "2026-11-01T10:00:00Z"}]`, ou um jogo com
     `"Name": null`.
  2. Abra o Blox Code.
- **Esperado:** abrir normalmente, ignorando a entrada ruim.
- **Aconteceu:** "Blox Code couldn't start — Object reference not set to an instance of an object",
  e o app fecha. Isso se repete toda vez até alguém apagar o arquivo à mão.
- **Causa provável:**
  - `Load()` aceita `null` dentro da lista e `Name` nulo.
  - `Recent` faz `g.PlaceId` em `null`, e `PlainName(null)` quebra.
  - O erro acontece dentro do `new MainWindow()`, então derruba a abertura inteira.
  - O próprio Blox Code não grava esse formato, então só acontece com um arquivo danificado ou
    editado à mão. Mesmo assim, um arquivo ruim não devia travar o app.
- **Sugestão:**
  - No `Load`, filtrar entradas nulas e trocar `Name` nulo por `""`.
  - Na Home, proteger o `RefreshRecent` com try/catch: sem recentes é melhor do que sem app.
- **Teste:** `B3` e `B3b` em `bug-hunt/Program.cs`.

## B2 — Histórico ilegível é apagado na próxima partida, sem cópia

- **Gravidade:** Médio (perda de dados, mas só do histórico de jogos)
- **Onde:** `shared/GameHistory.cs:44-56` (`Load` engole qualquer erro e devolve lista vazia) e
  `:158` (`Update` grava em cima a partir dessa lista vazia).
- **Como reproduzir:**
  1. Deixe o `History.json` com JSON inválido (cortado no meio, por exemplo).
  2. Jogue uma partida, ou use "Remove from recent" em qualquer jogo.
- **Esperado:** manter o arquivo, ou guardar uma cópia (`History.json.corrupt`), como o
  `Settings.json` já faz no `Store.Read`.
- **Aconteceu:** o arquivo vira `[]` e todo o histórico some.
- **Causa provável:**
  - `Load` não diferencia "não existe" de "não consegui ler".
  - O mesmo vale para um erro passageiro de leitura: o arquivo preso pelo antivírus por um
    instante dá `IOException`, e o histórico inteiro é sobrescrito só com a partida nova.
- **Sugestão:**
  - Em `Update`, se a leitura falhar, copiar o arquivo para `History.json.corrupt` antes de gravar.
  - Para `IOException`, tentar de novo em vez de seguir com a lista vazia.
- **Teste:** `B2`.

## B1 — Um número grande demais no log do Roblox desliga o acompanhamento daquela janela

- **Gravidade:** Médio
- **Onde:**
  - `shared/RobloxActivity.cs:57`, `:76` e `:78` (`long.Parse`);
  - `windows/Core/ActivityWatcher.cs:139` (a tarefa do leitor fica com erro e ninguém olha).
- **Como reproduzir:** passe ao `ActivityParser` a linha
  `[FLog::Output] ! Joining game '<job>' place 99999999999999999999 at 1.2.3.4`, ou um
  `universeid:` do mesmo tamanho.
- **Esperado:** ignorar a linha e seguir lendo.
- **Aconteceu:**
  - `OverflowException`. O `FollowAsync` termina com erro, e a janela continua registrada como
    "sendo lida", então nunca é lida de novo.
  - Daí em diante não há Discord, aviso de servidor nem histórico para aquela janela, e nenhum erro
    aparece no log.
- **Causa provável:** `long.Parse` em vez de `long.TryParse`, e nenhum `try` em volta do `Feed`.
- **Sugestão:**
  - Usar `TryParse` e ignorar a linha.
  - No watcher, envolver `parser.Feed` em try/catch com `Logger.Warn`, para uma linha estranha
    nunca parar o leitor.
- **Teste:** `B1`, `B1b` e `B1c` (o B1c mostra que o jogo seguinte, 555, deixa de ser lido).

## B4 — Link de servidor privado entra num servidor público

- **Gravidade:** Médio (função principal: entrar no jogo certo)
- **Onde:** `shared/RobloxLink.cs:31-33`
- **Como reproduzir:** cole na barra da Home
  `https://www.roblox.com/games/920587237/Adopt-Me?privateServerLinkCode=48211234567890` e aperte
  Enter.
- **Esperado:** abrir o servidor privado.
- **Aconteceu:** vira `roblox://experiences/start?placeId=920587237` e o código some. O jogador cai
  num servidor público, sem aviso.
- **Causa provável:** a regex só extrai o número do lugar.
- **Sugestão:** quando houver `privateServerLinkCode=...`, acrescentar `&linkCode=...` ao link
  `roblox://experiences/start`.
- **Teste:** `B4`.

## B5 — A barra recusa o próprio link "Join server" que o Blox Code gera

- **Gravidade:** Médio
- **Onde:** `shared/RobloxLink.cs:31` (a regex `games/(\d+)` não pega `games/start?placeId=`).
- **Como reproduzir:** cole na barra o link do botão "Join server" do Discord, por exemplo
  `https://www.roblox.com/games/start?placeId=920587237&gameInstanceId=<job>`.
- **Esperado:** entrar naquele servidor.
- **Aconteceu:** "That doesn't look like a Roblox game link". O formato que o próprio app gera em
  `RobloxGames.JoinLink` não é aceito.
- **Sugestão:** reconhecer `games/start?placeId=N` e levar junto o `gameInstanceId` (fica
  `roblox://experiences/start?placeId=N&gameInstanceId=...`).
- **Teste:** `B5`.

## B9 — O nome da versão do Roblox não é validado antes de virar pasta apagada

- **Gravidade:** Médio. É de segurança, mas para explorar é preciso que o servidor de versões do
  Roblox (HTTPS) responda algo malicioso.
- **Onde:**
  - `windows/Core/Deployment.cs:136`: a única checagem é `StartsWith("version-")`.
  - `windows/Core/Bootstrapper.cs:65`: `Path.Combine(Paths.Versions, info.VersionGuid)`.
  - `windows/Core/Bootstrapper.cs:195`: `Directory.Delete(versionDir, recursive: true)`.
- **Como reproduzir (no teste):** um `VersionGuid` como `version-x\..\..\..\..\Documents` passa na
  checagem e aponta para fora da pasta `Versions`.
- **Esperado:** recusar qualquer nome que não seja `version-` seguido de 16 dígitos hexadecimais.
- **Aconteceu:** o caminho resultante sai de `Versions`. Numa versão "nova" o Bootstrapper apaga
  essa pasta com tudo dentro e recria.
- **Sugestão:** validar com a `PinnedVersionRegex` (`^version-[0-9a-f]{16}$`), que já existe, no
  `Deployment.GetInfo`. Também conferir com `GetFullPath` que `versionDir` fica dentro de `Versions`
  antes do `Delete`.
- **Teste:** `B9`.

## B7 — Nome de jogo longo com emoji vira "�" no Discord

- **Gravidade:** Baixo (visual)
- **Onde:** `windows/Core/ActivityWatcher.cs:312` (`Clip` corta por unidade UTF-16).
- **Como reproduzir:** gere a atividade de um jogo cujo nome tem mais de 127 caracteres, com um
  emoji bem no ponto do corte.
- **Esperado:** cortar antes do emoji.
- **Aconteceu:** o texto termina com meio emoji. Ao virar JSON, aparece como "�…" no perfil do
  Discord.
- **Sugestão:** se o último caractere mantido for `char.IsHighSurrogate`, cortar um a menos (ou
  cortar por `StringInfo`).
- **Teste:** `B7`.

## B8 — Importar perfil de Fast Flags não valida as flags

- **Gravidade:** Baixo
- **Onde:** `windows/UI/Pages/FastFlagsPage.xaml.cs:141-145`
- **Como reproduzir:** em Flags, use "Import file…" num JSON com
  `{"Isso não é flag": 1, "DFIntX": {"a": 1}}`.
- **Esperado:** pular os nomes inválidos, como o editor de flags já faz
  (`AddFlagDialog.xaml.cs:122`).
- **Aconteceu:** as duas entradas entram no perfil. Ao usar o perfil, vão para o
  `ClientAppSettings.json` e para o `config.json` do Sober.
- **Sugestão:** filtrar com `ValidateName` e `ValidateValue` e dizer quantas foram puladas.
- **Teste:** `B8`.
- **Mesmo lugar, por leitura do código:**
  - "Rename…" para um nome que já existe sobrescreve o outro perfil sem perguntar (linha 71).
  - "Import" também sobrescreve um perfil de mesmo nome sem perguntar.
  - "Save current as…" pergunta antes.

## B6 — Links de compartilhamento do Roblox não são aceitos

- **Gravidade:** Baixo (falta de suporte)
- **Onde:** `shared/RobloxLink.cs`
- **Como reproduzir:** cole `https://www.roblox.com/share?code=...&type=Server`.
- **Esperado:** abrir o servidor, como o site do Roblox faz.
- **Aconteceu:** "That doesn't look like a Roblox game link".
- **Sugestão:** repassar ao Roblox como `roblox://navigation/share_links?code=...&type=...`.
  Ainda precisa testar com um link de verdade.
- **Teste:** `B6`.

## B10 — Valor `null` de flag no Settings.json chega ao app

- **Gravidade:** Baixo (só com o arquivo editado à mão)
- **Onde:** `shared/Settings.cs:96`. Ele troca o dicionário nulo, mas não os valores nulos dentro.
- **Como reproduzir:** `{"FastFlags": {"FFlagX": null}}` no Settings.json.
- **Esperado:** descartar ou corrigir o valor.
- **Aconteceu:**
  - O `null` chega ao app.
  - No Linux, `SoberConfig.ToJson` faz `value.Equals(...)` em `null`, o que quebra cada
    sincronização com o Sober. Essa parte foi vista no código, não executada.
- **Sugestão:** no `Store.Load`, remover as flags com valor nulo.
- **Teste:** `B10`.

---

## Suspeitas não confirmadas

- **Um jogo pode fingir entradas e saídas no log:**
  - O `ActivityParser` aceita as linhas "Joining game" e "Time to disconnect" em qualquer posição
    do log, e isso foi confirmado.
  - Se o `print()` dos scripts de um jogo também cai no log como `[FLog::Output]`, como o
    BloxstrapRPC usa, um jogo poderia mudar o status do Discord, sujar o histórico e, junto com o
    B1, desligar o acompanhamento.
  - Não deu para confirmar: os logs deste PC não têm mais sessões de jogo.
  - **Como confirmar:** num jogo seu do Studio, dê `print("! Joining game '...' place 1 at 1.1.1.1")`
    e veja o log.
- **"Free up space" com uma janela do Roblox aberta de uma versão antiga:**
  - O `Directory.Delete` vai apagando arquivo por arquivo e para no primeiro que está preso.
  - A janela aberta pode perder texturas e sons no meio do jogo.
  - Não testado com o Roblox aberto.
- **Histórico com o mutex ocupado:**
  - `GameHistory.Update` segue sem a trava se não a conseguir em 5 s.
  - Dois processos gravando ao mesmo tempo podem perder uma entrada.
  - Não reproduzido.

## O que foi testado e aguentou

- **Atualizações:** um manifesto com assinatura falsa ou corrompida é recusado (`X1`). Pela leitura
  do código, o download confere o SHA-256 e o tamanho, só aceita HTTPS e não volta para versões
  antigas.
- **Links `roblox://` vindos do navegador:**
  - O registro usa `-player "%1"` entre aspas.
  - Um link com `-uninstall -quiet` dentro continua sendo um argumento só e não muda o modo (`X2`).
  - O link vai ao Roblox como um único argumento (`ArgumentList`).
- **Argumentos estranhos** (vazios, `--`, `/`, 100 mil caracteres, `-channel` sem valor) não quebram
  nada (`X2`).
- **Barra da Home** com entradas extremas (vazio, `\0`, 100 mil dígitos, emoji, RTL, `NaN`, `-1`,
  `1e9`): nenhuma exceção (`X3`).
- **JSON de flags** malformado, com 100 mil níveis de `[` ou um número de 1 MB: dá um erro tratado,
  sem travar (`X4`).
- **Nomes e durações** extremos (`PlainName`, `Duration` negativa, data no futuro): ok (`X5`).
- **Zip dos pacotes do Roblox:** entradas com `../` são recusadas (zip slip), pela leitura do código.
- **Versão fixada** (`^version-[0-9a-f]{16}$`) e **canal** (`^[a-z0-9_-]{1,64}$`) são validados.
- **Settings.json** com JSON inválido: guarda uma cópia `.corrupt` e abre com os padrões.
- **Autoteste existente** (`--self-test`, 22 verificações): todas passam.
- **Builds** do Windows e do Linux: 0 avisos e 0 erros.
