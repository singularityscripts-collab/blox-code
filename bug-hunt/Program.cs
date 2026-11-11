// Caça-bugs do Blox Code. Each check asserts what SHOULD happen; a failure means the bug is there.
// Run: dotnet run --project bug-hunt   (exit code = number of bugs reproduced)
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using BloxCode.Core;

string sandbox = Path.Combine(Path.GetTempPath(), "bloxcode-bughunt-" + Guid.NewGuid().ToString("N")[..8]);
Directory.CreateDirectory(sandbox);
// Must happen before anything touches Paths: every file goes under the sandbox.
Environment.SetEnvironmentVariable("BLOXCODE_LAYOUT", "xdg");
Environment.SetEnvironmentVariable("HOME", sandbox);
foreach (var v in new[] { "XDG_DATA_HOME", "XDG_CONFIG_HOME", "XDG_STATE_HOME", "XDG_CACHE_HOME" })
    Environment.SetEnvironmentVariable(v, null);
Paths.EnsureCreated();
if (!Paths.Base.StartsWith(sandbox, StringComparison.OrdinalIgnoreCase))
    throw new Exception("Refusing to run: Paths.Base is not inside the sandbox: " + Paths.Base);

int bugs = 0, ok = 0;
void Check(string id, string name, Action body)
{
    try
    {
        body();
        ok++;
        Console.WriteLine($"ok    {id} {name}");
    }
    catch (Exception ex)
    {
        bugs++;
        Console.WriteLine($"BUG   {id} {name}\n      -> {ex.GetType().Name}: {ex.Message}");
    }
}
void Expect(bool condition, string what)
{
    if (!condition) throw new Exception(what);
}

const string Job = "0f5c3e2a-1b2c-4d5e-8f90-123456789abc";
string Line(string text) => $"2026-11-10T20:03:57.727Z,348.727173,1b98,6 {text}";

// ---------- B1: a place ID too big for a long kills the log follower ----------
Check("B1", "um número enorme no log não derruba o leitor do log", () =>
{
    var parser = new ActivityParser();
    parser.Feed(Line($"[FLog::Output] ! Joining game '{Job}' place 99999999999999999999 at 1.2.3.4"));
});
Check("B1b", "universeid enorme no log não derruba o leitor", () =>
{
    var parser = new ActivityParser();
    parser.Feed(Line($"[FLog::Output] ! Joining game '{Job}' place 123 at 1.2.3.4"));
    parser.Feed(Line("[FLog::GameJoinLoadTime] Report game_join_loadtime: placeid:123, join_time:1.2, universeid:99999999999999999999"));
});
Check("B1c", "o leitor do log continua depois de uma linha ruim (FollowAsync)", () =>
{
    string dir = Path.Combine(sandbox, "rlogs");
    Directory.CreateDirectory(dir);
    string log = Path.Combine(dir, "0.700_20261110T200000Z_Player_ABCD_last.log");
    File.WriteAllLines(log, new[]
    {
        Line($"[FLog::Output] ! Joining game '{Job}' place 99999999999999999999 at 1.2.3.4"),
        Line($"[FLog::Output] ! Joining game '{Job}' place 555 at 1.2.3.4"),
        Line("[FLog::Network] serverId: 1.2.3.4|53260"),
    });
    var parser = new ActivityParser();
    long joined = 0;
    parser.Joined += s => joined = s.PlaceId;
    var task = RobloxLog.FollowAsync(log, parser.Feed, () => false, DateTime.UtcNow, CancellationToken.None, dir, followNewer: false);
    try { task.Wait(TimeSpan.FromSeconds(5)); } catch (AggregateException) { }
    Expect(!task.IsFaulted, "FollowAsync terminou com erro: " + task.Exception?.InnerException?.Message);
    Expect(joined == 555, $"o jogo seguinte (555) devia ser lido; lido: {joined}");
});

// ---------- B2: History.json corrupt -> the next write wipes it with no backup ----------
Check("B2", "histórico corrompido não é apagado sem backup", () =>
{
    Directory.CreateDirectory(Paths.StateDir);
    string broken = "[{\"PlaceId\": 1, \"Name\": \"Meu jogo\", \"JoinedAt\": \"2026-11-01T10:00:00Z\"}, {\"PlaceId\": 2, ";   // cut short
    File.WriteAllText(GameHistory.FilePath, broken);
    GameHistory.Forget(999);   // any change: here, forgetting a game that isn't even there
    string after = File.ReadAllText(GameHistory.FilePath);
    bool backup = Directory.EnumerateFiles(Paths.StateDir, "History.json.*").Any(f => File.ReadAllText(f) == broken);
    Expect(after.Contains("Meu jogo") || backup, $"o arquivo virou '{after.Trim()}' e nenhuma cópia do original foi guardada");
});

