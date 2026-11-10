namespace BloxCode.Core;

/// <summary>What the launch menu asked for, like Bloxstrap's NextAction (Studio aside).</summary>
public enum LaunchMenuChoice { None, LaunchRoblox, LaunchStudio, Configure }

/// <summary>Text for the launch menu shown when Blox Code is opened without arguments.</summary>
public static class LaunchMenu
{
    /// <summary>One line describing what "Launch Roblox" will use: graphics, mods and flags.</summary>
    public static string Summary()
    {
        var preset = FastFlagPresets.CurrentGraphicsPreset;
        var parts = new List<string> { "Graphics: " + (preset?.ToString() ?? "custom") };

        int mods = CountMods();
        if (mods > 0)
            parts.Add(mods == 1 ? "1 mod" : $"{mods} mods");

        int flags = Store.Settings.FastFlags.Count;
        if (flags > 0)
            parts.Add(flags == 1 ? "1 Fast Flag" : $"{flags} Fast Flags");

        return string.Join(" · ", parts);
    }

    private static int CountMods()
    {
        try
        {
            if (!Directory.Exists(Paths.Modifications))
                return 0;
            var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true };
            return Directory.EnumerateFiles(Paths.Modifications, "*", options)
                .Count(f => !Path.GetFileName(f).Equals("README.txt", StringComparison.OrdinalIgnoreCase)
                            && !f.EndsWith(".bloxcode-tmp", StringComparison.Ordinal));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }
}
