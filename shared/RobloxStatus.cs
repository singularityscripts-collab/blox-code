using System.Text.Json;

namespace BloxCode.Core;

/// <summary>What Roblox's status page says right now.</summary>
public sealed record RobloxStatusReport(bool AllGood, string Summary, IReadOnlyList<string> Problems);

/// <summary>
/// Roblox's public status page (status.roblox.com, hosted on status.io). Asked only when a launch
/// fails, to say "Roblox itself is having problems" instead of leaving the user to guess.
/// </summary>
public static class RobloxStatus
{
    private const string Api = "https://api.status.io/1.0/status/59db90dbcdeb2f04dadcf16d";

    /// <summary>The current status, or null if the page couldn't be reached within a few seconds.</summary>
    public static async Task<RobloxStatusReport?> GetAsync(HttpClient http, CancellationToken ct)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            string json = await http.GetStringAsync(Api, timeout.Token);
            return Parse(json);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            Logger.Warn("RobloxStatus::Get", ex.Message);
            return null;
        }
    }

    /// <summary>Reads status.io's reply: the overall status, each part that isn't operational, and open incidents.</summary>
    public static RobloxStatusReport Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var result = doc.RootElement.GetProperty("result");
        var overall = result.GetProperty("status_overall");
        int code = overall.GetProperty("status_code").GetInt32();
        string summary = overall.GetProperty("status").GetString() ?? "Unknown";

        var problems = new List<string>();
        if (result.TryGetProperty("incidents", out var incidents) && incidents.ValueKind == JsonValueKind.Array)
            foreach (var incident in incidents.EnumerateArray())
                if (incident.TryGetProperty("name", out var name) && name.GetString() is { Length: > 0 } text)
                    problems.Add(text);
        if (result.TryGetProperty("status", out var parts) && parts.ValueKind == JsonValueKind.Array)
            foreach (var part in parts.EnumerateArray())
                if (part.TryGetProperty("status_code", out var partCode) && partCode.GetInt32() > 100
                    && part.TryGetProperty("name", out var name) && part.TryGetProperty("status", out var partStatus))
                    problems.Add($"{name.GetString()}: {partStatus.GetString()}");

        // status.io codes: 100 operational, 200 planned maintenance, 300 degraded, 400 partial outage, 500 outage, 600 security event.
        return new RobloxStatusReport(code <= 100 && problems.Count == 0, summary, problems);
    }

    /// <summary>One line for a failure message, or null when Roblox reports nothing wrong.</summary>
    public static string? Explain(RobloxStatusReport? report) =>
        report is null || report.AllGood
            ? null
            : $"Roblox's status page reports a problem right now ({report.Summary}"
              + (report.Problems.Count > 0 ? ": " + string.Join("; ", report.Problems.Take(2)) : "") + "), so this may not be on your side.";
}
