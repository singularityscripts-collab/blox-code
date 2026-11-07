using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using BloxCode.Core;

namespace BloxCode.UI.Pages;

public partial class AboutPage : UserControl
{
    public AboutPage()
    {
        InitializeComponent();
        VersionText.Text = $"v{App.Version} · .NET {Environment.Version}";
        Mascot.IsAnimated = Store.Settings.AnimateMascot;
        SourceButton.Visibility = Updater.Homepage is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private async void OnPoke(object sender, MouseButtonEventArgs e)
    {
        Mascot.Celebrate();
        await Task.Delay(1050);
        Mascot.Mood = MascotMood.Idle;
    }

    private void Open(string target) => Shell.Open(target, Window.GetWindow(this));

    private void OnOpenBase(object sender, RoutedEventArgs e) => Open(Paths.Base);
    private void OnOpenLogs(object sender, RoutedEventArgs e) => Open(Paths.Logs);
    private void OnSource(object sender, RoutedEventArgs e)
    {
        if (Updater.Homepage is { } url)
            Open(url);
    }

    private void OnBloxstrap(object sender, RoutedEventArgs e) => Open("https://github.com/bloxstraplabs/bloxstrap");
}
