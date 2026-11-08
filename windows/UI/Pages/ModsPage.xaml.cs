using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BloxCode.Core;
using Microsoft.Win32;

namespace BloxCode.UI.Pages;

/// <summary>
/// Pick-a-file mods (cursor, sounds, game font) on top, the raw Modifications folder below. The
/// slots write into the same folder, so everything still goes through the bootstrapper's mod step.
/// </summary>
public partial class ModsPage : UserControl
{
    private const string AllTab = "All", PresetsTab = "Presets", AdvancedTab = "Advanced";

    private readonly MainWindow _window;
    private readonly MediaPlayer _player = new();
    private string _tab = AllTab;
    private bool _previewFailed;

    public ModsPage(MainWindow window)
    {
        _window = window;
        InitializeComponent();
        FolderPath.Text = Paths.Modifications;

        foreach (string tab in ModCatalog.Groups.Select(g => g.Key).Prepend(PresetsTab).Prepend(AllTab).Append(AdvancedTab))
        {
            var segment = new RadioButton { Content = tab, GroupName = "modTabs" + GetHashCode(), IsChecked = tab == _tab };
            segment.SetResourceReference(StyleProperty, "Segment");
            segment.Checked += (_, _) =>
            {
                _tab = tab;
                Refresh();
            };
            Tabs.Children.Add(segment);
        }

        // Windows can't always play OGG; say so once instead of failing silently.
        _player.MediaFailed += (_, e) =>
        {
            Logger.Warn("ModsPage::Preview", e.ErrorException.Message);
            if (_previewFailed) return;
            _previewFailed = true;
            MessageDialog.Show(_window, "Windows can't play this one",
                "This sound's format isn't supported by Windows here (OGG needs Microsoft's Web Media Extensions). Roblox plays it fine in game.");
        };
        Unloaded += (_, _) => _player.Close();
        CleanPreviews();
        Refresh();
    }

