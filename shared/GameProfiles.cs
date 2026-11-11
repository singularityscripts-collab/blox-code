using System.Text.RegularExpressions;

namespace BloxCode.Core;

/// <summary>
/// Graphics per game: a game can always start with one graphics preset or one saved Fast Flag
/// profile, whatever the flags are otherwise. Only that launch gets them; the flags you have set
/// stay as they are. Settings.GameProfiles maps a place ID to "preset:Low" or "profile:&lt;name&gt;".
/// </summary>
public static partial class GameProfiles
{
    public const string PresetPrefix = "preset:";
    public const string ProfilePrefix = "profile:";

    // roblox://experiences/start?placeId=123, and the browser's roblox-player: links, where it is
    // URL-encoded inside placelauncherurl (…%26placeId%3D123).
    [GeneratedRegex(@"place[Ii]d(?:=|%3[Dd])(\d{1,19})")]
    private static partial Regex PlaceIdRegex();

    /// <summary>The place a launch link opens, if it names one.</summary>
    public static long? PlaceIdOf(string robloxArgs) =>
        PlaceIdRegex().Match(robloxArgs) is { Success: true } m && long.TryParse(m.Groups[1].Value, out long id) ? id : null;

    /// <summary>The choice saved for a game: "preset:Low", "profile:Max FPS", or null.</summary>
    public static string? ChoiceFor(long placeId) =>
        Store.Settings.GameProfiles.TryGetValue(placeId, out var choice) ? choice : null;

    /// <summary>Saves (or with null clears) a game's choice.</summary>
    public static void Set(long placeId, string? choice)
    {
        if (choice is null)
        {
            // A preset that pinned the quality left Roblox remembering that level for this game.
            if (Store.Settings.GameProfiles.Remove(placeId) && !Paths.IsXdg)
            {
                Store.State.ResetQualityCache = true;
                Store.SaveState();
            }
        }
        else
        {
            Store.Settings.GameProfiles[placeId] = choice;
        }
        Store.SaveSettings();
    }

    /// <summary>"Low preset", "Max FPS profile"; null when nothing is set.</summary>
    public static string? Describe(string? choice) => choice switch
    {
        null => null,
        _ when choice.StartsWith(PresetPrefix, StringComparison.Ordinal) => $"{choice[PresetPrefix.Length..]} preset",
        _ when choice.StartsWith(ProfilePrefix, StringComparison.Ordinal) => $"{choice[ProfilePrefix.Length..]} profile",
        _ => null,
    };

    /// <summary>
    /// The Fast Flags a launch should write: the game's own choice when its link names a game that
    /// has one (and the profile still exists), otherwise the current flags.
    /// </summary>
    public static Dictionary<string, string> FlagsForLaunch(string robloxArgs, out string? used)
    {
        used = null;
        if (PlaceIdOf(robloxArgs) is not { } placeId || ChoiceFor(placeId) is not { } choice)
            return Store.Settings.FastFlags;

        if (choice.StartsWith(PresetPrefix, StringComparison.Ordinal)
            && Enum.TryParse<GraphicsPreset>(choice[PresetPrefix.Length..], out var preset))
        {
            used = Describe(choice);
            return FastFlagPresets.WithGraphicsPreset(Store.Settings.FastFlags, preset);
        }
        if (choice.StartsWith(ProfilePrefix, StringComparison.Ordinal)
            && Store.Settings.FastFlagProfiles.TryGetValue(choice[ProfilePrefix.Length..], out var profile))
        {
            used = Describe(choice);
            return new Dictionary<string, string>(profile);
        }
        return Store.Settings.FastFlags;   // a profile that was deleted since
    }
}
