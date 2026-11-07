using System.Windows;
using System.Windows.Controls;
using BloxCode.Core;

namespace BloxCode.UI.Pages;

public partial class FastFlagsPage : UserControl
{
    private static readonly (string Label, string? Value)[] RendererOptions =
        { ("Auto", nameof(RenderingMode.Default)), ("D3D11", nameof(RenderingMode.D3D11)), ("Vulkan", nameof(RenderingMode.Vulkan)), ("OpenGL", nameof(RenderingMode.OpenGL)) };

    private readonly MainWindow _window;
    private bool _syncing;

    public FastFlagsPage(MainWindow window)
    {
        _window = window;
        InitializeComponent();
        BuildSegments(GraphicsSegments, "graphics", GraphicsPresetText.Options, v => FastFlagPresets.ApplyGraphicsPreset(Enum.Parse<GraphicsPreset>(v!)));
        BuildSegments(RendererSegments, "renderer", RendererOptions, v => FastFlagPresets.Renderer = Enum.Parse<RenderingMode>(v!));
        BuildSegments(MsaaSegments, "msaa", FastFlagPresets.MsaaModes, v => FastFlagPresets.MsaaSamples = v);
        BuildSegments(TextureSegments, "texture", FastFlagPresets.TextureLevels, v => FastFlagPresets.TextureLevel = v);
        Sync();
    }

    private void BuildSegments(Panel host, string group, (string Label, string? Value)[] options, Action<string?> apply)
    {
        foreach (var (label, value) in options)
        {
            var segment = new RadioButton { Content = label, GroupName = group + GetHashCode(), Tag = value };
            segment.SetResourceReference(StyleProperty, "Segment");
            segment.Checked += (_, _) =>
            {
                if (_syncing) return;
                apply(value);
                Store.SaveSettings();
                Sync();
            };
            host.Children.Add(segment);
        }
    }

    /// <summary>Reflects the settings dictionary in the preset controls and the editor summary.</summary>
    private void Sync()
    {
        _syncing = true;
        var graphics = FastFlagPresets.CurrentGraphicsPreset;
        SelectSegment(GraphicsSegments, graphics?.ToString(), fallbackToFirst: false);
        GraphicsHint.Text = GraphicsPresetText.Describe(graphics);
        ExclusiveFullscreen.IsChecked = FastFlagPresets.UseExclusiveFullscreen;
        DisableScaling.IsChecked = FastFlagPresets.DisableScaling;
        PauseLighting.IsChecked = FastFlagPresets.PauseLighting;
        PlainSky.IsChecked = FastFlagPresets.PlainSky;

        if (GraphicsAdvisor.Recommended is { } recommended && GraphicsAdvisor.Gpu is { } gpu)
        {
            RecommendRow.Visibility = Visibility.Visible;
            bool current = graphics == recommended;
            RecommendText.Text = current
                ? $"{recommended} is the preset suggested for your {gpu.Describe()}."
                : $"Suggested for your {gpu.Describe()}: {recommended}";
            RecommendButton.Visibility = current ? Visibility.Collapsed : Visibility.Visible;
        }
        SelectSegment(RendererSegments, FastFlagPresets.Renderer.ToString());
        SelectSegment(MsaaSegments, FastFlagPresets.MsaaSamples);
        SelectSegment(TextureSegments, FastFlagPresets.TextureLevel);
        _syncing = false;

        int custom = Store.Settings.FastFlags.Keys.Count(k => !FastFlagPresets.PresetFlags.Contains(k));
        EditorSummary.Text = custom switch
        {
            0 => "Add your own flags one by one or paste a JSON config.",
            1 => "1 custom flag set · add, edit, import or export",
            _ => $"{custom} custom flags set · add, edit, import or export",
        };
    }

    /// <summary>Checks the segment for the value; with no match, the first one, or none at all for a hand-mixed preset.</summary>
    private static void SelectSegment(Panel host, string? value, bool fallbackToFirst = true)
    {
        var segments = host.Children.OfType<RadioButton>().ToList();
        var match = segments.FirstOrDefault(r => (string?)r.Tag == value) ?? (fallbackToFirst ? segments.First() : null);
        foreach (var segment in segments)
            segment.IsChecked = segment == match;
    }

    private void OnPresetChanged(object sender, RoutedEventArgs e)
    {
        FastFlagPresets.UseExclusiveFullscreen = ExclusiveFullscreen.IsChecked == true;
        FastFlagPresets.DisableScaling = DisableScaling.IsChecked == true;
        FastFlagPresets.PauseLighting = PauseLighting.IsChecked == true;
        FastFlagPresets.PlainSky = PlainSky.IsChecked == true;
        Store.SaveSettings();
        Sync();
    }

    private void OnUseRecommended(object sender, RoutedEventArgs e)
    {
        if (GraphicsAdvisor.Recommended is not { } recommended)
            return;
        FastFlagPresets.ApplyGraphicsPreset(recommended);
        Store.SaveSettings();
        Sync();
    }

    private void OnOpenEditor(object sender, RoutedEventArgs e) => _window.ShowPage(new FastFlagEditorPage(_window));
}
