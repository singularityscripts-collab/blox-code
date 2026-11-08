using System.Security.Cryptography;

namespace BloxCode.Core;

public enum CursorStyle { Default, From2006, From2013 }

public enum EmojiStyle { Default, Catmoji, Windows11, Windows10, Windows8 }

/// <summary>
/// A ready-made mod: Roblox files (relative to the Modifications folder) and the built-in file each
/// one is replaced with. Recovered from Bloxstrap 2.11.4's ModPresetTask (see
/// docs/REVERSE_ENGINEERING.md); the files themselves are Bloxstrap's (MIT, shared/ModPresets).
/// </summary>
public sealed record ModPreset(string Id, string Title, string Description, IReadOnlyDictionary<string, string> Files, bool WindowsOnly = false);

/// <summary>
/// Bloxstrap's mod presets, worked the same way: turning one on writes its files into the
/// Modifications folder, turning it off deletes only the files that still match it (so a file the
/// user put there is never removed), and whether it's on is read back from the folder itself rather
/// than stored in the settings. Emoji fonts are downloaded on demand, as Bloxstrap does, and checked
/// by hash before they're used.
/// </summary>
public static class ModPresets
{
    private const string Cursors = "content/textures/Cursors/KeyboardMouse/";
    public const string EmojiFont = "content/fonts/TwemojiMozilla.ttf";

    public static readonly ModPreset OldCharacterSounds = new("old-sounds", "Old character sounds",
        "The classic walk, jump and get-up sounds; falling, landing, swimming and splashing go quiet.",
        new Dictionary<string, string>
        {
            ["content/sounds/action_footsteps_plastic.mp3"] = "Sounds.OldWalk.mp3",
            ["content/sounds/action_jump.mp3"] = "Sounds.OldJump.mp3",
            ["content/sounds/action_get_up.mp3"] = "Sounds.OldGetUp.mp3",
            // Bloxstrap 2.11.4 still names action_falling.mp3; current Roblox ships it as .ogg.
            ["content/sounds/action_falling.ogg"] = "Sounds.Empty.mp3",
            ["content/sounds/action_jump_land.mp3"] = "Sounds.Empty.mp3",
            ["content/sounds/action_swim.mp3"] = "Sounds.Empty.mp3",
            ["content/sounds/impact_water.mp3"] = "Sounds.Empty.mp3",
        });

    public static readonly ModPreset OldAvatarBackground = new("old-avatar-background", "Old avatar editor background",
        "The older, plain background behind your character in the avatar editor.",
        new Dictionary<string, string> { ["ExtraContent/places/Mobile.rbxl"] = "OldAvatarBackground.rbxl" },
        WindowsOnly: true);

    private static readonly Dictionary<CursorStyle, ModPreset> CursorPresets = new()
    {
        [CursorStyle.From2006] = CursorPreset("2006"),
        [CursorStyle.From2013] = CursorPreset("2013"),
    };

    private static ModPreset CursorPreset(string year) => new($"cursor-{year}", $"{year} cursor", $"Roblox's mouse cursor from {year}.",
        new Dictionary<string, string>
        {
            [Cursors + "ArrowCursor.png"] = $"Cursor.From{year}.ArrowCursor.png",
            [Cursors + "ArrowFarCursor.png"] = $"Cursor.From{year}.ArrowFarCursor.png",
        });

    /// <summary>The on/off presets this system supports.</summary>
    public static IEnumerable<ModPreset> Toggles =>
        new[] { OldCharacterSounds, OldAvatarBackground }.Where(p => !p.WindowsOnly || OperatingSystem.IsWindows());

    private static IEnumerable<ModPreset> All => Toggles.Concat(CursorPresets.Values);

    public static readonly (CursorStyle Style, string Label)[] CursorOptions =
    {
        (CursorStyle.Default, "Default"), (CursorStyle.From2006, "2006"), (CursorStyle.From2013, "2013"),
    };

    // ----- built-in files -----

    private static readonly Dictionary<string, byte[]> Cache = new();

    public static byte[] Resource(string name)
    {
        lock (Cache)
        {
            if (Cache.TryGetValue(name, out var data))
                return data;
            using var stream = typeof(ModPresets).Assembly.GetManifestResourceStream("ModPresets." + name)
                ?? throw new InvalidOperationException($"Built-in mod file {name} is missing from this build.");
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            return Cache[name] = buffer.ToArray();
        }
    }

    private static string Full(string modsDir, string rel) => Path.Combine(modsDir, rel.Replace('/', Path.DirectorySeparatorChar));

    private static bool Matches(string file, byte[] expected)
    {
        try
        {
            var info = new FileInfo(file);
            return info.Exists && info.Length == expected.Length && File.ReadAllBytes(file).AsSpan().SequenceEqual(expected);
        }
        catch (IOException) { return false; }
    }

    // ----- presets -----

    /// <summary>On when every file of the preset is in the folder, unchanged.</summary>
    public static bool IsOn(ModPreset preset, string? modsDir = null)
    {
        modsDir ??= Paths.Modifications;
        return preset.Files.All(f => Matches(Full(modsDir, f.Key), Resource(f.Value)));
    }

