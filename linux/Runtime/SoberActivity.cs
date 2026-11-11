using BloxCode.Core;
using BloxCode.Linux.Platform;

namespace BloxCode.Linux.Runtime;

/// <summary>
/// The Linux side of the activity watcher: while Sober runs, follows the Roblox log it writes
/// (~/.var/app/org.vinegarhq.Sober/data/sober/sober_logs/latest.log) with the same parser as
/// Windows, records the games played (the Home's recent games) and says where the server is,
/// through the desktop's own notifications (notify-send).
/// </summary>
public static class SoberActivity
{
    /// <summary>Sober's log; BLOXCODE_SOBER_LOG replaces it for testing.</summary>
    public static string LogPath => Environment.GetEnvironmentVariable("BLOXCODE_SOBER_LOG") is { Length: > 0 } test
        ? test
        : Path.Combine(Paths.Home, ".var", "app", "org.vinegarhq.Sober", "data", "sober", "sober_logs", "latest.log");

    public static bool IsWanted => Store.Settings.RememberGames || Store.Settings.ShowServerLocation;

    /// <summary>
    /// Follows the log of the Sober started at <paramref name="launchedAt"/> until <paramref name="running"/>
    /// says it closed. Only one watcher at a time; a second launch leaves it to the first.
    /// </summary>
    public static async Task RunAsync(HttpClient http, DateTime launchedAt, Func<bool> running, Action<string, string> notify, CancellationToken ct)
    {
        const string ident = "SoberActivity::Run";
        using var gate = InstanceLock.TryAcquire("activity");
        if (gate is null)
        {
            Logger.Info(ident, "Another watcher already follows Sober");
            return;
        }

        // Sober writes latest.log afresh for each run; wait (up to a minute) until this run's is there.
        string path = LogPath;
        var deadline = DateTime.UtcNow.AddMinutes(1);
        while (!(File.Exists(path) && File.GetLastWriteTimeUtc(path) >= launchedAt.AddSeconds(-5)))
        {
            if (DateTime.UtcNow > deadline || !running())
            {
                Logger.Info(ident, $"No fresh Sober log at {path}");
                return;
            }
            await Task.Delay(1000, ct);
        }

        Logger.Info(ident, $"Following {path}");
        var parser = new ActivityParser();
        bool live = false;
        parser.Joined += session => { if (live) _ = OnJoinedAsync(http, session, notify, showNotice: true, ct); };
        parser.Left += session =>
        {
            Logger.Info(ident, $"Left place {session.PlaceId}");
            if (live && Store.Settings.RememberGames)
                GameHistory.Left(session);
        };

        try
        {
            await RobloxLog.FollowAsync(path, line =>
            {
                // One odd line must not end the watching for good.
                try { parser.Feed(line); }
                catch (Exception ex) { Logger.Warn(ident, $"Skipped a log line: {ex.Message}"); }
            }, running, launchedAt, ct, followNewer: false, onCaughtUp: () =>
            {
                live = true;
                if (parser.Current is { } current)
                    _ = OnJoinedAsync(http, current, notify, showNotice: false, ct);
            });
        }
        finally
        {
            if (Store.Settings.RememberGames)
                GameHistory.CloseDangling(DateTime.UtcNow);
            Logger.Info(ident, "Sober closed");
        }
    }

    private static async Task OnJoinedAsync(HttpClient http, GameSession session, Action<string, string> notify, bool showNotice, CancellationToken ct)
    {
        Logger.Info("SoberActivity::Joined", $"Joined place {session.PlaceId} ({session.Kind} server)");
        try
        {
            if (Store.Settings.RememberGames)
                GameHistory.Joined(session, await RobloxGames.GetAsync(http, session.PlaceId, session.UniverseId, ct));
            if (showNotice && Store.Settings.ShowServerLocation)
            {
                string? where = await ServerLocation.GetAsync(http, session.Address, ct);
                notify(session.Kind == ServerKind.Public ? "Connected to a server" : "Connected to a private server",
                    where is null ? "Location unknown" : $"Location: {where}");
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Logger.Warn("SoberActivity::Joined", ex.Message);
        }
    }

    /// <summary>A desktop notification (GNOME, KDE, Cinnamon… all take notify-send); silently nothing without it.</summary>
    public static void Notify(string title, string body)
    {
        _ = Proc.TryRunAsync("notify-send", new[] { "--app-name=Blox Code", "--icon=bloxcode", title, body }, TimeSpan.FromSeconds(5));
    }
}
