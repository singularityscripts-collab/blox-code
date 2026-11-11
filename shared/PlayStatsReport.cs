namespace BloxCode.Core;

/// <summary>Time in one game over a period.</summary>
public sealed record GameTime(long PlaceId, string Name, string? IconUrl, TimeSpan Time, int Sessions);

/// <summary>Everything the Stats page shows for a period.</summary>
public sealed record PlayReport(
    TimeSpan Total,
    int Sessions,
    int Games,
    TimeSpan Longest,
    int StreakDays,
    IReadOnlyList<GameTime> TopGames,
    IReadOnlyList<(DateTime Day, TimeSpan Time)> Days);

/// <summary>
/// Play time from the history (GameHistory), for the Stats page: totals, the games played most,
/// time per day (a visit that crosses midnight counts on both days) and the streak of days in a row.
/// Local time throughout, since "a day" is the player's day.
/// </summary>
public static class PlayStatsReport
{
    /// <param name="days">How many days back from <paramref name="now"/> (today included); null = everything.</param>
    public static PlayReport Build(IEnumerable<PlayedGame> history, int? days, DateTime now)
    {
        var all = history.ToList();
        DateTime today = now.ToLocalTime().Date;
        DateTime from = days is { } d ? today.AddDays(1 - d) : all.Count == 0 ? today : all.Min(g => g.JoinedAt.ToLocalTime().Date);
        DateTime to = now.ToLocalTime();

        // Each visit clipped to the period, in local time.
        var visits = all
            .Select(g => (Game: g, Start: Max(g.JoinedAt.ToLocalTime(), from), End: Min((g.LeftAt ?? now).ToLocalTime(), to)))
            .Where(v => v.End > v.Start)
            .ToList();

        var perDay = new SortedDictionary<DateTime, TimeSpan>();
        for (var day = from; day <= today; day = day.AddDays(1))
            perDay[day] = TimeSpan.Zero;
        foreach (var (_, start, end) in visits)
        {
            // Split at each midnight it crosses.
            for (var cursor = start; cursor < end;)
            {
                var midnight = cursor.Date.AddDays(1);
                var piece = (end < midnight ? end : midnight) - cursor;
                if (perDay.ContainsKey(cursor.Date))
                    perDay[cursor.Date] += piece;
                cursor = cursor.Date.AddDays(1);
            }
        }

        var top = visits
            .GroupBy(v => v.Game.PlaceId)
            .Select(g =>
            {
                var latest = g.OrderByDescending(v => v.Game.JoinedAt).First().Game;
                return new GameTime(g.Key, latest.Name, latest.IconUrl, g.Aggregate(TimeSpan.Zero, (sum, v) => sum + (v.End - v.Start)), g.Count());
            })
            .OrderByDescending(g => g.Time)
            .ToList();

        return new PlayReport(
            Total: visits.Aggregate(TimeSpan.Zero, (sum, v) => sum + (v.End - v.Start)),
            Sessions: visits.Count,
            Games: top.Count,
            Longest: visits.Count == 0 ? TimeSpan.Zero : visits.Max(v => v.End - v.Start),
            StreakDays: Streak(all, today, now),
            TopGames: top,
            Days: perDay.Select(kv => (kv.Key, kv.Value)).ToList());
    }

    /// <summary>Days in a row with some play, counting back from today (or from yesterday, if today has none yet).</summary>
    public static int Streak(IEnumerable<PlayedGame> history, DateTime today, DateTime now)
    {
        var played = new HashSet<DateTime>();
        foreach (var g in history)
        {
            var start = g.JoinedAt.ToLocalTime();
            var end = (g.LeftAt ?? now).ToLocalTime();
            for (var day = start.Date; day <= end.Date; day = day.AddDays(1))
                played.Add(day);
        }
        var cursor = played.Contains(today) ? today : today.AddDays(-1);
        int streak = 0;
        while (played.Contains(cursor))
        {
            streak++;
            cursor = cursor.AddDays(-1);
        }
        return streak;
    }

    private static DateTime Max(DateTime a, DateTime b) => a > b ? a : b;
    private static DateTime Min(DateTime a, DateTime b) => a < b ? a : b;
}
