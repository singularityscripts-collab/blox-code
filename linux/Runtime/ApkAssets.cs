using System.IO.Compression;
using BloxCode.Core;

namespace BloxCode.Linux.Runtime;

public sealed record AssetEntry(string Path, long Size)
{
    public string Folder => Path.Contains('/') ? Path[..Path.LastIndexOf('/')] : "";
    public string Name => Path[(Path.LastIndexOf('/') + 1)..];
}

/// <summary>
/// Lists the media Roblox ships in the APK that Sober downloads (a zip). Only names and asset
/// files under assets/ are read, never code; this is what the mod manager offers to replace.
/// </summary>
public static class ApkAssets
{
    private const string Prefix = "assets/";

    /// <summary>The newest com.roblox.client base.apk under Sober's packages folder.</summary>
    public static string? FindApk(string? packagesDir = null)
    {
        packagesDir ??= Sober.PackagesDir;
        if (!Directory.Exists(packagesDir))
            return null;

        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            MaxRecursionDepth = 4,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint,
        };
        return Directory.EnumerateFiles(packagesDir, "base.apk", options)
            .Where(p => p.Replace('\\', '/').Contains("com.roblox.client", StringComparison.Ordinal))
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }

    public static List<AssetEntry> List(string apk)
    {
        using var zip = ZipFile.OpenRead(apk);
        return zip.Entries
            .Where(e => e.FullName.StartsWith(Prefix, StringComparison.Ordinal) && !e.FullName.EndsWith('/'))
            .Select(e => new AssetEntry(e.FullName[Prefix.Length..], e.Length))
            .Where(e => ModSync.IsSafe(e.Path))
            .OrderBy(e => e.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Copies one original asset out of the APK (to start a mod from, or to look at).</summary>
    public static void Extract(string apk, string assetPath, string destination)
    {
        if (!ModSync.IsSafe(assetPath))
            throw new ArgumentException("Not an asset path.", nameof(assetPath));
        using var zip = ZipFile.OpenRead(apk);
        var entry = zip.GetEntry(Prefix + assetPath) ?? throw new FileNotFoundException($"{assetPath} isn't in Roblox's files.");
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        string temp = destination + ".bloxcode-tmp";
        entry.ExtractToFile(temp, overwrite: true);
        File.Move(temp, destination, overwrite: true);
        Logger.Info("ApkAssets::Extract", $"{assetPath} -> {destination}");
    }

    /// <summary>Roblox's own font family files (name, JSON), for the custom game font.</summary>
    public static List<(string Name, string Json)> FontFamilies(string apk)
    {
        const string folder = Prefix + ModCatalog.FamiliesFolder + "/";
        using var zip = ZipFile.OpenRead(apk);
        var families = new List<(string, string)>();
        foreach (var entry in zip.Entries.Where(e => e.FullName.StartsWith(folder, StringComparison.Ordinal) && e.FullName.EndsWith(".json", StringComparison.OrdinalIgnoreCase)))
        {
            using var reader = new StreamReader(entry.Open());
            families.Add((entry.Name, reader.ReadToEnd()));
        }
        return families;
    }

    /// <summary>Rewrites the font families for the custom game font (or drops them when there's none).</summary>
    public static void RefreshCustomFont()
    {
        try
        {
            // No custom font: only the rewritten families (if any) need removing; the APK isn't opened.
            if (ModCatalog.CurrentFile(ModCatalog.Font) is null)
            {
                ModCatalog.RebuildFontFamilies(Array.Empty<(string, string)>());
                return;
            }
            var families = FindApk() is { } apk ? FontFamilies(apk) : new List<(string, string)>();
            // Without the APK there's nothing to rewrite from; keep what an earlier refresh made.
            if (families.Count > 0)
                ModCatalog.RebuildFontFamilies(families);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            Logger.Warn("ApkAssets::RefreshCustomFont", ex.Message);
        }
    }

    /// <summary>Quick filters over the asset list, for the chips above it.</summary>
    public static readonly (string Label, Func<AssetEntry, bool> Match)[] Categories =
    {
        ("All", _ => true),
        ("Cursors", e => e.Path.Contains("Cursors", StringComparison.OrdinalIgnoreCase)),
        ("Sounds", e => e.Path.StartsWith("content/sounds/", StringComparison.OrdinalIgnoreCase) || HasExtension(e, ".ogg", ".mp3", ".wav")),
        ("Fonts", e => HasExtension(e, ".ttf", ".otf", ".json") && e.Path.Contains("fonts", StringComparison.OrdinalIgnoreCase)),
        ("Textures", e => e.Path.Contains("textures", StringComparison.OrdinalIgnoreCase) && HasExtension(e, ".png", ".jpg", ".dds", ".ktx")),
    };

    private static bool HasExtension(AssetEntry e, params string[] extensions) =>
        extensions.Any(x => e.Path.EndsWith(x, StringComparison.OrdinalIgnoreCase));
}
