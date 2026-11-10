using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using BloxCode.Core;
using Microsoft.Win32;

namespace BloxCode.UI;

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

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