Check("B2b", "histórico preso por outro programa (antivírus) não é sobrescrito", () =>
{
    string good = "[{\"PlaceId\": 7, \"Name\": \"Guardado\", \"JoinedAt\": \"2026-11-01T10:00:00Z\"}]";
    File.WriteAllText(GameHistory.FilePath, good);
    using (var held = new FileStream(GameHistory.FilePath, FileMode.Open, FileAccess.Read, FileShare.None))
        GameHistory.Joined(new GameSession(8, Job, "1.2.3.4", ServerKind.Public, null, DateTime.UtcNow), null);
    string after = File.ReadAllText(GameHistory.FilePath);
    Expect(after.Contains("Guardado"), $"o histórico virou '{after.Trim()}'");
});

// ---------- B3: a null entry in History.json crashes the recent games ----------
Check("B3", "entrada nula no histórico não quebra a lista de recentes", () =>
{
    File.WriteAllText(GameHistory.FilePath, "[null, {\"PlaceId\": 5, \"Name\": \"x\", \"JoinedAt\": \"2026-11-01T10:00:00Z\"}]");
    var recent = GameHistory.Recent(4);
    var stats = GameHistory.Stats(TimeSpan.FromDays(7));
    Expect(recent.Count == 1, "esperava 1 jogo");
});

Check("B3b", "jogo sem nome (\"Name\": null) no histórico não quebra os recentes", () =>
{
    File.WriteAllText(GameHistory.FilePath, "[{\"PlaceId\": 5, \"Name\": null, \"JoinedAt\": \"2026-11-01T10:00:00Z\"}]");
    foreach (var game in GameHistory.Recent(4))
        GameHistory.PlainName(game.Name);   // what HomePage.GameCard does for each card
});

// ---------- B4: links the composer should understand ----------
Check("B4", "link de servidor privado mantém o código do servidor", () =>
{
    string? args = RobloxLink.ToLaunchArgs("https://www.roblox.com/games/920587237/Adopt-Me?privateServerLinkCode=48211234567890");
    Expect(args is not null && args.Contains("48211234567890"), $"virou '{args}': o código do servidor privado sumiu, entra num servidor público");
});
Check("B5", "o próprio link 'Join server' do Blox Code é aceito na barra", () =>
{
    var session = new GameSession(920587237, Job, "1.2.3.4", ServerKind.Public, null, DateTime.UtcNow);
    string link = RobloxGames.JoinLink(session);
    string? args = RobloxLink.ToLaunchArgs(link);
    Expect(args is not null, $"'{link}' foi recusado (\"That doesn't look like a Roblox game link\")");
    Expect(args!.Contains(Job), $"virou '{args}': o servidor (gameInstanceId) sumiu");
});
Check("B6", "link de compartilhamento do Roblox (roblox.com/share) é aceito", () =>
{
    string? args = RobloxLink.ToLaunchArgs("https://www.roblox.com/share?code=6c0a1b2c3d4e5f&type=Server");
    Expect(args is not null, "recusado");
});

Check("X6", "links que já funcionavam continuam iguais", () =>
{
    var cases = new (string Input, string? Expected)[]
    {
        ("920587237", "roblox://experiences/start?placeId=920587237"),
        ("  https://www.roblox.com/games/920587237/Adopt-Me  ", "roblox://experiences/start?placeId=920587237"),
        ("roblox.com/pt-br/games/123/x", "roblox://experiences/start?placeId=123"),
        ("roblox://experiences/start?placeId=1", "roblox://experiences/start?placeId=1"),
        ("", ""),
        ("hello", null),
        ("https://www.roblox.com/games/start?placeId=abc", null),
        ("https://www.roblox.com/games/1/x?privateServerLinkCode=a%20b%22", "roblox://experiences/start?placeId=1"),
        ("https://www.roblox.com/games/1/x?privateServerLinkCode=%E0%A4%A", "roblox://experiences/start?placeId=1"),
        ("https://www.roblox.com/games/start?placeId=7&gameInstanceId=not-a-guid", "roblox://experiences/start?placeId=7"),
        ("https://www.roblox.com/share?code=abc", null),
    };
    foreach (var (input, expected) in cases)
    {
        string? got = RobloxLink.ToLaunchArgs(input);
        Expect(got == expected, $"'{input}' -> '{got}', esperado '{expected}'");
    }
});

// ---------- B7: Discord text cut in the middle of an emoji ----------
Check("B7", "nome de jogo cortado para o Discord não parte um emoji ao meio", () =>
{
    string name = new string('a', 126) + "😀😀";   // the cut at 127 lands between the two halves of an emoji
    var session = new GameSession(1, Job, "1.2.3.4", ServerKind.Public, null, DateTime.UtcNow);
    JsonObject activity = ActivityWatcher.BuildActivity(session, new GameInfo(1, name, "Criador", null));
    string details = activity["details"]!.GetValue<string>();
    bool lone = details.Where((c, i) => char.IsHighSurrogate(c) && (i + 1 >= details.Length || !char.IsLowSurrogate(details[i + 1]))).Any();
    Expect(!lone, "o texto enviado ao Discord termina com meio emoji (surrogate solto): " + JsonSerializer.Serialize(details[^3..]));
});

