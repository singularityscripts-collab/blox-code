using System.Windows;
using System.Windows.Controls;
using BloxCode.Core;
using BloxCode.UI.Pages;

namespace BloxCode.UI;

public partial class MainWindow : Window
{
    private readonly Dictionary<string, Func<UserControl>> _pages;

    public MainWindow()
    {
        _pages = new()
        {
            ["Home"] = () => new HomePage(this),
            ["FastFlags"] = () => new FastFlagsPage(this),
            ["Mods"] = () => new ModsPage(this),
            ["Appearance"] = () => new AppearancePage(this),
            ["Behaviour"] = () => new BehaviourPage(this),
            ["About"] = () => new AboutPage(),
        };

        InitializeComponent();
        ThemeManager.ApplyChrome(this);
        PageHost.Content = _pages["Home"]();
        RefreshMascots();
        RefreshFooter();
        Closing += (_, _) => Store.SaveSettings();
    }

    public void Navigate(string page)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(NavHome.Parent).OfType<RadioButton>())
            if ((string)child.Tag == page)
                child.IsChecked = true;
    }

    /// <summary>Shows a sub-page (e.g. the Fast Flag editor) without changing the sidebar selection.</summary>
    public void ShowPage(UserControl page)
    {
        Store.SaveSettings();
        PageHost.Content = page;
        Motion.EnterPage(page);
    }

    public void RefreshFooter()
    {
        bool downloaded = Store.State.VersionGuid is not null;
        FooterTitle.Text = downloaded ? "Roblox is ready" : "Roblox not downloaded yet";
        FooterSubtitle.Text = Store.State.VersionGuid ?? "downloads on first launch";
        FooterVersion.Text = $"Blox Code v{App.Version}" + (Installer.IsInstalled ? "" : " · portable");
        FooterDot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, downloaded ? "Brush.Success" : "Brush.Warning");
    }

    public void RefreshMascots() => BrandMascot.IsAnimated = Store.Settings.AnimateMascot;

    /// <summary>Hides the menu, runs the bootstrapper and closes once Roblox has started.</summary>
    /// <summary>Like <see cref="LaunchRoblox"/>, for Roblox Studio.</summary>
    public void LaunchStudio()
    {
        Store.SaveSettings();
        var window = new BootstrapperWindow(LaunchSettings.ForStudio());
        Application.Current.MainWindow = window;
        window.Show();
        Close();
    }

    public void LaunchRoblox(string robloxArgs = "")
    {
        Store.SaveSettings();
        var bootstrapper = new BootstrapperWindow(LaunchSettings.ForPlayer(robloxArgs));
        Application.Current.MainWindow = bootstrapper;
        bootstrapper.Show();
        Close();
    }

    private void OnNavigate(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Tag: string key } && _pages.TryGetValue(key, out var create) && PageHost is not null)
        {
            ShowPage(create());
        }
    }

    private void OnPlay(object sender, RoutedEventArgs e) => LaunchRoblox();

    private void OnMinimize(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnMaximize(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    private void OnStateChanged(object? sender, EventArgs e)
    {
        // A chromeless maximized window overhangs the screen by the resize border; pad it back in.
        Root.Margin = WindowState == WindowState.Maximized ? new Thickness(7) : new Thickness(0);
        MaximizeButton.Content = WindowState == WindowState.Maximized ? "" : "";
    }
}
