namespace BloxCode.Core;

public enum RenderingMode { Default, D3D11, Vulkan, OpenGL }

/// <summary>Auto leaves graphics to Roblox; the others pin a whole set of allowlisted flags at once.</summary>
public enum GraphicsPreset { Auto, Lowest, Low, Medium, High }

/// <summary>
/// Friendly presets backed by real Fast Flags, modelled on Bloxstrap's FastFlagManager (2.11.4, read
/// with REA): a table of named presets, each owning one flag; SetPreset writes every preset under a
/// prefix, SetPresetEnum turns on exactly one of a group of mutually exclusive flags, and
/// GetPresetEnum maps the flags back to the option they spell. Changes are logged the same way.
/// Only flags on Roblox's local allowlist are used: Roblox ignores every other flag in
/// ClientAppSettings.json (devforum.roblox.com/t/3966569).
/// </summary>
public static class FastFlagPresets
{
    private const string LogIdent = "FastFlagManager";

    public const string ExclusiveFullscreen = "FFlagHandleAltEnterFullscreenManually";
    public const string DisableDpiScale = "DFFlagDisableDPIScale";
    public const string Msaa = "FIntDebugForceMSAASamples";
    public const string TextureOverrideEnabled = "DFFlagTextureQualityOverrideEnabled";
    public const string TextureQuality = "DFIntTextureQualityOverride";
    public const string QualityLevel = "DFIntDebugFRMQualityLevelOverride";
    public const string GrassMaxDistance = "FIntFRMMaxGrassDistance";
    public const string GrassMinDistance = "FIntFRMMinGrassDistance";
    public const string GrassMotion = "FIntGrassMovementReducedMotionFactor";
    public const string LodDistance = "DFIntCSGLevelOfDetailSwitchingDistance";
    public const string LodDistanceL12 = "DFIntCSGLevelOfDetailSwitchingDistanceL12";
    public const string LodDistanceL23 = "DFIntCSGLevelOfDetailSwitchingDistanceL23";
    public const string LodDistanceL34 = "DFIntCSGLevelOfDetailSwitchingDistanceL34";
    public const string PauseVoxelizer = "DFFlagDebugPauseVoxelizer";
    public const string GraySky = "FFlagDebugSkyGray";

    /// <summary>Roblox's allowlist for local Fast Flags, as published on the DevForum.</summary>
    public static readonly HashSet<string> Allowlist = new(StringComparer.Ordinal)
    {
        // Geometry
        LodDistance, LodDistanceL12, LodDistanceL23, LodDistanceL34,
        // Rendering
        ExclusiveFullscreen, TextureOverrideEnabled, TextureQuality, Msaa, DisableDpiScale,
        "FFlagDebugGraphicsPreferD3D11", GraySky, PauseVoxelizer,
        QualityLevel, GrassMaxDistance, GrassMinDistance,
        "FFlagDebugGraphicsPreferVulkan", "FFlagDebugGraphicsPreferOpenGL",
        // User interface
        GrassMotion,
    };

