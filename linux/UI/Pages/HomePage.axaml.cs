using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using BloxCode.Core;
using BloxCode.Linux.Platform;
using BloxCode.Linux.Runtime;

namespace BloxCode.Linux.UI.Pages;

public partial class HomePage : UserControl
{
    private readonly MainWindow _window;
    private string? _updatedNotice;
    private SoberStatus? _sober;
    private string? _nvidiaWarning;
    private bool _isDefault = true;
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
        StatusLine.Text = "checking sober…";

        // Shown once, the first time the menu opens after a self-update.
        if (Store.State.JustUpdatedTo is { } version)
        {
            _updatedNotice = version;
            Store.State.JustUpdatedTo = null;
            Store.SaveState();
        }

        // Keep the composer centred: the layout grid is at least as tall as the visible area.
        Scroller.GetObservable(ScrollViewer.ViewportProperty).Subscribe(new SizeObserver(size => Layout.MinHeight = size.Height));

        foreach (var (label, value) in GraphicsPresetText.Options)
        {
            var segment = new RadioButton { Content = label, GroupName = "homeGraphics" + GetHashCode(), Tag = value, Theme = Res.Theme("Segment") };
            segment.IsCheckedChanged += (_, _) =>
            {
                if (_syncing || segment.IsChecked != true) return;
                FastFlagPresets.ApplyGraphicsPreset(Enum.Parse<GraphicsPreset>(value!));
                Store.SaveSettings();
                RefreshQuickSettings();
            };
            GraphicsSegments.Children.Add(segment);
        }
        RefreshQuickSettings();