    /// <summary>Font and sound previews leave copies in the cache; clear the old ones (in-use ones stay).</summary>
    private static void CleanPreviews()
    {
        foreach (string dir in new[] { "font-preview", "sound-preview" }.Select(d => Path.Combine(Paths.Cache, d)).Where(Directory.Exists))
        {
            foreach (string entry in Directory.EnumerateFileSystemEntries(dir))
            {
                try
                {
                    if (Directory.Exists(entry)) Directory.Delete(entry, recursive: true);
                    else File.Delete(entry);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
        }
    }

    private void Refresh()
    {
        BuildPresets();
        BuildSlots();
        PresetsHost.Visibility = _tab is AllTab or PresetsTab ? Visibility.Visible : Visibility.Collapsed;
        Groups.Visibility = _tab is AdvancedTab or PresetsTab ? Visibility.Collapsed : Visibility.Visible;
        AdvancedCard.Visibility = _tab is AllTab or AdvancedTab ? Visibility.Visible : Visibility.Collapsed;
        ResetAllButton.Visibility = AnythingChanged() ? Visibility.Visible : Visibility.Collapsed;

        List<string> mods;
        try
        {
            Directory.CreateDirectory(Paths.Modifications);
            // A folder that can't be read (permissions, a file in use) is skipped, not fatal to the page.
            var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true };
            mods = Directory.EnumerateFiles(Paths.Modifications, "*", options)
                .Select(f => Path.GetRelativePath(Paths.Modifications, f))
                .Where(r => !r.Equals("README.txt", StringComparison.OrdinalIgnoreCase))
                .OrderBy(r => r)
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Logger.Warn("ModsPage::Refresh", ex.Message);
            mods = new();
        }

        ModList.ItemsSource = mods;
        CountText.Text = mods.Count switch
        {
            0 => "No files yet.",
            1 => "1 file, including the ones picked above:",
            _ => $"{mods.Count} files, including the ones picked above:",
        };
    }

    // ===== Presets (Bloxstrap's, see ModPresets) =====

    private bool _emojiBusy;

    private void BuildPresets()
    {
        PresetsHost.Children.Clear();
        var rows = new StackPanel();
        rows.Children.Add(new TextBlock { Text = "Presets" }.Styled("Text.H2"));
        rows.Children.Add(new TextBlock { Text = "Bloxstrap's one-click mods. Turning one off removes only its own files.", Margin = new Thickness(0, 2, 0, 6) }.Styled("Text.Muted"));

        var cursor = ModPresets.CurrentCursor();
        rows.Children.Add(PresetRow("Mouse cursor", "Roblox's arrow from 2006 or 2013.",
            Segments(ModPresets.CursorOptions.Select(o => (o.Label, o.Style == cursor, (Action)(() => Run(() => ModPresets.SetCursor(o.Style))))))));

        foreach (var preset in ModPresets.Toggles)
        {
            rows.Children.Add(new Border().Styled("Divider"));
            var toggle = new CheckBox { IsChecked = ModPresets.IsOn(preset), VerticalAlignment = VerticalAlignment.Center }.Styled("Toggle");
            toggle.Checked += (_, _) => Run(() => ModPresets.Set(preset, true));
            toggle.Unchecked += (_, _) => Run(() => ModPresets.Set(preset, false));
            rows.Children.Add(PresetRow(preset.Title, preset.Description, toggle));
        }

        rows.Children.Add(new Border().Styled("Divider"));
        var emoji = ModPresets.CurrentEmoji();
        string emojiText = _emojiBusy ? "Downloading the font…"
            : emoji is null ? "You have your own emoji font in the Modifications folder; picking one here replaces it."
            : "Emoji in chat and names. The fonts download from Bloxstrap's server when you pick one.";
        var emojiSegments = Segments(ModPresets.EmojiOptions.Select(o => (o.Label, o.Style == emoji, (Action)(() => PickEmoji(o.Style)))));
        emojiSegments.IsEnabled = !_emojiBusy;
        rows.Children.Add(PresetRow("Emoji style", emojiText, emojiSegments, stacked: true));

        PresetsHost.Children.Add(new Border { Child = rows }.Styled("Card"));
    }

    private static FrameworkElement PresetRow(string title, string description, FrameworkElement control, bool stacked = false)
    {
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
        text.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold });
        text.Children.Add(new TextBlock { Text = description, TextWrapping = TextWrapping.Wrap }.Styled("Text.Muted"));
        if (stacked)
        {
            control.HorizontalAlignment = HorizontalAlignment.Left;
            control.Margin = new Thickness(0, 8, 0, 0);
            var column = new StackPanel { Margin = new Thickness(0, 4, 0, 4) };
            column.Children.Add(text);
            column.Children.Add(control);
            return column;
        }
        var row = new DockPanel { Margin = new Thickness(0, 4, 0, 4) };
        control.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(control, Dock.Right);
        row.Children.Add(control);
        row.Children.Add(text);
        return row;
    }

    /// <summary>A pill-shaped segmented control; each option runs its action when picked.</summary>
    private FrameworkElement Segments(IEnumerable<(string Label, bool Selected, Action Pick)> options)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        string group = "preset" + Guid.NewGuid().ToString("N");
        foreach (var (label, selected, pick) in options)
        {
            var segment = new RadioButton { Content = label, GroupName = group, IsChecked = selected };
            segment.SetResourceReference(StyleProperty, "Segment");
            segment.Checked += (_, _) => { if (!selected) pick(); };
            panel.Children.Add(segment);
        }
        return new Border { Child = panel }.Styled("Segmented");
    }

    private async void PickEmoji(EmojiStyle style)
    {
        _emojiBusy = true;
        Refresh();
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            await ModPresets.SetEmojiAsync(style, App.Http, cts.Token);
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidDataException or IOException or UnauthorizedAccessException or OperationCanceledException)
        {
            Logger.Warn("ModsPage::PickEmoji", ex.Message);
            MessageDialog.Show(_window, "Couldn't change the emoji font", ex is OperationCanceledException ? "The download took too long. Check your connection and try again." : ex.Message);
        }
        finally
        {
            _emojiBusy = false;
            Refresh();
        }
    }

    private static bool AnythingChanged() =>
        ModCatalog.Slots.Any(s => ModCatalog.CurrentFile(s) is not null)
        || ModPresets.Toggles.Any(p => ModPresets.IsOn(p))
        || ModPresets.CurrentEmoji() is not EmojiStyle.Default;

    // ===== Slots =====

    private void BuildSlots()
    {
        Groups.Children.Clear();
        foreach (var group in ModCatalog.Groups.Where(g => _tab == AllTab || g.Key == _tab))
        {
            var rows = new StackPanel();
            rows.Children.Add(new TextBlock { Text = group.Key, Margin = new Thickness(0, 0, 0, 6) }.Styled("Text.H2"));
            bool first = true;
            foreach (var slot in group)
            {
                if (!first)
                    rows.Children.Add(new Border().Styled("Divider"));
                rows.Children.Add(SlotRow(slot));
                first = false;
            }
            Groups.Children.Add(new Border { Child = rows }.Styled("Card"));
        }
    }

    private FrameworkElement SlotRow(ModSlot slot)
    {
        string? current = ModCatalog.CurrentFile(slot);

        var choose = new Button { Content = current is null ? "Choose file…" : "Change…", Margin = new Thickness(6, 0, 0, 0) }.Styled("Button.Secondary");
        choose.Click += (_, _) => Pick(slot);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        if (slot.Kind == ModKind.Sound)
        {
            var play = new Button { Content = "", FontSize = 12, Padding = new Thickness(10, 7, 10, 7), Margin = new Thickness(0, 0, 4, 0),
                ToolTip = current is null ? "Play Roblox's sound" : "Play your sound" }.Styled("Button.Ghost");
            play.SetResourceReference(Control.FontFamilyProperty, "Font.Icons");
            play.Click += (_, _) => PlaySound(slot, current);
            buttons.Children.Add(play);
        }
        if (current is not null)
        {
            var reset = new Button { Content = "Reset" }.Styled("Button.Ghost");
            reset.Click += (_, _) => Run(() => ModCatalog.Reset(slot));
            buttons.Children.Add(reset);
        }
        buttons.Children.Add(choose);
        DockPanel.SetDock(buttons, Dock.Right);

        var status = new TextBlock { Margin = new Thickness(0, 3, 0, 0), FontSize = 12 };
        if (current is null)
        {
            status.Text = "Roblox default";
            status.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
        }
        else
        {
            var info = new FileInfo(current);
            status.Text = ModPresets.OwnerOf(slot.Targets[0]) is { } preset
                ? $"From the {preset.Title} preset"
                : $"Custom · {Size(info.Length)} · changed {info.LastWriteTime:d MMM}";
            status.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Accent");
        }

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
        text.Children.Add(new TextBlock { Text = slot.Title, FontWeight = FontWeights.SemiBold });
        text.Children.Add(new TextBlock { Text = slot.Description }.Styled("Text.Muted"));
        text.Children.Add(status);

        var preview = Preview(slot, current);
        DockPanel.SetDock(preview, Dock.Left);

        var row = new DockPanel { Margin = new Thickness(0, 4, 0, 4), Background = Brushes.Transparent, AllowDrop = true, ToolTip = $"{slot.FileTypes} · drop a file here" };
        row.Children.Add(buttons);
        row.Children.Add(preview);
        row.Children.Add(text);

        // Drag a file from Explorer straight onto the row.
        row.DragOver += (_, e) =>
        {
            e.Effects = DroppedFile(e) is { } f && ModCatalog.Accepts(slot, f) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        };
        row.Drop += (_, e) =>
        {
            if (DroppedFile(e) is { } file)
                Run(() => ModCatalog.Apply(slot, file));
        };
        return row;
    }

    /// <summary>A 44 px tile: the cursor image itself, "Aa" in the chosen font, or a note for sounds.</summary>
    private static FrameworkElement Preview(ModSlot slot, string? current)
    {
        var tile = new Border { Width = 44, Height = 44, CornerRadius = new CornerRadius(10), Margin = new Thickness(0, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center };
        tile.SetResourceReference(Border.BackgroundProperty, current is null ? "Brush.SurfaceAlt" : "Brush.AccentSoft");

        UIElement content = slot.Kind switch
        {
            ModKind.Image when LoadImage(current ?? RobloxOriginal(slot)) is { } image =>
                new Image { Source = image, Width = 32, Height = 32, Stretch = Stretch.Uniform },
            ModKind.Font => FontSample(current),
            ModKind.Sound => new TextBlock { Text = "", FontSize = 18 }.Styled("Text.Icon"),
            _ => new TextBlock { Text = "", FontSize = 18 }.Styled("Text.Icon"),
        };
        if (content is FrameworkElement element)
        {
            element.HorizontalAlignment = HorizontalAlignment.Center;
            element.VerticalAlignment = VerticalAlignment.Center;
        }
        if (content is TextBlock glyph)
            glyph.SetResourceReference(TextBlock.ForegroundProperty, current is null ? "Brush.TextMuted" : "Brush.Accent");
        tile.Child = content;
        return tile;
    }

    /// <summary>Roblox's own file for the slot, from the installed version (its backup once a mod replaced it).</summary>
    private static string? RobloxOriginal(ModSlot slot)
    {
        if (Store.State.VersionGuid is not { } guid)
            return null;
        string rel = slot.Targets[0].Replace('/', '\\');
        string version = Path.Combine(Paths.Versions, guid);
        string backup = Path.Combine(version, ".bloxcode-originals", rel);
        string file = Path.Combine(version, rel);
        return File.Exists(backup) ? backup : File.Exists(file) ? file : null;
    }

    private static BitmapImage? LoadImage(string? path)
    {
        if (path is null || !File.Exists(path))
            return null;
        try
        {
            // OnLoad reads the whole file now, so the mod can still be replaced or deleted.
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            image.UriSource = new Uri(path);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or InvalidOperationException)
        {
            return null;
        }
    }

    private static TextBlock FontSample(string? font)
    {
        var sample = new TextBlock { Text = "Aa", FontSize = 18, FontWeight = FontWeights.SemiBold };
        if (font is null)
            return sample;
        try
        {
            // WPF keeps font files open, so it reads a throwaway copy rather than the mod itself.
            string dir = Path.Combine(Paths.Cache, "font-preview", DateTime.UtcNow.Ticks.ToString());
            Directory.CreateDirectory(dir);
            string copy = Path.Combine(dir, "preview" + Path.GetExtension(font));
            File.Copy(font, copy);
            var face = new GlyphTypeface(new Uri(copy));
            string family = face.FamilyNames.Values.FirstOrDefault() ?? "";
            sample.FontFamily = new FontFamily(new Uri(dir + "\\"), "./#" + family);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FileFormatException or ArgumentException)
        {
            Logger.Warn("ModsPage::FontSample", ex.Message);
        }
        return sample;
    }

    /// <summary>Plays the slot's sound: the custom one, or Roblox's own from the installed version.</summary>
    private void PlaySound(ModSlot slot, string? current)
    {
        string? file = current ?? RobloxOriginal(slot);
        if (file is null)
        {
            MessageDialog.Show(_window, "Nothing to play yet", "Roblox's own sounds show up here after Blox Code has downloaded Roblox once.");
            return;
        }
        try
        {
            // A throwaway copy, so the player never holds the mod file open.
            string dir = Path.Combine(Paths.Cache, "sound-preview");
            Directory.CreateDirectory(dir);
            string copy = Path.Combine(dir, "preview-" + DateTime.UtcNow.Ticks + Path.GetExtension(file));
            File.Copy(file, copy);
            _player.Close();
            _player.Open(new Uri(copy));
            _player.Play();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Logger.Warn("ModsPage::Preview", ex.Message);
        }
    }

    private async void OnResetAll(object sender, RoutedEventArgs e)
    {
        var swapped = ModCatalog.Slots.Where(s => ModCatalog.CurrentFile(s) is not null).ToList();
        var presets = ModPresets.Toggles.Where(p => ModPresets.IsOn(p)).ToList();
        bool emoji = ModPresets.CurrentEmoji() is not EmojiStyle.Default and not null;
        int count = swapped.Count + presets.Count + (emoji ? 1 : 0);
        if (count == 0 || !MessageDialog.Ask(_window, $"Reset {count} mod(s)?",
                "Roblox's own cursor, sounds, font and emoji come back on the next launch. Files in the Advanced folder stay.", "Reset all", "Cancel"))
            return;
        Run(() =>
        {
            presets.ForEach(p => ModPresets.Set(p, false));
            swapped.ForEach(s => ModCatalog.Reset(s));
        });
        if (emoji)
            await ModPresets.SetEmojiAsync(EmojiStyle.Default, App.Http, CancellationToken.None); // only deletes, no download
        Refresh();
    }

    private static string? DroppedFile(DragEventArgs e) =>
        e.Data.GetData(DataFormats.FileDrop) is string[] { Length: 1 } files && File.Exists(files[0]) ? files[0] : null;

    private void Pick(ModSlot slot)
    {
        string patterns = string.Join(";", slot.Extensions.Select(x => "*" + x));
        var picker = new OpenFileDialog { Title = $"Choose a file for {slot.Title}", Filter = $"{slot.FileTypes}|{patterns}" };
        if (picker.ShowDialog(_window) == true)
            Run(() => ModCatalog.Apply(slot, picker.FileName));
    }

    private void Run(Action change)
    {
        try
        {
            change();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Logger.Warn("ModsPage::Run", ex.Message);
            MessageDialog.Show(_window, "Couldn't change that mod", ex.Message);
        }
        Refresh();
    }

    private static string Size(long bytes) => bytes < 1024 * 1024 ? $"{Math.Max(1, bytes / 1024)} KB" : $"{bytes / (1024.0 * 1024):0.#} MB";

    private void OnRefresh(object sender, RoutedEventArgs e) => Refresh();

    private void OnOpenFolder(object sender, RoutedEventArgs e) =>
        Shell.Open(Paths.Modifications, Window.GetWindow(this));
}

internal static class StyleExtensions
{
    /// <summary>Applies a style from the app's resources to a control built in code.</summary>
    public static T Styled<T>(this T element, string key) where T : FrameworkElement
    {
        element.SetResourceReference(FrameworkElement.StyleProperty, key);
        return element;
    }
}
