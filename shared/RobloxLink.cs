using System.Text.RegularExpressions;

namespace BloxCode.Core;

public static partial class RobloxLink
{
    // roblox.com/games/123/Name, with an optional language segment (roblox.com/pt/games/…, /pt-br/).
    [GeneratedRegex(@"roblox\.com/(?:[a-z]{2}(?:-[a-z]{2})?/)?games/(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex GameUrl();

    // roblox.com/games/start?placeId=123&gameInstanceId=… (the "Join server" link Blox Code puts on Discord).
    [GeneratedRegex(@"roblox\.com/(?:[a-z]{2}(?:-[a-z]{2})?/)?games/start\?", RegexOptions.IgnoreCase)]
    private static partial Regex StartUrl();

    // roblox.com/share?code=…&type=Server (the site's "Share" button).
    [GeneratedRegex(@"roblox\.com/(?:[a-z]{2}(?:-[a-z]{2})?/)?share\?", RegexOptions.IgnoreCase)]
    private static partial Regex ShareUrl();

    [GeneratedRegex(@"^\d+$")]
    private static partial Regex PlaceId();

    [GeneratedRegex(@"^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$")]
    private static partial Regex JobId();

    [GeneratedRegex(@"^[A-Za-z0-9_-]{1,200}$")]
    private static partial Regex Code();

    public static bool IsStudioUri(string arg) =>
        arg.StartsWith("roblox-studio:", StringComparison.OrdinalIgnoreCase) || arg.StartsWith("roblox-studio-auth:", StringComparison.OrdinalIgnoreCase);

    public static bool IsRobloxUri(string arg) =>
        arg.StartsWith("roblox:", StringComparison.OrdinalIgnoreCase) || arg.StartsWith("roblox-player:", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Turns what the user typed in the composer (a game URL, a place ID or a roblox URI) into
    /// launch arguments: "" for none, null when it isn't recognised. A private server's link code,
    /// a specific server and the site's share links are kept, so the player lands where the link says.
    /// </summary>
    public static string? ToLaunchArgs(string input)
    {
        input = input.Trim();
        if (input.Length == 0)
            return "";
        if (IsRobloxUri(input))
            return input;

        if (ShareUrl().IsMatch(input))
        {
            return Query(input, "code") is { } code && Code().IsMatch(code) && Query(input, "type") is { } type && Code().IsMatch(type)
                ? $"roblox://navigation/share_links?code={code}&type={type}"
                : null;
        }

        string? placeId;
        if (StartUrl().IsMatch(input))
            placeId = Query(input, "placeId");
        else if (GameUrl().Match(input) is { Success: true } game)
            placeId = game.Groups[1].Value;
        else
            placeId = input;
        if (placeId is null || !PlaceId().IsMatch(placeId))
            return null;

        string args = $"roblox://experiences/start?placeId={placeId}";
        if (Query(input, "gameInstanceId") is { } job && JobId().IsMatch(job))
            args += $"&gameInstanceId={job}";
        if ((Query(input, "privateServerLinkCode") ?? Query(input, "linkCode")) is { } linkCode && Code().IsMatch(linkCode))
            args += $"&linkCode={linkCode}";
        return args;
    }

    /// <summary>A query-string value of a pasted link (case-insensitive name), unescaped; null when absent.</summary>
    private static string? Query(string url, string name)
    {
        int start = url.IndexOf('?');
        if (start < 0)
            return null;
        int end = url.IndexOf('#', start);
        string query = end < 0 ? url[(start + 1)..] : url[(start + 1)..end];
        foreach (string pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            int eq = pair.IndexOf('=');
            if (eq > 0 && pair[..eq].Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                try { return Uri.UnescapeDataString(pair[(eq + 1)..]); }
                catch (UriFormatException) { return null; }
            }
        }
        return null;
    }
}
