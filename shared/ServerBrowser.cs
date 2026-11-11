using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace BloxCode.Core;

/// <summary>One public server of a game, as Roblox lists it.</summary>
public sealed record ServerInfo(string Id, int Playing, int MaxPlayers, double Fps, int? Ping);

/// <summary>A page of servers and where the next one starts.</summary>
public sealed record ServerPage(List<ServerInfo> Servers, string? NextCursor);

public enum ServerSort { MostPlayers, FewestPlayers }

/// <summary>
/// The public servers of a game, from Roblox's own public API (no account needed), so a server can
/// be picked before joining: by players, by how well it runs (its frame rate) and its ping. Roblox
/// doesn't say where a server is until you join it; Blox Code tells you then.
/// </summary>
public static class ServerBrowser
{
    public static async Task<ServerPage> GetAsync(HttpClient http, long placeId, ServerSort sort, string? cursor, CancellationToken ct)
    {
        string order = sort == ServerSort.MostPlayers ? "Desc" : "Asc";
        string url = $"https://games.roblox.com/v1/games/{placeId}/servers/Public?sortOrder={order}&excludeFullGames=true&limit=100"
                     + (cursor is { Length: > 0 } ? "&cursor=" + Uri.EscapeDataString(cursor) : "");
        var reply = await http.GetFromJsonAsync<Reply>(url, ct) ?? throw new InvalidDataException("Roblox sent no server list");
        var servers = (reply.Data ?? new())
            .Where(s => s.Id is { Length: > 0 } && s.MaxPlayers > 0)
            .Select(s => new ServerInfo(s.Id!, s.Playing, s.MaxPlayers, s.Fps ?? 0, s.Ping))
            .ToList();
        return new ServerPage(servers, reply.NextPageCursor);
    }

    /// <summary>The launch link that joins exactly this server.</summary>
    public static string JoinArgs(long placeId, ServerInfo server) =>
        $"roblox://experiences/start?placeId={placeId}&gameInstanceId={server.Id}";

    private sealed record Reply(
        [property: JsonPropertyName("nextPageCursor")] string? NextPageCursor,
        [property: JsonPropertyName("data")] List<ServerReply>? Data);

    private sealed record ServerReply(
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("playing")] int Playing,
        [property: JsonPropertyName("maxPlayers")] int MaxPlayers,
        [property: JsonPropertyName("fps")] double? Fps,
        [property: JsonPropertyName("ping")] int? Ping);
}
