namespace BloxCode.Core;

public sealed record Package(string Name, string Signature, long PackedSize, long Size)
{
    public string DownloadPath => Path.Combine(Paths.Downloads, Signature);
}

public static class PackageManifest
{
    /// <summary>
    /// Parses "{version}-rbxPkgManifest.txt": a "v0" header followed by groups of four lines
    /// (file name, MD5, packed size, unpacked size).
    /// </summary>
    public static List<Package> Parse(string text)
    {
        var lines = text.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length == 0 || lines[0] != "v0")
            throw new InvalidDataException($"Unexpected package manifest version: {lines.FirstOrDefault()} (expected v0!)");

        var packages = new List<Package>();
        for (int i = 1; i + 3 < lines.Length; i += 4)
            packages.Add(new Package(lines[i], lines[i + 1], long.Parse(lines[i + 2]), long.Parse(lines[i + 3])));
        return packages;
    }

    /// <summary>
    /// Where each WindowsStudio64 package is extracted. Worked out by reading each package's zip
    /// directory and finding its files in an install made by Roblox's own Studio installer.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> StudioDirectoryMap = new Dictionary<string, string>
    {
        ["RobloxStudio.zip"] = @"",
        ["redist.zip"] = @"",
        ["Libraries.zip"] = @"",
        ["LibrariesQt5.zip"] = @"",
        ["WebView2.zip"] = @"",
        ["WebView2RuntimeInstaller.zip"] = @"WebView2RuntimeInstaller\",
        ["ApplicationConfig.zip"] = @"ApplicationConfig\",
        ["BuiltInPlugins.zip"] = @"BuiltInPlugins\",
        ["BuiltInStandalonePlugins.zip"] = @"BuiltInStandalonePlugins\",
        ["Plugins.zip"] = @"Plugins\",
        ["StudioFonts.zip"] = @"StudioFonts\",
        ["shaders.zip"] = @"shaders\",
        ["ssl.zip"] = @"ssl\",

        ["content-avatar.zip"] = @"content\avatar\",
        ["content-configs.zip"] = @"content\configs\",
        ["content-fonts.zip"] = @"content\fonts\",
        ["content-models.zip"] = @"content\models\",
        ["content-qt_translations.zip"] = @"content\qt_translations\",
        ["content-sky.zip"] = @"content\sky\",
        ["content-sounds.zip"] = @"content\sounds\",
        ["content-textures2.zip"] = @"content\textures\",
        ["content-studio_svg_textures.zip"] = @"content\studio_svg_textures\",
        ["content-api-docs.zip"] = @"content\api_docs\",

        ["content-textures3.zip"] = @"PlatformContent\pc\textures\",
        ["content-terrain.zip"] = @"PlatformContent\pc\terrain\",
        ["content-platform-fonts.zip"] = @"PlatformContent\pc\fonts\",
        ["content-platform-dictionaries.zip"] = @"PlatformContent\pc\shared_compression_dictionaries\",

        ["extracontent-scripts.zip"] = @"ExtraContent\scripts\",
        ["extracontent-luapackages.zip"] = @"ExtraContent\LuaPackages\",
        ["extracontent-translations.zip"] = @"ExtraContent\translations\",
        ["extracontent-models.zip"] = @"ExtraContent\models\",
        ["extracontent-textures.zip"] = @"ExtraContent\textures\",
        ["studiocontent-models.zip"] = @"StudioContent\models\",
        ["studiocontent-textures.zip"] = @"StudioContent\textures\",
    };

    /// <summary>Where each WindowsPlayer package is extracted, relative to the version folder.</summary>
    public static readonly IReadOnlyDictionary<string, string> PlayerDirectoryMap = new Dictionary<string, string>
    {
        ["RobloxApp.zip"] = @"",
        ["redist.zip"] = @"",
        ["Libraries.zip"] = @"",
        ["shaders.zip"] = @"shaders\",
        ["ssl.zip"] = @"ssl\",
        ["WebView2.zip"] = @"",
        ["WebView2RuntimeInstaller.zip"] = @"WebView2RuntimeInstaller\",

        ["content-avatar.zip"] = @"content\avatar\",
        ["content-configs.zip"] = @"content\configs\",
        ["content-fonts.zip"] = @"content\fonts\",
        ["content-sky.zip"] = @"content\sky\",
        ["content-sounds.zip"] = @"content\sounds\",
        ["content-textures2.zip"] = @"content\textures\",
        ["content-models.zip"] = @"content\models\",

        ["content-textures3.zip"] = @"PlatformContent\pc\textures\",
        ["content-terrain.zip"] = @"PlatformContent\pc\terrain\",
        ["content-platform-fonts.zip"] = @"PlatformContent\pc\fonts\",
        ["content-platform-dictionaries.zip"] = @"PlatformContent\pc\shared_compression_dictionaries\",

        ["extracontent-luapackages.zip"] = @"ExtraContent\LuaPackages\",
        ["extracontent-translations.zip"] = @"ExtraContent\translations\",
        ["extracontent-models.zip"] = @"ExtraContent\models\",
        ["extracontent-textures.zip"] = @"ExtraContent\textures\",
        ["extracontent-places.zip"] = @"ExtraContent\places\",
    };
}
