# Bloxstrap → Blox Code: notas de engenharia reversa

Alvo analisado: `%LocalAppData%\Bloxstrap\Bloxstrap.exe` (12 589 224 bytes,
SHA-256 `9ad77580…46b2`). Ferramenta: REA MCP (`inspect_managed_artifact`,
`inspect_managed_members`, `inspect_analysis_view`). Nada do alvo foi executado.

## 1. Identificação do binário

| Observação | Evidência |
| --- | --- |
| O `.exe` é um apphost PE x64 **sem diretório CLI** – não é um assembly gerenciado direto. | `ev_8bb0dab3…` |
| É um *.NET single-file bundle* v6.0 com 49 arquivos (extraídos com `tools/extract-bundle.js`). O assembly principal é `Bloxstrap.dll`. | saída do extrator |
| `Bloxstrap.dll`: assembly **Bloxstrap 2.11.4.0**, `.NETCoreApp v6.0`, IL-only, 337 tipos, 2 626 métodos, MVID `5e97addc-2e46-44d2-8f93-cd1db1e367c4`. | `ev_4b0cca4c…` / `ev_316cc6cb…` |
| Dependências embutidas: Wpf.Ui, CommunityToolkit.Mvvm, DiscordRPC, Markdig, Newtonsoft.Json, SharpZipLib, AvalonEdit, XamlAnimatedGif, securifybv.ShellLink. | lista do bundle |

## 2. Fluxo de inicialização do Roblox (o "bootstrapper")

Tipos-chave inspecionados (`ev_e8a9f740…`): `Bootstrapper`, `RobloxInterfaces.Deployment`,
`LaunchHandler`, `LaunchSettings`, `Installer`, `Paths`, `FastFlagManager`,
`AppData.RobloxPlayerData`, `Models.Manifest.*`.

1. **Escolha de espelho (mirror)** – `Deployment.TestConnection` / `InitializeConnectivity`.
   Literais observados: `https://setup.rbxcdn.com`, `https://setup-aws.rbxcdn.com`,
   `https://setup-ak.rbxcdn.com`, `https://roblox-setup.cachefly.net`,
   `https://s3.amazonaws.com/setup.roblox.com`, `/versionStudio` e o valor esperado
   `version-012732894899482c` (mensagem: *"versionStudio response does not match"*).
2. **Versão mais recente** – `Deployment.GetInfo`: `https://clientsettingscdn.roblox.com`
   + `/v2/client-version/` + `WindowsPlayer` + `/channel/` + canal; em falha,
   *"Falling back to clientsettings..."* → `https://clientsettings.roblox.com`.
   Modelo `ClientVersion`: `Version`, `VersionGuid`, `BootstrapperVersion`, `Timestamp`.
   *Verificado em rede (2026-10-24):* para `production` o segmento `/channel/…` deve ser
   omitido — `/channel/production` responde 401 igual a um canal inexistente; `get_IsDefaultChannel`
   (compara com `"production"`) é o que decide isso no Bloxstrap.
3. **Canal** – regex `channel:([a-zA-Z0-9-_]+)` sobre a URI de launch, depois registro
   `SOFTWARE\ROBLOX Corporation\Environments\…\Channel`, padrão `production`.
4. **URL dos pacotes** – `Deployment.GetLocation` tem 6 instruções IL, sem desvio:
   `get_BaseUrl` + `ldstr "/channel/common"` + recurso. *Inferência:* todo download usa
   `{mirror}/channel/common/{versionGuid}-{arquivo}`.
5. **Manifesto** – `{version}-rbxPkgManifest.txt`; primeira linha precisa ser `v0`
   (*"Unexpected package manifest version: … (expected v0!)"*). Modelo `Package`:
   `Name`, `Signature` (MD5), `PackedSize`, `Size`, `DownloadPath`.
