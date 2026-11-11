using System.Diagnostics;
using System.Net.Http;
using System.Text.Json.Nodes;

namespace BloxCode.Core;

/// <summary>
/// Stays in the background while Roblox runs: reads every open player's log to see which game and
/// server it is in, shows that on Discord (Rich Presence), says roughly where the server is and keeps
/// the history. With several windows open (multi-instance) each log is followed on its own and
/// Discord shows the latest game. Ends by itself when the last Roblox player closes. One watcher at a
/// time: a newer launch takes over and picks up the windows that are already open.
/// </summary>
public static class ActivityWatcher
{
    private const string LockName = "BloxCode-Activity";
    private const string StopName = "BloxCode-Activity-Stop";

    public static bool DiscordAvailable => Updater.DiscordAppId is not null;

    public static bool IsWanted =>
        Store.Settings.ShowServerLocation || Store.Settings.RememberGames || Store.Settings.MultiInstance
        || (Store.Settings.DiscordRichPresence && DiscordAvailable) || Integrations.WantsWatcher || Store.Settings.BreakReminderMinutes > 0;

    /// <param name="launchedAt">When Roblox was started; its log is the first one created after this.</param>
    /// <param name="notify">Shows a notice (title, text, and a server invite link to offer for copying, or null); called from a worker thread.</param>
    public static async Task RunAsync(HttpClient http, DateTime launchedAt, Action<string, string, string?> notify)
    {
        const string ident = "ActivityWatcher::Run";

        // A named semaphore, not a mutex: async code may release it from another thread.
        using var gate = new Semaphore(1, 1, LockName);
        using var stopSignal = new EventWaitHandle(false, EventResetMode.AutoReset, StopName);
        if (!gate.WaitOne(0))
        {
            Logger.Info(ident, "Taking over from the previous watcher");
            stopSignal.Set();
            if (!gate.WaitOne(TimeSpan.FromSeconds(5)))
            {
                Logger.Warn(ident, "The previous watcher didn't stop; leaving it running");
                return;
            }
        }

        stopSignal.Reset();
        using var cts = new CancellationTokenSource();
        var stopWait = ThreadPool.RegisterWaitForSingleObject(stopSignal, (_, _) => cts.Cancel(), null, Timeout.Infinite, executeOnlyOnce: true);
        using var discord = Store.Settings.DiscordRichPresence && Updater.DiscordAppId is { } appId ? new DiscordRpc(appId) : null;
        bool tookOver = false;

        try
        {
            Logger.Info(ident, $"Watching (Discord: {(discord is null ? "off" : "on")}, server location: {(Store.Settings.ShowServerLocation ? "on" : "off")}, multi-instance: {(MultiInstance.IsHeld ? "on" : "off")})");
            await new Watch(http, launchedAt, discord, notify).RunAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            tookOver = true;
        }
        catch (Exception ex)
        {
            Logger.Error(ident, ex);
        }
        finally
        {
            stopWait.Unregister(null);
            // A newer watcher carries on with the same windows; only a real end closes open visits.
            if (Store.Settings.RememberGames && !tookOver)
                GameHistory.CloseDangling(DateTime.UtcNow);
            // Every Roblox window is closed: so are the programs that opened with it.
            if (!tookOver)
                Integrations.CloseAll();
            if (discord?.IsConnected == true && !tookOver)
            {
                try { await discord.SetActivityAsync(null, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(2)); } catch { }
            }
            gate.Release();
            Logger.Info(ident, tookOver ? "Handed over to a newer launch" : "Stopped");
        }
    }

    /// <summary>One run of the watcher: a follower per player log, and the sessions they report.</summary>
    private sealed class Watch(HttpClient http, DateTime launchedAt, DiscordRpc? discord, Action<string, string, string?> notify)
    {
        private readonly Dictionary<string, Follower> _followers = new(StringComparer.OrdinalIgnoreCase);
        private readonly object _sync = new();
        private readonly RobloxRunning _running = new();
        private readonly BreakReminder _break = new(TimeSpan.FromMinutes(Store.Settings.BreakReminderMinutes));

        private sealed class Follower
        {
            public required string Log;
            public Task? Task;
            public GameSession? Session;
            public CancellationTokenSource? Work;
            public DateTime LastLine = DateTime.UtcNow;
            public bool Live;
        }

