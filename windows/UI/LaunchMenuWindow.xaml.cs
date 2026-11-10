using System.Windows;
using System.Windows.Input;
using BloxCode.Core;

namespace BloxCode.UI;

/// <summary>
/// The first thing a plain start shows, modelled on Bloxstrap's LaunchMenuDialog: launch Roblox
/// with the current settings, or open the settings. Closing it ends Blox Code (Bloxstrap's
/// NextAction.Terminate).
/// </summary>
public partial class LaunchMenuWindow : Window
{
    public LaunchMenuChoice Choice { get; private set; } = LaunchMenuChoice.None;

    public LaunchMenuWindow()
    {
        InitializeComponent();
        ThemeManager.ApplyChrome(this);
        VersionText.Text = $"v{App.Version}";
        LaunchSummary.Text = LaunchMenu.Summary();
        StudioSummary.Text = Store.State.StudioVersionGuid is null ? "Downloads Studio the first time" : "With your Fast Flags";
        Mascot.IsAnimated = Store.Settings.AnimateMascot;
        SourceButton.Visibility = Updater.Homepage is null ? Visibility.Collapsed : Visibility.Visible;
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
                Close();
        };
        Loaded += (_, _) =>
        {
            Motion.EnterPage(Body);
            LaunchButton.Focus();
        };
    }

    private void Finish(LaunchMenuChoice choice)
    {
        Choice = choice;
        Close();
    }

    private void OnLaunch(object sender, RoutedEventArgs e) => Finish(LaunchMenuChoice.LaunchRoblox);

    private void OnConfigure(object sender, RoutedEventArgs e) => Finish(LaunchMenuChoice.Configure);

    private void OnStudio(object sender, RoutedEventArgs e) => Finish(LaunchMenuChoice.LaunchStudio);

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    private void OnWhatsNew(object sender, RoutedEventArgs e)
    {
        var notes = ReleaseNotes.For(Updater.Current);
        MessageDialog.Show(this, $"What's new in v{Updater.Current}" + (notes is { Title.Length: > 0 } n ? $" — {n.Title}" : ""),
            notes?.Body ?? "This build has no notes.");
    }

    private void OnSource(object sender, RoutedEventArgs e)
    {
        if (Updater.Homepage is { } url)
            Shell.Open(url, this);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (e.ButtonState == MouseButtonState.Pressed && e.OriginalSource is not System.Windows.Controls.Button)
            DragMove();
    }
}
