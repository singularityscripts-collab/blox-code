using System.Text.Json;
using System.Text.Json.Serialization;

namespace BloxCode.Core;

public enum AppTheme { System, Light, Dark }

public enum AccentColor { Mint, Sky, Violet, Rose, Amber }

/// <summary>What shows through the window: nothing, the wallpaper (Mica) or what is behind it, blurred (Acrylic).</summary>
public enum WindowBackdrop { Off, Mica, Acrylic }

public sealed class Settings
{
    public const string DefaultChannel = "production";

    public AppTheme Theme { get; set; } = AppTheme.System;
    public string Channel { get; set; } = DefaultChannel;
    public bool AnimateMascot { get; set; } = true;
    /// <summary>Page transitions and the other small interface animations.</summary>
    public bool Animations { get; set; } = true;
    public bool CleanupOldVersions { get; set; } = true;
    public bool ConfirmLaunches { get; set; }
    public bool AutoUpdate { get; set; } = true;
    /// <summary>Windows: show the game being played on the Discord profile.</summary>
    public bool DiscordRichPresence { get; set; } = true;
    /// <summary>Windows: let Discord friends join the same public server from that status.</summary>
    public bool DiscordJoinButton { get; set; }
    /// <summary>Windows: say roughly where the server is after joining a game.</summary>
    public bool ShowServerLocation { get; set; } = true;
    /// <summary>Windows: keep the games played (History.json) for the Home page and the stats.</summary>
    public bool RememberGames { get; set; } = true;
    /// <summary>Windows: let several Roblox players run at once (see MultiInstance).</summary>
    public bool MultiInstance { get; set; }
    /// <summary>Windows: a Roblox version-… to stay on instead of the channel's latest; null follows the channel.</summary>
    public string? PinnedVersion { get; set; }
    /// <summary>The highlight colour across the app and the launch window.</summary>
    public AccentColor Accent { get; set; } = AccentColor.Mint;
    /// <summary>The see-through window background, where the system supports it.</summary>
    public WindowBackdrop Backdrop { get; set; } = WindowBackdrop.Acrylic;
    /// <summary>Windows: an .ico for the Blox Code shortcuts and windows; null keeps Bit.</summary>
    public string? CustomIcon { get; set; }
    /// <summary>Saved sets of Fast Flags, by name, to switch between with one click.</summary>
    public Dictionary<string, Dictionary<string, string>> FastFlagProfiles { get; set; } = new();
    public Dictionary<string, string> FastFlags { get; set; } = new();
}

public sealed class State
{
    public string? VersionGuid { get; set; }
    public string? Channel { get; set; }
    public Dictionary<string, string> PackageHashes { get; set; } = new();
    /// <summary>
    /// Relative paths currently overwritten by a mod: inside the version folder on Windows, inside
    /// Sober's asset_overlay on Linux.
    /// </summary>
    public List<string> ModManifest { get; set; } = new();
    public bool ForceReinstall { get; set; }
    /// <summary>Set by the upgrade step so the next menu can say "updated to vX" once.</summary>
    public string? JustUpdatedTo { get; set; }
    /// <summary>Linux: Sober's own fflags were pulled into FastFlags once, so the first sync can't drop them.</summary>
    public bool SoberFlagsImported { get; set; }
    /// <summary>
    /// Windows: a fixed graphics quality was just removed, so Roblox's per-game quality cache
    /// (frm.cfg, filled at that fixed level while it was on) is cleared before the next launch.
    /// </summary>
    public bool ResetQualityCache { get; set; }
    /// <summary>Windows: the Roblox Studio version installed next to the player, and its package hashes.</summary>
    public string? StudioVersionGuid { get; set; }
    public Dictionary<string, string> StudioPackageHashes { get; set; } = new();
}

public static class Store
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    // The bootstrapper saves state from a worker thread while the UI may save settings.
    private static readonly object Sync = new();

    public static Settings Settings { get; private set; } = new();
    public static State State { get; private set; } = new();

    /// <summary>Raised after the settings reach the disk; the Linux app pushes Fast Flags to Sober from here.</summary>
    public static event Action? SettingsSaved;

    public static void Load()
    {
        Settings = Read<Settings>(Paths.SettingsFile) ?? new Settings();
        State = Read<State>(Paths.StateFile) ?? new State();

        // Hand-edited or older files can carry explicit nulls; never let them reach the rest of the app.
        Settings.FastFlags ??= new();
        Settings.FastFlagProfiles ??= new();
        Settings.PinnedVersion = string.IsNullOrWhiteSpace(Settings.PinnedVersion) ? null : Settings.PinnedVersion.Trim();
        Settings.Channel = string.IsNullOrWhiteSpace(Settings.Channel) ? Settings.DefaultChannel : Settings.Channel.Trim();
        State.PackageHashes ??= new();
        State.ModManifest ??= new();

        if (FastFlagPresets.UpgradeBlurryPresets())
            SaveSettings();
    }

    public static void SaveSettings()
    {
        if (Write(Paths.SettingsFile, Settings))
            SettingsSaved?.Invoke();
    }

    public static void SaveState() => Write(Paths.StateFile, State);

    private static T? Read<T>(string path) where T : class
    {
        if (!File.Exists(path))
            return null;
        try
        {
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options);
        }
        catch (Exception ex)
        {
            // Keep the broken file for the user instead of silently overwriting it with defaults.
            Logger.Error("Store::Read", ex);
            try { File.Copy(path, path + ".corrupt", overwrite: true); } catch { }
            return null;
        }
    }

    private static bool Write<T>(string path, T value)
    {
        lock (Sync)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                // Write a sibling file and swap it in, so a crash mid-write can't leave half a JSON file.
                string temp = path + ".tmp";
                File.WriteAllText(temp, JsonSerializer.Serialize(value, Options));
                File.Move(temp, path, overwrite: true);
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error("Store::Write", ex);
                return false;
            }
        }
    }
}