        public async Task RunAsync(CancellationToken ct)
        {
            var started = DateTime.UtcNow;
            // A Roblox update that came out meanwhile is fetched while you play, once per run.
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(Prefetch.Delay, ct);
                    await Prefetch.RunAsync(http, ct);
                }
                catch (OperationCanceledException) { }
                catch (Exception ex) { Logger.Warn("ActivityWatcher::Prefetch", ex.Message); }
            }, ct);
            try
            {
                while (true)
                {
                    ct.ThrowIfCancellationRequested();
                    foreach (string log in RobloxLog.PlayerLogs(launchedAt))
                        lock (_sync)
                            if (!_followers.ContainsKey(log))
                                Start(log, ct);

                    int players = _running.Count();
                    bool any;
                    lock (_sync) any = _followers.Count > 0;
                    if (players == 0 && (any || DateTime.UtcNow - started > TimeSpan.FromSeconds(15)))
                        return;   // Every window closed (or Roblox never really started).
                    if (players > 0 && !any && DateTime.UtcNow - started > TimeSpan.FromSeconds(60) && !MultiInstance.IsHeld)
                        return;   // No log to read and nothing to hold for: nothing for us to do.

                    EndSessionsOfClosedWindows(players);
                    RemindOfBreak();
                    await Task.Delay(2000, ct);
                }
            }
            finally
            {
                lock (_sync)
                    foreach (var f in _followers.Values)
                        f.Work?.Cancel();
            }
        }

        /// <summary>After the chosen stretch of continuous play, a gentle notice (Settings → Remind me to take a break).</summary>
        private void RemindOfBreak()
        {
            bool playing;
            lock (_sync) playing = _followers.Values.Any(f => f.Session is not null);
            if (_break.Tick(playing, DateTime.UtcNow, out var played))
            {
                Logger.Info("ActivityWatcher::Break", $"Reminding of a break after {played}");
                notify("Time for a break?", $"You've been playing for {GameHistory.Duration(played)}. Stretch, drink some water.", null);
            }
        }

        private void Start(string log, CancellationToken ct)
        {
            Logger.Info("ActivityWatcher::Watch", $"Reading {Path.GetFileName(log)}");
            var parser = new ActivityParser();
            var follower = new Follower { Log = log };
            _followers[log] = follower;
            parser.Joined += session => OnJoined(follower, session, ct);
            parser.Left += session => OnLeft(follower, session, ct);

            // On the thread pool, so the first lines (read synchronously) never run under _sync.
            follower.Task = Task.Run(() => RobloxLog.FollowAsync(log, line =>
            {
                follower.LastLine = DateTime.UtcNow;
                // One odd line must not end the reading of this window for good.
                try { parser.Feed(line); }
                catch (Exception ex) { Logger.Warn("ActivityWatcher::Feed", $"Skipped a log line: {ex.Message}"); }
            }, () => true, launchedAt, ct, followNewer: false, onCaughtUp: () =>
            {
                follower.Live = true;
                // Already in a game when we started reading (a late attach): show it, without the notice.
                if (parser.Current is { } current)
                    OnJoined(follower, current, ct, lateAttach: true);
            }));
        }

        private void OnJoined(Follower follower, GameSession session, CancellationToken ct, bool lateAttach = false)
        {
            if (!follower.Live && !lateAttach)
                return;   // An old join in a log read from the start: only the latest counts.
            lock (_sync)
            {
                follower.Work?.Cancel();
                follower.Work = CancellationTokenSource.CreateLinkedTokenSource(ct);
                follower.Session = session;
            }
            _ = OnJoinedAsync(session, showNotice: !lateAttach, follower.Work.Token);
        }

        private void OnLeft(Follower follower, GameSession session, CancellationToken ct)
        {
            if (!follower.Live)
                return;
            lock (_sync)
            {
                follower.Work?.Cancel();
                follower.Session = null;
            }
            Logger.Info("ActivityWatcher::Left", $"Left place {session.PlaceId}");
            if (Store.Settings.RememberGames)
                GameHistory.Left(session);
            _ = ShowLatestAsync(ct);
        }

        /// <summary>
        /// A window closed without a "leave" in its log (killed, crashed, closed mid-game): when fewer
        /// players run than sessions are open, the sessions whose logs went quiet longest are over.
        /// </summary>
        private void EndSessionsOfClosedWindows(int players)
        {
            List<Follower> ended;
            lock (_sync)
            {
                var open = _followers.Values.Where(f => f.Session is not null).ToList();
                int extra = open.Count - players;
                if (extra <= 0)
                    return;
                ended = open.Where(f => DateTime.UtcNow - f.LastLine > TimeSpan.FromSeconds(10))
                    .OrderBy(f => f.LastLine).Take(extra).ToList();
            }
            foreach (var f in ended)
            {
                var session = f.Session!;
                lock (_sync)
                {
                    f.Work?.Cancel();
                    f.Session = null;
                }
                Logger.Info("ActivityWatcher::Left", $"Window with place {session.PlaceId} closed");
                if (Store.Settings.RememberGames)
                    GameHistory.Left(session, f.LastLine);
            }
            if (ended.Count > 0)
                _ = ShowLatestAsync(CancellationToken.None);
        }

        /// <summary>Discord shows the game joined last among the windows still in one, or nothing.</summary>
        private async Task ShowLatestAsync(CancellationToken ct)
        {
            if (discord is null)
                return;
            GameSession? latest;
            lock (_sync)
                latest = _followers.Values.Select(f => f.Session).Where(s => s is not null).OrderByDescending(s => s!.JoinedAt).FirstOrDefault();
            try
            {
                if (latest is null)
                    await discord.SetActivityAsync(null, ct);
                else
                    await discord.SetActivityAsync(BuildActivity(latest, await RobloxGames.GetAsync(http, latest.PlaceId, latest.UniverseId, ct)), ct);
            }
            catch (Exception ex) when (ex is OperationCanceledException or IOException)
            {
            }
        }

        private async Task OnJoinedAsync(GameSession session, bool showNotice, CancellationToken ct)
        {
            Logger.Info("ActivityWatcher::Joined", $"Joined place {session.PlaceId} ({session.Kind} server)");
            try
            {
                var location = showNotice && Store.Settings.ShowServerLocation
                    ? ServerLocation.GetAsync(http, session.Address, ct)
                    : Task.FromResult<string?>(null);
                bool remember = showNotice && Store.Settings.RememberGames;
                var game = discord is not null || remember
                    ? RobloxGames.GetAsync(http, session.PlaceId, session.UniverseId, ct)
                    : Task.FromResult<GameInfo?>(null);

                if (remember)
                    GameHistory.Joined(session, await game);

                if (showNotice && Store.Settings.ShowServerLocation)
                {
                    string? where = await location;
                    ct.ThrowIfCancellationRequested();
                    // A public server can be shared: the invite link opens this very server (also pasted into Blox Code's bar).
                    notify(session.Kind == ServerKind.Public ? "Connected to a server" : "Connected to a private server",
                        where is null ? "Location unknown" : $"Location: {where}",
                        session.Kind == ServerKind.Public ? RobloxGames.JoinLink(session) : null);
                }

                if (discord is not null)
                {
                    var info = await game;
                    ct.ThrowIfCancellationRequested();
                    await discord.SetActivityAsync(BuildActivity(session, info), ct);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                Logger.Warn("ActivityWatcher::Joined", ex.Message);
            }
        }
    }

    /// <summary>What Discord shows: the game, who made it, how long, and buttons to its page (and server).</summary>
    public static JsonObject BuildActivity(GameSession session, GameInfo? game)
    {
        string name = Clip(game?.Name is { Length: > 0 } n ? n : "a Roblox game", 128);
        var assets = new JsonObject { ["large_text"] = name };
        if (game?.IconUrl is { } icon)
            assets["large_image"] = icon;
        if (BloxCodeIcon is { } bloxIcon)
        {
            assets["small_image"] = bloxIcon;
            assets["small_text"] = $"Blox Code v{Updater.Current}";
        }

        var buttons = new JsonArray();
        if (Store.Settings.DiscordJoinButton && session.Kind == ServerKind.Public)
            buttons.Add(new JsonObject { ["label"] = "Join server", ["url"] = RobloxGames.JoinLink(session) });
        buttons.Add(new JsonObject { ["label"] = "See game page", ["url"] = RobloxGames.GamePage(session.PlaceId) });

        var activity = new JsonObject
        {
            ["type"] = 0,
            ["details"] = name.Length >= 2 ? name : name + " ",
            ["timestamps"] = new JsonObject { ["start"] = new DateTimeOffset(session.JoinedAt).ToUnixTimeSeconds() },
            ["assets"] = assets,
            ["buttons"] = buttons,
        };
        string state = session.Kind != ServerKind.Public ? "In a private server"
            : game?.Creator is { Length: > 0 } creator ? $"by {creator}" : "";
        if (state.Length >= 2)
            activity["state"] = Clip(state, 128);
        return activity;
    }

    /// <summary>Bit, from the repository Blox Code updates from (Discord loads images by URL).</summary>
    private static string? BloxCodeIcon =>
        Updater.Homepage is { } home && home.StartsWith("https://github.com/", StringComparison.OrdinalIgnoreCase)
            ? home.Replace("https://github.com/", "https://raw.githubusercontent.com/", StringComparison.OrdinalIgnoreCase).TrimEnd('/') + "/main/linux/Assets/bloxcode-256.png"
            : null;

    /// <summary>At most <paramref name="max"/> characters, ending in … when cut; never splits an emoji (a surrogate pair) in two.</summary>
    public static string Clip(string text, int max)
    {
        if (text.Length <= max)
            return text;
        int keep = max - 1;
        if (char.IsHighSurrogate(text[keep - 1]))
            keep--;
        return text[..keep] + "…";
    }

    /// <summary>How many Roblox players are open, asked of Windows at most every two seconds.</summary>
    private sealed class RobloxRunning
    {
        private DateTime _checked;
        private int _count = 1;

        public int Count()
        {
            if (DateTime.UtcNow - _checked < TimeSpan.FromSeconds(2))
                return _count;
            _checked = DateTime.UtcNow;
            _count = MultiInstance.RunningPlayers();
            return _count;
        }
    }
}
