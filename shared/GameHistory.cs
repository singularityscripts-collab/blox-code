using System.Text.Json;
using System.Text.Json.Serialization;

namespace BloxCode.Core;

/// <summary>One visit to a game: what it was, which server, and for how long.</summary>
public sealed class PlayedGame
{
    public long PlaceId { get; set; }
    public long? UniverseId { get; set; }
    public string? JobId { get; set; }
    public string Name { get; set; } = "";
    public string? Creator { get; set; }
    public string? IconUrl { get; set; }
    public ServerKind Kind { get; set; }
    public DateTime JoinedAt { get; set; }
    public DateTime? LeftAt { get; set; }

    [JsonIgnore]
    public TimeSpan Played => ((LeftAt ?? DateTime.UtcNow) - JoinedAt) is { Ticks: > 0 } t ? t : TimeSpan.Zero;
}

public sealed record PlayStats(TimeSpan Total, int Sessions, string? TopGame, TimeSpan TopGameTime);

/// <summary>
/// The games played through Blox Code, newest first, in History.json next to State.json. The
/// background watcher (its own process) writes it while Roblox runs; the menu only reads it.
/// </summary>
public static class GameHistory
{
    public const int MaxEntries = 400;

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    // Several Blox Code processes (menu, watcher) may touch the file; a named mutex keeps writes whole.
    private const string LockName = "BloxCode-History";

    public static string FilePath => Path.Combine(Paths.StateDir, "History.json");

    public static List<PlayedGame> Load()
    {
        try
        {
            return File.Exists(FilePath)
                ? JsonSerializer.Deserialize<List<PlayedGame>>(File.ReadAllText(FilePath), Options) ?? new()
                : new();
        }
        catch (Exception ex)
        {
            Logger.Warn("GameHistory::Load", ex.Message);
            return new();
        }
    }

    /// <summary>Records a join; returns the entry so the leave can close it.</summary>
    public static void Joined(GameSession session, GameInfo? game) => Update(list =>
    {
        list.Insert(0, new PlayedGame
        {
            PlaceId = session.PlaceId,
            UniverseId = game?.UniverseId ?? session.UniverseId,
            JobId = session.JobId,
            Name = game?.Name is { Length: > 0 } name ? name : $"Place {session.PlaceId}",
            Creator = game?.Creator,
            IconUrl = game?.IconUrl,
            Kind = session.Kind,
            JoinedAt = session.JoinedAt,
        });
        if (list.Count > MaxEntries)
            list.RemoveRange(MaxEntries, list.Count - MaxEntries);
    });

    public static void Left(GameSession session, DateTime? at = null) => Update(list =>
    {
        var entry = list.FirstOrDefault(g => g.JobId == session.JobId && g.PlaceId == session.PlaceId && g.LeftAt is null);
        if (entry is not null)
            entry.LeftAt = at ?? DateTime.UtcNow;
    });

    /// <summary>Visits that never got a leave (Roblox crashed, PC turned off) end when they were last seen.</summary>
    public static void CloseDangling(DateTime at) => Update(list =>
    {
        foreach (var entry in list.Where(g => g.LeftAt is null))
            entry.LeftAt = at > entry.JoinedAt ? at : entry.JoinedAt;
    });

    public static void Forget(long placeId) => Update(list => list.RemoveAll(g => g.PlaceId == placeId));

    public static void Clear() => Update(list => list.Clear());

    /// <summary>The latest visit of each game, newest first.</summary>
    public static List<PlayedGame> Recent(int count, List<PlayedGame>? history = null) =>
        (history ?? Load()).GroupBy(g => g.PlaceId).Select(g => g.First()).Take(count).ToList();

    public static PlayStats Stats(TimeSpan window, List<PlayedGame>? history = null)
    {
        var since = DateTime.UtcNow - window;
        var visits = (history ?? Load()).Where(g => (g.LeftAt ?? DateTime.UtcNow) > since).ToList();
        TimeSpan Within(PlayedGame g) => (g.LeftAt ?? DateTime.UtcNow) - (g.JoinedAt > since ? g.JoinedAt : since) is { Ticks: > 0 } t ? t : TimeSpan.Zero;

        var top = visits.GroupBy(g => g.PlaceId)
            .Select(g => (Name: g.First().Name, Time: g.Aggregate(TimeSpan.Zero, (sum, v) => sum + Within(v))))
            .OrderByDescending(g => g.Time)
            .FirstOrDefault();
        return new PlayStats(visits.Aggregate(TimeSpan.Zero, (sum, v) => sum + Within(v)), visits.Count, top.Name, top.Time);
    }

    /// <summary>
    /// A game name without the emoji and seasonal tags games put in their titles ("[🎃] Blox Fruits"
    /// becomes "Blox Fruits"), for places whose fonts can't draw emoji.
    /// </summary>
    public static string PlainName(string name)
    {
        var text = new System.Text.StringBuilder();
        foreach (var rune in name.EnumerateRunes())
        {
            var category = System.Text.Rune.GetUnicodeCategory(rune);
            bool symbol = rune.Value >= 0x1F000 || category is System.Globalization.UnicodeCategory.OtherSymbol
                || category is System.Globalization.UnicodeCategory.Surrogate || rune.Value is 0xFE0F or 0x200D;
            if (!symbol)
                text.Append(rune.ToString());
        }
        string plain = System.Text.RegularExpressions.Regex.Replace(text.ToString(), @"[\[\(\{]\s*[\]\)\}]", "");
        plain = System.Text.RegularExpressions.Regex.Replace(plain, @"\s{2,}", " ").Trim();
        return plain.Length > 0 ? plain : name;
    }

    /// <summary>"3h 05m", "42m", "under a minute".</summary>
    public static string Duration(TimeSpan time) =>
        time.TotalHours >= 1 ? $"{(int)time.TotalHours}h {time.Minutes:00}m"
        : time.TotalMinutes >= 1 ? $"{(int)time.TotalMinutes}m"
        : "under a minute";

    /// <summary>"just now", "25m ago", "3h ago", "yesterday", "4 days ago".</summary>
    public static string Ago(DateTime utc)
    {
        var age = DateTime.UtcNow - utc;
        return age.TotalMinutes < 2 ? "just now"
            : age.TotalHours < 1 ? $"{(int)age.TotalMinutes}m ago"
            : age.TotalDays < 1 ? $"{(int)age.TotalHours}h ago"
            : age.TotalDays < 2 ? "yesterday"
            : $"{(int)age.TotalDays} days ago";
    }

    private static void Update(Action<List<PlayedGame>> change)
    {
        using var gate = new Mutex(false, LockName);
        bool owned = false;
        try
        {
            try { owned = gate.WaitOne(TimeSpan.FromSeconds(5)); }
            catch (AbandonedMutexException) { owned = true; }

            var list = Load();
            change(list);
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            string temp = FilePath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(list, Options));
            File.Move(temp, FilePath, overwrite: true);
        }
        catch (Exception ex)
        {
            Logger.Warn("GameHistory::Update", ex.Message);
        }
        finally
        {
            if (owned)
                gate.ReleaseMutex();
        }
    }
}
