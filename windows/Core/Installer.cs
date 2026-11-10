using System.Diagnostics;
using Microsoft.Win32;

namespace BloxCode.Core;

/// <summary>
/// Installs Blox Code per-user (no admin): copies itself to %LocalAppData%\BloxCode, registers the
/// roblox / roblox-player protocols, an Apps &amp; Features entry and shortcuts. Same registry layout
/// that Bloxstrap.Installer was observed to use.
/// </summary>
public static class Installer
{
    private const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\BloxCode";
    private static readonly string[] Protocols = { "roblox", "roblox-player" };

    public static string DesktopShortcut => Path.Combine(Paths.Desktop, "Blox Code.lnk");
    public static string StartMenuShortcut => Path.Combine(Paths.StartMenu, "Blox Code.lnk");
    public static string PlayShortcut => Path.Combine(Paths.StartMenu, "Play Roblox (Blox Code).lnk");

    public static bool IsInstalled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(UninstallKey);
            return key is not null && File.Exists(Paths.Application);
        }
    }

    /// <summary>The command currently registered for roblox-player:, if any.</summary>
    public static string? CurrentProtocolHandler()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Classes\roblox-player\shell\open\command");
        return key?.GetValue("") as string;
    }

    public static bool IsDefaultLauncher =>
        CurrentProtocolHandler()?.Contains(Paths.Application, StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>
    /// Roblox's own player and updater register roblox-player: for themselves whenever they install
    /// or update, which silently undoes Blox Code's links. Like Bloxstrap, an installed Blox Code
    /// takes them back on every run; another launcher (Bloxstrap, Fishstrap…) is left alone and the
    /// Home banner asks instead. Returns true when the links were taken back.
    /// </summary>
    public static bool ReclaimLinksFromRoblox()
    {
        const string ident = "Installer::ReclaimLinksFromRoblox";
        try
        {
            if (!IsInstalled || IsDefaultLauncher)
                return false;
            string? handler = CurrentProtocolHandler();
            if (handler is not null && !IsRobloxsOwn(handler))
                return false;

            RegisterProtocols();
            Logger.Info(ident, $"Roblox links pointed at {handler ?? "nothing"}; registered Blox Code again");
            return true;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            Logger.Warn(ident, ex.Message);
            return false;
        }
    }

    /// <summary>A command that starts Roblox's own player or launcher rather than another bootstrapper.</summary>
    public static bool IsRobloxsOwn(string command) =>
        command.Contains("RobloxPlayerBeta.exe", StringComparison.OrdinalIgnoreCase)
        || command.Contains("RobloxPlayerLauncher.exe", StringComparison.OrdinalIgnoreCase)
        || command.Contains(@"\Roblox\Versions\", StringComparison.OrdinalIgnoreCase);

    public static void Install(bool desktopShortcut)
    {
        const string ident = "Installer::Install";
        Paths.EnsureCreated();

        if (!Paths.IsRunningInstalled)
            File.Copy(Paths.Process, Paths.Application, overwrite: true);

        RegisterProtocols();

        using (var key = Registry.CurrentUser.CreateSubKey(UninstallKey))
        {
            string version = typeof(Installer).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";
            key.SetValue("DisplayName", "Blox Code");
            key.SetValue("DisplayIcon", IconLocation);
            key.SetValue("DisplayVersion", version);
            key.SetValue("Publisher", "Blox Code");
            key.SetValue("InstallLocation", Paths.Base);
            key.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"));
            key.SetValue("UninstallString", $"\"{Paths.Application}\" -uninstall");
            key.SetValue("QuietUninstallString", $"\"{Paths.Application}\" -uninstall -quiet");
            key.SetValue("ModifyPath", $"\"{Paths.Application}\" -settings");
            key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        }

        // A shortcut failing (locked file, odd shell setup) shouldn't leave a half-done install.
        TryShortcut(StartMenuShortcut, "", "Blox Code settings");
        TryShortcut(PlayShortcut, "-player", "Play Roblox with Blox Code");
        if (desktopShortcut)
            TryShortcut(DesktopShortcut, "", "Blox Code");
        RefreshIcons();

        File.WriteAllText(Path.Combine(Paths.Modifications, "README.txt"),
            "Files placed here are copied over the Roblox installation every launch.\r\n" +
            "Mirror Roblox's folder layout, e.g. content\\sounds\\ouch.ogg.\r\n" +
            "Delete a file to restore the original.\r\n");

        Logger.Info(ident, "Installation finished");
    }

    /// <summary>The icon picked in Appearance (copied next to Blox Code), when there is one.</summary>
    public static string? CustomIcon => Store.Settings.CustomIcon is { } icon && File.Exists(icon) ? icon : null;

    private static string IconLocation => CustomIcon is { } icon ? $"{icon},0" : $"{Paths.Application},0";

    /// <summary>Uses <paramref name="source"/> (an .ico) for the shortcuts and the Apps entry; null goes back to Bit.</summary>
    public static void SetCustomIcon(string? source)
    {
        string target = Path.Combine(Paths.Base, "CustomIcon.ico");
        if (source is null)
        {
            Store.Settings.CustomIcon = null;
            try { File.Delete(target); } catch (IOException) { }
        }
        else
        {
            if (!string.Equals(Path.GetFullPath(source), target, StringComparison.OrdinalIgnoreCase))
                File.Copy(source, target, overwrite: true);
            Store.Settings.CustomIcon = target;
        }
        Store.SaveSettings();
        if (!IsInstalled)
            return;

        // Re-create the shortcuts that exist with the new icon, and point Apps & Features at it.
        if (File.Exists(StartMenuShortcut)) TryShortcut(StartMenuShortcut, "", "Blox Code settings");
        if (File.Exists(PlayShortcut)) TryShortcut(PlayShortcut, "-player", "Play Roblox with Blox Code");
        if (File.Exists(DesktopShortcut)) TryShortcut(DesktopShortcut, "", "Blox Code");
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(UninstallKey, writable: true);
            key?.SetValue("DisplayIcon", IconLocation);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            Logger.Warn("Installer::SetCustomIcon", ex.Message);
        }
        RefreshIcons();
    }

    private static void TryShortcut(string lnkPath, string arguments, string description)
    {
        try
        {
            ShellLink.Create(lnkPath, Paths.Application, arguments, description, CustomIcon);
        }
        catch (Exception ex)
        {
            Logger.Warn("Installer::TryShortcut", $"Couldn't create {lnkPath}: {ex.Message}");
        }
    }

    /// <summary>
    /// After a self-update: keep Apps &amp; Features showing the right version, and make the
    /// shortcuts show the new exe's icon (Explorer keeps the old one cached per path otherwise).
    /// </summary>
    public static void RefreshAfterUpgrade()
    {
        using (var key = Registry.CurrentUser.OpenSubKey(UninstallKey, writable: true))
            key?.SetValue("DisplayVersion", Updater.Current.ToString());
        RefreshIcons();
    }

    /// <summary>
    /// Rewrites the shortcuts that exist (a deleted desktop icon stays deleted) and tells the shell
    /// the exe, the shortcuts and the icon associations changed, so it extracts the icon again.
    /// </summary>
    public static void RefreshIcons()
    {
        var shortcuts = new (string Path, string Arguments, string Description)[]
        {
            (StartMenuShortcut, "", "Blox Code settings"),
            (PlayShortcut, "-player", "Play Roblox with Blox Code"),
            (DesktopShortcut, "", "Blox Code"),
        };
        foreach (var (lnk, arguments, description) in shortcuts)
            if (File.Exists(lnk))
                TryShortcut(lnk, arguments, description);

        try
        {
            ShellLink.NotifyChanged(Paths.Application);
            foreach (var (lnk, _, _) in shortcuts)
                if (File.Exists(lnk))
                    ShellLink.NotifyChanged(lnk);
            ShellLink.NotifyAssociationsChanged();
        }
        catch (Exception ex)
        {
            Logger.Warn("Installer::RefreshIcons", ex.Message);
        }
    }

    public static void RegisterProtocols()
    {
        foreach (string protocol in Protocols)
        {
            using var key = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{protocol}");
            key.SetValue("", $"URL: {protocol} Protocol");
            key.SetValue("URL Protocol", "");

            using (var icon = key.CreateSubKey("DefaultIcon"))
                icon.SetValue("", $"{Paths.Application},0");
            using (var command = key.CreateSubKey(@"shell\open\command"))
                command.SetValue("", $"\"{Paths.Application}\" -player \"%1\"");
        }
    }

    public static void Uninstall()
    {
        const string ident = "Installer::Uninstall";

        // Only drop the protocol keys if they still point at us; another launcher may have taken over.
        if (IsDefaultLauncher)
            foreach (string protocol in Protocols)
                Registry.CurrentUser.DeleteSubKeyTree($@"Software\Classes\{protocol}", throwOnMissingSubKey: false);

        Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, throwOnMissingSubKey: false);

        foreach (string lnk in new[] { DesktopShortcut, StartMenuShortcut, PlayShortcut })
        {
            try { File.Delete(lnk); } catch { }
        }

        // Junctions first, so nothing can walk through them into the version folders.
        try { MultiInstance.RemoveIdleJunctions(); } catch (Exception ex) { Logger.Warn(ident, ex.Message); }
        Logger.Info(ident, "Removing install folder after exit");

        // The running exe can't delete itself, so let cmd wait for us to exit first. `timeout` refuses
        // to run without a console (and `&&` would then skip the delete), so ping is the delay.
        Process.Start(new ProcessStartInfo("cmd.exe", $"/c ping -n 6 127.0.0.1 >nul & rmdir /S /Q \"{Paths.Base}\"")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
        });
    }
}
