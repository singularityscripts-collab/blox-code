using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using BloxCode.Core;

namespace BloxCode.Linux.UI;

/// <summary>
/// The first thing a plain start shows, modelled on Bloxstrap's LaunchMenuDialog: launch Roblox
/// (through Sober) with the current settings, or open the settings. Closing it ends Blox Code.
/// </summary>
public partial class LaunchMenuWindow : Window
{
    private readonly TaskCompletionSource<LaunchMenuChoice> _choice = new();

    public LaunchMenuWindow()
    {
        InitializeComponent();
        VersionText.Text = $"v{Updater.Current}";
        LaunchSummary.Text = LaunchMenu.Summary();
        Mascot.IsAnimated = Store.Settings.AnimateMascot;
        SourceButton.IsVisible = Updater.Homepage is not null;
        Chrome.MakeDragArea(this, Body, maximizeOnDoubleClick: false);
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
                Close();
        };
        Opened += (_, _) =>
        {
            Motion.EnterPage(Body);
            LaunchButton.Focus();
        };
        Closed += (_, _) => _choice.TrySetResult(LaunchMenuChoice.None);
    }

    /// <summary>Shows the menu and waits for a choice; closing it is <see cref="LaunchMenuChoice.None"/>.</summary>
    public static async Task<LaunchMenuChoice> AskAsync()
    {
        var window = new LaunchMenuWindow();
        window.Show();
        window.Activate();
        return await window._choice.Task;
    }

    private void Finish(LaunchMenuChoice choice)
    {
        _choice.TrySetResult(choice);
        Close();
    }

    private void OnLaunch(object? sender, RoutedEventArgs e) => Finish(LaunchMenuChoice.LaunchRoblox);

    private void OnConfigure(object? sender, RoutedEventArgs e) => Finish(LaunchMenuChoice.Configure);

    private void OnClose(object? sender, RoutedEventArgs e) => Close();

    private async void OnWhatsNew(object? sender, RoutedEventArgs e)
    {
        var notes = ReleaseNotes.For(Updater.Current);
        await MessageDialog.ShowAsync(this, $"What's new in v{Updater.Current}" + (notes is { Title.Length: > 0 } n ? $" — {n.Title}" : ""),
            notes?.Body ?? "This build has no notes.");
    }

    private void OnSource(object? sender, RoutedEventArgs e)
    {
        if (Updater.Homepage is { } url)
            Shell.Open(url, this);
    }
}
