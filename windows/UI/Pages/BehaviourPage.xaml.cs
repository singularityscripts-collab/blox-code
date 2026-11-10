using System.Windows;
using System.Windows.Controls;
using BloxCode.Core;

namespace BloxCode.UI.Pages;

public partial class BehaviourPage : UserControl
{
    private readonly MainWindow _window;
    private readonly bool _ready;

    public BehaviourPage(MainWindow window)
    {
        _window = window;
        InitializeComponent();

        ChannelBox.Text = Store.Settings.Channel == Deployment.DefaultChannel ? "" : Store.Settings.Channel;
        Cleanup.IsChecked = Store.Settings.CleanupOldVersions;
        Confirm.IsChecked = Store.Settings.ConfirmLaunches;
        AutoUpdate.IsChecked = Store.Settings.AutoUpdate;
        Discord.IsChecked = Store.Settings.DiscordRichPresence && ActivityWatcher.DiscordAvailable;
        DiscordJoin.IsChecked = Store.Settings.DiscordJoinButton;
        Location.IsChecked = Store.Settings.ShowServerLocation;
        Remember.IsChecked = Store.Settings.RememberGames;
        Multi.IsChecked = Store.Settings.MultiInstance;
        RefreshMulti();
        PinBox.Text = Store.Settings.PinnedVersion ?? "";
        RefreshPin();
        _ = MeasureCleanupAsync();
        Discord.IsEnabled = ActivityWatcher.DiscordAvailable;
        if (!ActivityWatcher.DiscordAvailable)
            DiscordText.Text = "This build has no Discord app (set DiscordAppId in Directory.Build.props).";
        RefreshDiscordJoin();
        RefreshUpdateStatus();
        if (Store.State.ForceReinstall)
            ReinstallText.Text = "Scheduled — Roblox will be reinstalled on the next launch.";
        RefreshHandler();
        _ready = true;
    }

    private void RefreshHandler()
    {
        bool installed = Installer.IsInstalled;
        RegisterButton.IsEnabled = installed && !Installer.IsDefaultLauncher;
        UninstallButton.IsEnabled = installed;
        HandlerText.Text = !installed
            ? "Install Blox Code from Home first."
            : Installer.IsDefaultLauncher
                ? "Blox Code opens roblox:// and roblox-player: links."
                : $"Currently: {Installer.CurrentProtocolHandler() ?? "not registered"}";
    }

    private void OnChannelChanged(object sender, TextChangedEventArgs e)
    {
        if (!_ready) return;
        string channel = ChannelBox.Text.Trim().ToLowerInvariant();
        Store.Settings.Channel = channel.Length == 0 ? Deployment.DefaultChannel : channel;
    }

    private void OnToggle(object sender, RoutedEventArgs e)
    {
        Store.Settings.CleanupOldVersions = Cleanup.IsChecked == true;
        Store.Settings.ConfirmLaunches = Confirm.IsChecked == true;
        Store.Settings.AutoUpdate = AutoUpdate.IsChecked == true;
        if (ActivityWatcher.DiscordAvailable)
            Store.Settings.DiscordRichPresence = Discord.IsChecked == true;
        Store.Settings.DiscordJoinButton = DiscordJoin.IsChecked == true;
        Store.Settings.ShowServerLocation = Location.IsChecked == true;
        Store.Settings.RememberGames = Remember.IsChecked == true;
        Store.Settings.MultiInstance = Multi.IsChecked == true;
        RefreshMulti();
        RefreshDiscordJoin();
        Store.SaveSettings();
    }

    private void OnPinChanged(object sender, TextChangedEventArgs e)
    {
        if (!_ready) return;
        string pin = PinBox.Text.Trim().ToLowerInvariant();
        Store.Settings.PinnedVersion = pin.Length == 0 ? null : pin;
        RefreshPin();
    }

    private void OnPinCurrent(object sender, RoutedEventArgs e)
    {
        if (Store.State.VersionGuid is { } current)
            PinBox.Text = current;
    }

    private void RefreshPin()
    {
        string? pin = Store.Settings.PinnedVersion;
        PinButton.IsEnabled = Store.State.VersionGuid is { } current && current != pin;
        PinText.Text = pin is null
            ? "Stay on one version (version-…) instead of updating. Roblox may refuse old versions after a while."
            : Bootstrapper.PinnedVersionRegex().IsMatch(pin)
                ? $"Pinned: Roblox stays on {pin}. Clear the box to follow {Store.Settings.Channel} again."
                : "That isn't a version name: it looks like version-0123456789abcdef. Until it is, Roblox stays up to date.";
    }

    private void RefreshMulti()
    {
        int open = MultiInstance.RunningPlayers();
        string now = open switch { 0 => "", 1 => " 1 Roblox window is open now.", _ => $" {open} Roblox windows are open now." };
        MultiText.Text = Store.Settings.MultiInstance
            ? "Every Play, link or recent game opens one more window; the open ones stay." + now
            : "Every Play, link or recent game opens one more window instead of replacing the open one." + now;
        CloseAllButton.IsEnabled = open > 0;
    }

