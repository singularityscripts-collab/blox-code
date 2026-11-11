using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BloxCode.Core;

namespace BloxCode.UI.Pages;

/// <summary>
/// "Your time in Roblox": total play time, sessions, games and the day streak, a bar per day and
/// the games played most, for the week, the month or everything, from the local history.
/// </summary>
public partial class StatsPage : UserControl
{
    private readonly MainWindow _window;
    private readonly List<PlayedGame> _history;

    public StatsPage(MainWindow window)
    {
        _window = window;
        InitializeComponent();
        _history = Store.Settings.RememberGames ? GameHistory.Load() : new();
        Render();
    }

    private int? Days => PeriodWeek.IsChecked == true ? 7 : PeriodMonth.IsChecked == true ? 30 : null;

    private void OnPeriod(object sender, RoutedEventArgs e)
    {
        if (IsLoaded)
            Render();
    }

    private void Render()
    {
        var report = PlayStatsReport.Build(_history, Days, DateTime.UtcNow);
        Intro.Text = !Store.Settings.RememberGames
            ? "Turn on \"Remember the games I play\" in Settings to see your play time here."
            : _history.Count == 0
                ? "Play a game through Blox Code and your time shows up here. It stays on this PC."
                : "Kept only on this PC, from the games you open through Blox Code.";

        Numbers.Children.Clear();
        Numbers.Children.Add(Number(GameHistory.Duration(report.Total).Replace("under a minute", "0m"), "played"));
        Numbers.Children.Add(Number(report.Sessions.ToString(), report.Sessions == 1 ? "session" : "sessions"));
        Numbers.Children.Add(Number(report.Games.ToString(), report.Games == 1 ? "game" : "games"));
        Numbers.Children.Add(Number(report.StreakDays.ToString(), report.StreakDays == 1 ? "day in a row" : "days in a row"));

        DrawDays(report);

        TopGames.Children.Clear();
        var top = report.TopGames.Take(8).ToList();
        double most = top.Count > 0 ? top[0].Time.TotalSeconds : 1;
        foreach (var game in top)
            TopGames.Children.Add(GameRow(game, most));
        if (top.Count == 0)
            TopGames.Children.Add(Muted("Nothing in this period yet."));
    }

    private static UIElement Number(string value, string label)
    {
        var panel = new StackPanel();
        var big = new TextBlock { Text = value, FontSize = 26, FontWeight = FontWeights.Medium };
        big.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
        panel.Children.Add(big);
        var small = new TextBlock { Text = label, Margin = new Thickness(0, 2, 0, 0) };
        small.SetResourceReference(StyleProperty, "Text.Mono");
        panel.Children.Add(small);
        return panel;
    }

    /// <summary>One bar per day (the last 30 for "All time"), its height the time played that day.</summary>
    private void DrawDays(PlayReport report)
    {
        var days = report.Days.TakeLast(30).ToList();
        DayChart.Children.Clear();
        DayChart.ColumnDefinitions.Clear();
        double most = Math.Max(days.Count == 0 ? 0 : days.Max(d => d.Time.TotalMinutes), 1);
        for (int i = 0; i < days.Count; i++)
        {
            DayChart.ColumnDefinitions.Add(new ColumnDefinition());
            var (day, time) = days[i];
            var bar = new Border
            {
                Height = time > TimeSpan.Zero ? Math.Max(4, 120 * time.TotalMinutes / most) : 2,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(days.Count > 14 ? 2 : 5, 0, days.Count > 14 ? 2 : 5, 0),
                CornerRadius = new CornerRadius(3),
                ToolTip = $"{day:ddd d MMM}: {(time > TimeSpan.Zero ? GameHistory.Duration(time) : "no play")}",
            };
            bar.SetResourceReference(Border.BackgroundProperty, time > TimeSpan.Zero ? "Brush.Accent" : "Brush.Border");
            Grid.SetColumn(bar, i);
            DayChart.Children.Add(bar);
        }
        DaysTitle.Text = Days is null ? "the last 30 days" : "every day";
        ChartStart.Text = days.Count > 0 ? days[0].Day.ToString("d MMM") : "";
        ChartEnd.Text = "today";
    }

    private UIElement GameRow(GameTime game, double most)
    {
        var row = new DockPanel { Margin = new Thickness(0, 8, 0, 8) };
        var icon = new Border { Width = 40, Height = 40, CornerRadius = new CornerRadius(10), Margin = new Thickness(0, 0, 14, 0) };
        icon.SetResourceReference(Border.BackgroundProperty, "Brush.SurfaceAlt");
        if (game.IconUrl is { } url && Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            try
            {
                var image = new System.Windows.Media.Imaging.BitmapImage();
                image.BeginInit();
                image.UriSource = uri;
                image.DecodePixelWidth = 80;
                image.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                image.EndInit();
                icon.Background = new ImageBrush(image) { Stretch = Stretch.UniformToFill };
            }
            catch (Exception ex) { Logger.Warn("StatsPage::Icon", ex.Message); }
        }
        DockPanel.SetDock(icon, Dock.Left);
        row.Children.Add(icon);

        var time = new TextBlock { Text = GameHistory.Duration(game.Time), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
        time.SetResourceReference(StyleProperty, "Text.Mono");
        DockPanel.SetDock(time, Dock.Right);
        row.Children.Add(time);

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var name = new TextBlock { Text = GameHistory.PlainName(game.Name), TextTrimming = TextTrimming.CharacterEllipsis };
        name.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
        text.Children.Add(name);
        var track = new Grid { Height = 3, Margin = new Thickness(0, 7, 0, 0) };
        var back = new Border { CornerRadius = new CornerRadius(1.5) };
        back.SetResourceReference(Border.BackgroundProperty, "Brush.Border");
        track.Children.Add(back);
        var fill = new Border { CornerRadius = new CornerRadius(1.5), HorizontalAlignment = HorizontalAlignment.Left };
        fill.SetResourceReference(Border.BackgroundProperty, "Brush.Accent");
        track.Children.Add(fill);
        track.SizeChanged += (_, e) => fill.Width = e.NewSize.Width * Math.Clamp(game.Time.TotalSeconds / most, 0, 1);
        text.Children.Add(track);
        row.Children.Add(text);

        var button = new Button { Content = row, HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(6, 0, 6, 0), BorderThickness = new Thickness(0), ToolTip = $"Play {game.Name} · {game.Sessions} sessions" };
        button.SetResourceReference(StyleProperty, "Button.Base");
        button.Background = Brushes.Transparent;
        button.Click += (_, _) => _window.LaunchRoblox(GameShortcuts.LaunchLink(game.PlaceId));
        return button;
    }

    private static TextBlock Muted(string text)
    {
        var block = new TextBlock { Text = text, Margin = new Thickness(0, 8, 0, 0) };
        block.SetResourceReference(StyleProperty, "Text.Muted");
        return block;
    }
}
