using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using BloxCode.Core;
using BloxCode.Linux.Platform;
using BloxCode.Linux.Runtime;
using BloxCode.Linux.UI;

namespace BloxCode.Linux;

public partial class App : Application
{
    public static readonly HttpClient Http = CreateHttpClient();

    private static MenuInstance? _menu;
    private static bool _reportingError;
    private static DispatcherTimer? _flagSync;

    private static IClassicDesktopStyleApplicationLifetime? Desktop =>
        Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        base.OnFrameworkInitializationCompleted();
        if (Desktop is { } desktop)
            Dispatcher.UIThread.Post(() => _ = StartupAsync(desktop.Args ?? Array.Empty<string>()));
    }

    private static async Task StartupAsync(string[] args)
    {
        // Hooked first, so even a failure creating the data folders is caught and logged.
        Dispatcher.UIThread.UnhandledException += OnUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Logger.Error("App::UnhandledException", e.ExceptionObject as Exception ?? new Exception(e.ExceptionObject?.ToString()));
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Logger.Error("App::UnobservedTaskException", e.Exception);
            e.SetObserved();
        };

        try
        {
            Paths.EnsureCreated();
            Logger.Initialize();
            Logger.Info("App::Startup", $"Blox Code {Updater.Current} (Linux) - args: {string.Join(' ', args)} - {Distro.Current.Name}, {Distro.Session}");
            await StartAsync(args);
        }
        catch (Exception ex)
        {
            // Nothing is on screen yet and shutdown is explicit: without this the process would linger.
            Logger.Error("App::Startup", ex);
            if (args.Contains("--self-test"))
                Console.Error.WriteLine($"ui FAILED: {ex}");
            else
                await MessageDialog.ShowAsync(null, "Blox Code couldn't start", $"{ex.Message}\n\nLog: {Logger.FilePath}");
            Exit(1);
        }
    }

    private static async Task StartAsync(string[] args)
    {
        Store.Load();
        ApplyTheme(Store.Settings.Theme);
        Updater.AfterUpgrade = () => DesktopIntegration.RefreshAfterUpgrade(Icon);
        Store.SettingsSaved += ScheduleFlagSync;

        var launch = LaunchSettings.Parse(args.Where(a => a != "--ui" && a != "--self-test").ToArray());

        if (args.Contains("--self-test"))
        {
            // CI's UI smoke test: open the real menu, let it render, quit cleanly.
            var window = new MainWindow();
            window.Show();
            await Task.Delay(TimeSpan.FromSeconds(3));
            Logger.Info("App::SelfTest", $"Menu rendered at {window.Bounds.Width}x{window.Bounds.Height}");
            Console.WriteLine($"ui ok: menu rendered at {window.Bounds.Width}x{window.Bounds.Height}");
            Exit(0);
            return;
        }

        if (launch.Upgrade)
        {
            // We are the new version, started from the updates folder: swap ourselves in and hand over.
            if (!Updater.ApplyUpgrade(launch.ForwardArgs))
            {
                await MessageDialog.ShowAsync(null, "Update didn't finish", $"Blox Code couldn't replace its installed copy, so it'll keep using the current version and try again next time.\n\nLog: {Logger.FilePath}");
                Updater.RunInstalled(launch.ForwardArgs);
            }
            Exit();
            return;
        }

        Updater.CleanupUpdatesFolder();
        // Sober's updates re-register roblox:// for Sober; an installed Blox Code takes the links back.
        if (launch.Mode != LaunchMode.Uninstall)
            await DesktopIntegration.ReclaimLinksFromSoberAsync();

        if (await FindUpdateAsync(launch) is { } update)
        {
            Show(LaunchWindow.ForUpdate(update, launch.ForwardArgs));
            return;
        }

        switch (launch.Mode)
        {
            case LaunchMode.Player:
                if (Store.Settings.ConfirmLaunches && !launch.Quiet &&
                    !await MessageDialog.AskAsync(null, "Launch Roblox?", "Something asked Blox Code to start Roblox.", "Launch", "Not now"))
                {
                    Exit();
                    return;
                }
                Show(LaunchWindow.ForLaunch(launch));
                break;

            case LaunchMode.Uninstall:
                if (launch.Quiet || await MessageDialog.AskAsync(null, "Uninstall Blox Code?",
                        "This removes Blox Code, its menu entry and its mods from Sober. Sober itself, its settings and your Fast Flags in it stay.",
                        "Uninstall", "Cancel", danger: true))
                {
                    await DesktopIntegration.UninstallAsync();
                }
                Exit();
                break;

            default:
                // A plain start asks first, like Bloxstrap: play now, or open the settings. -menu skips it.
                if (!launch.ExplicitMenu)
                {
                    switch (await LaunchMenuWindow.AskAsync())
                    {
                        case LaunchMenuChoice.LaunchRoblox:
                            Show(LaunchWindow.ForLaunch(LaunchSettings.ForPlayer()));
                            return;
                        case LaunchMenuChoice.None:
                            Exit();
                            return;
                    }
                }
                if (!OpenMenu())
                    Exit();
                break;
        }
    }

    /// <summary>Opens the menu unless one is already open (that one is brought forward instead).</summary>
    public static bool OpenMenu()
    {
        _menu ??= MenuInstance.Claim(() => Dispatcher.UIThread.Post(FocusMenu));
        if (_menu is null)
        {
            Logger.Info("App::OpenMenu", "Found an already existing menu window");
            return false;
        }
        Show(new MainWindow());
        return true;
    }

    /// <summary>The menu closed (maybe into a launch): another start may open a menu again.</summary>
    public static void MenuClosed()
    {
        _menu?.Dispose();
        _menu = null;
    }

    private static void FocusMenu()
    {
        if (Desktop?.Windows.OfType<MainWindow>().FirstOrDefault() is { } menu)
        {
            if (menu.WindowState == WindowState.Minimized)
                menu.WindowState = WindowState.Normal;
            menu.Activate();
        }
    }

    /// <summary>Shows a window and from then on quits when the last one closes.</summary>
    public static void Show(Window window)
    {
        window.Show();
        if (Desktop is { } desktop)
            desktop.ShutdownMode = ShutdownMode.OnLastWindowClose;
    }

    public static void Exit(int code = 0)
    {
        _menu?.Dispose();
        _menu = null;
        if (Desktop is { } desktop)
            desktop.Shutdown(code);
        else
            Environment.Exit(code);
    }

    public static void ApplyTheme(AppTheme theme)
    {
        if (Current is not null)
            Current.RequestedThemeVariant = theme switch
            {
                AppTheme.Light => ThemeVariant.Light,
                AppTheme.Dark => ThemeVariant.Dark,
                _ => ThemeVariant.Default,
            };
    }

    /// <summary>The Bit icon as PNG bytes, for the hicolor theme.</summary>
    public static byte[] Icon(int size)
    {
        using var stream = AssetLoader.Open(new Uri($"avares://BloxCode/Assets/bloxcode-{size}.png"));
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }

    /// <summary>
    /// Fast Flags go into Sober's config as soon as they're saved (a little debounced), so they also
    /// apply when Sober is opened directly rather than through Blox Code. Runs on the UI thread, the
    /// only one that edits the flags while a menu is open.
    /// </summary>
    private static void ScheduleFlagSync()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(ScheduleFlagSync);
            return;
        }

        if (_flagSync is null)
        {
            _flagSync = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
            _flagSync.Tick += (_, _) =>
            {
                _flagSync.Stop();
                try
                {
                    if (Directory.Exists(Sober.AppDir) || File.Exists(Sober.ConfigFile))
                        SoberConfig.ApplyFastFlags();
                }
                catch (Exception ex)
                {
                    Logger.Warn("App::FlagSync", ex.Message);
                }
            };
        }
        _flagSync.Stop();
        _flagSync.Start();
    }

    /// <summary>
    /// Every start of the installed copy asks the release feed for something newer, capped at a few
    /// seconds so a slow network never holds up a game launch.
    /// </summary>
    private static async Task<UpdateInfo?> FindUpdateAsync(LaunchSettings launch)
    {
        if (launch.NoUpdate || launch.Mode == LaunchMode.Uninstall || !Store.Settings.AutoUpdate
            || !Paths.IsRunningInstalled || !Updater.IsConfigured)
            return null;

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(4));
            return await Updater.CheckAsync(Http, cts.Token);
        }
        catch (Exception ex)
        {
            Logger.Warn("App::FindUpdate", $"Update check failed: {ex.Message}");
            return null;
        }
    }

    private static HttpClient CreateHttpClient()
    {
        var http = new HttpClient(new SocketsHttpHandler
        {
            AutomaticDecompression = System.Net.DecompressionMethods.All,
            ConnectTimeout = TimeSpan.FromSeconds(10),
        })
        {
            Timeout = TimeSpan.FromMinutes(10),
        };
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"BloxCode/{Updater.Current}");
        return http;
    }

    private static async void OnUnhandledException(object? sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Logger.Error("App::OnUnhandledException", e.Exception);
        e.Handled = true;

        // If showing the dialog itself fails, don't recurse into this handler forever.
        if (_reportingError)
            return;
        _reportingError = true;
        try
        {
            await MessageDialog.ShowAsync(Desktop?.Windows.FirstOrDefault(w => w.IsVisible), "Something went wrong", e.Exception.Message + $"\n\nLog: {Logger.FilePath}");
        }
        catch (Exception dialogError)
        {
            Logger.Error("App::OnUnhandledException", dialogError);
        }
        finally
        {
            _reportingError = false;
        }

        // If the failure took the only window with it, don't leave a headless process behind.
        if (Desktop is { } desktop && !desktop.Windows.Any(w => w.IsVisible))
            Exit(1);
    }
}
