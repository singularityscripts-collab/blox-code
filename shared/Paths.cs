namespace BloxCode.Core;

/// <summary>
/// Where Blox Code keeps its files. Windows keeps everything under %LocalAppData%\BloxCode, as it
/// always has; Linux follows the XDG base directories (config, data, state and cache).
/// </summary>
public static class Paths
{
    /// <summary>Linux layout. BLOXCODE_LAYOUT=xdg forces it elsewhere, so the Linux code can be tested on Windows.</summary>
    public static bool IsXdg { get; } = OperatingSystem.IsLinux() || Environment.GetEnvironmentVariable("BLOXCODE_LAYOUT") == "xdg";

    public static string Home { get; } = Environment.GetEnvironmentVariable("HOME") is { Length: > 0 } home && IsXdg
        ? home
        : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    public static string LocalAppData => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    public static string Desktop => Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
    public static string StartMenu => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs");

    public static string DataHome { get; } = Xdg("XDG_DATA_HOME", ".local/share");
    public static string ConfigHome { get; } = Xdg("XDG_CONFIG_HOME", ".config");

    /// <summary>Program data: mods (and, on Windows, Roblox itself).</summary>
    public static string Base { get; } = IsXdg ? Path.Combine(DataHome, "bloxcode") : Path.Combine(LocalAppData, "BloxCode");

    public static string Config { get; } = IsXdg ? Path.Combine(ConfigHome, "bloxcode") : Base;
    public static string StateDir { get; } = IsXdg ? Path.Combine(Xdg("XDG_STATE_HOME", ".local/state"), "bloxcode") : Base;
    public static string Cache { get; } = IsXdg ? Path.Combine(Xdg("XDG_CACHE_HOME", ".cache"), "bloxcode") : Path.Combine(Path.GetTempPath(), "BloxCode");

    public static string Versions => Path.Combine(Base, "Versions");
    public static string Downloads => Path.Combine(Base, "Downloads");
    public static string Modifications => Path.Combine(Base, "Modifications");
    public static string Logs { get; } = IsXdg ? Path.Combine(StateDir, "logs") : Path.Combine(Base, "Logs");

    public static string SettingsFile => Path.Combine(Config, "Settings.json");
    public static string StateFile => Path.Combine(StateDir, "State.json");

    /// <summary>Where the installed copy of Blox Code lives.</summary>
    public static string Application { get; } = IsXdg ? Path.Combine(Home, ".local", "bin", "bloxcode") : Path.Combine(Base, "BloxCode.exe");

    /// <summary>Running from an AppImage, the only form of the Linux build that can install itself.</summary>
    public static bool IsAppImage => IsXdg && Environment.GetEnvironmentVariable("APPIMAGE") is { Length: > 0 };

    /// <summary>
    /// The AppImage was unpacked to a temporary folder instead of mounted (started with
    /// --appimage-extract-and-run, usually because the system has no FUSE). Anything we start from
    /// it, like the updated copy, has to be started the same way or it won't open at all.
    /// </summary>
    public static bool IsExtractedAppImage => IsAppImage &&
        (Environment.GetEnvironmentVariable("APPIMAGE_EXTRACT_AND_RUN") == "1"
         || (Environment.GetEnvironmentVariable("APPDIR") ?? "").Contains("appimage_extracted", StringComparison.Ordinal)
         || (Environment.ProcessPath ?? "").Contains("/appimage_extracted", StringComparison.Ordinal));

    /// <summary>
    /// The executable that is currently running. Inside an AppImage that is the .AppImage file
    /// (the runtime exports it as APPIMAGE), not the binary in the temporary mount.
    /// </summary>
    public static string Process => IsXdg && Environment.GetEnvironmentVariable("APPIMAGE") is { Length: > 0 } appImage
        ? appImage
        : Environment.ProcessPath ?? Application;

    public static StringComparison PathComparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    public static bool IsRunningInstalled =>
        string.Equals(Path.GetFullPath(Process), Path.GetFullPath(Application), PathComparison);

    public static void EnsureCreated()
    {
        var dirs = IsXdg
            ? new[] { Base, Config, StateDir, Modifications, Logs }
            : new[] { Base, Versions, Downloads, Modifications, Logs };
        foreach (var dir in dirs)
            Directory.CreateDirectory(dir);
    }

    /// <summary>
    /// Copies an executable into place. On Linux it never rewrites the file in place: a running
    /// AppImage is mounted from that very file, so the copy goes to a sibling that is renamed over
    /// the old one (the running copy keeps the old inode).
    /// </summary>
    public static void InstallExecutable(string source, string destination)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        if (OperatingSystem.IsWindows())
        {
            try
            {
                File.Copy(source, destination, overwrite: true);
            }
            catch (IOException) when (File.Exists(destination))
            {
                // Another Blox Code still runs from it (the one watching Roblox, or an open settings
                // window): Windows won't let a running .exe be overwritten, but it can be renamed. The
                // old copy keeps running from its new name and is deleted on a later start.
                File.Move(destination, destination + "." + Guid.NewGuid().ToString("N")[..8] + OldSuffix);
                File.Copy(source, destination);
            }
            return;
        }

        string temp = destination + ".new";
        File.Copy(source, temp, overwrite: true);
        MakeExecutable(temp);
        File.Move(temp, destination, overwrite: true);
    }

    private const string OldSuffix = ".old";

    /// <summary>Deletes the copies <see cref="InstallExecutable"/> moved aside; one still running stays until next time.</summary>
    public static void DeleteOldExecutables(string destination)
    {
        string? dir = Path.GetDirectoryName(destination);
        if (dir is null || !Directory.Exists(dir))
            return;
        foreach (string old in Directory.EnumerateFiles(dir, Path.GetFileName(destination) + ".*" + OldSuffix))
        {
            try { File.Delete(old); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    public static void MakeExecutable(string path)
    {
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
    }

    /// <summary>An XDG base directory: the variable when it holds an absolute path, else the default under $HOME.</summary>
    private static string Xdg(string variable, string fallback) =>
        Environment.GetEnvironmentVariable(variable) is { Length: > 0 } value && Path.IsPathRooted(value) && value.StartsWith('/')
            ? value
            : Path.Combine(Home, fallback);
}
