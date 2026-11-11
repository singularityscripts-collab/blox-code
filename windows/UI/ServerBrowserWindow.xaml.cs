using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using BloxCode.Core;

namespace BloxCode.UI;

/// <summary>
/// The public servers of one game, to pick one before joining (by players or ping); Join launches
/// straight into it. Closes with the chosen launch link in <see cref="Choice"/>.
/// </summary>
public partial class ServerBrowserWindow : Window
{
    private readonly long _placeId;
    private readonly List<ServerInfo> _servers = new();
    private string? _cursor;
    private CancellationTokenSource _cts = new();
    private bool _loading;

    /// <summary>The launch link for the server that was picked; null when closed without joining.</summary>
    public string? Choice { get; private set; }

    public ServerBrowserWindow(Window owner, long placeId, string gameName)
    {
        _placeId = placeId;
        InitializeComponent();
        Owner = owner;
        ThemeManager.ApplyChrome(this);
        GameText.Text = gameName;
        Loaded += async (_, _) => await LoadAsync(reset: true);
        Closed += (_, _) => _cts.Cancel();
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
    }

    private ServerSort ApiSort => SortEmpty.IsChecked == true ? ServerSort.FewestPlayers : ServerSort.MostPlayers;

    private async Task LoadAsync(bool reset)
    {
        if (_loading && !reset)
            return;
        if (reset)
        {
            _cts.Cancel();
            _cts = new CancellationTokenSource();
            _servers.Clear();
            _cursor = null;
            ServerList.Children.Clear();
        }
        var ct = _cts.Token;
        _loading = true;
        MoreButton.IsEnabled = false;
        RetryButton.Visibility = Visibility.Collapsed;
        StatusText.Text = _servers.Count == 0 ? "Asking Roblox for servers…" : "";
        try
        {
            var page = await ServerBrowser.GetAsync(App.Http, _placeId, ApiSort, _cursor, ct);
            if (ct.IsCancellationRequested)
                return;
            _servers.AddRange(page.Servers.Where(s => _servers.All(o => o.Id != s.Id)));
            _cursor = page.NextCursor;
            Render();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidDataException or System.Text.Json.JsonException)
        {
            Logger.Warn("ServerBrowserWindow::Load", ex.Message);
            StatusText.Text = ex is HttpRequestException { StatusCode: System.Net.HttpStatusCode.TooManyRequests }
                ? "Roblox asked us to slow down. Try again in a minute."
                : $"Couldn't get the servers: {ex.Message}";
            RetryButton.Visibility = Visibility.Visible;
        }
        finally
        {
            _loading = false;
            MoreButton.IsEnabled = true;
        }
    }

    private void Render()
    {
        IEnumerable<ServerInfo> shown = _servers;
        if (SortPing.IsChecked == true)
            shown = shown.OrderBy(s => s.Ping ?? int.MaxValue);
        else if (SortEmpty.IsChecked == true)
            shown = shown.OrderBy(s => s.Playing);
        else
            shown = shown.OrderByDescending(s => s.Playing);

        ServerList.Children.Clear();
        foreach (var server in shown)
            ServerList.Children.Add(Row(server));
        StatusText.Text = _servers.Count == 0 ? "No public server with room right now." : "";
        CountText.Text = _servers.Count == 0 ? "" : $"{_servers.Count} servers";
        MoreButton.Visibility = _cursor is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private UIElement Row(ServerInfo server)
    {
        var row = new DockPanel { Margin = new Thickness(0, 0, 4, 0) };
        var join = new Button { Content = "Join", Padding = new Thickness(16, 6, 16, 6), VerticalAlignment = VerticalAlignment.Center };
        join.SetResourceReference(StyleProperty, "Button.Primary");
        join.Click += (_, _) =>
        {
            Choice = ServerBrowser.JoinArgs(_placeId, server);
            Close();
        };
        DockPanel.SetDock(join, Dock.Right);
        row.Children.Add(join);

        // Players, as a number and a thin bar of how full it is.
        var players = new StackPanel { Width = 150, VerticalAlignment = VerticalAlignment.Center };
        players.Children.Add(new TextBlock { Text = $"{server.Playing} / {server.MaxPlayers} players", FontSize = 13.5 });
        var track = new Border { Height = 3, CornerRadius = new CornerRadius(1.5), Margin = new Thickness(0, 6, 24, 0) };
        track.SetResourceReference(Border.BackgroundProperty, "Brush.ToggleOff");
        var fill = new Border { Height = 3, CornerRadius = new CornerRadius(1.5), HorizontalAlignment = HorizontalAlignment.Left, Width = 126.0 * Math.Clamp(server.Playing / (double)server.MaxPlayers, 0, 1) };
        fill.SetResourceReference(Border.BackgroundProperty, "Brush.Accent");
        players.Children.Add(new Grid { Children = { track, fill }, Margin = new Thickness(0, 0, 0, 0) });
        DockPanel.SetDock(players, Dock.Left);
        row.Children.Add(players);

        var details = new TextBlock
        {
            Text = (server.Ping is { } ping ? $"{ping} ms" : "ping ?") + $"  ·  {Math.Round(server.Fps):0} fps",
            VerticalAlignment = VerticalAlignment.Center,
        };
        details.SetResourceReference(StyleProperty, "Text.Mono");
        row.Children.Add(details);

        var line = new Border { Child = row, Padding = new Thickness(0, 10, 0, 10), BorderThickness = new Thickness(0, 0, 0, 1) };
        line.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
        return line;
    }

    private async void OnSortChanged(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded)
            return;
        // Players sort on Roblox's side (its order decides which servers come first); ping sorts what's loaded.
        if (sender == SortPing)
            Render();
        else
            await LoadAsync(reset: true);
    }

    private async void OnRetry(object sender, RoutedEventArgs e) => await LoadAsync(reset: _servers.Count == 0);

    private async void OnMore(object sender, RoutedEventArgs e) => await LoadAsync(reset: false);

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    private void OnDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed && e.OriginalSource is not Button)
            DragMove();
    }
}
