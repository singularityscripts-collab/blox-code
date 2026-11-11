using BloxCode.Core;

namespace BloxCode.Linux.Runtime;

/// <summary>Sober (or Flatpak) is missing: the menu, not the launch window, is where that gets fixed.</summary>
public sealed class SoberMissingException(string message) : Exception(message);

/// <summary>
/// The Linux counterpart of the Windows bootstrapper. Sober keeps Roblox itself up to date, so a
/// launch is: find Sober, write the Fast Flags into its config, sync mods into its overlay, start it.
/// </summary>
public sealed class SoberLauncher(LaunchSettings launch, IProgress<BootstrapProgress> progress)
{
    public string? SoberVersion { get; private set; }

    /// <summary>When Sober was started (null when it wasn't); its log is written from then on.</summary>
    public DateTime? LaunchedAt { get; private set; }

    public List<string> FailedMods { get; private set; } = new();

    public async Task RunAsync(CancellationToken ct)
    {
        const string ident = "SoberLauncher::Run";

        Report(BootstrapPhase.Connecting, "Looking for Sober");
        var status = await Sober.DetectAsync(ct);
        switch (status.State)
        {
            case SoberState.NoFlatpak:
                throw new SoberMissingException("Flatpak isn't installed, and Sober needs it. Open Blox Code to see how to install it.");
            case SoberState.NotInstalled:
                throw new SoberMissingException("Sober isn't installed yet. Open Blox Code and click Install Sober.");
        }
        SoberVersion = status.Version;
        Logger.Info(ident, $"Sober {status.Version ?? "?"} ({status.Installation ?? "?"})");

        Report(BootstrapPhase.Installing, "Applying your Fast Flags", 0.4);
        ct.ThrowIfCancellationRequested();
        SoberConfig.ApplyFastFlags();

        Report(BootstrapPhase.Installing, "Syncing mods", 0.7);
        ct.ThrowIfCancellationRequested();
        ApkAssets.RefreshCustomFont();
        FailedMods = ModSync.Sync().Failed;

        if (launch.NoLaunch)
        {
            Report(BootstrapPhase.Done, "Sober is ready", 1);
            return;
        }

        if (Sober.IsRunning)
            Logger.Info(ident, "Sober is already running; new settings apply the next time it starts");

        Report(BootstrapPhase.Launching, "Starting Sober", 1);
        LaunchedAt = DateTime.UtcNow;
        await Sober.LaunchAsync(launch.RobloxArgs, ct);
        Report(BootstrapPhase.Done, "Roblox is running", 1);
    }

    private void Report(BootstrapPhase phase, string status, double? fraction = null) =>
        progress.Report(new BootstrapProgress(phase, status, fraction));
}
