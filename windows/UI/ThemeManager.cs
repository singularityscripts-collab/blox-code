using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using BloxCode.Core;
using Microsoft.Win32;

namespace BloxCode.UI;

/// <summary>A window that can show the Mica or Acrylic backdrop behind its content.</summary>
public interface IGlassWindow
{
    /// <summary>The brush key the window is painted with when the backdrop is off.</summary>
    string SolidBackground { get; }
}

public static class ThemeManager
{
    public static bool IsDark { get; private set; }

    public static void Apply(AppTheme theme)
    {
        IsDark = theme switch
        {
            AppTheme.Dark => true,
            AppTheme.Light => false,
            _ => SystemPrefersDark(),
        };

        var dictionaries = Application.Current.Resources.MergedDictionaries;
        var palette = new ResourceDictionary
        {
            Source = new Uri($"pack://application:,,,/BloxCode;component/UI/Themes/{(IsDark ? "Dark" : "Light")}.xaml"),
        };

        // Slot 0 is always the palette; Styles.xaml sits after it and resolves brushes dynamically.
        if (dictionaries.Count > 0)
            dictionaries[0] = palette;
        else
            dictionaries.Add(palette);

        ApplyAccent(Store.Settings.Accent);

        foreach (Window window in Application.Current.Windows)
            ApplyChrome(window);
    }

    private static readonly string[] AccentKeys = { "Brush.Accent", "Brush.AccentHover", "Brush.AccentSoft", "Brush.AccentLine", "Brush.OnAccent", "Brush.AccentFill" };

    /// <summary>Accent, hover, soft fill, line and text-on-accent for each colour, dark then light.</summary>
    private static readonly Dictionary<AccentColor, (string[] Dark, string[] Light)> Accents = new()
    {
        [AccentColor.Sky] = (new[] { "#60A5FA", "#82B8FB", "#131F2E", "#1E3A5F", "#071A33" }, new[] { "#1D63C9", "#174FA3", "#E3EDFA", "#BCD3F2", "#FFFFFF" }),
        [AccentColor.Violet] = (new[] { "#A78BFA", "#BBA5FB", "#1C1830", "#352B5C", "#1A0F3D" }, new[] { "#6D4AD6", "#5838B3", "#EEE9FB", "#D3C7F5", "#FFFFFF" }),
        [AccentColor.Rose] = (new[] { "#FB7185", "#FC8C9C", "#2A1519", "#4F2630", "#3A0610" }, new[] { "#C2334D", "#A1283F", "#FBE7EA", "#F2C3CC", "#FFFFFF" }),
        [AccentColor.Amber] = (new[] { "#FBBF24", "#FCCD55", "#2A2210", "#4D3C17", "#2B1D02" }, new[] { "#A86A00", "#8A5700", "#FBF1DC", "#F0D9A8", "#FFFFFF" }),
    };