        RenderBanners();
        AttachedToVisualTree += async (_, _) =>
        {
            Composer.Focus();
            await RefreshAsync();
        };
    }

    /// <summary>Looks at Sober, the link handler and the GPU again, then redraws the banners.</summary>
    public async Task RefreshAsync()
    {
        try
        {
            _sober = await Sober.DetectAsync();
            _isDefault = !DesktopIntegration.IsInstalled || await DesktopIntegration.IsDefaultAsync();
            _nvidiaWarning = _sober.State == SoberState.Installed ? await Sober.CheckNvidiaAsync() : null;
        }
        catch (Exception ex)
        {
            Logger.Error("HomePage::Refresh", ex);
        }

        int mods = ModSync.ListMods().Count;
        int flags = Store.Settings.FastFlags.Count;
        StatusLine.Text = _sober?.State switch
        {
            SoberState.Installed => $"sober {_sober.Version ?? "installed"} · {flags} fast flags · {mods} mods",
            SoberState.NotInstalled => "sober isn't installed yet",
            SoberState.NoFlatpak => "flatpak isn't installed",
            _ => "",
        };
        RenderBanners();
    }

    private void RenderBanners()
    {
        Banners.Children.Clear();

        if (_updatedNotice is { } updated)
        {
            Banners.Children.Add(Banner($"Updated to v{updated}", "Blox Code reinstalled itself with the latest release.",
                new[] { (Updater.Repository is null ? "Got it" : "What's new", (Action)(() =>
                {
                    if (Updater.Repository is { } repo)
                        Shell.Open($"https://github.com/{repo}/releases/tag/v{updated}", _window);
                    _updatedNotice = null;
                    RenderBanners();
                })) }));
        }

        switch (_sober?.State)
        {
            case SoberState.NoFlatpak:
                Banners.Children.Add(Banner("Sober needs Flatpak",
                    $"Roblox runs on Linux through Sober, which comes from Flathub. Install Flatpak on {Distro.Current.Name} first (this one needs your password), then come back here.",
                    commands: new[] { Distro.Current.InstallFlatpakCommand },
                    buttons: new[] { ("Check again", (Action)(() => _ = RefreshAsync())) }));
                break;

            case SoberState.NotInstalled:
                Banners.Children.Add(Banner("Install Sober to play",
                    "Sober runs Roblox natively on Linux. Blox Code installs it for your user from Flathub — no password needed, about 1 GB the first time.",
                    buttons: new[] { ("Install Sober", (Action)InstallSober) }));
                break;
        }

        if (_nvidiaWarning is { } nvidia)
        {
            Banners.Children.Add(Banner("Sober may run without your NVIDIA GPU", nvidia +
                " Updating Flatpak installs the matching extension.", commands: new[] { "flatpak update" }, warning: true));
        }

        if (!DesktopIntegration.IsInstalled)
        {
            Banners.Children.Add(DesktopIntegration.CanInstall
                ? Banner("Blox Code isn't installed yet",
                    "Install it so roblox:// links open here, with an app menu entry and automatic updates.",
                    buttons: new[] { ("Install", (Action)(() => _ = InstallAsync())) })
                : Banner("Running from a build folder",
                    "Install from BloxCode-x86_64.AppImage to get the app menu entry, roblox:// links and updates."));
        }
        else if (!_isDefault)
        {
            Banners.Children.Add(Banner("Roblox links don't open Blox Code",
                "Your browser hands roblox:// links to something else (usually Sober itself), so Fast Flags and mods only apply when you start from here.",
                buttons: new[] { ("Use Blox Code", (Action)(() => _ = RegisterAsync())) }));
        }
    }

    private Border Banner(string title, string text, (string Label, Action Action)[]? buttons = null, string[]? commands = null, bool warning = false)
    {
        var body = new StackPanel();
        body.Children.Add(new TextBlock { Text = title, Classes = { "h2" } });
        body.Children.Add(new TextBlock { Text = text, Classes = { "muted" }, Margin = new Thickness(0, 3, 0, 0) });
        foreach (string command in commands ?? Array.Empty<string>())
            body.Children.Add(UI.CommandRow.Create(command));

        var row = new DockPanel();
        buttons ??= Array.Empty<(string, Action)>();
        if (buttons.Length > 0)
        {
            var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0), Spacing = 6 };
            for (int i = 0; i < buttons.Length; i++)
            {
                var (label, action) = buttons[i];
                var button = new Button { Content = label };
                button.Classes.Add(i == 0 ? "primary" : "secondary");
                button.Click += (_, _) => action();
                actions.Children.Add(button);
            }
            DockPanel.SetDock(actions, Dock.Right);
            row.Children.Add(actions);
        }
        row.Children.Add(body);

        var card = new Border { Classes = { "card", "banner" }, Child = row };
        if (warning)
            Res.BindBrush(card, Border.BorderBrushProperty, "Brush.Warning");
        return card;
    }

    private async void InstallSober()
    {
        if (!await MessageDialog.AskAsync(_window, "Install Sober?",
                $"Blox Code will run:\n\n{Sober.InstallCommand}\n\nfor your user (no sudo). Sober's runtime is a large download the first time.",
                "Install", "Cancel"))
            return;

        var window = LaunchWindow.ForSoberInstall(() => _ = RefreshAsync());
        await window.ShowDialog(_window);
        await RefreshAsync();
    }

    private async Task InstallAsync()
    {
        try
        {
            await DesktopIntegration.InstallAsync(App.Icon);
            await MessageDialog.ShowAsync(_window, "Installed",
                "Blox Code is in your app menu now and opens Roblox links. You can delete the AppImage you downloaded.");
        }
        catch (Exception ex)
        {
            Logger.Error("HomePage::Install", ex);
            await MessageDialog.ShowAsync(_window, "Couldn't finish", ex.Message);
        }
        _window.RefreshFooter();
        await RefreshAsync();
    }

    private async Task RegisterAsync()
    {
        try
        {
            await DesktopIntegration.RegisterSchemesAsync();
        }
        catch (Exception ex)
        {
            Logger.Error("HomePage::Register", ex);
            await MessageDialog.ShowAsync(_window, "Couldn't finish", ex.Message);
        }
        await RefreshAsync();
    }

    private void OnComposerChanged(object? sender, TextChangedEventArgs e)
    {
        string text = Composer.Text ?? "";
        bool valid = RobloxLink.ToLaunchArgs(text) is not null;
        SendButton.IsEnabled = valid;
        ComposerHint.Text = text.Trim().Length == 0
            ? "Enter to launch · empty opens the Roblox app"
            : valid ? "Enter to join this experience" : "That doesn't look like a Roblox game link";
    }

    private void OnComposerKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            OnSend(sender, e);
    }

    private void OnSend(object? sender, RoutedEventArgs e)
    {
        if (RobloxLink.ToLaunchArgs(Composer.Text ?? "") is { } args)
            _window.LaunchRoblox(args);
    }

    private void OnOpenRoblox(object? sender, RoutedEventArgs e) => _window.LaunchRoblox();

    private void OnFastFlags(object? sender, RoutedEventArgs e) => _window.Navigate("FastFlags");

    private void OnMods(object? sender, RoutedEventArgs e) => _window.Navigate("Mods");

    private void OnSober(object? sender, RoutedEventArgs e) => _window.Navigate("Sober");

    private sealed class SizeObserver(Action<Size> next) : IObserver<Size>
    {
        public void OnCompleted() { }
        public void OnError(Exception error) { }
        public void OnNext(Size value) => next(value);
    }
}
