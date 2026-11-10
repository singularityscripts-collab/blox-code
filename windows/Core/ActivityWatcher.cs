using System.Diagnostics;
using System.Net.Http;
using System.Text.Json.Nodes;

namespace BloxCode.Core;

/// <summary>
/// Stays in the background while Roblox runs: reads its log to see which game and server the player
/// is in, shows that on Discord (Rich Presence) and says roughly where the server is. Ends by itself
/// when the last Roblox player closes. One watcher at a time: a newer launch takes over.
/// </summary>
public static class ActivityWatcher
{
    private const string LockName = "BloxCode-Activity";
    private const string StopName = "BloxCode-Activity-Stop";

    public static bool DiscordAvailable => Updater.DiscordAppId is not null;

    public static bool IsWanted =>
        Store.Settings.ShowServerLocation || Store.Settings.RememberGames || Store.Settings.MultiInstance
        || (Store.Settings.DiscordRichPresence && DiscordAvailable);

    /// <param name="launchedAt">When Roblox was started; its log is the first one created after this.</param>
    /// <param name="notify">Shows a notice (title, text); called from a worker thread.</param>
    public static async Task RunAsync(HttpClient http, DateTime launchedAt, Action<string, string> notify)
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

        try
        {
            Logger.Info(ident, $"Watching (Discord: {(discord is null ? "off" : "on")}, server location: {(Store.Settings.ShowServerLocation ? "on" : "off")})");
            await WatchAsync(http, launchedAt, discord, notify, cts.Token);

            // Multi-instance: the singleton name must stay taken while any player is open, log or not.
            if (MultiInstance.IsHeld)
            {
                var running = new RobloxRunning();
                while (running.Now())
                    await Task.Delay(2000, cts.Token);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Logger.Error(ident, ex);
        }
        finally
        {
            stopWait.Unregister(null);
            if (Store.Settings.RememberGames)
                GameHistory.CloseDangling(DateTime.UtcNow);
            if (discord?.IsConnected == true)
            {
                try { await discord.SetActivityAsync(null, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(2)); } catch { }
            }
            gate.Release();
            Logger.Info(ident, "Stopped");
        }
    }

    private static async Task WatchAsync(HttpClient http, DateTime launchedAt, DiscordRpc? discord, Action<string, string> notify, CancellationToken ct)
    {
        var running = new RobloxRunning();
        var started = DateTime.UtcNow;
        string? log = null;

        while (!ct.IsCancellationRequested)
        {
            log ??= RobloxLog.NewestPlayerLog(launchedAt) ?? (DateTime.UtcNow - started > TimeSpan.FromSeconds(15) ? RecentPlayerLog() : null);
            if (log is null)
            {
                // Roblox writes its log within a second or two; give up if it is gone or never started.
                if (DateTime.UtcNow - started > TimeSpan.FromSeconds(45) || (DateTime.UtcNow - started > TimeSpan.FromSeconds(10) && !running.Now()))
                    return;
                await Task.Delay(1000, ct);
                continue;
            }

            Logger.Info("ActivityWatcher::Watch", $"Reading {Path.GetFileName(log)}");
            var parser = new ActivityParser();
            bool live = false;
            CancellationTokenSource? sessionWork = null;

            parser.Joined += session =>
            {
                if (!live)
                    return;   // An old join in a log we attached to late: only the latest counts.
                sessionWork?.Cancel();
                sessionWork = CancellationTokenSource.CreateLinkedTokenSource(ct);
                _ = OnJoinedAsync(http, session, discord, notify, showNotice: true, sessionWork.Token);
            };
            parser.Left += session =>
            {
                if (!live)
                    return;
                sessionWork?.Cancel();
                Logger.Info("ActivityWatcher::Left", $"Left place {session.PlaceId}");
                if (Store.Settings.RememberGames)
                    GameHistory.Left(session);
                if (discord is not null)
                    _ = discord.SetActivityAsync(null, ct);
            };

            string? next = await RobloxLog.FollowAsync(log, parser.Feed, running.Now, launchedAt, ct, onCaughtUp: () =>
            {
                live = true;
                // Already in a game when we started reading (a late attach): show it, without the notice.
                if (parser.Current is { } current)
                {
                    sessionWork = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    _ = OnJoinedAsync(http, current, discord, notify, showNotice: false, sessionWork.Token);
                }
            });

            sessionWork?.Cancel();
            if (discord is not null)
                await discord.SetActivityAsync(null, ct);
            if (next is null)
                return;
            log = next;
            launchedAt = DateTime.UtcNow.AddSeconds(-10);
        }
    }

    private static async Task OnJoinedAsync(HttpClient http, GameSession session, DiscordRpc? discord, Action<string, string> notify, bool showNotice, CancellationToken ct)
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
                notify(session.Kind == ServerKind.Public ? "Connected to a server" : "Connected to a private server",
                    where is null ? "Location unknown" : $"Location: {where}");
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

    private static string Clip(string text, int max) => text.Length <= max ? text : text[..(max - 1)] + "…";

    /// <summary>When Roblox was already open, the launch hands over to it and no new log appears.</summary>
    private static string? RecentPlayerLog()
    {
        var dir = new DirectoryInfo(RobloxLog.Directory);
        if (!dir.Exists)
            return null;
        return dir.EnumerateFiles("*_Player_*.log")
            .Where(f => DateTime.UtcNow - f.LastWriteTimeUtc < TimeSpan.FromMinutes(2))
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .FirstOrDefault()?.FullName;
    }

    /// <summary>Whether a Roblox player is open, asked of Windows at most every two seconds.</summary>
    private sealed class RobloxRunning
    {
        private DateTime _checked;
        private bool _running = true;

        public bool Now()
        {
            if (DateTime.UtcNow - _checked < TimeSpan.FromSeconds(2))
                return _running;
            _checked = DateTime.UtcNow;
            var players = Process.GetProcessesByName("RobloxPlayerBeta");
            _running = players.Length > 0;
            foreach (var p in players)
                p.Dispose();
            return _running;
        }
    }
}