    /// <summary>
    /// Mint is the palette's own; other colours are set on the application's resources, which take
    /// precedence over the merged palette, so every DynamicResource picks them up at once.
    /// </summary>
    public static void ApplyAccent(AccentColor accent)
    {
        var resources = Application.Current.Resources;
        foreach (string key in AccentKeys)
            resources.Remove(key);
        if (!Accents.TryGetValue(accent, out var colours))
            return;

        string[] set = IsDark ? colours.Dark : colours.Light;
        string[] values = { set[0], set[1], set[2], set[3], set[4], set[0] };
        for (int i = 0; i < AccentKeys.Length; i++)
        {
            var brush = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(values[i]));
            brush.Freeze();
            resources[AccentKeys[i]] = brush;
        }
    }

    public static System.Windows.Media.Color Swatch(AccentColor accent) =>
        (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(
            Accents.TryGetValue(accent, out var c) ? (IsDark ? c.Dark : c.Light)[0] : IsDark ? "#4ADE9A" : "#077257");

    private static bool _following;

    /// <summary>When the theme is "System", re-apply it as soon as Windows switches light/dark.</summary>
    public static void FollowSystemTheme()
    {
        if (_following) return;
        _following = true;

        SystemEvents.UserPreferenceChanged += (_, e) =>
        {
            if (e.Category != UserPreferenceCategory.General)
                return;
            Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                if (Store.Settings.Theme == AppTheme.System && SystemPrefersDark() != IsDark)
                    Apply(AppTheme.System);
                else
                    foreach (Window window in Application.Current.Windows)
                        ApplyChrome(window); // Windows' "Transparency effects" may have changed
            });
        };
    }

    private static bool SystemPrefersDark()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("AppsUseLightTheme") is int light && light == 0;
    }

    /// <summary>Rounded Win11 corners + dark/light DWM frame to match the palette.</summary>
    public static void ApplyChrome(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero)
        {
            window.SourceInitialized -= OnSourceInitialized;
            window.SourceInitialized += OnSourceInitialized;
            return;
        }

        int dark = IsDark ? 1 : 0;
        DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
        int round = DWMWCP_ROUND;
        DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref round, sizeof(int));
        if (window is IGlassWindow glass)
            ApplyBackdrop(window, glass, hwnd);
    }

    /// <summary>
    /// Mica and Acrylic need Windows 11 22H2 (build 22621) and Windows' own "Transparency effects"
    /// switch on; otherwise the windows stay solid.
    /// </summary>
    public static bool BackdropSupported =>
        Environment.OSVersion.Version.Build >= 22621 && SystemTransparencyOn();

    public static bool BackdropActive => Store.Settings.Backdrop != WindowBackdrop.Off && BackdropSupported;

    // Surfaces inside a see-through window: light or dark veils over the backdrop instead of solid fills.
    private static readonly (string Key, string Dark, string Light)[] GlassBrushes =
    {
        ("Brush.CardFill", "#0BFFFFFF", "#80FFFFFF"),
        ("Brush.CardBorder", "#14FFFFFF", "#14000000"),
        ("Brush.Surface", "#0FFFFFFF", "#A6FFFFFF"),
        ("Brush.SurfaceAlt", "#14FFFFFF", "#0D000000"),
        ("Brush.Hover", "#12FFFFFF", "#0D000000"),
        ("Brush.Well", "#33000000", "#0A000000"),
        ("Brush.Border", "#12FFFFFF", "#12000000"),
        ("Brush.BorderStrong", "#1FFFFFFF", "#1F000000"),
    };

    private static void ApplyBackdrop(Window window, IGlassWindow glass, IntPtr hwnd)
    {
        bool on = BackdropActive;
        var kind = Store.Settings.Backdrop;

        if (HwndSource.FromHwnd(hwnd)?.CompositionTarget is { } target)
            target.BackgroundColor = on ? System.Windows.Media.Colors.Transparent : System.Windows.Media.Colors.Black;
        if (System.Windows.Shell.WindowChrome.GetWindowChrome(window) is { } chrome && !chrome.IsFrozen)
            chrome.GlassFrameThickness = on ? new Thickness(-1) : new Thickness(0, 0, 0, 1);
        var margins = on ? new MARGINS { Left = -1, Right = -1, Top = -1, Bottom = -1 } : new MARGINS { Bottom = 1 };
        DwmExtendFrameIntoClientArea(hwnd, ref margins);

        int type = !on ? DWMSBT_NONE : kind == WindowBackdrop.Mica ? DWMSBT_MAINWINDOW : DWMSBT_TRANSIENTWINDOW;
        if (BackdropSupported)
            DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref type, sizeof(int));

        foreach (var (key, dark, light) in GlassBrushes)
        {
            if (on)
                window.Resources[key] = Brush(IsDark ? dark : light);
            else
                window.Resources.Remove(key);
        }

        // A thin veil of the theme's ground keeps text readable over a busy wallpaper; Acrylic, which
        // shows the windows behind, gets a little more of it than Mica.
        if (on)
            window.Background = Brush(IsDark
                ? kind == WindowBackdrop.Mica ? "#330E0F11" : "#590E0F11"
                : kind == WindowBackdrop.Mica ? "#40FAFAFA" : "#73FAFAFA");
        else
            window.SetResourceReference(Window.BackgroundProperty, glass.SolidBackground);
    }

    private static System.Windows.Media.SolidColorBrush Brush(string hex)
    {
        var brush = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }

    private static bool SystemTransparencyOn()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("EnableTransparency") is not int value || value != 0;
    }

    private static void OnSourceInitialized(object? sender, EventArgs e)
    {
        if (sender is Window window)
        {
            window.SourceInitialized -= OnSourceInitialized;
            ApplyChrome(window);
        }
    }

    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWCP_ROUND = 2;
    private const int DWMWA_SYSTEMBACKDROP_TYPE = 38;
    private const int DWMSBT_NONE = 1, DWMSBT_MAINWINDOW = 2, DWMSBT_TRANSIENTWINDOW = 3;

    [StructLayout(LayoutKind.Sequential)]
    private struct MARGINS { public int Left, Right, Top, Bottom; }


    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref MARGINS margins);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
