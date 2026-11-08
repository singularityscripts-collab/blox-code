using System.Text.Json;
using System.Text.Json.Nodes;

namespace BloxCode.Core;

public enum ModKind { Image, Sound, Font }

/// <summary>
/// One thing a player commonly swaps (the cursor, the death sound…): the Roblox files it replaces,
/// as paths relative to the Modifications folder. They're the same on Windows (the version folder)
/// and in the Android package Sober runs (its assets/ folder).
/// </summary>
public sealed record ModSlot(string Id, string Group, string Title, string Description, ModKind Kind, params string[] Targets)
{
    public string[] Extensions => Kind switch
    {
        ModKind.Image => new[] { ".png" },
        ModKind.Sound => new[] { ".ogg", ".mp3", ".wav" },
        _ => new[] { ".ttf", ".otf" },
    };

    public string FileTypes => Kind switch
    {
        ModKind.Image => "PNG image",
        ModKind.Sound => "OGG, MP3 or WAV",
        _ => "TTF or OTF font",
    };
}

/// <summary>
/// Pick-a-file mods: each slot copies the chosen file into the Modifications folder at the right
/// place, so it rides along with the regular mod sync (and its backups) on both systems. The game
/// font works like Bloxstrap's custom font: the file becomes content/fonts/CustomFont.ttf and every
/// font family Roblox ships is rewritten to point at it.
/// </summary>
public static class ModCatalog
{
    public const string CustomFont = "content/fonts/CustomFont.ttf";
    public const string FamiliesFolder = "content/fonts/families";
    private const string CustomFontAsset = "rbxasset://fonts/CustomFont.ttf";
    private const string Cursors = "content/textures/Cursors/KeyboardMouse/";

    public static readonly ModSlot[] Slots =
    {
        new("cursor", "Cursor", "Mouse cursor", "The arrow you aim and click with. A 64 × 64 PNG with a transparent background works best.",
            ModKind.Image, Cursors + "ArrowCursor.png", Cursors + "ArrowFarCursor.png"),
        new("cursor-text", "Cursor", "Text cursor", "The I-beam shown over text boxes.", ModKind.Image, Cursors + "IBeamCursor.png"),

        new("death", "Sounds", "Death", "The sound a character makes when it dies.", ModKind.Sound, "content/sounds/ouch.ogg"),
        new("jump", "Sounds", "Jump", "Every jump.", ModKind.Sound, "content/sounds/action_jump.mp3"),
        new("land", "Sounds", "Landing", "Touching the ground after a jump or fall.", ModKind.Sound, "content/sounds/action_jump_land.mp3"),
        new("footsteps", "Sounds", "Footsteps", "Walking and running.", ModKind.Sound, "content/sounds/action_footsteps_plastic.mp3"),
        new("falling", "Sounds", "Falling", "The wind while falling.", ModKind.Sound, "content/sounds/action_falling.ogg"),
        new("swim", "Sounds", "Swimming", "Moving through water.", ModKind.Sound, "content/sounds/action_swim.mp3"),
        new("splash", "Sounds", "Water splash", "Jumping into water.", ModKind.Sound, "content/sounds/impact_water.mp3"),
        new("getup", "Sounds", "Getting up", "Standing back up after a fall.", ModKind.Sound, "content/sounds/action_get_up.mp3"),
        new("explosion", "Sounds", "Explosion", "Roblox's default explosion.", ModKind.Sound, "content/sounds/impact_explosion_03.mp3"),

        new("font", "Font", "Game font", "Replaces every built-in Roblox font: menus, chat and games that use Roblox's fonts.",
            ModKind.Font, CustomFont),
    };

    public static IEnumerable<IGrouping<string, ModSlot>> Groups => Slots.GroupBy(s => s.Group);

    public static ModSlot Font { get; } = Slots.Single(s => s.Kind == ModKind.Font);

    private static string Full(string modsDir, string rel) => Path.Combine(modsDir, rel.Replace('/', Path.DirectorySeparatorChar));

    /// <summary>The file a slot currently uses in the Modifications folder, or null for Roblox's own.</summary>
    public static string? CurrentFile(ModSlot slot, string? modsDir = null)
    {
        string path = Full(modsDir ?? Paths.Modifications, slot.Targets[0]);
        return File.Exists(path) ? path : null;
    }

