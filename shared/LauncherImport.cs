using System.Text.Json;

namespace BloxCode.Core;

/// <summary>What another launcher has that Blox Code can bring over.</summary>
public sealed record ImportSource(string Name, string Folder, int Flags, int Mods, bool HasSettings);

/// <summary>What an import brought over.</summary>
public sealed record ImportResult(string Source, int Flags, int SkippedFlags, int Mods, int Settings);

/// <summary>
/// Brings Fast Flags, mods and a few preferences over from Bloxstrap and its forks (Fishstrap,
/// Voidstrap, Froststrap), which all keep the same layout under %LocalAppData%\&lt;Name&gt;:
/// Settings.json, and Modifications\ with the Fast Flags in ClientSettings\ClientAppSettings.json.
/// Nothing in the other launcher's folder is changed.
/// </summary>
public static class LauncherImport
{
    public static readonly string[] Launchers = { "Bloxstrap", "Fishstrap", "Voidstrap", "Froststrap" };

    private static readonly string FlagsFile = Path.Combine("ClientSettings", "ClientAppSettings.json");

    /// <summary>The launchers found under <paramref name="root"/> (default %LocalAppData%) that have something to import.</summary>
    public static List<ImportSource> Find(string? root = null)
    {
        var found = new List<ImportSource>();
        foreach (string name in Launchers)
        {
            string folder = Path.Combine(root ?? Paths.LocalAppData, name);
            if (!Directory.Exists(folder))
                continue;
            int flags = ReadFlags(folder).Valid.Count;
            int mods = ModFiles(folder).Count();
            bool settings = File.Exists(Path.Combine(folder, "Settings.json"));
            if (flags > 0 || mods > 0 || settings)
                found.Add(new ImportSource(name, folder, flags, mods, settings));
        }
        return found;
    }

    /// <summary>
    /// Imports one launcher. Its Fast Flags become a profile named after it, and the current flags
    /// too when there are none yet; mod files are copied where Blox Code has no file of its own;
    /// Discord, server location and launch confirmation follow the other launcher's choices.
    /// </summary>
    public static ImportResult Import(ImportSource source)
    {
        var (flags, skipped) = ReadFlags(source.Folder);
        if (flags.Count > 0)
        {
            Store.Settings.FastFlagProfiles[source.Name] = new(flags);
            if (Store.Settings.FastFlags.Count == 0)
                Store.Settings.FastFlags = new(flags);
        }

        int mods = 0;
        string modsRoot = Path.Combine(source.Folder, "Modifications");
        foreach (string file in ModFiles(source.Folder))
        {
            string relative = Path.GetRelativePath(modsRoot, file);
            string target = Path.GetFullPath(Path.Combine(Paths.Modifications, relative));
            if (!target.StartsWith(Path.GetFullPath(Paths.Modifications), StringComparison.OrdinalIgnoreCase) || File.Exists(target))
                continue;
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
            mods++;
        }

        int settings = ImportSettings(Path.Combine(source.Folder, "Settings.json"));
        Store.SaveSettings();
        Logger.Info("LauncherImport::Import", $"{source.Name}: {flags.Count} flags ({skipped} skipped), {mods} mod files, {settings} settings");
        return new ImportResult(source.Name, flags.Count, skipped, mods, settings);
    }

    private static (Dictionary<string, string> Valid, int Skipped) ReadFlags(string folder)
    {
        string file = Path.Combine(folder, "Modifications", FlagsFile);
        try
        {
            return File.Exists(file)
                ? FastFlagValidator.ValidOnly(FastFlagValidator.ParseJson(File.ReadAllText(file)))
                : (new(), 0);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            Logger.Warn("LauncherImport::ReadFlags", $"{file}: {ex.Message}");
            return (new(), 0);
        }
    }

    /// <summary>The other launcher's mod files, minus its Fast Flags file (imported as flags instead).</summary>
    private static IEnumerable<string> ModFiles(string folder)
    {
        string root = Path.Combine(folder, "Modifications");
        if (!Directory.Exists(root))
            return Enumerable.Empty<string>();
        string flags = Path.Combine(root, FlagsFile);
        try
        {
            return Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                .Where(f => !f.Equals(flags, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Enumerable.Empty<string>();
        }
    }

    /// <summary>The preferences both apps share; returns how many were read.</summary>
    private static int ImportSettings(string file)
    {
        if (!File.Exists(file))
            return 0;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(file));
            var root = doc.RootElement;
            int count = 0;
            bool? Bool(string key) => root.TryGetProperty(key, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False ? v.GetBoolean() : null;

            if (Bool("UseDiscordRichPresence") is { } discord) { Store.Settings.DiscordRichPresence = discord; count++; }
            if (Bool("ShowServerDetails") is { } location) { Store.Settings.ShowServerLocation = location; count++; }
            if (Bool("ConfirmLaunches") is { } confirm) { Store.Settings.ConfirmLaunches = confirm; count++; }
            return count;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            Logger.Warn("LauncherImport::Settings", $"{file}: {ex.Message}");
            return 0;
        }
    }
}