// ---------- B8: a profile imported from a file skips the flag checks ----------
Check("B8", "importar perfil recusa nomes de flag inválidos (como o editor faz)", () =>
{
    // What FastFlagsPage.OnImportProfile stores (before the fix: ParseJson's result as-is).
    var (flags, skipped) = FastFlagValidator.ValidOnly(FastFlagValidator.ParseJson("{\"Isso não é flag\": 1, \"FFlagOk\": true, \"DFIntX\": {\"a\": 1}, \"DFIntY\": \"60\"}"));
    var invalid = flags.Where(f => FastFlagValidator.ValidateName(f.Key) is not null || FastFlagValidator.ValidateValue(f.Key, f.Value) is not null).ToList();
    Expect(invalid.Count == 0, $"{invalid.Count} entradas inválidas entram no perfil: " + string.Join(", ", invalid.Select(f => $"{f.Key}={f.Value}")));
    Expect(flags.Count == 2 && skipped == 2 && flags["FFlagOk"] == "True" && flags["DFIntY"] == "60", $"esperava FFlagOk e DFIntY, 2 puladas; veio {flags.Count}/{skipped}");
});

// ---------- B9: the Roblox version name isn't checked before it becomes a folder that gets deleted ----------
Check("B9", "nome de versão do Roblox não sai da pasta Versions", () =>
{
    // Before the fix Deployment.GetInfo only checked StartsWith("version-") and the Bootstrapper did
    // Path.Combine(Versions, guid): this name pointed outside Versions, at a folder that got deleted.
    foreach (string guid in new[] { @"version-x\..\..\..\..\Documents", "version-../../x", "version-", "version-0123456789ABCDEF", "Version-0123456789abcdef", "version-0123456789abcdef0" })
    {
        Expect(!Deployment.IsValidVersionGuid(guid), $"'{guid}' foi aceito como versão");
        bool threw = false;
        try { Deployment.VersionFolder(guid); } catch (InvalidDataException) { threw = true; }
        Expect(threw, $"VersionFolder('{guid}') não recusou");
    }
    string ok = Deployment.VersionFolder("version-0123456789abcdef");
    Expect(ok == Path.Combine(Path.GetFullPath(Paths.Versions), "version-0123456789abcdef"), "uma versão válida devia ficar dentro de Versions: " + ok);
});

// ---------- B10: Settings.json with a null flag value ----------
Check("B10", "Settings.json com valor de flag null carrega sem erro e sem null", () =>
{
    Directory.CreateDirectory(Paths.Config);
    File.WriteAllText(Paths.SettingsFile, "{\"FastFlags\": {\"FFlagX\": null, \"DFIntTaskSchedulerTargetFps\": \"240\"}}");
    Store.Load();
    Expect(Store.Settings.FastFlags.Values.All(v => v is not null), "um valor null chegou ao app");
});

// ---------- extras: things that held up ----------
Check("X1", "manifesto de atualização sem assinatura válida é recusado", () =>
{
    bool okSig = Updater.VerifySignature("{}"u8.ToArray(), "AAAA"u8.ToArray(), Updater.PublicKey ?? "");
    Expect(!okSig, "aceitou");
});
Check("X2", "argumentos estranhos não quebram o LaunchSettings", () =>
{
    foreach (var args in new[] { new[] { "-player" }, new[] { "-channel" }, new[] { "", " ", "--", "/" }, new[] { "-player", "-uninstall" }, new[] { new string('a', 100_000) } })
        LaunchSettings.Parse(args);
    var s = LaunchSettings.Parse(new[] { "-player", "roblox-player:1+launchmode:play -uninstall -quiet" });
    Expect(s.Mode == LaunchMode.Player, "um link com -uninstall dentro mudou o modo");
});
Check("X3", "barra aceita entradas extremas sem erro", () =>
{
    foreach (string input in new[] { "", "   ", "\0\0", new string('9', 100_000), "😀", "‮123", "roblox.com/games/abc", "-1", "1e9", "NaN" })
        RobloxLink.ToLaunchArgs(input);
});
Check("X4", "JSON de flags malformado gera erro tratável, não trava", () =>
{
    foreach (string text in new[] { "", "{", "[1,2]", new string('[', 100_000), "{\"a\":" + new string('1', 1_000_000) + "}" })
    {
        try { FastFlagValidator.ParseJson(text); } catch (JsonException) { }
    }
});
Check("X5", "PlainName e Duration com entradas estranhas", () =>
{
    foreach (string name in new[] { "", "🎃", "[🎃]", "()", new string('x', 100_000), "‮ evil" })
        GameHistory.PlainName(name);
    GameHistory.Duration(TimeSpan.FromDays(-3));
    GameHistory.Ago(DateTime.UtcNow.AddYears(5));
});

// ================= Funções novas =================

