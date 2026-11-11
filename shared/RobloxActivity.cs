using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace BloxCode.Core;

public enum ServerKind { Public, Private, Reserved }

/// <summary>The game Roblox is connected to, as read from its log.</summary>
public sealed record GameSession(long PlaceId, string JobId, string Address, ServerKind Kind, long? UniverseId, DateTime JoinedAt);

/// <summary>
/// Follows the lines Roblox writes to its log and works out when a game is joined and left. The
/// lines it looks for are the ones Bloxstrap's activity watcher reads (FLog::Output, FLog::Network,
/// FLog::GameJoinUtil and FLog::SingleSurfaceApp), checked against real logs.
/// </summary>
public sealed partial class ActivityParser
{
    private long _placeId;
    private string _jobId = "";
    private string _address = "";
    private long? _universeId;
    private ServerKind _kind = ServerKind.Public;
    private bool _joining;

    public GameSession? Current { get; private set; }

    /// <summary>A game finished loading: the server is known and the player is in.</summary>
    public event Action<GameSession>? Joined;

    /// <summary>The player left the game, was disconnected or is teleporting elsewhere.</summary>
    public event Action<GameSession>? Left;

    // 2026-11-09T20:03:57.727Z,348.727173,1b98,6 [FLog::Output] ! Joining game '<job>' place 2753915549 at 1.2.3.4
    [GeneratedRegex(@"! Joining game '([0-9a-f\-]{36})' place ([0-9]+) at ([0-9\.]+)")]
    private static partial Regex JoiningRegex();

    // [FLog::Network] UDMUX Address = 1.2.3.4, Port = 53260 | RCC Server Address = 10.0.0.1, Port = 53260
    [GeneratedRegex(@"UDMUX Address = ([0-9\.]+), Port = [0-9]+")]
    private static partial Regex UdmuxRegex();

    // [FLog::Network] serverId: 1.2.3.4|53260
    [GeneratedRegex(@"\[FLog::Network\] serverId: ([0-9\.]+)\|[0-9]+")]
    private static partial Regex JoinedRegex();

    // [FLog::GameJoinLoadTime] Report game_join_loadtime: placeid:2753915549, join_time:1.29, universeid:994732206, …
    [GeneratedRegex(@"game_join_loadtime: placeid:([0-9]+),.*?universeid:([0-9]+)")]
    private static partial Regex UniverseRegex();

    public void Feed(string line)
    {
        // Numbers too big for a long can't be a real place or universe: such a line is ignored.
        if (line.Contains("! Joining game", StringComparison.Ordinal) && JoiningRegex().Match(line) is { Success: true } joining
            && long.TryParse(joining.Groups[2].Value, out long placeId))
        {
            // A new join (first game, or the far side of a teleport) closes whatever came before.
            LeaveCurrent();
            _jobId = joining.Groups[1].Value;
            _placeId = placeId;
            _address = joining.Groups[3].Value;
            _universeId = null;
            _joining = true;
            return;
        }

        if (!_joining)
        {
            if (Current is not null && IsLeaving(line))
                LeaveCurrent();
            else if (line.Contains("GameJoinUtil::joinGamePostPrivateServer", StringComparison.Ordinal))
                _kind = ServerKind.Private;
            else if (line.Contains("GameJoinUtil::initiateTeleportToReservedServer", StringComparison.Ordinal))
                _kind = ServerKind.Reserved;
            return;
        }

        if (line.Contains("game_join_loadtime", StringComparison.Ordinal) && UniverseRegex().Match(line) is { Success: true } universe
            && long.TryParse(universe.Groups[1].Value, out long loadedPlace) && loadedPlace == _placeId
            && long.TryParse(universe.Groups[2].Value, out long universeId))
        {
            _universeId = universeId;
        }
        else if (line.Contains("UDMUX Address", StringComparison.Ordinal) && UdmuxRegex().Match(line) is { Success: true } udmux)
        {
            // Behind Roblox's proxy the "at" address is internal; the UDMUX one is what players connect to.
            _address = udmux.Groups[1].Value;
        }
        else if (line.Contains("serverId:", StringComparison.Ordinal) && JoinedRegex().IsMatch(line))
        {
            _joining = false;
            Current = new GameSession(_placeId, _jobId, _address, _kind, _universeId, DateTime.UtcNow);
            _kind = ServerKind.Public;
            Joined?.Invoke(Current);
        }
        else if (IsLeaving(line))
        {
            // Gave up before the server answered.
            _joining = false;
            _kind = ServerKind.Public;
        }
    }

