using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using BloxCode.Core;

namespace BloxCode.UI;

public partial class BootstrapperWindow : Window, IGlassWindow
{
    public string SolidBackground => "Brush.Sidebar";

    private readonly LaunchSettings? _launch;
    private readonly CancellationTokenSource _cts = new();
    private readonly Stopwatch _elapsed = Stopwatch.StartNew();
    private readonly DispatcherTimer _ticker = new() { Interval = TimeSpan.FromMilliseconds(130) };

    private double? _fraction;      // null = indeterminate, Bit paces back and forth
    private double _paceX;
    private int _paceDir = 1;
    private string _sizeText = "";
    private bool _finished;
    private bool _closed;

    /// <summary>Real launch.</summary>
    public BootstrapperWindow(LaunchSettings launch) : this()
    {
        _launch = launch;
        Loaded += async (_, _) => await RunAsync();
    }

    /// <summary>Preview mode for the Appearance page: plays a fake download, launches nothing.</summary>
    public static BootstrapperWindow Preview(Window owner)
    {
        var window = new BootstrapperWindow { Owner = owner, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        window.Loaded += async (_, _) => await window.RunPreviewAsync();
        return window;
    }

    /// <summary>Self-update: download the new Blox Code, restart into it with the same arguments.</summary>
    public static BootstrapperWindow ForUpdate(UpdateInfo update, string[] forwardArgs)
    {
        var window = new BootstrapperWindow();
        window.VersionText.Text = $"blox code {Updater.Current} → {update.Version}";
        window.Loaded += async (_, _) => await window.RunUpdateAsync(update, forwardArgs);
        return window;
    }

    private async Task RunUpdateAsync(UpdateInfo update, string[] forwardArgs)
    {
        try
        {
            var progress = new Progress<(long Done, long Total)>(p =>
                OnProgress(new BootstrapProgress(BootstrapPhase.Downloading, $"Updating Blox Code to v{update.Version}",
                    p.Total > 0 ? (double)p.Done / p.Total : null, $"{p.Done / 1048576.0:0.0} MB / {p.Total / 1048576.0:0.0} MB")));

            OnProgress(new BootstrapProgress(BootstrapPhase.Downloading, $"Updating Blox Code to v{update.Version}"));
            string exe = await Updater.DownloadAsync(App.Http, update, progress, _cts.Token);

            OnProgress(new BootstrapProgress(BootstrapPhase.Installing, "Reinstalling Blox Code", 1));
            Updater.LaunchUpgrade(exe, forwardArgs);
            Succeed($"Updated to v{update.Version} — restarting");
            await Task.Delay(900);
            Application.Current.Shutdown();
        }
        catch (OperationCanceledException)
        {
            SafeClose();
        }
        catch (Exception ex)
        {
            Logger.Error("BootstrapperWindow::RunUpdate", ex);
            Fail($"Couldn't update Blox Code: {Describe(ex)}");
            DetailText.Text = "(you can keep going on this version — it'll try again next time)";
            CancelButton.Content = "Continue";
            _continueArgs = forwardArgs;
        }
    }

    private string[]? _continueArgs;

    private BootstrapperWindow()
    {
        InitializeComponent();
        ThemeManager.ApplyChrome(this);
        Mascot.IsAnimated = Store.Settings.AnimateMascot;
        ApplyLaunchTheme(Store.Settings.LaunchTheme);
        VersionText.Text = $"{Store.Settings.Channel} · blox code {App.Version}";

        _ticker.Tick += (_, _) => Tick();
        _ticker.Start();
        Closed += (_, _) => { _closed = true; _ticker.Stop(); _cts.Cancel(); };
    }

    private LaunchTheme _theme;

    /// <summary>Look → Launch screen: Bit on the bar (as designed first), a quiet title and thin bar, or a text bar.</summary>
    private void ApplyLaunchTheme(LaunchTheme theme)
    {
        _theme = theme;
        if (theme == LaunchTheme.Bit)
            return;
        Stage.Visibility = Visibility.Collapsed;
        Height = 250;
        if (theme == LaunchTheme.Minimal)
        {
            Progress.Height = 3;
            Progress.Margin = new Thickness(0, 14, 0, 0);
            StatusText.FontSize = 20;
            Spinner.Visibility = Visibility.Collapsed;
        }
        else
        {
            Progress.Visibility = Visibility.Collapsed;
            AsciiBar.Visibility = Visibility.Visible;
            AsciiBar.Text = Bar(null);
            StatusText.FontFamily = (System.Windows.Media.FontFamily)FindResource("Font.Mono");
            StatusText.FontWeight = FontWeights.Normal;
            StatusText.FontSize = 14;
        }
    }

    /// <summary>[██████░░░░…] for the Terminal theme; dots when the size isn't known yet.</summary>
    public static string Bar(double? fraction, int width = 32) => fraction is { } f
        ? "[" + new string('█', (int)Math.Round(Math.Clamp(f, 0, 1) * width)) + new string('░', width - (int)Math.Round(Math.Clamp(f, 0, 1) * width)) + "]"
        : "[" + new string('·', width) + "]";

    private void SafeClose()
    {
        if (!_closed)
            Close();
    }

    private async Task RunAsync()
    {
        // Only one bootstrapper at a time; a second launch waits and then finds Roblox up to date.
        using var gate = new Semaphore(1, 1, "BloxCode-Bootstrapper");
        bool entered = gate.WaitOne(0);
        if (!entered)
        {
            OnProgress(new BootstrapProgress(BootstrapPhase.Connecting, "Waiting for another Blox Code launch to finish"));
            entered = await Task.Run(() => gate.WaitOne(TimeSpan.FromMinutes(3)));
        }

        try
        {
            var progress = new Progress<BootstrapProgress>(OnProgress);
            var bootstrapper = new Bootstrapper(App.Http, _launch!, progress);
            try
            {
                await Task.Run(() => bootstrapper.RunAsync(_cts.Token));
            }
            finally
            {
                // Let a queued launch start now rather than after our closing animation.
                if (entered)
                {
                    gate.Release();
                    entered = false;
                }
            }

            VersionText.Text = $"{Store.State.Channel} · {bootstrapper.VersionGuid}";
            string app = _launch!.Mode == LaunchMode.Studio ? "Roblox Studio" : "Roblox";
            Succeed(_launch.NoLaunch ? $"{app} is up to date" : $"{app} is running");
            if (bootstrapper.LaunchedAt is { } launchedAt && ActivityWatcher.IsWanted)
                WatchRoblox(launchedAt);
            await Task.Delay(1400);
            SafeClose();
        }
        catch (OperationCanceledException)
        {
            SafeClose();
        }
        catch (Exception ex)
        {
            Logger.Error("BootstrapperWindow::Run", ex);
            Fail(Describe(ex));
            // A network or Roblox failure may be Roblox's own outage: ask its status page and say so.
            if (ex is not IOException and not OperationCanceledException)
                _ = AddRobloxStatusAsync();
        }
        finally
        {
            if (entered)
                gate.Release();
        }
    }

    private async Task AddRobloxStatusAsync()
    {
        string? note = RobloxStatus.Explain(await RobloxStatus.GetAsync(App.Http, CancellationToken.None));
        if (note is null)
            return;
        Logger.Info("BootstrapperWindow::Fail", note);
        StatusText.Text += "\n" + note;
        Height = 380;
    }

    /// <summary>Keeps Blox Code running in the background (no window) until Roblox closes, for Discord and the server notice.</summary>
    private static void WatchRoblox(DateTime launchedAt)
    {
        var app = Application.Current;
        app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var dispatcher = app.Dispatcher;
        Task.Run(() => ActivityWatcher.RunAsync(App.Http, launchedAt,
                (title, body, invite) => dispatcher.BeginInvoke(() => ToastWindow.Show(title, body, copyLabel: "Copy invite link", copy: invite))))
            .ContinueWith(_ => dispatcher.BeginInvoke(() => app.Shutdown()));
    }

    private async Task RunPreviewAsync()
    {
        var steps = new (string Status, double? Fraction)[]
        {
            ("Connecting to Roblox", null), ("Checking for updates", null),
        };
        try
        {
            foreach (var (status, fraction) in steps)
            {
                OnProgress(new BootstrapProgress(BootstrapPhase.Checking, status, fraction));
                await Task.Delay(1200, _cts.Token);
            }
            for (int i = 0; i <= 100; i++)
            {
                string package = i < 55 ? "RobloxApp.zip" : i < 80 ? "content-textures2.zip" : "extracontent-luapackages.zip";
                OnProgress(new BootstrapProgress(BootstrapPhase.Downloading, $"Downloading {package}", i / 100.0, $"{i * 1.24:0.0} MB / {124.0:0.0} MB"));
                await Task.Delay(45, _cts.Token);
            }
            OnProgress(new BootstrapProgress(BootstrapPhase.Installing, "Applying your tweaks", 1));
            await Task.Delay(900, _cts.Token);
            Succeed("Preview finished — nothing was launched");
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void OnProgress(BootstrapProgress p)
    {
        if (_finished)
            return;

        StatusText.Text = p.Status + "…";
        _sizeText = p.Detail ?? "";
        _fraction = p.Fraction;

        Progress.IsIndeterminate = false;
        Progress.Value = p.Fraction ?? 0;
        Progress.Opacity = p.Fraction is null ? 0.5 : 1;
        PercentText.Text = p.Fraction is { } f ? $"{(int)Math.Round(f * 100)}%" : "";
        if (_theme == LaunchTheme.Terminal)
            AsciiBar.Text = Bar(p.Fraction);

        Mascot.Mood = p.Phase switch
        {
            BootstrapPhase.Downloading => MascotMood.Walk,
            BootstrapPhase.Installing => MascotMood.Work,
            BootstrapPhase.Launching => MascotMood.Happy,
            _ => MascotMood.Walk,
        };

        TaskbarItemInfo ??= new System.Windows.Shell.TaskbarItemInfo();
        TaskbarItemInfo.ProgressState = p.Fraction is null
            ? System.Windows.Shell.TaskbarItemProgressState.Indeterminate
            : System.Windows.Shell.TaskbarItemProgressState.Normal;
        TaskbarItemInfo.ProgressValue = p.Fraction ?? 0;
    }

    private void Tick()
    {
        if (!_finished)
        {
            string size = _sizeText.Length > 0 ? $" · {_sizeText}" : "";
            DetailText.Text = $"({(int)_elapsed.Elapsed.TotalSeconds}s{size} · esc to cancel)";
        }
        PlaceMascot();
    }

    private void PlaceMascot()
    {
        double track = Math.Max(0, Stage.ActualWidth - Mascot.ActualWidth);
        double x;

        if (_fraction is double f && !_finished)
        {
            x = f * track;
            _paceX = x;
        }
        else if (!_finished && Mascot.IsAnimated)
        {
            // Indeterminate: pace back and forth, one "pixel" per tick.
            _paceX += _paceDir * Mascot.Unit;
            if (_paceX >= track) { _paceX = track; _paceDir = -1; }
            if (_paceX <= 0) { _paceX = 0; _paceDir = 1; }
            x = _paceX;
        }
        else
        {
            x = _paceX;
        }

        Canvas.SetLeft(Mascot, x);
    }

    private void OnStageSizeChanged(object sender, SizeChangedEventArgs e) => PlaceMascot();

    private void Succeed(string message)
    {
        _finished = true;
        _fraction = 1;
        Progress.Value = 1;
        Progress.Opacity = 1;
        PercentText.Text = "100%";
        if (_theme == LaunchTheme.Terminal)
            AsciiBar.Text = Bar(1);
        StatusText.Text = message;
        DetailText.Text = $"({(int)_elapsed.Elapsed.TotalSeconds}s · done)";
        ShowGlyph("✓", "Brush.Success");
        CancelButton.Content = "Close";
        Mascot.Celebrate();
        if (TaskbarItemInfo is not null)
            TaskbarItemInfo.ProgressState = System.Windows.Shell.TaskbarItemProgressState.None;
    }

    private void Fail(string message)
    {
        _finished = true;
        StatusText.Text = message;
        StatusText.TextWrapping = TextWrapping.Wrap;
        StatusText.TextTrimming = TextTrimming.None;
        DetailText.Text = "(Bit tripped — the log has the details)";
        ShowGlyph("✗", "Brush.Danger");
        Progress.SetResourceReference(ForegroundProperty, "Brush.Danger");
        Mascot.Mood = MascotMood.Sad;
        CancelButton.Content = "Close";
        LogButton.Visibility = Visibility.Visible;
        // The failure message may wrap onto more lines than the status line has room for.
        Height = 340;
        if (TaskbarItemInfo is not null)
            TaskbarItemInfo.ProgressState = System.Windows.Shell.TaskbarItemProgressState.Error;
    }

    private void ShowGlyph(string glyph, string brushKey)
    {
        Spinner.Visibility = Visibility.Collapsed;
        ResultGlyph.Text = glyph;
        ResultGlyph.SetResourceReference(TextBlock.ForegroundProperty, brushKey);
        ResultGlyph.Visibility = Visibility.Visible;
    }

    private static string Describe(Exception ex) => ex switch
    {
        AggregateException { InnerExceptions.Count: > 0 } when ex.Message.Contains("mirror") =>
            "Couldn't reach Roblox. Check your connection or status.roblox.com.",
        HttpRequestException http => $"Network error: {http.Message}",
        InvalidChannelException channel => channel.Message,
        IOException io => $"File error: {io.Message} (is Roblox still open?)",
        _ => ex.Message,
    };

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        // A failed self-update shouldn't block the user: carry on with what they originally asked for.
        if (_continueArgs is { } args)
        {
            Updater.RunCopy(Paths.Process, args);
            _continueArgs = null;
        }

        _cts.Cancel();
        SafeClose();
    }

    private void OnOpenLog(object sender, RoutedEventArgs e)
    {
        if (Logger.FilePath is not null)
            Shell.Open(Logger.FilePath, this);
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
            OnCancel(sender, e);
    }
}