    public static bool Accepts(ModSlot slot, string file) =>
        slot.Extensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase);

    /// <summary>Copies the file over every target of the slot. Throws with a readable message on a bad pick.</summary>
    public static void Apply(ModSlot slot, string sourceFile, string? modsDir = null)
    {
        modsDir ??= Paths.Modifications;
        if (!Accepts(slot, sourceFile))
            throw new InvalidOperationException($"{slot.Title} needs a {slot.FileTypes} file.");
        if (new FileInfo(sourceFile).Length == 0)
            throw new InvalidOperationException("That file is empty.");

        foreach (string rel in slot.Targets)
        {
            string target = Full(modsDir, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            string temp = target + ".bloxcode-tmp";
            File.Copy(sourceFile, temp, overwrite: true);
            File.Move(temp, target, overwrite: true);
        }
        Logger.Info("ModCatalog::Apply", $"{slot.Id} <- {Path.GetFileName(sourceFile)}");
    }

    /// <summary>Back to Roblox's own file: removes the slot's copies (and, for the font, the rewritten families).</summary>
    public static void Reset(ModSlot slot, string? modsDir = null)
    {
        modsDir ??= Paths.Modifications;
        foreach (string rel in slot.Targets)
        {
            string target = Full(modsDir, rel);
            if (File.Exists(target))
                File.Delete(target);
            PruneEmpty(Path.GetDirectoryName(target)!, modsDir);
        }
        if (slot.Kind == ModKind.Font)
            RebuildFontFamilies(Array.Empty<(string, string)>(), modsDir);
        Logger.Info("ModCatalog::Reset", slot.Id);
    }

    /// <summary>
    /// Keeps the rewritten font families in step with the custom font: with a font, each original
    /// family (file name, JSON) is written with every face pointing at it; without one, the
    /// rewritten copies are removed. Files are only touched when their content changes, so mod
    /// syncs stay quiet. Families the user wrote by hand (not pointing at CustomFont) are left alone.
    /// </summary>
    public static int RebuildFontFamilies(IEnumerable<(string Name, string Json)> originals, string? modsDir = null)
    {
        modsDir ??= Paths.Modifications;
        string familiesDir = Full(modsDir, FamiliesFolder);
        bool hasFont = File.Exists(Full(modsDir, CustomFont));
        var wanted = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (hasFont)
        {
            foreach (var (name, json) in originals)
            {
                if (Path.GetFileName(name) != name || !name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                    continue;
                try
                {
                    if (JsonNode.Parse(json) is JsonObject family && family["faces"] is JsonArray faces)
                    {
                        foreach (var face in faces.OfType<JsonObject>())
                            face["assetId"] = CustomFontAsset;
                        wanted[name] = family.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
                    }
                }
                catch (JsonException ex)
                {
                    Logger.Warn("ModCatalog::RebuildFontFamilies", $"Skipped {name}: {ex.Message}");
                }
            }
        }

        if (Directory.Exists(familiesDir))
            foreach (string file in Directory.EnumerateFiles(familiesDir, "*.json"))
                if (!wanted.ContainsKey(Path.GetFileName(file)) && IsRewritten(file))
                    File.Delete(file);

        foreach (var (name, json) in wanted)
        {
            string target = Path.Combine(familiesDir, name);
            if (File.Exists(target) && File.ReadAllText(target) == json)
                continue;
            Directory.CreateDirectory(familiesDir);
            File.WriteAllText(target, json);
        }

        PruneEmpty(familiesDir, modsDir);
        return wanted.Count;
    }

    private static bool IsRewritten(string file)
    {
        try { return File.ReadAllText(file).Contains(CustomFontAsset, StringComparison.Ordinal); }
        catch (IOException) { return false; }
    }

    /// <summary>Removes now-empty folders up to (not including) the Modifications folder.</summary>
    internal static void PruneEmpty(string dir, string root)
    {
        string stop = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
        for (var current = Path.GetFullPath(dir); current.Length > stop.Length && current.StartsWith(stop, Paths.PathComparison);
             current = Path.GetDirectoryName(current)!)
        {
            if (!Directory.Exists(current) || Directory.EnumerateFileSystemEntries(current).Any())
                return;
            Directory.Delete(current);
        }
    }
}
