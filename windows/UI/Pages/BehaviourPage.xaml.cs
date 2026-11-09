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
        RefreshDiscordJoin();
        Store.SaveSettings();
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
