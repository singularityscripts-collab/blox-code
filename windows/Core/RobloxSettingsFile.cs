using System.Globalization;
using System.Xml;
using System.Xml.Linq;

namespace BloxCode.Core;

/// <summary>The settings from Roblox's own menu that Blox Code can change before a game starts.</summary>
public sealed record RobloxGameSettings(
    double MouseSensitivity,   // camera sensitivity, 0.1–4
    double Volume,             // 0–1
    int Quality,               // 0 = automatic, 1–10 = the manual graphics level
    bool Fullscreen,
    bool PerformanceStats,
    bool ReducedMotion,
    bool InvertCamera);

/// <summary>
/// Roblox's GlobalBasicSettings_13.xml, the file behind its in-game settings menu, like Fishstrap's
/// "Global Basic Settings" editor. Roblox rewrites it when it closes, so changes are only made while
/// no Roblox window is open; only the text of those settings changes (the rest of the file stays
/// byte for byte), and a copy of the original is kept once.
/// The frame rate cap lives here too, but Roblox ignores it from this file now, so it isn't offered.
/// </summary>
public static class RobloxSettingsFile
{
    public static string DefaultPath => Path.Combine(Paths.LocalAppData, "Roblox", "GlobalBasicSettings_13.xml");

    public static RobloxGameSettings? Read(string? path = null)
    {
        path ??= DefaultPath;
        if (!File.Exists(path))
            return null;
        try
        {
            var doc = XDocument.Load(path, LoadOptions.PreserveWhitespace);
            var props = Properties(doc);
            double Float(string name, double fallback) => props.TryGetValue(name, out var e) && double.TryParse(e.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : fallback;
            bool Bool(string name) => props.TryGetValue(name, out var e) && e.Value.Trim().Equals("true", StringComparison.OrdinalIgnoreCase);
            int quality = props.TryGetValue("SavedQualityLevel", out var q) && int.TryParse(q.Value, out int level) ? Math.Clamp(level, 0, 10) : 0;
            return new RobloxGameSettings(Float("MouseSensitivity", 1), Float("MasterVolume", 1), quality,
                Bool("Fullscreen"), Bool("PerformanceStatsVisible"), Bool("ReducedMotion"), Bool("CameraYInverted"));
        }
        catch (Exception ex) when (ex is XmlException or IOException or UnauthorizedAccessException)
        {
            Logger.Warn("RobloxSettingsFile::Read", ex.Message);
            return null;
        }
    }

    /// <summary>Writes the values into the file; false (logged) if it isn't there or can't be written.</summary>
    public static bool Write(RobloxGameSettings settings, string? path = null)
    {
        path ??= DefaultPath;
        if (!File.Exists(path))
            return false;
        try
        {
            string backup = path + ".bloxcode-backup";
            if (!File.Exists(backup))
                File.Copy(path, backup);

            // Only the text inside each setting's tag changes; every other byte stays as Roblox wrote it.
            string text = File.ReadAllText(path);
            void Set(string name, string value) =>
                text = System.Text.RegularExpressions.Regex.Replace(text,
                    "(<(\\w+) name=\"" + name + "\">)[^<]*(</\\2>)", m => m.Groups[1].Value + value + m.Groups[3].Value);
            string F(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);
            string B(bool v) => v ? "true" : "false";

            Set("MouseSensitivity", F(Math.Clamp(settings.MouseSensitivity, 0.1, 4)));
            Set("MasterVolume", F(Math.Clamp(settings.Volume, 0, 1)));
            int quality = Math.Clamp(settings.Quality, 0, 10);
            Set("SavedQualityLevel", quality.ToString(CultureInfo.InvariantCulture));
            // The manual level also shows in GraphicsQualityLevel (1–21 scale); automatic leaves it to Roblox.
            if (quality > 0)
                Set("GraphicsQualityLevel", (quality * 2 + 1).ToString(CultureInfo.InvariantCulture));
            Set("Fullscreen", B(settings.Fullscreen));
            Set("PerformanceStatsVisible", B(settings.PerformanceStats));
            Set("ReducedMotion", B(settings.ReducedMotion));
            Set("CameraYInverted", B(settings.InvertCamera));

            XDocument.Parse(text);   // still well-formed before it replaces the original
            string temp = path + ".tmp";
            File.WriteAllText(temp, text, new System.Text.UTF8Encoding(false));
            File.Move(temp, path, overwrite: true);
            Logger.Info("RobloxSettingsFile::Write", $"Saved Roblox's settings: {settings}");
            return true;
        }
        catch (Exception ex) when (ex is XmlException or IOException or UnauthorizedAccessException)
        {
            Logger.Warn("RobloxSettingsFile::Write", ex.Message);
            return false;
        }
    }

    /// <summary>The UserGameSettings item's properties by name.</summary>
    private static Dictionary<string, XElement> Properties(XDocument doc)
    {
        var item = doc.Root?.Elements("Item").FirstOrDefault(i => (string?)i.Attribute("class") == "UserGameSettings")
                   ?? throw new XmlException("No UserGameSettings in Roblox's settings file");
        var result = new Dictionary<string, XElement>();
        foreach (var prop in item.Element("Properties")?.Elements() ?? Enumerable.Empty<XElement>())
            if ((string?)prop.Attribute("name") is { } name)
                result.TryAdd(name, prop);
        return result;
    }
}