Check("F1", "importar do Bloxstrap/Fishstrap traz flags, mods e preferências sem mexer na origem", () =>
{
    string fake = Path.Combine(sandbox, "fake-localappdata");
    string fish = Path.Combine(fake, "Fishstrap");
    Directory.CreateDirectory(Path.Combine(fish, "Modifications", "ClientSettings"));
    Directory.CreateDirectory(Path.Combine(fish, "Modifications", "content", "sounds"));
    File.WriteAllText(Path.Combine(fish, "Modifications", "ClientSettings", "ClientAppSettings.json"),
        "{\"DFIntTaskSchedulerTargetFps\": 144, \"FFlagDebugGraphicsPreferVulkan\": true, \"Lixo\": 1}");
    File.WriteAllText(Path.Combine(fish, "Modifications", "content", "sounds", "ouch.ogg"), "som");
    File.WriteAllText(Path.Combine(fish, "Settings.json"), "{\"UseDiscordRichPresence\": false, \"ShowServerDetails\": false, \"ConfirmLaunches\": true}");
    Directory.CreateDirectory(Path.Combine(fake, "Bloxstrap"));   // empty: not offered

    Store.Load();
    Store.Settings.FastFlags.Clear();
    var found = LauncherImport.Find(fake);
    Expect(found.Count == 1 && found[0].Name == "Fishstrap" && found[0].Flags == 2 && found[0].Mods == 1, $"encontrado: {string.Join(", ", found)}");

    var result = LauncherImport.Import(found[0]);
    Expect(result.Flags == 2 && result.SkippedFlags == 1 && result.Mods == 1 && result.Settings == 3, $"resultado: {result}");
    Expect(Store.Settings.FastFlags["DFIntTaskSchedulerTargetFps"] == "144" && Store.Settings.FastFlags["FFlagDebugGraphicsPreferVulkan"] == "True", "flags não aplicadas");
    Expect(Store.Settings.FastFlagProfiles.ContainsKey("Fishstrap"), "perfil Fishstrap não criado");
    Expect(File.ReadAllText(Path.Combine(Paths.Modifications, "content", "sounds", "ouch.ogg")) == "som", "mod não copiado");
    Expect(!Store.Settings.DiscordRichPresence && !Store.Settings.ShowServerLocation && Store.Settings.ConfirmLaunches, "preferências não vieram");
    Expect(File.Exists(Path.Combine(fish, "Modifications", "content", "sounds", "ouch.ogg")), "a origem foi alterada");

    // A second import doesn't overwrite a mod the user already has, and existing flags stay put.
    File.WriteAllText(Path.Combine(Paths.Modifications, "content", "sounds", "ouch.ogg"), "meu");
    Store.Settings.FastFlags = new() { ["FFlagMinha"] = "True" };
    var again = LauncherImport.Import(found[0]);
    Expect(again.Mods == 0 && File.ReadAllText(Path.Combine(Paths.Modifications, "content", "sounds", "ouch.ogg")) == "meu", "sobrescreveu um mod do usuário");
    Expect(Store.Settings.FastFlags.Count == 1 && Store.Settings.FastFlags.ContainsKey("FFlagMinha"), "trocou as flags que o usuário já tinha");
});

Check("F2", "aviso de servidor tem botão que copia o convite (e o convite funciona na barra)", () =>
{
    var session = new GameSession(920587237, Job, "1.2.3.4", ServerKind.Public, null, DateTime.UtcNow);
    string invite = RobloxGames.JoinLink(session);
    Expect(RobloxLink.ToLaunchArgs(invite)?.Contains(Job) == true, "o convite não abre o servidor na barra");
    // The rest opens a real notice window and writes the clipboard: only with BUGHUNT_UI=1.
    if (Environment.GetEnvironmentVariable("BUGHUNT_UI") != "1")
        return;

    string? label = null;
    string stage = "start";
    Exception? error = null;
    var ui = new Thread(() =>
    {
        try
        {
            stage = "app";
            var app = new BloxCode.App();
            app.InitializeComponent();
            stage = "theme";
            BloxCode.UI.ThemeManager.Apply(AppTheme.Dark);
            stage = "toast";
            BloxCode.UI.ToastWindow.Show("Connected to a server", "Location: São Paulo, BR", copyLabel: "Copy invite link", copy: invite);
            var toast = (BloxCode.UI.ToastWindow)typeof(BloxCode.UI.ToastWindow).GetField("_current", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!.GetValue(null)!;
            var button = (System.Windows.Controls.Button)toast.FindName("ActionButton");
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(900) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                stage = "tick";
                try {
                // A screenshot of the corner, for the report.
                var area = System.Windows.SystemParameters.WorkArea;
                using var bmp = new System.Drawing.Bitmap(420, 200);
                using (var g = System.Drawing.Graphics.FromImage(bmp))
                    g.CopyFromScreen((int)(area.Right * 1.0) - 420, (int)area.Bottom - 200, 0, 0, bmp.Size);
                bmp.Save(Path.Combine(Path.GetTempPath(), "bloxcode-f2-toast.png"));
                stage = "click";
                button.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                } catch (Exception ex) { error = ex; System.Windows.Threading.Dispatcher.CurrentDispatcher.InvokeShutdown(); return; }
                // The copy runs on its own thread for up to 2 s; read the button after that.
                var after = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(2600) };
                after.Tick += (_, _) => { after.Stop(); label = button.Content as string; System.Windows.Threading.Dispatcher.CurrentDispatcher.InvokeShutdown(); };
                after.Start();
            };
            timer.Start();
            stage = "waiting";
            System.Windows.Threading.Dispatcher.Run();
        }
        catch (Exception ex) { error = ex; }
    });
    ui.SetApartmentState(ApartmentState.STA);
    ui.IsBackground = true;
    ui.Start();
    Expect(ui.Join(TimeSpan.FromSeconds(20)), $"a janela de aviso não respondeu em 20 s (parou em: {stage})");
    if (error is not null) throw error;
    Expect(label == "Copied", $"o botão diz '{label}'");
    // Reading the clipboard back isn't possible in every environment (a sandboxed shell sees it empty);
    // "Copied" only shows once Clipboard.SetDataObject returned without an error.
});

