using System.Text;
using BloxCode.Core;
using BloxCode.Linux.Runtime;

namespace BloxCode.Linux.Platform;

/// <summary>
/// Installs Blox Code for the current user, the Linux way: the AppImage goes to ~/.local/bin/bloxcode,
/// a launcher entry to ~/.local/share/applications, the icon into the hicolor theme, and the
/// roblox / roblox-player link schemes are pointed at that entry. No sudo anywhere.
/// </summary>
public static class DesktopIntegration
{
    public const string DesktopId = "bloxcode.desktop";
    public const string WmClass = "bloxcode";
    public static readonly string[] Schemes = { "x-scheme-handler/roblox", "x-scheme-handler/roblox-player" };
    private static readonly int[] IconSizes = { 48, 64, 128, 256 };

    public static string ApplicationsDir => Path.Combine(Paths.DataHome, "applications");
    public static string DesktopFile => Path.Combine(ApplicationsDir, DesktopId);
    public static string IconFile(int size) => Path.Combine(Paths.DataHome, "icons", "hicolor", $"{size}x{size}", "apps", "bloxcode.png");
    public static string MimeAppsFile => Path.Combine(Paths.ConfigHome, "mimeapps.list");

    public static bool IsInstalled => File.Exists(Paths.Application) && File.Exists(DesktopFile);

    /// <summary>Only the AppImage (or an already installed copy) is a complete program that can be copied.</summary>
    public static bool CanInstall => Paths.IsAppImage || Paths.IsRunningInstalled;

    public static async Task InstallAsync(Func<int, byte[]> icon)
    {
        const string ident = "DesktopIntegration::Install";
        Paths.EnsureCreated();

        if (!Paths.IsRunningInstalled)
        {
            if (!CanInstall)
                throw new InvalidOperationException("Install from the AppImage: this copy of Blox Code isn't a single file that can be copied.");
            Paths.InstallExecutable(Paths.Process, Paths.Application);
            Logger.Info(ident, $"Copied {Paths.Process} to {Paths.Application}");
        }

        WriteIcons(icon);
        WriteDesktopFile();
        WriteModsReadme();
        await RefreshDatabaseAsync();
        await RegisterSchemesAsync();
        Logger.Info(ident, "Installed");
    }

    /// <summary>After a self-update: the entry and icons may have changed between versions.</summary>
    public static void RefreshAfterUpgrade(Func<int, byte[]> icon)
    {
        try
        {
            WriteIcons(icon);
            WriteDesktopFile();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Logger.Error("DesktopIntegration::RefreshAfterUpgrade", ex);
        }
    }

    private static void WriteIcons(Func<int, byte[]> icon)
    {
        foreach (int size in IconSizes)
        {
            string path = IconFile(size);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            WriteAtomic(path, icon(size));
        }
    }

    /// <param name="extractAndRun">No FUSE here: the menu entry must unpack the AppImage too, or it won't open.</param>
    public static string DesktopEntry(string exe, bool extractAndRun = false)
    {
        string command = (extractAndRun ? "env APPIMAGE_EXTRACT_AND_RUN=1 " : "") + QuoteExec(exe);
        return new StringBuilder()
            .Append("[Desktop Entry]\n")
            .Append("Type=Application\n")
            .Append("Version=1.5\n")
            .Append("Name=Blox Code\n")
            .Append("GenericName=Roblox Launcher\n")
            .Append("Comment=Play Roblox through Sober, with Fast Flags and mods\n")
            .Append($"Exec={command} %u\n")
            .Append($"TryExec={EscapeValue(exe)}\n")
            .Append("Icon=bloxcode\n")
            .Append("Terminal=false\n")
            .Append("Categories=Game;\n")
            .Append("Keywords=roblox;sober;fastflags;mods;\n")
            .Append($"MimeType={string.Join(';', Schemes)};\n")
            .Append("StartupNotify=true\n")
            .Append($"StartupWMClass={WmClass}\n")
            .Append("Actions=play;\n")
            .Append('\n')
            .Append("[Desktop Action play]\n")
            .Append("Name=Play Roblox\n")
            .Append($"Exec={command} -player\n")
            .ToString();
    }

    public static void WriteDesktopFile()
    {
        Directory.CreateDirectory(ApplicationsDir);
        WriteAtomic(DesktopFile, Encoding.UTF8.GetBytes(DesktopEntry(Paths.Application, Paths.IsExtractedAppImage)));
        Paths.MakeExecutable(DesktopFile);
    }

