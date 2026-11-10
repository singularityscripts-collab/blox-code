using System.Text.RegularExpressions;

namespace BloxCode.Core;

public static partial class RobloxLink
{
    [GeneratedRegex(@"roblox\.com/(?:[a-z]{2}/)?games/(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex GameUrl();

    [GeneratedRegex(@"^\d+$")]
    private static partial Regex PlaceId();

    public static bool IsStudioUri(string arg) =>
        arg.StartsWith("roblox-studio:", StringComparison.OrdinalIgnoreCase) || arg.StartsWith("roblox-studio-auth:", StringComparison.OrdinalIgnoreCase);

    public static bool IsRobloxUri(string arg) =>
        arg.StartsWith("roblox:", StringComparison.OrdinalIgnoreCase) || arg.StartsWith("roblox-player:", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Turns what the user typed in the composer (a game URL, a place ID or a roblox URI) into
    /// launch arguments: "" for none, null when it isn't recognised.
    /// </summary>
    public static string? ToLaunchArgs(string input)
    {
        input = input.Trim();
        if (input.Length == 0)
            return "";
        if (IsRobloxUri(input))
            return input;

        var match = GameUrl().Match(input);
        string? placeId = match.Success ? match.Groups[1].Value : PlaceId().IsMatch(input) ? input : null;
        return placeId is null ? null : $"roblox://experiences/start?placeId={placeId}";
    }
}