    /// <summary>Preset name to the flag it owns, Bloxstrap's naming where Bloxstrap has the preset.</summary>
    public static readonly IReadOnlyDictionary<string, string> Presets = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["Rendering.ManualFullscreen"] = ExclusiveFullscreen,
        ["Rendering.DisableScaling"] = DisableDpiScale,
        ["Rendering.MSAA"] = Msaa,
        ["Rendering.TextureQuality.OverrideEnabled"] = TextureOverrideEnabled,
        ["Rendering.TextureQuality.Level"] = TextureQuality,
        ["Rendering.Mode.D3D11"] = "FFlagDebugGraphicsPreferD3D11",
        ["Rendering.Mode.Vulkan"] = "FFlagDebugGraphicsPreferVulkan",
        ["Rendering.Mode.OpenGL"] = "FFlagDebugGraphicsPreferOpenGL",
        ["Rendering.QualityLevel"] = QualityLevel,
        ["Rendering.Grass.MaxDistance"] = GrassMaxDistance,
        ["Rendering.Grass.MinDistance"] = GrassMinDistance,
        ["Rendering.Grass.Motion"] = GrassMotion,
        ["Geometry.LOD.Distance"] = LodDistance,
        ["Geometry.LOD.L12"] = LodDistanceL12,
        ["Geometry.LOD.L23"] = LodDistanceL23,
        ["Geometry.LOD.L34"] = LodDistanceL34,
        ["Rendering.PauseVoxelizer"] = PauseVoxelizer,
        ["Rendering.GraySky"] = GraySky,
    };

    /// <summary>Flags owned by the presets above; the editor hides them unless asked, like Bloxstrap.</summary>
    public static readonly HashSet<string> PresetFlags = new(Presets.Values, StringComparer.Ordinal);

    public static bool IsAllowlisted(string flag) => Allowlist.Contains(flag);

    public static readonly (string Label, string? Value)[] MsaaModes =
        { ("Auto", null), ("1x", "1"), ("2x", "2"), ("4x", "4") };

    public static readonly (string Label, string? Value)[] TextureLevels =
        { ("Auto", null), ("Lowest", "0"), ("Low", "1"), ("Medium", "2"), ("High", "3") };

    public static readonly IReadOnlyDictionary<RenderingMode, string> RenderingModes = new Dictionary<RenderingMode, string>
    {
        [RenderingMode.Default] = "None",
        [RenderingMode.D3D11] = "D3D11",
        [RenderingMode.Vulkan] = "Vulkan",
        [RenderingMode.OpenGL] = "OpenGL",
    };

    private static Dictionary<string, string> Flags => Store.Settings.FastFlags;

    public static string? Get(string flag) => Flags.TryGetValue(flag, out var v) ? v : null;

    /// <summary>Sets or (with null) removes one flag, logging what changes like Bloxstrap's SetValue.</summary>
    public static void Set(string flag, string? value)
    {
        string? old = Get(flag);
        if (value is null)
        {
            if (old is not null)
            {
                Logger.Info($"{LogIdent}::SetValue", $"Deletion of '{flag}' is pending");
                // While the quality was pinned, Roblox remembered that level for every game it ran.
                if (flag == QualityLevel && !Paths.IsXdg)
                {
                    Store.State.ResetQualityCache = true;
                    Store.SaveState();
                }
            }
            Flags.Remove(flag);
        }
        else if (old != value)
        {
            Logger.Info($"{LogIdent}::SetValue", old is null
                ? $"Setting of '{flag}' to '{value}' is pending"
                : $"Changing of '{flag}' from '{old}' to '{value}' is pending");
            Flags[flag] = value;
        }
    }

    public static string? GetPreset(string name)
    {
        if (Presets.TryGetValue(name, out var flag))
            return Get(flag);
        Logger.Warn($"{LogIdent}::GetPreset", $"Could not find preset {name}");
        return null;
    }

    /// <summary>Writes the value to every preset whose name starts with the prefix.</summary>
    public static void SetPreset(string prefix, string? value)
    {
        foreach (var (name, flag) in Presets)
            if (name.StartsWith(prefix, StringComparison.Ordinal))
                Set(flag, value);
    }

    /// <summary>Sets prefix.target to the value and clears its siblings, so only one option is on.</summary>
    public static void SetPresetEnum(string prefix, string target, string? value)
    {
        foreach (var (name, flag) in Presets)
            if (name.StartsWith(prefix + ".", StringComparison.Ordinal))
                Set(flag, name == prefix + "." + target ? value : null);
    }

    /// <summary>The option whose flag carries the value, or the mapping's first option when none does.</summary>
    public static T GetPresetEnum<T>(IReadOnlyDictionary<T, string> mapping, string prefix, string value) where T : notnull
    {
        foreach (var (option, name) in mapping)
            if (name != "None" && GetPreset($"{prefix}.{name}") == value)
                return option;
        return mapping.First().Key;
    }

    public static bool UseExclusiveFullscreen
    {
        get => GetPreset("Rendering.ManualFullscreen")?.Equals("False", StringComparison.OrdinalIgnoreCase) == true;
        set => SetPreset("Rendering.ManualFullscreen", value ? "False" : null);
    }

    public static bool DisableScaling
    {
        get => GetPreset("Rendering.DisableScaling")?.Equals("True", StringComparison.OrdinalIgnoreCase) == true;
        set => SetPreset("Rendering.DisableScaling", value ? "True" : null);
    }

    public static string? MsaaSamples
    {
        get => GetPreset("Rendering.MSAA");
        set => SetPreset("Rendering.MSAA", value);
    }

    public static string? TextureLevel
    {
        get => GetPreset("Rendering.TextureQuality.Level");
        set
        {
            SetPreset("Rendering.TextureQuality.Level", value);
            SetPreset("Rendering.TextureQuality.OverrideEnabled", value is null ? null : "True");
        }
    }

    /// <summary>
    /// Stops Roblox recomputing its lighting grid as things move: a solid FPS gain on weak GPUs, but
    /// shadows and light from moving objects stop updating.
    /// </summary>
    public static bool PauseLighting
    {
        get => GetPreset("Rendering.PauseVoxelizer")?.Equals("True", StringComparison.OrdinalIgnoreCase) == true;
        set => SetPreset("Rendering.PauseVoxelizer", value ? "True" : null);
    }

    /// <summary>A plain gray sky instead of the skybox: a little less to draw.</summary>
    public static bool PlainSky
    {
        get => GetPreset("Rendering.GraySky")?.Equals("True", StringComparison.OrdinalIgnoreCase) == true;
        set => SetPreset("Rendering.GraySky", value ? "True" : null);
    }

    /// <summary>Direct3D 11, Vulkan or OpenGL instead of Roblox's own pick (Windows only).</summary>
    public static RenderingMode Renderer
    {
        get => GetPresetEnum(RenderingModes, "Rendering.Mode", "True");
        set => SetPresetEnum("Rendering.Mode", RenderingModes[value], value == RenderingMode.Default ? null : "True");
    }

    /// <summary>
    /// Every flag a graphics preset writes, with its value per level (null = Roblox's default). The
    /// quality level is Roblox's 1-21 FRM scale, the one the in-game graphics slider moves along.
    /// Lowest stays at quality 4 and texture level 1: quality 1-3 and texture level 0 make the
    /// whole picture blurry, which costs more than the few frames they win back. Medium leaves the
    /// textures to Roblox, which picks the sharpest the GPU's memory allows.
    /// </summary>
    private static readonly (string Flag, string? Lowest, string? Low, string? Medium, string? High)[] GraphicsTable =
    {
        (QualityLevel,           "4",    "6",    "11",   "21"),
        (Msaa,                   "1",    "1",    null,   "4"),
        (TextureOverrideEnabled, "True", "True", null,   "True"),
        (TextureQuality,         "1",    "2",    null,   "3"),
        (GrassMaxDistance,       "0",    "0",    null,   null),
        (GrassMinDistance,       "0",    "0",    null,   null),
        (GrassMotion,            "0",    "0",    null,   null),
        (LodDistance,            "0",    null,   null,   null),
        (LodDistanceL12,         "0",    null,   null,   null),
        (LodDistanceL23,         "0",    null,   null,   null),
        (LodDistanceL34,         "0",    null,   null,   null),
    };

    private static string? ValueFor(GraphicsPreset preset, (string Flag, string? Lowest, string? Low, string? Medium, string? High) row) => preset switch
    {
        GraphicsPreset.Lowest => row.Lowest,
        GraphicsPreset.Low => row.Low,
        GraphicsPreset.Medium => row.Medium,
        GraphicsPreset.High => row.High,
        _ => null,
    };

    public static void ApplyGraphicsPreset(GraphicsPreset preset)
    {
        foreach (var row in GraphicsTable)
            Set(row.Flag, ValueFor(preset, row));
    }

    /// <summary>A copy of <paramref name="flags"/> with a graphics preset in place; changes nothing else (per-game launches).</summary>
    public static Dictionary<string, string> WithGraphicsPreset(IReadOnlyDictionary<string, string> flags, GraphicsPreset preset)
    {
        var result = new Dictionary<string, string>(flags);
        foreach (var row in GraphicsTable)
        {
            if (ValueFor(preset, row) is { } value)
                result[row.Flag] = value;
            else
                result.Remove(row.Flag);
        }
        return result;
    }

    /// <summary>
    /// Earlier versions' presets, flag by flag (missing = unset): 1.2.0's blurry Lowest and Low
    /// (quality 1/5, textures 0/1) and the Medium that forced textures to level 2 until 1.4.
    /// </summary>
    private static readonly (GraphicsPreset Preset, Dictionary<string, string> Flags)[] LegacyPresets =
    {
        (GraphicsPreset.Lowest, Legacy(("1", "1", "0"), grass: true, lod: true)),
        (GraphicsPreset.Low, Legacy(("5", "1", "1"), grass: true, lod: false)),
        (GraphicsPreset.Medium, Legacy(("11", null, "2"), grass: false, lod: false)),
    };

    private static Dictionary<string, string> Legacy((string Quality, string? Msaa, string Texture) core, bool grass, bool lod)
    {
        var flags = new Dictionary<string, string> { [QualityLevel] = core.Quality, [TextureOverrideEnabled] = "True", [TextureQuality] = core.Texture };
        if (core.Msaa is { } msaa) flags[Msaa] = msaa;
        if (grass) flags[GrassMaxDistance] = flags[GrassMinDistance] = flags[GrassMotion] = "0";
        if (lod) flags[LodDistance] = flags[LodDistanceL12] = flags[LodDistanceL23] = flags[LodDistanceL34] = "0";
        return flags;
    }

    /// <summary>
    /// Flags that still spell exactly one of an earlier version's presets move to today's version
    /// of that preset; anything mixed by hand is left alone.
    /// </summary>
    public static bool UpgradeBlurryPresets()
    {
        foreach (var (preset, flags) in LegacyPresets)
        {
            if (GraphicsTable.All(row => Get(row.Flag) == flags.GetValueOrDefault(row.Flag)))
            {
                Logger.Info($"{LogIdent}::UpgradeBlurryPresets", $"Moving an older {preset} preset to the current one");
                ApplyGraphicsPreset(preset);
                return true;
            }
        }
        return false;
    }

    /// <summary>The preset the current flags spell exactly, or null when they've been mixed by hand.</summary>
    public static GraphicsPreset? CurrentGraphicsPreset =>
        Enum.GetValues<GraphicsPreset>().Cast<GraphicsPreset?>()
            .FirstOrDefault(p => GraphicsTable.All(row => Get(row.Flag) == ValueFor(p!.Value, row)));
}

