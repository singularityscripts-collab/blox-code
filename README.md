<p align="center"><img src="docs/icon.png" width="96" alt="Bit, o mascote do Blox Code"></p>

# Blox Code

Um launcher de Roblox com o layout do **Claude desktop**, um tema próprio e estrelando o **Bit**,
nosso mascote: um bloco de montar com um terminal no lugar do rosto. Presets gráficos, Fast Flags
com editor, mods e links `roblox://`, no **Windows** e no **Linux**.

## Baixar

| Sistema | Arquivo | Como o Roblox roda |
| --- | --- | --- |
| **Windows** 10/11 | [**BloxCode.exe**](https://github.com/singularityscripts-collab/blox-code/releases/latest/download/BloxCode.exe) | O Blox Code baixa e abre o Roblox para Windows |
| **Linux** — Arch, CachyOS, Manjaro, Ubuntu, Mint, Debian… | [**BloxCode-x86_64.AppImage**](https://github.com/singularityscripts-collab/blox-code/releases/latest/download/BloxCode-x86_64.AppImage) | Pelo [Sober](https://sober.vinegarhq.org/), que o Blox Code instala para você |

Os dois arquivos saem juntos em cada [release](https://github.com/singularityscripts-collab/blox-code/releases/latest) e têm o mesmo número de versão. Cada um se atualiza sozinho, sempre com o arquivo do próprio sistema.
O que mudou em cada versão está no [log de atualizações](CHANGELOG.md).

**Aviso do Windows na primeira vez:** o `BloxCode.exe` ainda não é assinado, então o SmartScreen
pode mostrar "O Windows protegeu o computador". Clique em **Mais informações → Executar mesmo
assim**. A Microsoft analisou o arquivo e confirmou que é só falta de reputação, não um problema. As
atualizações automáticas não mostram esse aviso. Veja a [política de assinatura](#code-signing-policy).

## Nos dois sistemas

- **Tela inicial** como a do Bloxstrap: ao abrir o Blox Code, escolha **Launch Roblox** (com o
  preset gráfico, os mods e as flags atuais) ou **Configure Blox Code**. `-menu` abre direto as
  configurações.
- Visual minimalista: **Midnight** (grafite com acento menta `#3EE6B0`) e **Daylight** (quase branco
  com menta `#077257`), superfícies chapadas com bordas finas, Inter no texto e JetBrains Mono na
  marca e no código (as duas vêm dentro do programa). Animações suaves, que dá para desligar.
  Cole um link de jogo ou um place ID na Home e aperte Enter.
- **Preset gráfico** Auto / Lowest / Low / Medium / High, feito só com flags da
  [allowlist do Roblox](https://devforum.roblox.com/t/allowlist-for-local-client-configuration-via-fast-flags/3966569),
  com uma sugestão baseada na sua placa de vídeo e interruptores extras para ganhar FPS.
- Tela de launch com status ao vivo (`▖ … (12s · esc to cancel)`) e o Bit andando em cima da barra
  de progresso, digitando código e pulando quando o Roblox abre.
- **Fast Flag Editor** no estilo do Bloxstrap: Add new (nome único ou Import JSON colado/de arquivo),
  Delete selected, Export JSON, busca, validação de prefixo/valor, chave liga/desliga para booleanas
  e aviso nas flags que o Roblox ignora por estarem fora da allowlist.
- **Mods** sem fuçar pasta: os presets do Bloxstrap com um clique (cursor de 2006/2013, sons
  antigos do personagem, fundo antigo do editor de avatar e estilo de emoji) ou um arquivo seu
  (escolhido ou arrastado) para o cursor, os sons ou a fonte do jogo. **Reset** traz o original de
  volta. Para o resto, a pasta `Modifications`. Os arquivos dos presets são do
  [Bloxstrap](https://github.com/bloxstraplabs/bloxstrap) (MIT, licença em
  [shared/ModPresets](shared/ModPresets)).
- Abre os links `roblox://` do navegador, instala sem pedir senha/admin e se atualiza sozinho
  por um feed assinado que funciona em qualquer hospedagem.
- Tema Sistema / Claro / Escuro.

## Windows

Baixe o `BloxCode.exe`, abra e clique em **Install**. Ele vai para `%LocalAppData%\BloxCode`, ganha
atalhos e uma entrada em Aplicativos, e passa a abrir os links do Roblox.

- Escolhe o espelho de CDN mais rápido, consulta a versão do canal e baixa os pacotes do
  `rbxPkgManifest`, com cache por MD5 (reaproveita o cache do Bloxstrap quando o hash bate).
- Extrai no mesmo layout do Bloxstrap em `%LocalAppData%\BloxCode\Versions\<versão>`, instala o
  WebView2 se faltar e limpa versões antigas.
- Presets de Fast Flags: fullscreen exclusivo, DPI, Rendering API (Direct3D 11 / Vulkan / OpenGL),
  MSAA, texturas. As flags vão para o `ClientAppSettings.json` do Roblox.
- Mods: tudo em `Modifications\` é copiado por cima do Roblox; os originais ficam guardados.

O fluxo de atualização e launch foi obtido por engenharia reversa do Bloxstrap 2.11.4 com o REA MCP e
reescrito do zero em C#/WPF — veja [docs/REVERSE_ENGINEERING.md](docs/REVERSE_ENGINEERING.md).

## Linux

Distros baseadas no **Arch** (Arch, CachyOS, EndeavourOS, Manjaro) e no **Debian/Ubuntu** (Ubuntu
22.04+, Linux Mint 21+, Pop!_OS, Zorin, Debian 12+). O Roblox para Windows não roda mais no Wine,
então o Blox Code usa o [Sober](https://sober.vinegarhq.org/), que roda a versão Android do Roblox
nativamente e se atualiza sozinho.

1. Baixe o `BloxCode-x86_64.AppImage`, deixe executável e abra:

   ```bash
   chmod +x BloxCode-x86_64.AppImage && ./BloxCode-x86_64.AppImage
   ```

2. Na tela inicial, clique em **Install** (vai para o menu de aplicativos, em `~/.local/bin/bloxcode`,
   e passa a abrir os links `roblox://`) e em **Install Sober** se ele ainda não estiver instalado.

O que muda em relação ao Windows:

- Fast Flags vão para o `config.json` do Sober, como valores JSON de verdade, assim que você muda.
- A tela **Mods** lista os cursores, sons, fontes e texturas do pacote do Roblox para você escolher o
  que trocar; os arquivos vão para o `asset_overlay` do Sober.
- A tela **Sober** explica as opções do próprio Sober (gráficos, OpenGL/Vulkan, HiDPI, Discord…) e
  tem botões para restaurá-lo.
- Avisa quando o driver da NVIDIA não bate com a extensão GL do Flatpak, e exporta um zip de
  diagnóstico em Behaviour.

Se o AppImage não abrir, falta o FUSE: `sudo pacman -S --needed fuse3` (Arch) ou
`sudo apt install fuse3` (Debian/Ubuntu); ou rode com `--appimage-extract-and-run`. O Sober precisa
do Flatpak (`sudo pacman -S flatpak` ou `sudo apt install flatpak`); o Blox Code mostra o comando
certo se ele faltar e nunca roda `sudo` sozinho. Detalhes em [docs/LINUX_PLAN.md](docs/LINUX_PLAN.md);
roteiro de teste em [docs/LINUX_TESTING.md](docs/LINUX_TESTING.md).

## Atualização automática

Toda vez que a cópia instalada abre (menu ou link do Roblox), ela lê o `latest.json` do feed de
atualização (no máximo 4 s). Se houver versão nova, baixa o arquivo do seu sistema, confere o
SHA-256, reinicia com `-upgrade`, se substitui e continua o que você tinha pedido (abrir o jogo, o
menu…). Mesmo fluxo do Bloxstrap. Dá para desligar em Behaviour.

O feed não depende de nenhum site:

- Os endereços ficam em `<UpdateFeeds>` no [Directory.Build.props](Directory.Build.props), separados
  por `;`, e são tentados na ordem. Qualquer hospedagem de arquivos estáticos serve.
- O `latest.json` só vale se a assinatura em `latest.json.sig` (ECDSA P-256) bater com a chave
  pública em `<UpdatePublicKey>`. O arquivo baixado só roda se o SHA-256 bater com o do manifesto.
  Quem invadir uma hospedagem não consegue publicar um programa falso.
- Se um endereço de download falhar, o próximo da lista é tentado.

| | Windows | Linux |
| --- | --- | --- |
| Baixa | `BloxCode.exe` para `%TEMP%\BloxCode\Updates` | `BloxCode-x86_64.AppImage` para `~/.cache/bloxcode/updates` |
| Substitui | `%LocalAppData%\BloxCode\BloxCode.exe` | `~/.local/bin/bloxcode` |

## Desenvolvimento

Requer o .NET SDK 10.

```bash
dotnet run --project windows
```

```bash
dotnet publish windows/BloxCode.csproj -c Release -o dist
```

O publish gera um único `dist/BloxCode.exe` self-contained (não precisa de .NET instalado). O AppImage
do Linux é gerado no Linux (de preferência Ubuntu 22.04, a base mais antiga suportada):

```bash
bash linux/packaging/build-appimage.sh
```

Para mexer na versão Linux pelo Windows, rode o `BloxCode.Linux` com `BLOXCODE_LAYOUT=xdg` e um `HOME`
temporário (as pastas seguem o padrão do Linux dentro dele). `BloxCode --self-test` roda os testes
sem tela numa pasta temporária, em qualquer sistema.

Argumentos: `-player [uri]`, `-menu`/`-settings`, `-uninstall [-quiet]`, `-nolaunch`, `-force`,
`-channel <nome>` (só Windows), `-noupdate`.

Para publicar uma versão:

1. Aumente `<Version>` no [Directory.Build.props](Directory.Build.props) (é o mesmo número para os
   dois sistemas) e escreva a seção no [CHANGELOG.md](CHANGELOG.md).
2. Faça commit e envie a tag `vX.Y.Z` para o GitHub. O [GitHub Actions](.github/workflows/build.yml)
   compila o `.exe` e o `.AppImage`, testa o AppImage no Ubuntu, Debian e Arch e publica o release.
3. Baixe os dois arquivos do release e monte o feed assinado (a chave nunca sai do seu PC):

   ```bash
   node tools/release.js fetch-github --github singularityscripts-collab/blox-code
   node tools/release.js feed --github singularityscripts-collab/blox-code --exe dist/release/vX.Y.Z/BloxCode.exe --appimage dist/release/vX.Y.Z/BloxCode-x86_64.AppImage
   ```

4. Faça commit da pasta `updates/` e envie para o GitHub. É esse push que avisa as cópias instaladas.
   Confira com `node tools/release.js verify https://raw.githubusercontent.com/singularityscripts-collab/blox-code/main/updates/`.

Sem `--github`, a ferramenta monta uma pasta `dist/feed` para qualquer outra hospedagem estática (com
os arquivos ao lado do `latest.json`), que pode entrar como endereço extra em `<UpdateFeeds>`.

A chave de assinatura foi criada uma vez com `node tools/release.js keygen` e fica fora do repositório,
em `~/.bloxcode-release/update-signing-key.pem`. **Guarde uma cópia dela:** sem essa chave, as cópias
instaladas não aceitam mais nenhuma atualização.

```
windows/         Blox Code para Windows (WPF, BloxCode.csproj)
  Core/          Deployment, PackageManifest, Bootstrapper, Installer, ShellLink
  UI/            MainWindow (estilo Claude desktop), BootstrapperWindow, BitMascot, BlockSpinner
  UI/Pages/      Home, Fast Flags, Mods, Appearance, Behaviour, About
linux/           Blox Code para Linux (Avalonia, BloxCode.Linux.csproj)
  Runtime/       Sober, SoberConfig, ModSync, ApkAssets, SoberLauncher
  Platform/      DesktopIntegration (.desktop, ícones, xdg-mime), Distro, Diagnostics, Proc
  UI/            MainWindow, LaunchWindow, BitMascot, páginas (incluindo Sober)
  SelfTest.cs    `--self-test`
  packaging/     AppRun, bloxcode.desktop, build-appimage.sh (gera o AppImage)
shared/          usado pelos dois (BloxCode.Core.csproj): Settings, Logger, Paths, Fast Flags, Updater
docs/            engenharia reversa do Bloxstrap, plano e roteiro de teste do Linux
tools/
  extract-bundle.js  extrai um .NET single-file bundle (usado no Bloxstrap.exe)
  make-icon.js       gera o ícone do Bit (.ico do Windows e PNGs do Linux)
  release.js         chave de assinatura, feed de atualização assinado e conferência
updates/         latest.json + latest.json.sig: o feed que as cópias instaladas leem
```

Fora do escopo por enquanto: Roblox Studio, e Discord Rich Presence no Windows.

## Code signing policy

O `BloxCode.exe` ainda não tem assinatura de código da Microsoft (Authenticode), por isso o aviso do
SmartScreen na primeira vez. O pedido de certificado gratuito à SignPath Foundation foi negado por
falta de visibilidade do projeto; dá para pedir de novo quando o projeto crescer.

- **Atualizações:** são assinadas mesmo assim. O app só aceita um `latest.json` assinado com a chave
  do Blox Code (ECDSA P-256) e confere o SHA-256 de cada arquivo antes de instalar.
- **De onde vêm os arquivos:** só dos releases em
  [github.com/singularityscripts-collab/blox-code](https://github.com/singularityscripts-collab/blox-code),
  compilados a partir deste código.
- **Mantenedor e quem aprova cada release:** [singularityscripts-collab](https://github.com/singularityscripts-collab)
- **Mudanças no sistema:** o instalador avisa o que vai fazer antes de fazer (pasta do usuário em
  `%LocalAppData%BloxCode`, atalhos no menu Iniciar e na área de trabalho, links `roblox://` e uma
  entrada em Aplicativos). O Blox Code se desinstala por Configurações → Aplicativos ou pela página
  Behaviour.

## Privacy

Blox Code has no accounts, ads, analytics or telemetry, and sends no personal data anywhere. It only
connects to these services, to do what you ask it to:

- **The update feed** (the addresses in `<UpdateFeeds>`): to check for and download Blox Code
  updates. You can turn this off in Behaviour.
- **Roblox**, on Windows: `clientsettingscdn.roblox.com` / `clientsettings.roblox.com` to find the
  current Roblox version, and Roblox's download mirrors (`setup.rbxcdn.com`, `setup-aws.rbxcdn.com`,
  `setup-ak.rbxcdn.com`, `s3.amazonaws.com/setup.roblox.com`) to
  download the Roblox player.
- **Flathub** (`dl.flathub.org`), on Linux: only when you click Install Sober, through Flatpak.

These requests carry nothing about you beyond what any download does (your IP address and a
`BloxCode/<version>` user agent). Roblox and Sober, once started, are separate programs with their
own privacy policies. Logs and the Linux diagnostics zip stay on your computer unless you share them.

Projeto de fã — sem afiliação com Roblox Corporation, Anthropic, a equipe do Bloxstrap ou a
VinegarHQ (Sober) (MIT). As fontes [Inter](https://github.com/rsms/inter) e
[JetBrains Mono](https://github.com/JetBrains/JetBrainsMono) são distribuídas sob a SIL Open Font
License 1.1; as licenças estão em [shared/Fonts](shared/Fonts).