    /// <summary>
    /// Quotes a program path for an Exec key: inside double quotes, ", `, $ and \ are escaped
    /// with a backslash, then the string escaping of the file doubles every backslash; % is %%.
    /// </summary>
    public static string QuoteExec(string path)
    {
        if (path.All(c => char.IsAsciiLetterOrDigit(c) || "/._-+".Contains(c)))
            return path;
        var quoted = new StringBuilder("\"");
        foreach (char c in path)
        {
            if (c is '"' or '`' or '$' or '\\')
                quoted.Append('\\');
            quoted.Append(c == '%' ? "%%" : c.ToString());
        }
        quoted.Append('"');
        return quoted.ToString().Replace("\\", "\\\\");
    }

    /// <summary>The string escapes of a plain (non-Exec) value.</summary>
    private static string EscapeValue(string value) =>
        value.Replace("\\", "\\\\").Replace("\n", "\\n").Replace("\t", "\\t").Replace("\r", "\\r");

    private static void WriteModsReadme()
    {
        string readme = Path.Combine(Paths.Modifications, "README.txt");
        if (File.Exists(readme))
            return;
        File.WriteAllText(readme,
            "Blox Code mods\n\n" +
            "Files here are copied into Sober's asset_overlay before each launch, which overrides Roblox's\n" +
            "own files. Use the same layout as the assets/ folder inside Roblox's APK, for example:\n\n" +
            "    content/textures/Cursors/KeyboardMouse/ArrowCursor.png\n" +
            "    content/sounds/ouch.ogg\n\n" +
            "The Mods page in Blox Code lists every original asset and can put a file in the right place.\n" +
            "Delete a file here and the original comes back on the next launch.\n");
    }

    // ===== roblox:// links =====

    /// <summary>The desktop entry that opens roblox:// links now, or null.</summary>
    public static async Task<string?> CurrentHandlerAsync()
    {
        if (Proc.Which("xdg-mime") is { } xdgMime &&
            await Proc.TryRunAsync(xdgMime, new[] { "query", "default", Schemes[0] }, TimeSpan.FromSeconds(10)) is { Ok: true } query)
        {
            string answer = query.Output.Trim();
            if (answer.Length > 0)
                return answer;
        }
        return MimeApps.Read(MimeAppsFile).GetValueOrDefault(Schemes[0]);
    }

    public static async Task<bool> IsDefaultAsync() => await CurrentHandlerAsync() == DesktopId;

    /// <summary>Makes Blox Code open roblox:// and roblox-player: links.</summary>
    public static Task RegisterSchemesAsync() => SetHandlerAsync(DesktopId);

    /// <summary>
    /// Sober registers itself for roblox:// links when Flatpak installs or updates it, which quietly
    /// undoes Blox Code's. An installed Blox Code takes them back on every run, like Bloxstrap does on
    /// Windows; any other handler is left alone and the Home banner asks instead.
    /// </summary>
    public static async Task<bool> ReclaimLinksFromSoberAsync()
    {
        try
        {
            if (!IsInstalled)
                return false;
            string? handler = await CurrentHandlerAsync();
            if (handler == DesktopId || (handler is not null && handler != Sober.DesktopId))
                return false;
            await RegisterSchemesAsync();
            Logger.Info("DesktopIntegration::ReclaimLinks", $"roblox:// pointed at {handler ?? "nothing"}; registered Blox Code again");
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Logger.Warn("DesktopIntegration::ReclaimLinks", ex.Message);
            return false;
        }
    }

    /// <summary>
    /// Points the link schemes at a desktop entry with xdg-mime, and writes mimeapps.list
    /// directly when xdg-mime is missing or didn't take.
    /// </summary>
    public static async Task SetHandlerAsync(string desktopId)
    {
        const string ident = "DesktopIntegration::SetHandler";
        if (Proc.Which("xdg-mime") is { } xdgMime)
        {
            var set = await Proc.TryRunAsync(xdgMime, new[] { "default", desktopId }.Concat(Schemes), TimeSpan.FromSeconds(15));
            if (set is { Ok: true } && await CurrentHandlerAsync() == desktopId)
            {
                Logger.Info(ident, $"xdg-mime: links open {desktopId}");
                return;
            }
            Logger.Warn(ident, $"xdg-mime didn't take ({set?.Problem ?? "no answer"}); writing mimeapps.list");
        }

        MimeApps.SetDefaults(MimeAppsFile, Schemes, desktopId);
        Logger.Info(ident, $"mimeapps.list: links open {desktopId}");
    }

    public static async Task RefreshDatabaseAsync()
    {
        if (Proc.Which("update-desktop-database") is { } update)
            await Proc.TryRunAsync(update, new[] { ApplicationsDir }, TimeSpan.FromSeconds(15));
    }

    // ===== Uninstall =====

