using System.Net.Http;
using System.Windows;
using System.Windows.Threading;
using BloxCode.Core;
using BloxCode.UI;

namespace BloxCode;

public partial class App : Application
{
    public static readonly HttpClient Http = CreateHttpClient();

    public static string Version => typeof(App).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Start-up dialogs must not count as "the last window" and end the app early.
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        // Hooked first, so even a failure creating the data folder is caught and logged.
        DispatcherUnhandledException += OnUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Logger.Error("App::UnhandledException", args.ExceptionObject as Exception ?? new Exception(args.ExceptionObject?.ToString()));
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Logger.Error("App::UnobservedTaskException", args.Exception);
            args.SetObserved();
        };

        try
        {
            Paths.EnsureCreated();
            Logger.Initialize();
            Logger.Info("App::OnStartup", $"Blox Code {Version} - args: {string.Join(' ', e.Args)}");
            await StartAsync(e.Args);
        }
        catch (Exception ex)
        {
            // Nothing is on screen yet and ShutdownMode is explicit: without this the process would
            // linger invisibly forever.
            Logger.Error("App::OnStartup", ex);
            MessageDialog.Show(null, "Blox Code couldn't start", $"{ex.Message}\n\nLog: {Logger.FilePath}");
            Shutdown(1);
        }
    }

    private async Task StartAsync(string[] args)
    {
        Store.Load();
        ThemeManager.Apply(Store.Settings.Theme);
        ThemeManager.FollowSystemTheme();
        // After a self-update the Apps & Features entry must show the new version.
        Updater.AfterUpgrade = Installer.RefreshAfterUpgrade;

        var launch = LaunchSettings.Parse(args);

        if (launch.Upgrade)
        {
            // We are the new version, started from Temp\Updates: swap ourselves in and hand over.
            if (!Updater.ApplyUpgrade(launch.ForwardArgs))
            {
                MessageDialog.Show(null, "Update didn't finish", $"Blox Code couldn't replace its installed copy, so it'll keep using the current version and try again next time.\n\nLog: {Logger.FilePath}");
                // Still do what the user originally asked for (e.g. join the game) with the old copy.
                Updater.RunInstalled(launch.ForwardArgs);
            }
            Shutdown();
            return;
        }

        Updater.CleanupUpdatesFolder();
        // Roblox's own updater re-registers roblox:// for itself; an installed Blox Code takes it back, as Bloxstrap does.
        if (launch.Mode != LaunchMode.Uninstall)
            Installer.ReclaimLinksFromRoblox();

        if (await FindUpdateAsync(launch) is { } update)
        {
            BootstrapperWindow.ForUpdate(update, launch.ForwardArgs).Show();
            ShutdownMode = ShutdownMode.OnLastWindowClose;
            return;
        }

        switch (launch.Mode)
        {
            case LaunchMode.Player:
            case LaunchMode.Studio:
                if (Store.Settings.ConfirmLaunches && !launch.Quiet &&
                    !MessageDialog.Ask(null, "Launch Roblox?", "Something asked Blox Code to start Roblox.", "Launch", "Not now"))
                {
                    Shutdown();
                    return;
                }
                new BootstrapperWindow(launch).Show();
                break;

            case LaunchMode.Uninstall:
                if (launch.Quiet || MessageDialog.Ask(null, "Uninstall Blox Code?",
                        "This removes Blox Code, its shortcuts and the Roblox files it downloaded.", "Uninstall", "Cancel", danger: true))
                {
                    Installer.Uninstall();
                }
                Shutdown();
                break;

            default:
                // A plain start asks first, like Bloxstrap: play now, or open the settings. -menu skips it.
                if (!launch.ExplicitMenu)
                {
                    var menu = new LaunchMenuWindow();
                    menu.ShowDialog();
                    switch (menu.Choice)
                    {
                        case LaunchMenuChoice.LaunchRoblox:
                            new BootstrapperWindow(LaunchSettings.ForPlayer()).Show();
                            ShutdownMode = ShutdownMode.OnLastWindowClose;
                            return;
                        case LaunchMenuChoice.LaunchStudio:
                            new BootstrapperWindow(LaunchSettings.ForStudio()).Show();
                            ShutdownMode = ShutdownMode.OnLastWindowClose;
                            return;
                        case LaunchMenuChoice.None:
                            Shutdown();
                            return;
                    }
                }

                // One menu at a time: two would overwrite each other's settings. Bring the open one forward.
                // Per settings folder: two menus on the same settings would overwrite each other, a
                // separate (test or portable) settings folder can't clash.
                string scope = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                    System.Text.Encoding.UTF8.GetBytes(Paths.Config.ToLowerInvariant())))[..12];
                _menuLock = new Mutex(true, Paths.Config == Path.Combine(Paths.LocalAppData, "BloxCode") ? "BloxCode-Menu" : "BloxCode-Menu-" + scope, out bool first);
                if (!first)
                {
                    Logger.Info("App::Start", "Found an already existing menu window");
                    FocusOtherInstance();
                    Shutdown();
                    return;
                }
                new MainWindow().Show();
                break;
        }

        ShutdownMode = ShutdownMode.OnLastWindowClose;
    }

    private static Mutex? _menuLock;
    private bool _reportingError;

    /// <summary>
    /// The settings window closed. After Play this process carries on in the background (Discord,
    /// history, multi-instance) for as long as Roblox runs; it must not keep the menu "open" meanwhile,
    /// or every later start would think a menu is showing and quit. Called on the UI thread, which owns the lock.
    /// </summary>
    public static void MenuClosed()
    {
        if (_menuLock is not { } menuLock)
            return;
        _menuLock = null;
        try { menuLock.ReleaseMutex(); }
        catch (ApplicationException) { }   // Not owned by this thread: closing the handle frees it all the same.
        menuLock.Dispose();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        MenuClosed();
        base.OnExit(e);
    }

    private static void FocusOtherInstance()
    {
        using var current = System.Diagnostics.Process.GetCurrentProcess();
        foreach (var process in System.Diagnostics.Process.GetProcessesByName(current.ProcessName))
        {
            using (process)
            {
                if (process.Id == current.Id || process.MainWindowHandle == IntPtr.Zero)
                    continue;
                ShowWindow(process.MainWindowHandle, SW_RESTORE);
                SetForegroundWindow(process.MainWindowHandle);
                return;
            }
        }
    }

    private const int SW_RESTORE = 9;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    /// <summary>
    /// Every start of the installed copy asks the release feed for something newer. The check is
    /// capped at a few seconds so a slow network never holds up a game launch.
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
            // Fail fast on a host that never accepts the connection instead of waiting on the OS (~21 s).
            ConnectTimeout = TimeSpan.FromSeconds(10),
        })
        {
            Timeout = TimeSpan.FromMinutes(10),
        };
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"BloxCode/{Version}");
        return http;
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Logger.Error("App::OnUnhandledException", e.Exception);
        e.Handled = true;

        // If showing the dialog itself fails, don't recurse into this handler forever.
        if (_reportingError)
            return;
        _reportingError = true;
        try
        {
            MessageDialog.Show(null, "Something went wrong", e.Exception.Message + $"\n\nLog: {Logger.FilePath}");
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
        if (!Windows.OfType<Window>().Any(w => w.IsVisible))
            Shutdown(1);
    }
}
