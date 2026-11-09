using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using BloxCode.Core;
using BloxCode.Linux.Platform;
using BloxCode.Linux.Runtime;

namespace BloxCode.Linux.UI;

/// <summary>
/// The launch screen: Bit walks along the progress bar while Blox Code gets Sober ready. Also used
/// for self-updates, for installing Sober, and as a preview from the Appearance page.
/// </summary>
public partial class LaunchWindow : Window
{
    private readonly CancellationTokenSource _cts = new();
    private readonly Stopwatch _elapsed = Stopwatch.StartNew();
    private readonly DispatcherTimer _ticker = new() { Interval = TimeSpan.FromMilliseconds(130) };

    private double? _fraction;      // null = indeterminate, Bit paces back and forth
    private double _paceX;
    private int _paceDir = 1;
    private string _sizeText = "";
    private bool _finished;
    private bool _closed;
    private string[]? _continueArgs;

    public LaunchWindow()
    {
        InitializeComponent();
        Mascot.IsAnimated = Store.Settings.AnimateMascot;
        VersionText.Text = $"sober · blox code {Updater.Current}";
        Chrome.MakeDragArea(this, TitleBar, maximizeOnDoubleClick: false);

        _ticker.Tick += (_, _) => Tick();
        _ticker.Start();
        Closed += (_, _) => { _closed = true; _ticker.Stop(); _cts.Cancel(); };
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
                OnCancel(this, new RoutedEventArgs());
        };
        Stage.SizeChanged += (_, _) => PlaceMascot();
    }

    /// <summary>A real launch: Fast Flags and mods into Sober, then Sober itself.</summary>
    public static LaunchWindow ForLaunch(LaunchSettings launch)
    {
        var window = new LaunchWindow();
        window.Opened += async (_, _) => await window.RunLaunchAsync(launch);
        return window;
    }

    /// <summary>Preview mode for the Appearance page: plays a fake launch, starts nothing.</summary>
    public static LaunchWindow Preview()
    {
        var window = new LaunchWindow { WindowStartupLocation = WindowStartupLocation.CenterOwner };
        window.Opened += async (_, _) => await window.RunPreviewAsync();
        return window;
    }

    /// <summary>Self-update: download the new Blox Code, restart into it with the same arguments.</summary>
    public static LaunchWindow ForUpdate(UpdateInfo update, string[] forwardArgs)
    {
        var window = new LaunchWindow();
        window.VersionText.Text = $"blox code {Updater.Current} → {update.Version}";
        window.Opened += async (_, _) => await window.RunUpdateAsync(update, forwardArgs);
        return window;
    }

    /// <summary>Installs Sober from Flathub; <paramref name="done"/> runs on success.</summary>
    public static LaunchWindow ForSoberInstall(Action done)
    {
        var window = new LaunchWindow { WindowStartupLocation = WindowStartupLocation.CenterOwner };
        window.VersionText.Text = $"flathub · {Sober.AppId}";
        window.Opened += async (_, _) => await window.RunSoberInstallAsync(done);
        return window;
    }

    private async Task RunLaunchAsync(LaunchSettings launch)
    {
        // Only one launch at a time; a second one waits for the first.
        using var gate = InstanceLock.TryAcquire("launch") ?? await WaitForGateAsync();
        try
        {
            var launcher = new SoberLauncher(launch, new Progress<BootstrapProgress>(OnProgress));
            await Task.Run(() => launcher.RunAsync(_cts.Token));
            gate?.Dispose();

            if (launcher.SoberVersion is { } version)
                VersionText.Text = $"sober {version} · blox code {Updater.Current}";
            Succeed(launch.NoLaunch ? "Sober is ready" : "Roblox is running");
            if (launcher.FailedMods.Count > 0)
                DetailText.Text = $"({launcher.FailedMods.Count} mod(s) couldn't be applied — see the log)";
            await Task.Delay(1400);
            SafeClose();
        }
        catch (OperationCanceledException)
        {
            SafeClose();
        }
        catch (Exception ex)
        {
            Logger.Error("LaunchWindow::RunLaunch", ex);
            Fail(Describe(ex));
            if (ex is SoberMissingException)
                MenuButton.IsVisible = true;
            if (ex is SoberLaunchException { Output: { Length: > 0 } output })
                DetailText.Text = output.Split('\n').Last();
        }
    }

    private async Task<InstanceLock?> WaitForGateAsync()
    {
        OnProgress(new BootstrapProgress(BootstrapPhase.Connecting, "Waiting for another Blox Code launch to finish"));
        return await InstanceLock.AcquireAsync("launch", TimeSpan.FromMinutes(3), _cts.Token);
    }

    private async Task RunUpdateAsync(UpdateInfo update, string[] forwardArgs)
    {
        try
        {
            var progress = new Progress<(long Done, long Total)>(p =>
                OnProgress(new BootstrapProgress(BootstrapPhase.Downloading, $"Updating Blox Code to v{update.Version}",
                    p.Total > 0 ? (double)p.Done / p.Total : null, $"{p.Done / 1048576.0:0.0} MB / {p.Total / 1048576.0:0.0} MB")));

            OnProgress(new BootstrapProgress(BootstrapPhase.Downloading, $"Updating Blox Code to v{update.Version}"));
            string file = await Updater.DownloadAsync(App.Http, update, progress, _cts.Token);

            OnProgress(new BootstrapProgress(BootstrapPhase.Installing, "Reinstalling Blox Code", 1));
            Updater.LaunchUpgrade(file, forwardArgs);
            Succeed($"Updated to v{update.Version} — restarting");
            await Task.Delay(900);
            App.Exit();
        }
        catch (OperationCanceledException)
        {
            SafeClose();
        }
        catch (Exception ex)
        {
            Logger.Error("LaunchWindow::RunUpdate", ex);
            Fail($"Couldn't update Blox Code: {Describe(ex)}");
            DetailText.Text = "(you can keep going on this version — it'll try again next time)";
            CancelButton.Content = "Continue";
            _continueArgs = forwardArgs;
        }
    }

    private async Task RunSoberInstallAsync(Action done)
    {
        try
        {
            await Sober.InstallAsync(new Progress<BootstrapProgress>(OnProgress), _cts.Token);
            Succeed("Sober is installed");
            done();
            await Task.Delay(1400);
            SafeClose();
        }
        catch (OperationCanceledException)
        {
            SafeClose();
        }
        catch (Exception ex)
        {
            Logger.Error("LaunchWindow::RunSoberInstall", ex);
            Fail(Describe(ex));
        }
    }

    private async Task RunPreviewAsync()
    {
        try
        {
            foreach (string status in new[] { "Looking for Sober", "Checking Sober's settings" })
            {
                OnProgress(new BootstrapProgress(BootstrapPhase.Checking, status));
                await Task.Delay(1200, _cts.Token);
            }
            for (int i = 0; i <= 100; i++)
            {
                string step = i < 55 ? "Applying your Fast Flags" : "Syncing mods";
                OnProgress(new BootstrapProgress(i < 55 ? BootstrapPhase.Downloading : BootstrapPhase.Installing, step, i / 100.0, $"{i}%"));
                await Task.Delay(45, _cts.Token);
            }
            OnProgress(new BootstrapProgress(BootstrapPhase.Launching, "Starting Sober", 1));
            await Task.Delay(900, _cts.Token);
            Succeed("Preview finished — nothing was launched");
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void OnProgress(BootstrapProgress p)
    {
        if (_finished || _closed)
            return;

        StatusText.Text = p.Status + "…";
        _sizeText = p.Detail ?? "";
        _fraction = p.Fraction;

        Progress.Value = p.Fraction ?? 0;
        PercentText.Text = p.Fraction is { } f ? $"{(int)Math.Round(f * 100)}%" : "";
        Progress.Opacity = p.Fraction is null ? 0.5 : 1;

        Mascot.Mood = p.Phase switch
        {
            BootstrapPhase.Downloading => MascotMood.Walk,
            BootstrapPhase.Installing => MascotMood.Work,
            BootstrapPhase.Launching => MascotMood.Happy,
            _ => MascotMood.Walk,
        };
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
        double track = Math.Max(0, Stage.Bounds.Width - Mascot.Bounds.Width);
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

    private void Succeed(string message)
    {
        _finished = true;
        _fraction = 1;
        Progress.Value = 1;
        PercentText.Text = "100%";
        Progress.Opacity = 1;
        StatusText.Text = message;
        DetailText.Text = $"({(int)_elapsed.Elapsed.TotalSeconds}s · done)";
        ShowGlyph("✓", "Brush.Success");
        CancelButton.Content = "Close";
        Mascot.Celebrate();
    }

    private void Fail(string message)
    {
        _finished = true;
        StatusText.Text = message;
        StatusText.TextWrapping = Avalonia.Media.TextWrapping.Wrap;
        StatusText.TextTrimming = Avalonia.Media.TextTrimming.None;
        DetailText.Text = "(Bit tripped — the log has the details)";
        ShowGlyph("✗", "Brush.Danger");
        Progress.Classes.Add("failed");
        Mascot.Mood = MascotMood.Sad;
        CancelButton.Content = "Close";
        LogButton.IsVisible = true;
        // The failure message may need more room than the status line has.
        Height = 340;
    }

    private void ShowGlyph(string glyph, string brushKey)
    {
        Spinner.IsVisible = false;
        ResultGlyph.Text = glyph;
        ResultGlyph[!TextBlock.ForegroundProperty] = this.GetResourceObservable(brushKey).ToBinding();
        ResultGlyph.IsVisible = true;
    }

    private static string Describe(Exception ex) => ex switch
    {
        SoberMissingException or SoberLaunchException or SoberConfigException => ex.Message,
        HttpRequestException http => $"Network error: {http.Message}",
        TimeoutException timeout => timeout.Message,
        IOException io => $"File error: {io.Message}",
        _ => ex.Message,
    };

    private void SafeClose()
    {
        if (!_closed)
            Close();
    }

    private void OnCancel(object? sender, RoutedEventArgs e)
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

    private void OnOpenLog(object? sender, RoutedEventArgs e)
    {
        if (Logger.FilePath is not null)
            Shell.Open(Logger.FilePath, this);
    }

    private void OnOpenMenu(object? sender, RoutedEventArgs e)
    {
        App.OpenMenu();
        SafeClose();
    }
}
