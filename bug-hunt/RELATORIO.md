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
