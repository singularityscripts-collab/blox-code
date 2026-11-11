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
        LoadGameSettings();
    }

    // ---- Roblox's own settings (GlobalBasicSettings_13.xml) --------------------------------------

    private bool _loadingGameSettings;

    /// <summary>Shows Roblox's current values; read-only while a Roblox window is open (it rewrites the file when it closes).</summary>
    private void LoadGameSettings()
    {
        var settings = RobloxSettingsFile.Read();
        bool open = MultiInstance.RunningPlayers() > 0;
        GameSettingsCard.IsEnabled = settings is not null && !open;
        GameSettingsNote.Text = settings is null
            ? "Roblox hasn't saved its settings on this PC yet. Open it once, then come back."
            : open
                ? "Close Roblox to change these: it saves its own settings when it closes and would undo yours."
                : "Roblox's own settings menu, changed here before a game starts.";
        if (settings is null)
            return;

        _loadingGameSettings = true;
        Sensitivity.Value = settings.MouseSensitivity;
        Volume.Value = Math.Round(settings.Volume * 100);
        Quality.Value = settings.Quality;
        GameFullscreen.IsChecked = settings.Fullscreen;
        GameStats.IsChecked = settings.PerformanceStats;
        GameInvert.IsChecked = settings.InvertCamera;
        GameMotion.IsChecked = settings.ReducedMotion;
        _loadingGameSettings = false;
        ShowGameSettingValues();
    }

    private void ShowGameSettingValues()
    {
        SensitivityValue.Text = Sensitivity.Value.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
        VolumeValue.Text = $"{Volume.Value:0}%";
        QualityValue.Text = Quality.Value < 1 ? "Auto" : $"{Quality.Value:0}";
    }

    private void OnGameSettingChanged(object sender, RoutedEventArgs e)
    {
        if (_loadingGameSettings || !IsLoaded)
            return;
        ShowGameSettingValues();
        if (MultiInstance.RunningPlayers() > 0)
        {
            LoadGameSettings();   // Roblox was opened meanwhile: show its values, locked
            return;
        }
        RobloxSettingsFile.Write(new RobloxGameSettings(Sensitivity.Value, Volume.Value / 100, (int)Quality.Value,
            GameFullscreen.IsChecked == true, GameStats.IsChecked == true, GameMotion.IsChecked == true, GameInvert.IsChecked == true));
    }

    // ---- Profiles: named copies of the whole flag set -------------------------------------------

    private static bool SameFlags(Dictionary<string, string> a, Dictionary<string, string> b) =>
        a.Count == b.Count && a.All(kv => b.TryGetValue(kv.Key, out var v) && v == kv.Value);

    private void RefreshProfiles()
    {
        var profiles = Store.Settings.FastFlagProfiles;
        string? active = profiles.FirstOrDefault(p => SameFlags(p.Value, Store.Settings.FastFlags)).Key;
        ProfilesHint.Text = profiles.Count == 0
            ? "Save the flags you have now under a name (say \"Max FPS\" or \"Pretty\") and switch between sets with one click."
            : active is null
                ? "Your current flags don't match a saved profile. Click one to switch; right-click for more."
                : $"Using {active}. Click another to switch; right-click for more.";
        ProfileChips.Children.Clear();
        foreach (var (name, flags) in profiles.OrderBy(p => p.Key, StringComparer.CurrentCultureIgnoreCase))
            ProfileChips.Children.Add(ProfileChip(name, flags, name == active));
    }

    private Button ProfileChip(string name, Dictionary<string, string> flags, bool active)
    {
        var chip = new Button
        {
            Content = $"{name}  ·  {flags.Count}",
            Margin = new Thickness(4),
            Padding = new Thickness(12, 6, 12, 6),
            ToolTip = active ? "In use" : $"Switch to {name} ({flags.Count} flags)",
        };
        chip.SetResourceReference(StyleProperty, active ? "Button.Primary" : "Button.Secondary");
        chip.Click += (_, _) => ApplyProfile(name);

        var menu = new ContextMenu();
        void Add(string header, Action action) { var item = new MenuItem { Header = header }; item.Click += (_, _) => action(); menu.Items.Add(item); }
        Add("Use this profile", () => ApplyProfile(name));
        Add("Update with current flags", () =>
        {
            Store.Settings.FastFlagProfiles[name] = new(Store.Settings.FastFlags);
            Store.SaveSettings();
            RefreshProfiles();
        });
        Add("Rename…", () =>
        {
            if (MessageDialog.Prompt(_window, "Rename profile", "A new name for this set of flags.", name, "Rename") is not { } renamed || renamed == name)
                return;
            if (Store.Settings.FastFlagProfiles.ContainsKey(renamed) &&
                !MessageDialog.Ask(_window, $"Replace {renamed}?", "Another profile already has this name; renaming replaces it.", "Replace", "Cancel"))
                return;
            Store.Settings.FastFlagProfiles.Remove(name);
            Store.Settings.FastFlagProfiles[renamed] = flags;
            Store.SaveSettings();
            RefreshProfiles();
        });
        Add("Export to a file…", () => ExportProfile(name, flags));
        Add("Delete", () =>
        {
            if (!MessageDialog.Ask(_window, $"Delete {name}?", "Only the saved profile goes; the flags you have now stay as they are.", "Delete", "Cancel", danger: true))
                return;
            Store.Settings.FastFlagProfiles.Remove(name);
            Store.SaveSettings();
            RefreshProfiles();
        });
        chip.ContextMenu = menu;
        return chip;
    }

    private void ApplyProfile(string name)
    {
        if (!Store.Settings.FastFlagProfiles.TryGetValue(name, out var flags))
            return;
        // Unsaved flags would be lost: offer to keep them first.
        bool saved = Store.Settings.FastFlags.Count == 0 || Store.Settings.FastFlagProfiles.Values.Any(p => SameFlags(p, Store.Settings.FastFlags));
        if (!saved && !MessageDialog.Ask(_window, $"Switch to {name}?",
                "The flags you have now aren't saved in a profile and will be replaced. Save them first with \"Save current as…\" if you want them back later.",
                "Switch", "Cancel"))
            return;
        Store.Settings.FastFlags = new(flags);
        Store.SaveSettings();
        Sync();
    }

    private void OnSaveProfile(object sender, RoutedEventArgs e)
    {
        if (MessageDialog.Prompt(_window, "Save as a profile", $"A name for the {Store.Settings.FastFlags.Count} flags you have now.", "", "Save") is not { } name)
            return;
        if (Store.Settings.FastFlagProfiles.ContainsKey(name) &&
            !MessageDialog.Ask(_window, $"Replace {name}?", "A profile with this name already exists.", "Replace", "Cancel"))
            return;
        Store.Settings.FastFlagProfiles[name] = new(Store.Settings.FastFlags);
        Store.SaveSettings();
        RefreshProfiles();
    }

    private void ExportProfile(string name, Dictionary<string, string> flags)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            FileName = string.Concat(name.Split(Path.GetInvalidFileNameChars())) + ".json",
            Filter = "Fast Flags (*.json)|*.json",
        };
        if (dialog.ShowDialog(_window) != true)
            return;
        try
        {
            File.WriteAllText(dialog.FileName, System.Text.Json.JsonSerializer.Serialize(flags, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            MessageDialog.Show(_window, "Couldn't export", ex.Message);
        }
    }

    private void OnImportProfile(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "Fast Flags (*.json;*.txt)|*.json;*.txt|All files|*.*" };
        if (dialog.ShowDialog(_window) != true)
            return;
        try
        {
            // Same rules as the editor's import: entries that aren't real flags are left out.
            var (flags, skipped) = FastFlagValidator.ValidOnly(FastFlagValidator.ParseJson(File.ReadAllText(dialog.FileName)));
            if (flags.Count == 0)
            {
                MessageDialog.Show(_window, "Nothing to import", skipped == 0 ? "That file has no flags." : $"None of its {skipped} entries is a valid Fast Flag.");
                return;
            }
            string found = $"{flags.Count} flags found" + (skipped > 0 ? $" ({skipped} invalid left out)" : "") + ". A name for this profile:";
            if (MessageDialog.Prompt(_window, "Import as a profile", found, Path.GetFileNameWithoutExtension(dialog.FileName), "Import") is not { } name)
                return;
            if (Store.Settings.FastFlagProfiles.ContainsKey(name) &&
                !MessageDialog.Ask(_window, $"Replace {name}?", "A profile with this name already exists.", "Replace", "Cancel"))
                return;
            Store.Settings.FastFlagProfiles[name] = flags;
            Store.SaveSettings();
            RefreshProfiles();
        }
        catch (Exception ex)
        {
            MessageDialog.Show(_window, "Couldn't import", $"That file isn't a Fast Flags JSON: {ex.Message}");
        }
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
        RefreshProfiles();
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