Check("F3", "perfil gráfico por jogo vale só para aquele jogo e não mexe nas flags salvas", () =>
{
    Store.Load();
    Store.Settings.FastFlags = new() { ["FFlagMinha"] = "True" };
    Store.Settings.FastFlagProfiles["Max FPS"] = new() { ["DFIntTaskSchedulerTargetFps"] = "240" };
    GameProfiles.Set(111, GameProfiles.PresetPrefix + "Lowest");
    GameProfiles.Set(222, GameProfiles.ProfilePrefix + "Max FPS");
    GameProfiles.Set(333, GameProfiles.ProfilePrefix + "Apagado");

    Expect(GameProfiles.PlaceIdOf("roblox://experiences/start?placeId=111") == 111, "placeId do roblox://");
    Expect(GameProfiles.PlaceIdOf("roblox-player:1+launchmode:play+placelauncherurl:https%3A%2F%2Fassetgame.roblox.com%2Fgame%2FPlaceLauncher.ashx%3Frequest%3DRequestGame%26placeId%3D222+browsertrackerid:1") == 222, "placeId do link do navegador");
    Expect(GameProfiles.PlaceIdOf("roblox-player:1+launchmode:app") is null, "sem jogo");

    var lowest = GameProfiles.FlagsForLaunch("roblox://experiences/start?placeId=111", out string? used1);
    Expect(used1 == "Lowest preset" && lowest.ContainsKey("FFlagMinha") && lowest.Count > 1, $"preset: {used1}, {lowest.Count} flags");
    var profile = GameProfiles.FlagsForLaunch("roblox://experiences/start?placeId=222", out string? used2);
    Expect(used2 == "Max FPS profile" && profile.Count == 1 && profile["DFIntTaskSchedulerTargetFps"] == "240", $"perfil: {used2}");
    var other = GameProfiles.FlagsForLaunch("roblox://experiences/start?placeId=999", out string? used3);
    Expect(used3 is null && ReferenceEquals(other, Store.Settings.FastFlags), "outro jogo devia usar as flags normais");
    var deleted = GameProfiles.FlagsForLaunch("roblox://experiences/start?placeId=333", out string? used4);
    Expect(used4 is null && deleted.ContainsKey("FFlagMinha"), "perfil apagado devia cair nas flags normais");
    Expect(Store.Settings.FastFlags.Count == 1, "as flags salvas mudaram");

    Store.Load();   // survives a save and reload
    Expect(GameProfiles.ChoiceFor(111) == "preset:Lowest" && GameProfiles.ChoiceFor(222) == "profile:Max FPS", "não foi salvo");
    GameProfiles.Set(111, null);
    Expect(GameProfiles.ChoiceFor(111) is null, "não limpou");
});

Check("F4", "atalho de jogo na área de trabalho (com o ícone do jogo) e jogos fixados", () =>
{
    // Pinned games survive a damaged Settings.json: nulls and repeats are dropped.
    File.WriteAllText(Paths.SettingsFile, "{\"PinnedGames\": [null, {\"PlaceId\": 5, \"Name\": null}, {\"PlaceId\": 5, \"Name\": \"x\"}, {\"PlaceId\": 0}]}");
    Store.Load();
    Expect(Store.Settings.PinnedGames.Count == 1 && Store.Settings.PinnedGames[0].Name == "", $"fixados: {Store.Settings.PinnedGames.Count}");

    // The icon: the real game icon from Roblox, made into an .ico Windows can read.
    using var http = new System.Net.Http.HttpClient();
    var info = RobloxGames.GetAsync(http, 920587237, null, CancellationToken.None).GetAwaiter().GetResult();
    Expect(info?.IconUrl is not null, "o Roblox não devolveu o ícone do jogo (sem internet?)");
    string folder = Path.Combine(sandbox, "Desktop");
    Directory.CreateDirectory(folder);
    string lnk = BloxCode.Core.GameShortcuts.CreateAsync(http, 920587237, "Adopt Me! [🎃] <test>", info!.IconUrl, folder).GetAwaiter().GetResult();
    Expect(Path.GetFileName(lnk) == "Adopt Me! test.lnk" || Path.GetFileName(lnk) == "Adopt Me! test.lnk", $"nome do atalho: {Path.GetFileName(lnk)}");
    string ico = Path.Combine(BloxCode.Core.GameShortcuts.IconsFolder, "920587237.ico");
    Expect(File.Exists(ico), "o ícone não foi salvo");
    var frame = System.Windows.Media.Imaging.BitmapDecoder.Create(new Uri(ico), System.Windows.Media.Imaging.BitmapCreateOptions.None, System.Windows.Media.Imaging.BitmapCacheOption.OnLoad).Frames[0];
    Expect(frame.PixelWidth == 256 && frame.PixelHeight == 256, $"ícone {frame.PixelWidth}x{frame.PixelHeight}");

    dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
    dynamic link = shell.CreateShortcut(lnk);
    string args = link.Arguments, icon = link.IconLocation;
    Expect(args == "-player \"roblox://experiences/start?placeId=920587237\"", $"argumentos: {args}");
    Expect(icon.StartsWith(ico, StringComparison.OrdinalIgnoreCase), $"ícone do atalho: {icon}");
});