    public static void Set(ModPreset preset, bool on, string? modsDir = null)
    {
        modsDir ??= Paths.Modifications;
        foreach (var (rel, resource) in preset.Files)
        {
            string target = Full(modsDir, rel);
            byte[] data = Resource(resource);
            if (on)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.WriteAllBytes(target, data);
            }
            else if (Matches(target, data))
            {
                File.Delete(target); // only the preset's own copy; a file the user swapped in stays
                PruneEmpty(Path.GetDirectoryName(target)!, modsDir);
            }
        }
        Logger.Info("ModPresets::Set", $"{preset.Id} -> {(on ? "on" : "off")}");
    }

    public static CursorStyle CurrentCursor(string? modsDir = null) =>
        CursorPresets.FirstOrDefault(p => IsOn(p.Value, modsDir)).Key; // Default when none matches

    public static void SetCursor(CursorStyle style, string? modsDir = null)
    {
        foreach (var (key, preset) in CursorPresets)
            if (key != style)
                Set(preset, false, modsDir);
        if (CursorPresets.TryGetValue(style, out var chosen))
            Set(chosen, true, modsDir);
    }

    /// <summary>The preset a Modifications file came from, for "from the … preset" labels; null if none.</summary>
    public static ModPreset? OwnerOf(string rel, string? modsDir = null)
    {
        modsDir ??= Paths.Modifications;
        string file = Full(modsDir, rel);
        if (!File.Exists(file))
            return null;
        return All.FirstOrDefault(p => p.Files.TryGetValue(rel, out var resource) && Matches(file, Resource(resource)));
    }

    // ----- emoji -----

    private const string EmojiSource = "https://github.com/bloxstraplabs/rbxcustom-fontemojis/releases/download/my-phone-is-78-percent/";

    /// <summary>Bloxstrap's emoji fonts: file name, size and SHA-256 (Bloxstrap checks MD5; these were taken from the same files).</summary>
    public static readonly IReadOnlyDictionary<EmojiStyle, (string File, long Size, string Sha256)> EmojiFonts = new Dictionary<EmojiStyle, (string, long, string)>
    {
        [EmojiStyle.Catmoji] = ("Catmoji.ttf", 1335828, "58d781ff4800ab10144a6fc0bb30479881f5048ecd57196148c9ab791fdab622"),
        [EmojiStyle.Windows11] = ("Win1122H2SegoeUIEmoji.ttf", 2838884, "4e3cec7d1995b6d74102c0b4669e4507ac35cbf9a9830a93ac14c6e40dfe36a9"),
        [EmojiStyle.Windows10] = ("Win10April2018SegoeUIEmoji.ttf", 2072388, "7c0244dd8eeb7c6bdecdfc3f9e59833527fc18a66d0295ce47339069692a2b4f"),
        [EmojiStyle.Windows8] = ("Win8.1SegoeUIEmoji.ttf", 676304, "86be288eed6561684be645f671409210c914815e3833a0fc3b587cbf64c03928"),
    };

    public static readonly (EmojiStyle Style, string Label)[] EmojiOptions =
    {
        (EmojiStyle.Default, "Default"), (EmojiStyle.Catmoji, "Catmoji"), (EmojiStyle.Windows11, "Windows 11"),
        (EmojiStyle.Windows10, "Windows 10"), (EmojiStyle.Windows8, "Windows 8"),
    };

    private static string? Sha256Of(string file)
    {
        try
        {
            using var stream = File.OpenRead(file);
            return Convert.ToHexStringLower(SHA256.HashData(stream));
        }
        catch (IOException) { return null; }
    }

    /// <summary>The emoji font in the folder: Default when there is none; null when it's a font the user put there.</summary>
    public static EmojiStyle? CurrentEmoji(string? modsDir = null)
    {
        string file = Full(modsDir ?? Paths.Modifications, EmojiFont);
        if (!File.Exists(file))
            return EmojiStyle.Default;
        string? hash = Sha256Of(file);
        return EmojiFonts.FirstOrDefault(e => e.Value.Sha256 == hash) is { Value.File: not null } match ? match.Key : null;
    }

    public static async Task SetEmojiAsync(EmojiStyle style, HttpClient http, CancellationToken ct, string? modsDir = null)
    {
        modsDir ??= Paths.Modifications;
        string target = Full(modsDir, EmojiFont);

        if (style == EmojiStyle.Default)
        {
            // Like the other presets: only a font that came from here is removed.
            if (CurrentEmoji(modsDir) is { } current && current != EmojiStyle.Default)
            {
                File.Delete(target);
                PruneEmpty(Path.GetDirectoryName(target)!, modsDir);
            }
            Logger.Info("ModPresets::SetEmoji", "Default");
            return;
        }

        var (name, size, sha256) = EmojiFonts[style];
        byte[] data = await http.GetByteArrayAsync(EmojiSource + name, ct);
        if (data.Length != size || Convert.ToHexStringLower(SHA256.HashData(data)) != sha256)
            throw new InvalidDataException($"The {style} emoji font didn't download correctly; try again.");

        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        string temp = target + ".bloxcode-tmp";
        await File.WriteAllBytesAsync(temp, data, ct);
        File.Move(temp, target, overwrite: true);
        Logger.Info("ModPresets::SetEmoji", $"{style} ({data.Length} bytes)");
    }

    private static void PruneEmpty(string dir, string root) => ModCatalog.PruneEmpty(dir, root);
}