/// <summary>Labels and explanations for the graphics preset control, shared by both apps.</summary>
public static class GraphicsPresetText
{
    public static readonly (string Label, string? Value)[] Options =
        Enum.GetValues<GraphicsPreset>().Select(p => (p.ToString(), (string?)p.ToString())).ToArray();

    /// <summary>A few words for the Home page.</summary>
    public static string Short(GraphicsPreset? preset) => preset switch
    {
        GraphicsPreset.Auto => "Roblox decides",
        GraphicsPreset.Lowest => "Most FPS",
        GraphicsPreset.Low => "For older PCs",
        GraphicsPreset.Medium => "Balanced",
        GraphicsPreset.High => "Best looks",
        _ => "Custom flags",
    };

    public static string Describe(GraphicsPreset? preset) => preset switch
    {
        GraphicsPreset.Auto => "Roblox picks the quality itself and the in-game graphics slider works as usual.",
        GraphicsPreset.Lowest => "The most FPS without a blurry picture: quality 4 of 21, low textures, no grass and simplified distant parts. The in-game slider stops having an effect.",
        GraphicsPreset.Low => "Quality 6 of 21, medium textures and no grass. Good for older laptops and integrated graphics. The in-game slider stops having an effect.",
        GraphicsPreset.Medium => "Balanced: quality 11 of 21, and Roblox picks the sharpest textures your graphics card has room for. The in-game slider stops having an effect.",
        GraphicsPreset.High => "Quality 21 of 21, high textures and 4x anti-aliasing. Needs a strong graphics card. The in-game slider stops having an effect.",
        _ => "Custom: some of these flags were changed by hand. Pick a preset to set them all again.",
    };
}