    private static bool IsLeaving(string line) =>
        line.Contains("[FLog::Network] Time to disconnect replication data", StringComparison.Ordinal)
        || line.Contains("[FLog::SingleSurfaceApp] initiateTeleport", StringComparison.Ordinal)
        || line.Contains("[FLog::SingleSurfaceApp] leaveUGCGameInternal", StringComparison.Ordinal);

    private void LeaveCurrent()
    {
        if (Current is not { } left)
            return;
        Current = null;
        Left?.Invoke(left);
    }
}

/// <summary>Finds the log of the Roblox player started at a given moment and hands each new line to a parser.</summary>
public static class RobloxLog
{
    /// <summary>Where Roblox writes its logs; BLOXCODE_ROBLOX_LOGS replaces it for testing.</summary>
    public static string Directory => Environment.GetEnvironmentVariable("BLOXCODE_ROBLOX_LOGS") is { Length: > 0 } test
        ? test
        : Path.Combine(Paths.LocalAppData, "Roblox", "logs");

    /// <summary>
    /// Player logs only. Roblox's crash handler writes "…_Player_XXXX_CrashHandler_last.log" next to
    /// each player's own log; it has none of the game lines.
    /// </summary>
    private static IEnumerable<FileInfo> PlayerLogFiles(string? directory)
    {
        var dir = new DirectoryInfo(directory ?? Directory);
        return dir.Exists
            ? dir.EnumerateFiles("*_Player_*.log").Where(f => !f.Name.Contains("CrashHandler", StringComparison.OrdinalIgnoreCase))
            : Enumerable.Empty<FileInfo>();
    }

    /// <summary>The newest player log created at or after <paramref name="since"/>, if there is one yet.</summary>
    public static string? NewestPlayerLog(DateTime since, string? directory = null) =>
        PlayerLogFiles(directory)
            .Where(f => f.CreationTimeUtc >= since.AddSeconds(-5))
            .OrderByDescending(f => f.CreationTimeUtc)
            .FirstOrDefault()?.FullName;

    /// <summary>
    /// The logs of the players that may be open now: created since <paramref name="since"/>, or still
    /// being written (an open window from before, or one in the tray), oldest first.
    /// </summary>
    public static List<string> PlayerLogs(DateTime since, string? directory = null) =>
        PlayerLogFiles(directory)
            .Where(f => f.CreationTimeUtc >= since.AddSeconds(-5) || DateTime.UtcNow - f.LastWriteTimeUtc < TimeSpan.FromMinutes(3))
            .OrderBy(f => f.CreationTimeUtc)
            .Select(f => f.FullName)
            .ToList();

    /// <summary>
    /// Reads <paramref name="path"/> from the start and keeps reading what Roblox appends, until
    /// <paramref name="keepGoing"/> says stop or (with <paramref name="followNewer"/>) a newer log replaces it (returns that log's path).
    /// <paramref name="onCaughtUp"/> runs once, when everything already written has been read.
    /// </summary>
    public static async Task<string?> FollowAsync(string path, Action<string> onLine, Func<bool> keepGoing, DateTime since, CancellationToken ct,
        string? directory = null, Action? onCaughtUp = null, bool followNewer = true)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        var nextCheck = DateTime.UtcNow;

        while (!ct.IsCancellationRequested)
        {
            string? line = await reader.ReadLineAsync(ct);
            if (line is not null)
            {
                onLine(line);
                continue;
            }

            if (onCaughtUp is not null)
            {
                onCaughtUp();
                onCaughtUp = null;
            }

            if (!keepGoing())
                return null;

            // Roblox restarted (a new player process writes a new log): follow that one instead.
            if (followNewer && DateTime.UtcNow >= nextCheck)
            {
                nextCheck = DateTime.UtcNow.AddSeconds(3);
                if (NewestPlayerLog(since, directory) is { } newest && !string.Equals(newest, path, StringComparison.OrdinalIgnoreCase))
                    return newest;
            }
            await Task.Delay(500, ct);
        }
        return null;
    }
}

