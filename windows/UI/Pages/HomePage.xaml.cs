using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using BloxCode.Core;

namespace BloxCode.UI.Pages;

public partial class HomePage : UserControl
{
    private readonly MainWindow _window;

    public HomePage(MainWindow window)
    {
        _window = window;
        InitializeComponent();

        Mascot.IsAnimated = Store.Settings.AnimateMascot;
        Greeting.Text = DateTime.Now.Hour switch
        {
            < 5 => "Up late?",
            < 12 => "Good morning",
            < 18 => "Good afternoon",
            _ => "Good evening",
        };
        ChannelPill.Text = Store.Settings.Channel;
        StatusLine.Text = Store.State.VersionGuid is { } v
            ? $"roblox {v} · {Store.Settings.FastFlags.Count} fast flags · {Store.State.ModManifest.Count} mods"
            : "Roblox will be downloaded on first launch";

        foreach (var (label, value) in GraphicsPresetText.Options)
        {
            var segment = new RadioButton { Content = label, GroupName = "homeGraphics" + GetHashCode(), Tag = value };
            segment.SetResourceReference(StyleProperty, "Segment");
            segment.Checked += (_, _) =>
            {
                if (_syncing) return;
                FastFlagPresets.ApplyGraphicsPreset(Enum.Parse<GraphicsPreset>(value!));
                Store.SaveSettings();
                RefreshQuickSettings();
            };
            GraphicsSegments.Children.Add(segment);
        }
        RefreshQuickSettings();

        RefreshBanner();
        Loaded += (_, _) => Composer.Focus();
    }

    private string? _updatedNotice;
    private bool _syncing;

    /// <summary>The graphics preset, which mods are swapped and how many flags are set.</summary>
    private void RefreshQuickSettings()
    {
        _syncing = true;
        var preset = FastFlagPresets.CurrentGraphicsPreset;
        foreach (var segment in GraphicsSegments.Children.OfType<RadioButton>())
            segment.IsChecked = (string?)segment.Tag == preset?.ToString();
        GraphicsSummary.Text = GraphicsPresetText.Short(preset)
            + (GraphicsAdvisor.Recommended is { } suggested && suggested != preset ? $" · suggested: {suggested}" : "");
        _syncing = false;

        var swapped = ModCatalog.Slots.Where(s => ModCatalog.CurrentFile(s) is not null).Select(s => s.Title.ToLowerInvariant()).ToList();
        ModsSummary.Text = swapped.Count == 0 ? "Cursor, sounds and font" : "Custom " + string.Join(", ", swapped);
        int custom = Store.Settings.FastFlags.Keys.Count(k => !FastFlagPresets.PresetFlags.Contains(k));
        FlagsSummary.Text = custom switch { 0 => "Presets and the editor", 1 => "1 custom flag", _ => $"{custom} custom flags" };
    }

    private void RefreshBanner()
    {
        // Shown once, the first time the menu opens after a self-update.
        if (_updatedNotice is null && Store.State.JustUpdatedTo is { } version)
        {
            _updatedNotice = version;
            Store.State.JustUpdatedTo = null;
            Store.SaveState();
        }

        if (_updatedNotice is not null)
        {
            Banner.Visibility = Visibility.Visible;
            BannerTitle.Text = $"Updated to v{_updatedNotice}";
            BannerText.Text = "Blox Code reinstalled itself with the latest release.";
            BannerButton.Content = Updater.Repository is null ? "Got it" : "What's new";
            DesktopShortcut.Visibility = DesktopShortcutLabel.Visibility = Visibility.Collapsed;
        }
        else if (!Installer.IsInstalled)
        {
            Banner.Visibility = Visibility.Visible;
            BannerTitle.Text = "Blox Code isn't installed yet";
            BannerText.Text = "Install it so roblox:// links open here, with Start menu shortcuts and an Apps & Features entry.";
            BannerButton.Content = "Install";
        }
        else if (!Installer.IsDefaultLauncher)
        {
            string current = Installer.CurrentProtocolHandler() is { } cmd ? ExecutableName(cmd) : "nothing";
            Banner.Visibility = Visibility.Visible;
            BannerTitle.Text = "Roblox links don't open Blox Code";
            BannerText.Text = $"roblox-player: is currently handled by {current}.";
            BannerButton.Content = "Use Blox Code";
            DesktopShortcut.Visibility = DesktopShortcutLabel.Visibility = Visibility.Collapsed;
        }
        else
        {
            Banner.Visibility = Visibility.Collapsed;
        }
    }

    private static string ExecutableName(string command)
    {
        var match = Regex.Match(command, "\"([^\"]+\\.exe)\"|(\\S+\\.exe)", RegexOptions.IgnoreCase);
        return match.Success ? Path.GetFileName(match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value) : command;
    }

    private void OnBannerAction(object sender, RoutedEventArgs e)
    {
        if (_updatedNotice is not null)
        {
            if (Updater.Repository is { } repo)
                Shell.Open($"https://github.com/{repo}/releases/tag/v{_updatedNotice}", _window);
            DesktopShortcut.Visibility = DesktopShortcutLabel.Visibility = Visibility.Visible;
            _updatedNotice = null;
            Banner.Visibility = Visibility.Collapsed;
            RefreshBanner();
            return;
        }

        try
        {
            if (!Installer.IsInstalled)
            {
                Installer.Install(DesktopShortcut.IsChecked == true);
                MessageDialog.Show(_window, "Installed", "Blox Code now opens Roblox links. You can launch it from the Start menu.");
            }
            else
            {
                Installer.RegisterProtocols();
            }
        }
        catch (Exception ex)
        {
            Logger.Error("HomePage::OnBannerAction", ex);
            MessageDialog.Show(_window, "Couldn't finish", ex.Message);
        }

        RefreshBanner();
        _window.RefreshFooter();
    }

    private void OnComposerChanged(object sender, TextChangedEventArgs e)
    {
        bool valid = RobloxLink.ToLaunchArgs(Composer.Text) is not null;
        SendButton.IsEnabled = valid;
        ComposerHint.Text = Composer.Text.Trim().Length == 0
            ? "Enter to launch · empty opens the Roblox app"
            : valid ? "Enter to join this experience" : "That doesn't look like a Roblox game link";
    }

    private void OnComposerKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            OnSend(sender, e);
    }

    private void OnSend(object sender, RoutedEventArgs e)
    {
        if (RobloxLink.ToLaunchArgs(Composer.Text) is { } args)
            _window.LaunchRoblox(args);
    }

    private void OnOpenRoblox(object sender, RoutedEventArgs e) => _window.LaunchRoblox();

    private void OnFastFlags(object sender, RoutedEventArgs e) => _window.Navigate("FastFlags");

    private void OnMods(object sender, RoutedEventArgs e) => _window.Navigate("Mods");

    private void OnReinstall(object sender, RoutedEventArgs e)
    {
        if (!MessageDialog.Ask(_window, "Reinstall Roblox?", "Blox Code will re-download and re-extract Roblox the next time it launches.", "Reinstall now", "Cancel"))
            return;
        Store.State.ForceReinstall = true;
        Store.SaveState();
        _window.LaunchRoblox();
    }
}
