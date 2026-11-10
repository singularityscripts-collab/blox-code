using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using BloxCode.Core;
using BloxCode.Linux.Platform;
using BloxCode.Linux.UI.Pages;

namespace BloxCode.Linux.UI;

public partial class MainWindow : Window
{
    private readonly Dictionary<string, Func<Control>> _pages;

    public MainWindow()
    {
        _pages = new()
        {
            ["Home"] = () => new HomePage(this),
            ["FastFlags"] = () => new FastFlagsPage(this),
            ["Mods"] = () => new ModsPage(this),
            ["Sober"] = () => new SoberPage(this),
            ["Appearance"] = () => new AppearancePage(this),
            ["Behaviour"] = () => new BehaviourPage(this),
            ["About"] = () => new AboutPage(),
        };

        InitializeComponent();
        Chrome.MakeDragArea(this, Brand);
        Chrome.MakeDragArea(this, TitleBar);
        Chrome.AddResizeGrips(this, Root);

        NavHome.IsChecked = true;
        RefreshMascots();
        RefreshFooter();
        Closing += (_, _) => Store.SaveSettings();
        Closed += (_, _) => App.MenuClosed();
        this.GetObservable(WindowStateProperty).Subscribe(new StateObserver(this));
    }

    public void Navigate(string page)
    {
        foreach (var item in Nav.Children.OfType<RadioButton>())
            if ((string?)item.Tag == page)
                item.IsChecked = true;
    }

    /// <summary>Shows a sub-page (e.g. the Fast Flag editor) without changing the selected tab.</summary>
    public void ShowPage(Control page)
    {
        Store.SaveSettings();
        PageHost.Content = page;
        Motion.EnterPage(page);
    }

    public void RefreshFooter()
    {
        bool installed = DesktopIntegration.IsInstalled;
        FooterTitle.Text = installed ? "Opens roblox:// links" : "Not installed yet";
        FooterSubtitle.Text = $"{Distro.Current.Name} · Sober";
        FooterVersion.Text = $"Blox Code v{Updater.Current}" + (installed ? "" : " · portable");
        Res.BindBrush(FooterDot, Avalonia.Controls.Shapes.Shape.FillProperty, installed ? "Brush.Success" : "Brush.Warning");
    }

    public void RefreshMascots() => BrandMascot.IsAnimated = Store.Settings.AnimateMascot;

    /// <summary>Hides the menu, runs the launch window and closes once Sober has started.</summary>
    public void LaunchRoblox(string robloxArgs = "")
    {
        Store.SaveSettings();
        App.Show(LaunchWindow.ForLaunch(LaunchSettings.ForPlayer(robloxArgs)));
        Close();
    }

    private void OnNavigate(object? sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { IsChecked: true, Tag: string key } && _pages.TryGetValue(key, out var create))
        {
            ShowPage(create());
        }
    }

    private void OnMinimize(object? sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnMaximize(object? sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void OnClose(object? sender, RoutedEventArgs e) => Close();

    private sealed class StateObserver(MainWindow window) : IObserver<WindowState>
    {
        public void OnCompleted() { }
        public void OnError(Exception error) { }

        public void OnNext(WindowState state)
        {
            bool maximized = state == WindowState.Maximized;
            window.Frame.BorderThickness = new Thickness(maximized ? 0 : 1);
            window.MaximizeIcon.Data = Res.Get<Avalonia.Media.Geometry>(maximized ? "Icon.Restore" : "Icon.Maximize");
            ToolTip.SetTip(window.MaximizeButton, maximized ? "Restore" : "Maximize");
        }
    }
}
