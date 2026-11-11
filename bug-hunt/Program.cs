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

Console.WriteLine($"\n{bugs} bug(s) reproduzido(s), {ok} verificação(ões) ok. Sandbox: {sandbox}");
try { Directory.Delete(sandbox, recursive: true); } catch { }
return bugs;
