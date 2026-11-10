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

        RefreshBanner();
        RefreshRecent();
        Loaded += (_, _) => Composer.Focus();
    }

    private string? _updatedNotice;

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
            BannerButton.Content = NotesFor(_updatedNotice) is null ? "Got it" : "What's new";
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

    private static (string Title, string Body)? NotesFor(string version) =>
        Version.TryParse(version, out var v) ? ReleaseNotes.For(v) : null;

    private static string ExecutableName(string command)
    {
        var match = Regex.Match(command, "\"([^\"]+\\.exe)\"|(\\S+\\.exe)", RegexOptions.IgnoreCase);
        return match.Success ? Path.GetFileName(match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value) : command;
    }

    private void OnBannerAction(object sender, RoutedEventArgs e)
    {
        if (_updatedNotice is not null)
        {
            if (NotesFor(_updatedNotice) is { } notes)
                MessageDialog.Show(_window, $"What's new in v{_updatedNotice}" + (notes.Title.Length > 0 ? $" — {notes.Title}" : ""), notes.Body);
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

    /// <summary>The last games played, as cards that jump straight back in, and this week's play time.</summary>
    private void RefreshRecent()
    {
        var history = Store.Settings.RememberGames ? GameHistory.Load() : new();
        var recent = GameHistory.Recent(4, history);
        RecentSection.Visibility = recent.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        RecentGames.Children.Clear();
        foreach (var game in recent)
            RecentGames.Children.Add(GameCard(game));

        var week = GameHistory.Stats(TimeSpan.FromDays(7), history);
        StatsText.Text = week.Total < TimeSpan.FromMinutes(1)
            ? ""
            : $"{GameHistory.Duration(week.Total)} this week" + (week.TopGame is { } top && recent.Count > 1 ? $" · mostly {Trim(GameHistory.PlainName(top), 22)}" : "");
    }

    private static string Trim(string text, int max) => text.Length <= max ? text : text[..(max - 1)] + "…";

    private Button GameCard(PlayedGame game)
    {
        var icon = new Border { Height = 118, CornerRadius = new CornerRadius(12), Margin = new Thickness(0, 0, 0, 10) };
        icon.SetResourceReference(Border.BackgroundProperty, "Brush.SurfaceAlt");
        if (game.IconUrl is { } url && Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            try
            {
                var image = new System.Windows.Media.Imaging.BitmapImage();
                image.BeginInit();
                image.UriSource = uri;
                image.DecodePixelWidth = 224;
                image.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                image.EndInit();
                icon.Background = new System.Windows.Media.ImageBrush(image) { Stretch = System.Windows.Media.Stretch.UniformToFill };
            }
            catch (Exception ex)
            {
                Logger.Warn("HomePage::GameCard", ex.Message);
            }
        }

        var name = new TextBlock { Text = GameHistory.PlainName(game.Name), FontSize = 13, FontWeight = FontWeights.Medium, TextTrimming = TextTrimming.CharacterEllipsis };
        name.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
        var when = new TextBlock { Text = $"{GameHistory.Ago(game.LeftAt ?? game.JoinedAt)} · {GameHistory.Duration(game.Played)}", Margin = new Thickness(0, 3, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
        when.SetResourceReference(StyleProperty, "Text.Mono");

        var card = new Button
        {
            Content = new StackPanel { Children = { icon, name, when } },
            Padding = new Thickness(6),
            Margin = new Thickness(0),
            BorderThickness = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Top,
            ToolTip = $"Play {game.Name}" + (game.Creator is { Length: > 0 } by ? $" by {by}" : ""),
        };
        card.SetResourceReference(StyleProperty, "Button.Base");
        // Flat until hovered: just the game's icon, its name and when it was played
        card.Background = System.Windows.Media.Brushes.Transparent;
        card.BorderBrush = System.Windows.Media.Brushes.Transparent;
        card.SetResourceReference(TagProperty, "Brush.Hover");
        card.Click += (_, _) => _window.LaunchRoblox($"roblox://experiences/start?placeId={game.PlaceId}");

        var menu = new ContextMenu();
        var play = new MenuItem { Header = "Play" };
        play.Click += (_, _) => _window.LaunchRoblox($"roblox://experiences/start?placeId={game.PlaceId}");
        menu.Items.Add(play);
        // The same server is only worth trying while it may still be up, and only public ones take a server id.
        if (game.Kind == ServerKind.Public && game.JobId is { } job && DateTime.UtcNow - (game.LeftAt ?? game.JoinedAt) < TimeSpan.FromHours(6))
        {
            var rejoin = new MenuItem { Header = "Rejoin the same server" };
            rejoin.Click += (_, _) => _window.LaunchRoblox($"roblox://experiences/start?placeId={game.PlaceId}&gameInstanceId={job}");
            menu.Items.Add(rejoin);
        }
        var page = new MenuItem { Header = "Open the game page" };
        page.Click += (_, _) => Process.Start(new ProcessStartInfo(RobloxGames.GamePage(game.PlaceId)) { UseShellExecute = true });
        menu.Items.Add(page);
        var forget = new MenuItem { Header = "Remove from recent" };
        forget.Click += (_, _) => { GameHistory.Forget(game.PlaceId); RefreshRecent(); };
        menu.Items.Add(forget);
        card.ContextMenu = menu;
        return card;
    }

    private void OnComposerChanged(object sender, TextChangedEventArgs e)
    {
        bool empty = Composer.Text.Trim().Length == 0;
        bool valid = RobloxLink.ToLaunchArgs(Composer.Text) is not null;
        SendButton.IsEnabled = empty || valid;
        ComposerHint.Text = empty
            ? "Enter to launch · empty opens Roblox"
            : valid ? "Enter to join this experience" : "That doesn't look like a Roblox game link";
    }

    /// <summary>Focused, the bar firms up and gets an accent line along its bottom, like Windows' search box.</summary>
    private void OnComposerFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        bool focused = Composer.IsKeyboardFocusWithin;
        Bar.SetResourceReference(Border.BackgroundProperty, focused ? "Brush.SurfaceAlt" : "Brush.Surface");
        if (focused)
        {
            Bar.BorderThickness = new Thickness(1, 1, 1, 2);
            Bar.SetResourceReference(Border.BorderBrushProperty, "Brush.Accent");
        }
        else
        {
            Bar.BorderThickness = new Thickness(1);
            Bar.SetResourceReference(Border.BorderBrushProperty, "Brush.BorderStrong");
        }
    }

    private void OnComposerKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            OnSend(sender, e);
    }

    private void OnSend(object sender, RoutedEventArgs e)
    {
        if (Composer.Text.Trim().Length == 0)
            _window.LaunchRoblox();
        else if (RobloxLink.ToLaunchArgs(Composer.Text) is { } args)
            _window.LaunchRoblox(args);
    }

    private void OnOpenStudio(object sender, RoutedEventArgs e) => _window.LaunchStudio();

    private void OnReinstall(object sender, RoutedEventArgs e)
    {
        if (!MessageDialog.Ask(_window, "Reinstall Roblox?", "Blox Code will re-download and re-extract Roblox the next time it launches.", "Reinstall now", "Cancel"))
            return;
        Store.State.ForceReinstall = true;
        Store.SaveState();
        _window.LaunchRoblox();
    }
}
