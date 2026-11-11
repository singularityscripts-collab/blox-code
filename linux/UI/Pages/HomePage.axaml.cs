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

    public HomePage(MainWindow window)
    {
        _window = window;
        InitializeComponent();

        StatusLine.Text = "checking sober…";

        // Shown once, the first time the menu opens after a self-update.
        if (Store.State.JustUpdatedTo is { } version)
        {
            _updatedNotice = version;
            Store.State.JustUpdatedTo = null;
            Store.SaveState();
        }

        // Keep the game bar centred: the layout grid is at least as tall as the visible area.
        Scroller.GetObservable(ScrollViewer.ViewportProperty).Subscribe(new SizeObserver(size => Layout.MinHeight = size.Height));

        RenderBanners();
        RefreshRecent();
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

    private static (string Title, string Body)? NotesFor(string version) =>
        Version.TryParse(version, out var v) ? ReleaseNotes.For(v) : null;

    private void RenderBanners()
    {
        Banners.Children.Clear();

        if (_updatedNotice is { } updated)
        {
            Banners.Children.Add(Banner($"Updated to v{updated}", "Blox Code reinstalled itself with the latest release.",
                new[] { (NotesFor(updated) is null ? "Got it" : "What's new", (Action)(() =>
                {
                    if (NotesFor(updated) is { } notes)
                        _ = MessageDialog.ShowAsync(_window, $"What's new in v{updated}" + (notes.Title.Length > 0 ? $" — {notes.Title}" : ""), notes.Body);
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
        bool empty = text.Trim().Length == 0;
        bool valid = RobloxLink.ToLaunchArgs(text) is not null;
        SendButton.IsEnabled = empty || valid;
        ComposerHint.Text = empty
            ? "Enter to launch · empty opens Roblox"
            : valid ? "Enter to join this experience" : "That doesn't look like a Roblox game link";
    }

    private void OnComposerKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            OnSend(sender, e);
    }

    private void OnSend(object? sender, RoutedEventArgs e)
    {
        if ((Composer.Text ?? "").Trim().Length == 0)
            _window.LaunchRoblox();
        else if (RobloxLink.ToLaunchArgs(Composer.Text ?? "") is { } args)
            _window.LaunchRoblox(args);
    }

    private void OnSober(object? sender, RoutedEventArgs e) => _window.Navigate("Sober");

    /// <summary>The last games played (read from Sober's log while it ran), as cards that start them again.</summary>
    private void RefreshRecent()
    {
        try
        {
            var history = Store.Settings.RememberGames ? GameHistory.Load() : new();
            var recent = GameHistory.Recent(4, history);
            RecentSection.IsVisible = recent.Count > 0;
            RecentGames.Children.Clear();
            foreach (var game in recent)
                RecentGames.Children.Add(GameCard(game));
            var week = GameHistory.Stats(TimeSpan.FromDays(7), history);
            StatsText.Text = week.Total < TimeSpan.FromMinutes(1) ? "" : $"{GameHistory.Duration(week.Total)} this week";
        }
        catch (Exception ex)
        {
            // A nicety: whatever is wrong with the history must never keep the menu from opening.
            Logger.Error("HomePage::RefreshRecent", ex);
            RecentSection.IsVisible = false;
        }
    }

    private Button GameCard(PlayedGame game)
    {
        var icon = new Border { Height = 110, CornerRadius = new CornerRadius(12), Margin = new Thickness(0, 0, 0, 10) };
        Res.BindBrush(icon, Border.BackgroundProperty, "Brush.SurfaceAlt");
        if (game.IconUrl is { } url)
            _ = LoadIconAsync(icon, url);

        var name = new TextBlock { Text = GameHistory.PlainName(game.Name), FontWeight = Avalonia.Media.FontWeight.Medium, TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis };
        var when = new TextBlock { Text = $"{GameHistory.Ago(game.LeftAt ?? game.JoinedAt)} · {GameHistory.Duration(game.Played)}", Margin = new Thickness(0, 3, 0, 0), TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis, Classes = { "mono" } };
        var card = new Button
        {
            Content = new StackPanel { Children = { icon, name, when } },
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(6),
            Margin = new Thickness(0),
        };
        ToolTip.SetTip(card, $"Play {game.Name}");
        card.Click += (_, _) => _window.LaunchRoblox($"roblox://experiences/start?placeId={game.PlaceId}");
        return card;
    }

    private static async Task LoadIconAsync(Border target, string url)
    {
        try
        {
            byte[] data = await App.Http.GetByteArrayAsync(url);
            using var stream = new MemoryStream(data);
            target.Background = new Avalonia.Media.ImageBrush(new Avalonia.Media.Imaging.Bitmap(stream)) { Stretch = Avalonia.Media.Stretch.UniformToFill };
        }
        catch (Exception ex)
        {
            Logger.Warn("HomePage::Icon", ex.Message);
        }
    }

    private sealed class SizeObserver(Action<Size> next) : IObserver<Size>
    {
        public void OnCompleted() { }
        public void OnError(Exception error) { }
        public void OnNext(Size value) => next(value);
    }
}