Check("F5", "programas que abrem com o Roblox: abre uma vez só e fecha só o que o Blox Code abriu", () =>
{
    Store.Load();
    string ping = Path.Combine(Environment.SystemDirectory, "PING.EXE");
    Store.Settings.Integrations = new()
    {
        new Integration { Name = "Ping", Path = ping, Arguments = "-n 60 127.0.0.1", CloseWithRoblox = true },
        new Integration { Name = "Sumiu", Path = @"C:\nao\existe\programa.exe" },
    };
    Store.State.IntegrationProcesses.Clear();

    BloxCode.Core.Integrations.StartAll();
    Expect(Store.State.IntegrationProcesses.Count == 1, $"esperava 1 programa aberto, foram {Store.State.IntegrationProcesses.Count}");
    int pid = Store.State.IntegrationProcesses[0].Pid;
    Thread.Sleep(800);
    BloxCode.Core.Integrations.StartAll();   // a second launch: it is already open
    Expect(Store.State.IntegrationProcesses.Count == 1, "abriu o mesmo programa duas vezes");

    // An entry whose start time doesn't match (the ID was reused by another program) is left alone.
    var stranger = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(ping, "-n 60 127.0.0.1") { UseShellExecute = false, CreateNoWindow = true })!;
    Store.State.IntegrationProcesses.Add(new StartedIntegration { Pid = stranger.Id, StartedAt = DateTime.UtcNow.AddHours(-3), Path = ping });

    BloxCode.Core.Integrations.CloseAll();
    Thread.Sleep(500);
    bool ours = true;
    try { using var p = System.Diagnostics.Process.GetProcessById(pid); ours = !p.HasExited; } catch (ArgumentException) { ours = false; }
    bool theirs = !stranger.HasExited;
    stranger.Kill();
    Expect(!ours, "o programa aberto pelo Blox Code não foi fechado");
    Expect(theirs, "fechou um programa que o Blox Code não abriu");
    Expect(Store.State.IntegrationProcesses.Count == 0, "a lista não foi limpa");
});

Check("F6", "status do Roblox: lê a página de status e explica um problema", () =>
{
    // A reply in status.io's shape, with an outage.
    var bad = RobloxStatus.Parse("{\"result\":{\"status_overall\":{\"status\":\"Partial Service Disruption\",\"status_code\":400},"
        + "\"status\":[{\"name\":\"Player\",\"status\":\"Partial Service Disruption\",\"status_code\":400},{\"name\":\"User\",\"status\":\"Operational\",\"status_code\":100}],"
        + "\"incidents\":[{\"name\":\"Players unable to join experiences\"}]}}");
    Expect(!bad.AllGood && bad.Problems.Count == 2, $"problemas: {string.Join(" | ", bad.Problems)}");
    string? note = RobloxStatus.Explain(bad);
    Expect(note is not null && note.Contains("Players unable to join experiences") && note.Contains("Player: Partial Service Disruption"), note ?? "sem nota");
    var good = RobloxStatus.Parse("{\"result\":{\"status_overall\":{\"status\":\"Operational\",\"status_code\":100},\"status\":[{\"name\":\"Player\",\"status\":\"Operational\",\"status_code\":100}],\"incidents\":[]}}");
    Expect(good.AllGood && RobloxStatus.Explain(good) is null, "tudo certo devia ficar calado");
    Expect(RobloxStatus.Explain(null) is null, "sem resposta devia ficar calado");

    // The real page answers, in that shape.
    using var http = new System.Net.Http.HttpClient();
    var live = RobloxStatus.GetAsync(http, CancellationToken.None).GetAwaiter().GetResult();
    Expect(live is not null, "a página de status do Roblox não respondeu");
    Console.WriteLine($"      (status do Roblox agora: {live!.Summary}{(live.Problems.Count > 0 ? " — " + string.Join("; ", live.Problems) : "")})");
});

