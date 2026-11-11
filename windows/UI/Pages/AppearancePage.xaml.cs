using System.Windows;
using System.Windows.Controls;
using BloxCode.Core;

namespace BloxCode.UI.Pages;

public partial class AppearancePage : UserControl
{
    private readonly MainWindow _window;
    private readonly bool _ready;

    public AppearancePage(MainWindow window)
    {
        _window = window;
        InitializeComponent();

        (Store.Settings.Theme switch
        {
            AppTheme.Light => ThemeLight,
            AppTheme.Dark => ThemeDark,
            _ => ThemeSystem,
        }).IsChecked = true;
        (Store.Settings.Backdrop switch
        {
            WindowBackdrop.Off => BackdropOff,
            WindowBackdrop.Mica => BackdropMica,
            _ => BackdropAcrylic,
        }).IsChecked = true;
        RefreshBackdropText();
        Animate.IsChecked = Store.Settings.AnimateMascot;
        Animations.IsChecked = Store.Settings.Animations;
        Mascot.IsAnimated = Store.Settings.AnimateMascot;
        BuildAccentSwatches();
        BuildLaunchThemes();
        RefreshIcon();
        _ready = true;
    }

    private void BuildLaunchThemes()
    {
        foreach (var theme in Enum.GetValues<LaunchTheme>())
        {
            var segment = new RadioButton { Content = theme.ToString(), GroupName = "launchTheme" + GetHashCode(), IsChecked = Store.Settings.LaunchTheme == theme };
            segment.SetResourceReference(StyleProperty, "Segment");
            segment.Checked += (_, _) =>
            {
                Store.Settings.LaunchTheme = theme;
                Store.SaveSettings();
                RefreshLaunchThemeText();
            };
            LaunchThemeSegments.Children.Add(segment);
        }
        RefreshLaunchThemeText();
    }

    private void RefreshLaunchThemeText() => LaunchThemeText.Text = Store.Settings.LaunchTheme switch
    {
        LaunchTheme.Minimal => "Just what is happening and a thin bar.",
        LaunchTheme.Terminal => "A progress bar drawn in text, like a terminal.",
        _ => "Bit walks the progress bar while Roblox downloads and types away while it unpacks.",
    } + " The preview fakes a download and launches nothing.";

    private void BuildAccentSwatches()
    {
        AccentSwatches.Children.Clear();
        foreach (var accent in Enum.GetValues<AccentColor>())
        {
            bool selected = accent == Store.Settings.Accent;
            var dot = new System.Windows.Shapes.Ellipse { Width = 18, Height = 18, Fill = new System.Windows.Media.SolidColorBrush(ThemeManager.Swatch(accent)) };
            var ring = new Border
            {
                Width = 28, Height = 28, CornerRadius = new CornerRadius(14), BorderThickness = new Thickness(2),
                Child = dot, Background = System.Windows.Media.Brushes.Transparent,
            };
            if (selected)
                ring.SetResourceReference(Border.BorderBrushProperty, "Brush.Text");
            else
                ring.BorderBrush = System.Windows.Media.Brushes.Transparent;
            var button = new Button { Content = ring, Margin = new Thickness(2, 0, 2, 0), Padding = new Thickness(0), ToolTip = accent.ToString(), Cursor = System.Windows.Input.Cursors.Hand };
            button.Template = new ControlTemplate(typeof(Button)) { VisualTree = new FrameworkElementFactory(typeof(ContentPresenter)) };
            button.Click += (_, _) =>
            {
                Store.Settings.Accent = accent;
                ThemeManager.ApplyAccent(accent);
                Store.SaveSettings();
                BuildAccentSwatches();
            };
            AccentSwatches.Children.Add(button);
        }
    }

    private void RefreshIcon()
    {
        string? custom = Installer.CustomIcon;
        IconText.Text = custom is null
            ? "Bit, on the Start menu, desktop and Apps & Features."
            : Installer.IsInstalled ? "Your icon, on the Start menu, desktop and Apps & Features." : "Your icon, used once Blox Code is installed.";
        ResetIconButton.IsEnabled = custom is not null;
        try
        {
            var image = new System.Windows.Media.Imaging.BitmapImage();
            image.BeginInit();
            image.UriSource = custom is null ? new Uri("pack://application:,,,/BloxCode;component/Resources/BloxCode.ico") : new Uri(custom);
            image.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            image.CreateOptions = System.Windows.Media.Imaging.BitmapCreateOptions.IgnoreImageCache;
            image.EndInit();
            IconPreview.Source = image;
        }
        catch (Exception ex)
        {
            Logger.Warn("AppearancePage::RefreshIcon", ex.Message);
            IconPreview.Source = null;
        }
    }

    private void OnChooseIcon(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "Icons (*.ico)|*.ico" };
        if (dialog.ShowDialog(_window) != true)
            return;
        try
        {
            Installer.SetCustomIcon(dialog.FileName);
        }
        catch (Exception ex)
        {
            MessageDialog.Show(_window, "Couldn't use that icon", ex.Message);
        }
        RefreshIcon();
    }

    private void OnResetIcon(object sender, RoutedEventArgs e)
    {
        Installer.SetCustomIcon(null);
        RefreshIcon();
    }

    private void OnAnimationsChanged(object sender, RoutedEventArgs e)
    {
        Store.Settings.Animations = Animations.IsChecked == true;
        Store.SaveSettings();
    }

    private void OnThemeChanged(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        Store.Settings.Theme = ThemeLight.IsChecked == true ? AppTheme.Light
            : ThemeDark.IsChecked == true ? AppTheme.Dark
            : AppTheme.System;
        ThemeManager.Apply(Store.Settings.Theme);
        Store.SaveSettings();
    }

    private void OnBackdropChanged(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        Store.Settings.Backdrop = BackdropOff.IsChecked == true ? WindowBackdrop.Off
            : BackdropMica.IsChecked == true ? WindowBackdrop.Mica
            : WindowBackdrop.Acrylic;
        foreach (Window window in Application.Current.Windows)
            ThemeManager.ApplyChrome(window);
        RefreshBackdropText();
        Store.SaveSettings();
    }

    private void RefreshBackdropText() =>
        BackdropText.Text = !ThemeManager.BackdropSupported
            ? Environment.OSVersion.Version.Build >= 22621
                ? "Turn on Transparency effects in Windows' colour settings to use it."
                : "Needs Windows 11 (22H2 or later)."
            : Store.Settings.Backdrop switch
            {
                WindowBackdrop.Mica => "Your wallpaper's colours show through the window.",
                WindowBackdrop.Acrylic => "What's behind the window shows through, blurred.",
                _ => "A solid window.",
            };

    private void OnAnimateChanged(object sender, RoutedEventArgs e)
    {
        Store.Settings.AnimateMascot = Animate.IsChecked == true;
        Mascot.IsAnimated = Store.Settings.AnimateMascot;
        _window.RefreshMascots();
        Store.SaveSettings();
    }

    private void OnPreview(object sender, RoutedEventArgs e) => BootstrapperWindow.Preview(_window).ShowDialog();
}