/// <summary>Name, creator and icon of a Roblox experience, from Roblox's public web APIs.</summary>
public sealed record GameInfo(long UniverseId, string Name, string Creator, string? IconUrl);

public static class RobloxGames
{
    private static readonly Dictionary<long, GameInfo> Cache = new();

    public static async Task<GameInfo?> GetAsync(HttpClient http, long placeId, long? universeId, CancellationToken ct)
    {
        try
        {
            long universe = universeId ?? (await http.GetFromJsonAsync<UniverseReply>($"https://apis.roblox.com/universes/v1/places/{placeId}/universe", ct))?.UniverseId
                ?? throw new InvalidDataException("no universe for this place");
            lock (Cache)
                if (Cache.TryGetValue(universe, out var cached))
                    return cached;

            var games = await http.GetFromJsonAsync<DataReply<GameReply>>($"https://games.roblox.com/v1/games?universeIds={universe}", ct);
            if (games?.Data.FirstOrDefault() is not { } game)
                return null;

            string? icon = null;
            try
            {
                var icons = await http.GetFromJsonAsync<DataReply<IconReply>>(
                    $"https://thumbnails.roblox.com/v1/games/icons?universeIds={universe}&returnPolicy=PlaceHolder&size=512x512&format=Png&isCircular=false", ct);
                icon = icons?.Data.FirstOrDefault(i => i.State == "Completed")?.ImageUrl;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Logger.Warn("RobloxGames::Icon", ex.Message);
            }

            var info = new GameInfo(universe, game.Name, game.Creator?.Name ?? "", icon);
            lock (Cache)
                Cache[universe] = info;
            return info;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Logger.Warn("RobloxGames::Get", $"place {placeId}: {ex.Message}");
            return null;
        }
    }

    public static string GamePage(long placeId) => $"https://www.roblox.com/games/{placeId}";

    /// <summary>A web link that opens Roblox straight into this server (it works for friends, not just us).</summary>
    public static string JoinLink(GameSession session) =>
        $"https://www.roblox.com/games/start?placeId={session.PlaceId}&gameInstanceId={session.JobId}";

    private sealed record UniverseReply([property: JsonPropertyName("universeId")] long? UniverseId);
    private sealed record DataReply<T>([property: JsonPropertyName("data")] List<T> Data);
    private sealed record CreatorReply([property: JsonPropertyName("name")] string? Name);
    private sealed record GameReply([property: JsonPropertyName("name")] string Name, [property: JsonPropertyName("creator")] CreatorReply? Creator);
    private sealed record IconReply([property: JsonPropertyName("state")] string? State, [property: JsonPropertyName("imageUrl")] string? ImageUrl);
}

/// <summary>Roughly where a Roblox server is, from its public address (ipinfo.io, no account needed).</summary>
public static class ServerLocation
{
    private static readonly Dictionary<string, string?> Cache = new();

    public static async Task<string?> GetAsync(HttpClient http, string address, CancellationToken ct)
    {
        lock (Cache)
            if (Cache.TryGetValue(address, out var cached))
                return cached;
        try
        {
            var reply = await http.GetFromJsonAsync<IpInfoReply>($"https://ipinfo.io/{address}/json", ct);
            string? text = reply is null ? null : Describe(reply.City, reply.Region, reply.Country);
            lock (Cache)
                Cache[address] = text;
            return text;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Logger.Warn("ServerLocation::Get", ex.Message);
            return null;
        }
    }

    /// <summary>"São Paulo, BR", or "Frankfurt, Hesse, DE" when the region adds something.</summary>
    public static string? Describe(string? city, string? region, string? country)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(city))
            parts.Add(city.Trim());
        if (!string.IsNullOrWhiteSpace(region) && !string.Equals(region.Trim(), city?.Trim(), StringComparison.OrdinalIgnoreCase))
            parts.Add(region.Trim());
        if (!string.IsNullOrWhiteSpace(country))
            parts.Add(country.Trim());
        return parts.Count == 0 ? null : string.Join(", ", parts);
    }

    private sealed record IpInfoReply(
        [property: JsonPropertyName("city")] string? City,
        [property: JsonPropertyName("region")] string? Region,
        [property: JsonPropertyName("country")] string? Country);
}
