namespace BloxCode.Core;

public enum LaunchMode { Menu, Player, Studio, Uninstall }

public sealed class LaunchSettings
{
    public LaunchMode Mode { get; private set; } = LaunchMode.Menu;

    /// <summary>The roblox:/roblox-player: URI (or other args) handed straight to RobloxPlayerBeta.exe.</summary>
    public string RobloxArgs { get; private set; } = "";

    /// <summary>
    /// The settings were asked for by name (-menu, -settings). Without it, a plain start shows the
    /// launch menu first, like Bloxstrap: play Roblox right away or open the settings.
    /// </summary>
    public bool ExplicitMenu { get; private set; }

    public bool Quiet { get; private set; }
    public bool NoLaunch { get; private set; }
    public bool Force { get; private set; }
    public string? Channel { get; private set; }

    /// <summary>Running as a freshly downloaded update that must replace the installed copy.</summary>
    public bool Upgrade { get; private set; }

    /// <summary>Skip the self-update check (set when relaunching right after an update).</summary>
    public bool NoUpdate { get; private set; }

    /// <summary>The original arguments minus the update plumbing, to pass along across a restart.</summary>
    public string[] ForwardArgs { get; private set; } = Array.Empty<string>();

    public static LaunchSettings Parse(string[] args)
    {
        var s = new LaunchSettings
        {
            ForwardArgs = args.Where(a => !IsFlag(a, "upgrade") && !IsFlag(a, "noupdate")).ToArray(),
        };

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];

            if (RobloxLink.IsStudioUri(arg))
            {
                s.Mode = LaunchMode.Studio;
                s.RobloxArgs = arg;
                continue;
            }

            if (RobloxLink.IsRobloxUri(arg))
            {
                s.Mode = LaunchMode.Player;
                s.RobloxArgs = arg;
                continue;
            }

            switch (arg.TrimStart('-', '/').ToLowerInvariant())
            {
                case "player":
                    s.Mode = LaunchMode.Player;
                    if (i + 1 < args.Length && !args[i + 1].StartsWith('-'))
                        s.RobloxArgs = args[++i];
                    break;
                case "studio":
                    s.Mode = LaunchMode.Studio;
                    if (i + 1 < args.Length && !args[i + 1].StartsWith('-'))
                        s.RobloxArgs = args[++i];
                    break;
                case "menu":
                case "settings":
                case "preferences":
                    s.Mode = LaunchMode.Menu;
                    s.ExplicitMenu = true;
                    break;
                case "uninstall":
                    s.Mode = LaunchMode.Uninstall;
                    break;
                case "quiet":
                    s.Quiet = true;
                    break;
                case "nolaunch":
                    s.NoLaunch = true;
                    break;
                case "force":
                    s.Force = true;
                    break;
                case "channel":
                    if (i + 1 < args.Length)
                        s.Channel = args[++i];
                    break;
                case "upgrade":
                    s.Upgrade = true;
                    break;
                case "noupdate":
                    s.NoUpdate = true;
                    break;
                default:
                    Logger.Warn("LaunchSettings::Parse", $"Unknown argument: {arg}");
                    break;
            }
        }

        return s;
    }

    private static bool IsFlag(string arg, string name) =>
        arg.TrimStart('-', '/').Equals(name, StringComparison.OrdinalIgnoreCase) && (arg.StartsWith('-') || arg.StartsWith('/'));

    public static LaunchSettings ForPlayer(string robloxArgs = "") =>
        new() { Mode = LaunchMode.Player, RobloxArgs = robloxArgs };

    public static LaunchSettings ForStudio(string studioArgs = "") =>
        new() { Mode = LaunchMode.Studio, RobloxArgs = studioArgs };
}