    private void OnClearHistory(object sender, RoutedEventArgs e)
    {
        if (MessageDialog.Ask(_window, "Clear your game history?", "Recent games and play time on Home start over.", "Clear", "Cancel", danger: true))
            GameHistory.Clear();
    }

    private void OnCloseAll(object sender, RoutedEventArgs e)
    {
        int open = MultiInstance.RunningPlayers();
        if (open == 0)
        {
            MessageDialog.Show(_window, "No Roblox open", "There's no Roblox window to close.");
            return;
        }
        if (MessageDialog.Ask(_window, open == 1 ? "Close Roblox?" : $"Close all {open} Roblox windows?", "Anything unsaved in those games is lost.", "Close all", "Cancel", danger: true))
        {
            MultiInstance.CloseAllPlayers();
            MultiInstance.RemoveIdleJunctions();
            RefreshMulti();
        }
    }

    private async Task MeasureCleanupAsync()
    {
        long bytes = await Task.Run(DiskCleanup.Measure);
        CleanText.Text = bytes < 1 << 20
            ? "Nothing worth cleaning: no old Roblox versions, downloads or logs piling up."
            : $"{DiskCleanup.Size(bytes)} in old Roblox versions, cached downloads and old logs.";
        CleanButton.IsEnabled = bytes >= 1 << 20;
    }

    private async void OnCleanUp(object sender, RoutedEventArgs e)
    {
        CleanButton.IsEnabled = false;
        CleanText.Text = "Cleaning…";
        long freed = await Task.Run(DiskCleanup.Run);
        CleanText.Text = $"Freed {DiskCleanup.Size(freed)}.";
    }

    private void RefreshDiscordJoin() => DiscordJoin.IsEnabled = Discord.IsEnabled && Discord.IsChecked == true;

    private void RefreshUpdateStatus(string? message = null)
    {
        bool usable = Updater.IsConfigured && Paths.IsRunningInstalled;
        AutoUpdate.IsEnabled = Updater.IsConfigured;
        CheckUpdatesButton.IsEnabled = usable;
        UpdateText.Text = message ?? (!Updater.IsConfigured
            ? $"v{Updater.Current} · this build has no update source (set UpdateFeeds and UpdatePublicKey in Directory.Build.props)."
            : !Paths.IsRunningInstalled
                ? $"v{Updater.Current} · install Blox Code from Home to get updates."
                : $"v{Updater.Current} · signed releases come from {Updater.FeedHost}{(Updater.Feeds.Count > 1 ? $" (+{Updater.Feeds.Count - 1} backup{(Updater.Feeds.Count > 2 ? "s" : "")})" : "")}.");
    }

    private async void OnCheckUpdates(object sender, RoutedEventArgs e)
    {
        CheckUpdatesButton.IsEnabled = false;
        RefreshUpdateStatus("Checking…");
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var update = await Updater.CheckAsync(App.Http, cts.Token);
            if (update is null)
            {
                RefreshUpdateStatus($"v{Updater.Current} · you're on the latest version.");
                return;
            }

            if (MessageDialog.Ask(_window, $"Blox Code v{update.Version} is out", $"You have v{Updater.Current}. Update and reinstall now? Blox Code will reopen right after.", "Update now", "Later"))
            {
                Store.SaveSettings();
                var updater = BootstrapperWindow.ForUpdate(update, Array.Empty<string>());
                Application.Current.MainWindow = updater;
                updater.Show();
                _window.Close();
                return;
            }
            RefreshUpdateStatus($"v{update.Version} is available — it installs automatically on the next start.");
        }
        catch (Exception ex)
        {
            Logger.Warn("BehaviourPage::OnCheckUpdates", ex.Message);
            RefreshUpdateStatus($"Couldn't check for updates: {ex.Message}");
        }
        finally
        {
            CheckUpdatesButton.IsEnabled = Updater.IsConfigured && Paths.IsRunningInstalled;
        }
    }

    private void OnRegister(object sender, RoutedEventArgs e)
    {
        Installer.RegisterProtocols();
        RefreshHandler();
    }

    private void OnForceReinstall(object sender, RoutedEventArgs e)
    {
        Store.State.ForceReinstall = true;
        Store.SaveState();
        ReinstallText.Text = "Scheduled — Roblox will be reinstalled on the next launch.";
    }

    private void OnUninstall(object sender, RoutedEventArgs e)
    {
        if (!MessageDialog.Ask(_window, "Uninstall Blox Code?",
                "This removes Blox Code, its shortcuts and the Roblox files it downloaded.", "Uninstall", "Cancel", danger: true))
            return;

        Installer.Uninstall();
        Application.Current.Shutdown();
    }
}