Check("F7", "configurações do Roblox: lê e grava só os valores, o resto do arquivo fica igual", () =>
{
    // A copy of the real file (the original is only read).
    string real = BloxCode.Core.RobloxSettingsFile.DefaultPath;
    Expect(File.Exists(real), "não há GlobalBasicSettings_13.xml neste PC para testar");
    string copy = Path.Combine(sandbox, "GlobalBasicSettings_13.xml");
    File.Copy(real, copy);
    string before = File.ReadAllText(copy);

    var read = BloxCode.Core.RobloxSettingsFile.Read(copy);
    Expect(read is not null, "não leu");
    var changed = read! with { MouseSensitivity = 2.35, Volume = 0.4, Quality = 7, Fullscreen = !read.Fullscreen, PerformanceStats = true };
    Expect(BloxCode.Core.RobloxSettingsFile.Write(changed, copy), "não gravou");
    var again = BloxCode.Core.RobloxSettingsFile.Read(copy)!;
    Expect(again == changed with { MouseSensitivity = 2.35 }, $"releu {again}");

    // Every line but the changed settings is byte for byte the same.
    var a = before.Split('\n'); var b = File.ReadAllText(copy).Split('\n');
    Expect(a.Length == b.Length, $"número de linhas mudou ({a.Length} -> {b.Length})");
    var diff = a.Zip(b).Where(p => p.First != p.Second).Select(p => System.Text.RegularExpressions.Regex.Match(p.Second, "name=\"(\\w+)\"").Groups[1].Value).ToList();
    var allowed = new[] { "MouseSensitivity", "MasterVolume", "SavedQualityLevel", "GraphicsQualityLevel", "Fullscreen", "PerformanceStatsVisible", "ReducedMotion", "CameraYInverted" };
    Expect(diff.All(allowed.Contains), "mudou outras linhas: " + string.Join(", ", diff.Where(d => !allowed.Contains(d))));
    Expect(File.Exists(copy + ".bloxcode-backup") && File.ReadAllText(copy + ".bloxcode-backup") == before, "sem cópia do original");
    Expect(BloxCode.Core.RobloxSettingsFile.Read(Path.Combine(sandbox, "nao-existe.xml")) is null, "arquivo inexistente devia dar null");
});

if (Environment.GetEnvironmentVariable("BUGHUNT_SKIP_SERVERS") != "1") Check("F8", "lista de servidores de um jogo e link para entrar num servidor escolhido", () =>
{
    using var http = new System.Net.Http.HttpClient();
    var full = ServerBrowser.GetAsync(http, 920587237, ServerSort.MostPlayers, null, CancellationToken.None).GetAwaiter().GetResult();
    Expect(full.Servers.Count > 0, "nenhum servidor (sem internet?)");
    Expect(full.Servers.All(s => s.Playing < s.MaxPlayers), "veio servidor cheio");
    var empty = ServerBrowser.GetAsync(http, 920587237, ServerSort.FewestPlayers, null, CancellationToken.None).GetAwaiter().GetResult();
    Expect(empty.Servers.Count > 0 && empty.Servers.Average(s => s.Playing) < full.Servers.Average(s => s.Playing), "ordem por jogadores não funcionou");
    if (full.NextCursor is { } cursor)
    {
        var next = ServerBrowser.GetAsync(http, 920587237, ServerSort.MostPlayers, cursor, CancellationToken.None).GetAwaiter().GetResult();
        // Live lists shift between requests (the window skips repeats); the next page must still bring new ones.
        Expect(next.Servers.Count(s => full.Servers.All(f => f.Id != s.Id)) > 0, "a página seguinte não trouxe servidores novos");
    }
    string args = ServerBrowser.JoinArgs(920587237, full.Servers[0]);
    Expect(RobloxLink.ToLaunchArgs(args) == args && args.Contains(full.Servers[0].Id), $"link: {args}");
    Console.WriteLine($"      ({full.Servers.Count} servidores; o mais cheio: {full.Servers[0].Playing}/{full.Servers[0].MaxPlayers}, {full.Servers[0].Ping} ms)");
});

Check("F9", "baixar a próxima versão do Roblox em segundo plano (só os 3 menores pacotes no teste)", () =>
{
    using var http = new System.Net.Http.HttpClient();
    Store.Load();
    var live = Deployment.GetInfo(http, Deployment.DefaultChannel, CancellationToken.None).GetAwaiter().GetResult();

    Store.State.VersionGuid = live.VersionGuid;   // already current: nothing to do
    Expect(Prefetch.RunAsync(http, CancellationToken.None, limit: 3).GetAwaiter().GetResult() == 0, "baixou algo com o Roblox já atualizado");

    Store.State.VersionGuid = "version-0000000000000000";   // an older version is installed
    int fetched = Prefetch.RunAsync(http, CancellationToken.None, limit: 3).GetAwaiter().GetResult();
    Expect(fetched == 3, $"baixou {fetched} pacotes");
    var files = Directory.GetFiles(Paths.Downloads);
    Expect(files.Length == 3 && files.All(f => !f.EndsWith(".prefetch")), "arquivos no cache: " + string.Join(", ", files.Select(Path.GetFileName)));
    foreach (string file in files)
    {
        using var stream = File.OpenRead(file);
        Expect(Convert.ToHexStringLower(System.Security.Cryptography.MD5.HashData(stream)) == Path.GetFileName(file), $"{Path.GetFileName(file)} não bate com o MD5");
    }
    Expect(Prefetch.RunAsync(http, CancellationToken.None, limit: 3).GetAwaiter().GetResult() == 0, "baixou de novo o que já estava no cache");

    Store.Settings.PinnedVersion = "version-0123456789abcdef";   // a pinned version is never prefetched over
    Expect(Prefetch.RunAsync(http, CancellationToken.None, limit: 3).GetAwaiter().GetResult() == 0, "baixou com uma versão fixada");
    Store.Settings.PinnedVersion = null;
});