6. **Cache** – `Downloads\{md5}` (confirmado: o diretório Downloads do Bloxstrap contém
   arquivos nomeados pelo MD5 e `PlayerState.json` → `PackageHashes`). Verificação via
   `Utility.MD5Hash`.
7. **Mapa pacote → pasta** (literais de `RobloxPlayerData`/`CommonAppData`):
   `RobloxApp.zip`→raiz, `shaders.zip`→`shaders\`, `ssl.zip`→`ssl\`,
   `WebView2RuntimeInstaller.zip`→`WebView2RuntimeInstaller\`, `content-*.zip`→`content\…`,
   `content-textures3/terrain/platform-fonts/platform-dictionaries` → `PlatformContent\pc\…`,
   `extracontent-*.zip` → `ExtraContent\…`. Pacotes fora do mapa: *"was not found in the
   package map!"*.
8. **Pós-extração** – grava `AppSettings.xml` (`<ContentFolder>content</ContentFolder>`,
   `<BaseUrl>http://www.roblox.com</BaseUrl>`), instala WebView2 se necessário
   (`MicrosoftEdgeWebview2Setup.exe`), aplica `Modifications\` com `ModManifest`,
   escreve FastFlags em `ClientSettings\ClientAppSettings.json`, limpa `Versions\` antigos.
9. **Launch** – `Bootstrapper.StartRoblox` inicia `RobloxPlayerBeta.exe` com os
   argumentos da URI (*"Got Roblox launch arguments"*) e espera o log em `logs\*.log`.

## 3. Integração com o Windows

* Protocolos: `Software\Classes\roblox` e `roblox-player`, com `URL Protocol`,
  `DefaultIcon` e `shell\open\command` = `"exe" -player "%1"`.
* Desinstalação: `Software\Microsoft\Windows\CurrentVersion\Uninstall\Bloxstrap`
  (`DisplayName`, `DisplayIcon`, `DisplayVersion`, `InstallDate`, `UninstallString`
  `"exe" -uninstall`, `QuietUninstallString`, …); remoção da pasta via
  `cmd.exe /c timeout 5 && del … && rmdir …`.
* Argumentos: `-player`, `-studio`, `-settings`/`-menu`/`-preferences`, `-uninstall`,
  `-quiet`, `-nolaunch`, `-watcher`, `-backgroundupdater`, `-channel`, `-version`, `-force`.
* Mutex de instância: `Bloxstrap-Bootstrapper`; o Roblox usa `ROBLOX_singletonMutex`.
* Tema do sistema: `SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize`.

## 4. Presets de FastFlags observados

| Preset | Flag |
| --- | --- |
| `Rendering.ManualFullscreen` | `FFlagHandleAltEnterFullscreenManually` |
| `Rendering.DisableScaling` | `DFFlagDisableDPIScale` |
| `Rendering.MSAA` | `FIntDebugForceMSAASamples` |
| `Rendering.TextureQuality.*` | `DFFlagTextureQualityOverrideEnabled`, `DFIntTextureQualityOverride` |

### Como o `FastFlagManager` funciona

Lido com REA no `Bloxstrap.dll` extraído (SHA-256 `5509838e…5b34`; evidência
`ev_44559692…31d3`, tipo `Bloxstrap.FastFlagManager`, 17 métodos, 30 literais):

* **`PresetFlags`** (`.cctor`): dicionário estático *nome do preset → flag*, com só as 5 entradas
  da tabela acima. Na 2.11.4 os presets antigos (FPS, iluminação, modo de renderização…) já
  tinham saído, por causa da allowlist do Roblox.
* **`SetValue(key, value)`**: `null` remove a flag e loga *"Deletion of 'X' is pending"*; se a flag
  já existe com outro valor, loga *"Changing of 'X' from 'a' to 'b' is pending"*; senão, *"Setting
  of 'X' to 'v' is pending"*. O valor é guardado como `value.ToString()`.
* **`SetPreset(prefix, value)`**: chama `SetValue` para todo preset cujo nome começa com o prefixo.
* **`SetPresetEnum(prefix, target, value)`**: para os presets do prefixo, `prefix + "." + target`
  recebe o valor e os outros recebem `null`, então só uma opção fica ligada.
* **`GetPreset(name)`**: nome desconhecido loga *"Could not find preset"* e retorna `null`.
* **`GetPresetEnum<T>(mapping, prefix, value)`**: a primeira opção (exceto `"None"`) cujo preset tem
  o valor; se nenhuma tiver, a primeira do mapa.
* **`MSAAModes`** = Default→`null`, 1x→`1`, 2x→`2`, 4x→`4`. **`TextureQualityLevels`** =
  Default→`null`, níveis 0–3.
* **`Load`**: depois de ler o arquivo, força `Rendering.ManualFullscreen` = `False` (fullscreen
  exclusivo ligado por padrão). O Blox Code **não** copia esse padrão: deixa a escolha com o usuário.

O Blox Code reimplementa esse modelo em `shared/FastFlagPresets.cs` e acrescenta presets para as
outras flags da
[allowlist](https://devforum.roblox.com/t/allowlist-for-local-client-configuration-via-fast-flags/3966569)
(qualidade FRM, grama, LOD de CSG e `FFlagDebugGraphicsPrefer*`), que alimentam o preset gráfico
Lowest/Low/Medium/High.

### Presets de mods (`ModPresetTask`)

Lido com REA no mesmo `Bloxstrap.dll` (`inspect_managed_artifact` → recursos;
`inspect_managed_members` nos tipos `ModsViewModel`, `ModPresetTask`, `EmojiModPresetTask`,
`FontModPresetTask`, `EmojiTypeEx` e nas bases `BoolBaseTask`/`EnumBaseTask<T>`):

| Observado | Evidência |
| --- | --- |
| Cada preset é um mapa "caminho dentro de `Modifications` → recurso embutido" (`_pathMap`), com um `ModPresetFileData` por arquivo que compara o hash (`HashMatches`). | campos `_pathMap`/`_fileDataMap`, chamadas `HashMatches`, `get_FullFilePath`, `get_ResourceStream` |
| O estado ligado/desligado não fica salvo: o construtor lê a pasta e só considera ligado se **todos** os arquivos batem com o recurso. | `ModPresetTask..ctor` chama `HashMatches` e `set_OriginalState` |
| Ligar grava o recurso no caminho (cria a pasta); desligar apaga **só** os arquivos que ainda batem com o recurso. | `Execute`: `CreateDirectory`, `WriteAllBytes`, `HashMatches` antes de `File.Delete` |
| Sons antigos: `action_footsteps_plastic.mp3`→`OldWalk`, `action_jump.mp3`→`OldJump`, `action_get_up.mp3`→`OldGetUp`; `action_falling`, `action_jump_land`, `action_swim` e `impact_water` → `Empty.mp3`. | literais `ldstr` em `ModsViewModel` |
| Cursor 2006/2013: `ArrowCursor.png` e `ArrowFarCursor.png` em `content\textures\Cursors\KeyboardMouse`. | enum `CursorType` + literais |
| Fundo antigo do editor de avatar: `ExtraContent\places\Mobile.rbxl` → `OldAvatarBackground.rbxl`. | literais |
| Emoji: `content\fonts\TwemojiMozilla.ttf`, baixado de `bloxstraplabs/rbxcustom-fontemojis` (Catmoji, Windows 11/10/8), estado pelo MD5 do arquivo. | `EmojiTypeEx.get_Filenames/get_Hashes`, URL base |
| Os 9 arquivos embutidos (`Bloxstrap.Resources.Mods.*`) foram extraídos dos recursos do assembly e conferidos pelo SHA-256 que o REA reportou. | `managed` → `resources` |

O Blox Code reimplementa isso em `shared/ModPresets.cs`, com os arquivos do Bloxstrap (MIT) em
`shared/ModPresets`. Diferenças: o falling usa `action_falling.ogg`, o nome atual no Roblox (o
Bloxstrap 2.11.4 ainda aponta para `.mp3`); as fontes de emoji são conferidas por SHA-256; o fundo
do avatar só aparece no Windows (o Sober roda o cliente Android).

## 5. O que o Blox Code reimplementa / não reimplementa

Reimplementado do zero (sem copiar código do Bloxstrap): itens 1–9 da seção 2, integração
com o Windows (seção 3, sem Studio), editor de FastFlags + presets, mods com backup/restauração.

Fora do escopo desta versão: Roblox Studio, Discord Rich Presence/activity watcher,
background updater, temas customizados XML, auto-update do próprio bootstrapper.

## 6. MultiBloxy 1.1.0: várias janelas do Roblox

Binário analisado com o REA (`inspect_managed_artifact` e `inspect_managed_members`, só leitura,
sem executar): `MultiBloxy.exe`, .NET Framework 4.8, x86 (prefer-32-bit), SHA-256
`168eedac1f655cd39f33fd8cfa5c971c46b08dda912b78fe330797eade7912c3`, 13 tipos e 61 métodos.

* **`Program.OpenMutex`** cria um `Mutex` chamado `ROBLOX_singletonEvent`. O Roblox usa esse
  nome para um *evento*: cada player novo cria ou abre o evento e o mais antigo fecha. Como o nome já
  existe com outro tipo, a criação do evento falha e nenhum player encontra o outro.
* Se o Roblox já estava aberto, o nome pertence ao evento dele e o `Mutex` não pode ser criado
  (texto: "when {0} was launched, Roblox was already running and had registered its handle").
  A opção **Fix** chama **`HandleCloser.CloseAllHandles`**: `NtQuerySystemInformation` lista os
  handles do sistema, os de `RobloxPlayerBeta` são duplicados para o próprio processo
  (`OpenProcess` + `DuplicateHandle`), `NtQueryObject` dá o nome, e o que contém o nome do
  evento é fechado dentro do Roblox com `DuplicateHandle` + `DUPLICATE_CLOSE_SOURCE`.
* **`StopRobloxInstances`** é só `Process.GetProcessesByName("RobloxPlayerBeta")` + `Kill`.
* O resto é a bandeja (pausar, recarregar, idioma, `config.xml`).

O Blox Code (`windows/Core/MultiInstance.cs`) usa o mesmo mecanismo, reescrito:

* Sem app na bandeja: o lançamento que abre o Roblox pega o nome antes de iniciar o player, e o
  processo que acompanha o Roblox segura o nome até o último player fechar.
* A tabela de handles vem de `SystemExtendedHandleInformation` (64), com PID de 64 bits; a tabela
  antiga guarda o PID em 16 bits e erra com PIDs acima de 65535.
* Só se pergunta o nome a handles do tipo `Event` (o tipo vem antes, por `ObjectTypeInformation`),
  então o `NtQueryObject` nunca é chamado num pipe, que pode travar.
* Testado com um processo falso chamado `RobloxPlayerBeta` que segura o evento: o handle foi fechado
  em ~0,5 s, o nome virou `Mutex` e um segundo processo não conseguiu mais criar o evento.

## Desconhecidos

* MultiBloxy: não foi testado contra o Roblox de verdade se o anticheat dele permite abrir o processo
  com `PROCESS_DUP_HANDLE`. Se não permitir, o Blox Code registra a falha e o lançamento só
  substitui a janela aberta; com o Roblox fechado antes do primeiro lançamento isso não é preciso.

* A lógica exata de `InitializeConnectivity` (ordem/paralelismo dos testes) está num state
  machine assíncrono; o Blox Code testa os espelhos em paralelo e usa o primeiro válido.
* Não foi observado em execução nenhum comportamento — tudo acima é evidência estática.
