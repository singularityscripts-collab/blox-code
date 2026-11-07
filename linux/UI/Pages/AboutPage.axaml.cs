using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using BloxCode.Core;
using BloxCode.Linux.Platform;

namespace BloxCode.Linux.UI.Pages;

public partial class AboutPage : UserControl
{
    public AboutPage()
    {
        InitializeComponent();
        Mascot.IsAnimated = Store.Settings.AnimateMascot;
        VersionText.Text = $"v{Updater.Current} · {Distro.Current.Name}";
        RepoButton.IsVisible = Updater.Homepage is not null;
    }

    private async void OnPoke(object? sender, PointerPressedEventArgs e)
    {
        // One hop, then back to idle, like the Windows About page.
        Mascot.Celebrate();
        await Task.Delay(1050);
        Mascot.Mood = MascotMood.Idle;
    }

    private Window? Owner => TopLevel.GetTopLevel(this) as Window;

    private void OnRepo(object? sender, RoutedEventArgs e)
    {
        if (Updater.Homepage is { } url)
            Shell.Open(url, Owner);
    }

    // The notes are compiled in from CHANGELOG.md, so this works with no website at all.
    private async void OnChangelog(object? sender, RoutedEventArgs e)
    {
        var notes = ReleaseNotes.For(Updater.Current);
        await MessageDialog.ShowAsync(Owner, $"What's new in v{Updater.Current}" + (notes is { Title.Length: > 0 } n ? $" — {n.Title}" : ""),
            notes?.Body ?? "This build has no notes.");
    }

    private void OnSober(object? sender, RoutedEventArgs e) => Shell.Open("https://sober.vinegarhq.org/", Owner);
}