    /// <summary>
    /// Removes Blox Code: hands links back to Sober, takes its mods out of Sober, deletes the entry,
    /// icons, program and data. Sober's own settings and Fast Flags stay as they are.
    /// </summary>
    public static async Task UninstallAsync()
    {
        const string ident = "DesktopIntegration::Uninstall";

        try
        {
            if (await IsDefaultAsync())
            {
                if (File.Exists(Path.Combine(Paths.DataHome, "flatpak", "exports", "share", "applications", Sober.DesktopId))
                    || File.Exists(Path.Combine("/var/lib/flatpak/exports/share/applications", Sober.DesktopId)))
                    await SetHandlerAsync(Sober.DesktopId);
                else
                    MimeApps.RemoveDefaults(MimeAppsFile, Schemes, DesktopId);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Logger.Error(ident, ex);
        }

        try { ModSync.RemoveAllFromSober(); }
        catch (Exception ex) { Logger.Error(ident, ex); }

        var files = new List<string> { DesktopFile, Paths.Application };
        files.AddRange(IconSizes.Select(IconFile));
        foreach (string file in files)
        {
            try { File.Delete(file); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Logger.Warn(ident, $"{file}: {ex.Message}"); }
        }

        await RefreshDatabaseAsync();
        Logger.Info(ident, "Uninstalled; removing data");

        foreach (string dir in new[] { Paths.Base, Paths.Config, Paths.StateDir, Paths.Cache }.Distinct())
        {
            try
            {
                if (Directory.Exists(dir))
                    Directory.Delete(dir, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Logger.Warn(ident, $"{dir}: {ex.Message}");
            }
        }
    }

    private static void WriteAtomic(string path, byte[] bytes)
    {
        string temp = path + ".bloxcode-tmp";
        File.WriteAllBytes(temp, bytes);
        File.Move(temp, path, overwrite: true);
    }
}

/// <summary>Minimal reader/writer for the [Default Applications] group of mimeapps.list.</summary>
public static class MimeApps
{
    private const string DefaultGroup = "[Default Applications]";

    public static Dictionary<string, string> Read(string path)
    {
        var defaults = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!File.Exists(path))
            return defaults;

        bool inGroup = false;
        foreach (string raw in File.ReadAllLines(path))
        {
            string line = raw.Trim();
            if (line.StartsWith('['))
            {
                inGroup = line == DefaultGroup;
                continue;
            }
            int eq = line.IndexOf('=');
            if (inGroup && eq > 0)
            {
                string first = line[(eq + 1)..].Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? "";
                if (first.Length > 0)
                    defaults[line[..eq].Trim()] = first;
            }
        }
        return defaults;
    }

    public static void SetDefaults(string path, IEnumerable<string> mimeTypes, string desktopId) =>
        Edit(path, mimeTypes, _ => $"{desktopId};");

    public static void RemoveDefaults(string path, IEnumerable<string> mimeTypes, string desktopId) =>
        Edit(path, mimeTypes, current => current is null ? null
            : string.Join(';', current.Split(';', StringSplitOptions.RemoveEmptyEntries).Where(d => d.Trim() != desktopId)) is { Length: > 0 } rest ? rest + ";" : null);

    /// <summary>Rewrites the given keys of [Default Applications], keeping every other line and group as it was.</summary>
    private static void Edit(string path, IEnumerable<string> mimeTypes, Func<string?, string?> value)
    {
        var keys = mimeTypes.ToList();
        var lines = File.Exists(path) ? File.ReadAllLines(path).ToList() : new List<string>();
        var done = new HashSet<string>(StringComparer.Ordinal);

        int start = lines.FindIndex(l => l.Trim() == DefaultGroup);
        if (start < 0)
        {
            if (lines.Count > 0 && lines[^1].Trim().Length > 0)
                lines.Add("");
            lines.Add(DefaultGroup);
            start = lines.Count - 1;
        }

        int end = start + 1;
        while (end < lines.Count && !lines[end].Trim().StartsWith('['))
            end++;

        for (int i = start + 1; i < end; i++)
        {
            int eq = lines[i].IndexOf('=');
            if (eq <= 0) continue;
            string key = lines[i][..eq].Trim();
            if (!keys.Contains(key)) continue;

            string? updated = value(lines[i][(eq + 1)..].Trim());
            if (updated is null || done.Contains(key))
            {
                lines.RemoveAt(i);
                i--;
                end--;
            }
            else
            {
                lines[i] = $"{key}={updated}";
            }
            done.Add(key);
        }

        int insertAt = end;
        foreach (string key in keys.Where(k => !done.Contains(k)))
        {
            if (value(null) is { } fresh)
                lines.Insert(insertAt++, $"{key}={fresh}");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temp = path + ".bloxcode-tmp";
        File.WriteAllText(temp, string.Join('\n', lines) + "\n");
        File.Move(temp, path, overwrite: true);
    }
}