Check("F10", "estatísticas: total, jogos, dia a dia (cruzando a meia-noite) e sequência de dias", () =>
{
    DateTime L(int daysAgo, int hour, int minute = 0) => DateTime.Today.AddDays(-daysAgo).AddHours(hour).AddMinutes(minute).ToUniversalTime();
    var now = DateTime.Today.AddHours(20).ToUniversalTime();
    var history = new List<PlayedGame>
    {
        new() { PlaceId = 1, Name = "A", JoinedAt = L(0, 18), LeftAt = L(0, 19) },               // today, 1 h
        new() { PlaceId = 2, Name = "B", JoinedAt = L(1, 23), LeftAt = L(0, 1) },                // across midnight: 1 h yesterday + 1 h today
        new() { PlaceId = 1, Name = "A", JoinedAt = L(2, 10), LeftAt = L(2, 10, 30) },           // 2 days ago
        new() { PlaceId = 3, Name = "C", JoinedAt = L(20, 10), LeftAt = L(20, 15) },             // outside the week
    };
    var week = PlayStatsReport.Build(history, 7, now);
    Expect(week.Total == TimeSpan.FromHours(3.5), $"total {week.Total}");
    Expect(week.Sessions == 3 && week.Games == 2, $"{week.Sessions} sessões, {week.Games} jogos");
    Expect(week.TopGames[0].PlaceId == 2 && week.TopGames[0].Time == TimeSpan.FromHours(2), $"mais jogado: {week.TopGames[0]}");
    Expect(week.Days.Count == 7, $"{week.Days.Count} dias");
    Expect(week.Days[^1].Time == TimeSpan.FromHours(2) && week.Days[^2].Time == TimeSpan.FromHours(1) && week.Days[^3].Time == TimeSpan.FromMinutes(30), "tempo por dia errado: " + string.Join(", ", week.Days.Select(x => x.Time)));
    Expect(week.StreakDays == 3, $"sequência {week.StreakDays}");
    Expect(week.Longest == TimeSpan.FromHours(2), $"maior sessão {week.Longest}");
    var all = PlayStatsReport.Build(history, null, now);
    Expect(all.Total == TimeSpan.FromHours(8.5) && all.Games == 3, $"tudo: {all.Total}, {all.Games}");
    var none = PlayStatsReport.Build(new List<PlayedGame>(), 30, now);
    Expect(none.Total == TimeSpan.Zero && none.Days.Count == 30 && none.StreakDays == 0, "vazio");
});

Check("F11", "lembrete de pausa: avisa a cada intervalo e recomeça depois de 10 min longe", () =>
{
    var t0 = new DateTime(2026, 11, 11, 12, 0, 0, DateTimeKind.Utc);
    var reminder = new BreakReminder(TimeSpan.FromHours(1));
    var fired = new List<int>();
    // 0–130 min playing (with a 5-minute gap at 70, a teleport-like pause that doesn't reset).
    for (int m = 0; m <= 130; m++)
        if (reminder.Tick(playing: m is < 70 or > 75, t0.AddMinutes(m), out _))
            fired.Add(m);
    Expect(fired.SequenceEqual(new[] { 60, 120 }), "avisos em: " + string.Join(", ", fired));
    // 15 minutes away, then playing again: the count starts over.
    for (int m = 131; m <= 146; m++)
        reminder.Tick(false, t0.AddMinutes(m), out _);
    fired.Clear();
    for (int m = 147; m <= 260; m++)
        if (reminder.Tick(true, t0.AddMinutes(m), out var played) && played >= TimeSpan.FromHours(1))
            fired.Add(m);
    Expect(fired.FirstOrDefault() == 207, "depois da pausa o primeiro aviso devia ser 60 min depois: " + string.Join(", ", fired));
    var off = new BreakReminder(TimeSpan.Zero);
    Expect(!off.Tick(true, t0.AddHours(10), out _), "desligado não devia avisar");
});

Console.WriteLine($"\n{bugs} bug(s) reproduzido(s), {ok} verificação(ões) ok. Sandbox: {sandbox}");
try { Directory.Delete(sandbox, recursive: true); } catch { }
return bugs;
