using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using BloxCode.Core;

namespace BloxCode.UI;

/// <summary>A short notice in the bottom-right corner (e.g. where the server is) that fades out by itself.</summary>
public partial class ToastWindow : Window
{
    private static ToastWindow? _current;
    private readonly DispatcherTimer _hide;

    private readonly string? _copy;

    private ToastWindow(string title, string body, TimeSpan duration, string? copyLabel, string? copy)
    {
        InitializeComponent();
        TitleText.Text = title;
        BodyText.Text = body;
        _copy = copy;
        if (copy is not null)
        {
            ActionButton.Content = copyLabel ?? "Copy";
            ActionButton.Visibility = Visibility.Visible;
        }
        Mascot.IsAnimated = Store.Settings.AnimateMascot;

        _hide = new DispatcherTimer { Interval = duration };
        _hide.Tick += (_, _) => FadeOut();
        SourceInitialized += (_, _) => MakeNonActivating();
        Loaded += (_, _) =>
        {
            PlaceInCorner();
            BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));
            _hide.Start();
        };
    }

    /// <summary>
    /// Shows the notice, replacing one that is still on screen. With <paramref name="copy"/>, it
    /// carries a button that puts that text on the clipboard (and stays a little longer).
    /// </summary>
    public static void Show(string title, string body, TimeSpan? duration = null, string? copyLabel = null, string? copy = null)
    {
        _current?.Close();
        _current = new ToastWindow(title, body, duration ?? TimeSpan.FromSeconds(copy is null ? 7 : 12), copyLabel, copy);
        _current.Closed += (sender, _) => { if (ReferenceEquals(_current, sender)) _current = null; };
        _current.Show();
    }

    private void PlaceInCorner()
    {
        var area = SystemParameters.WorkArea;
        Left = area.Right - ActualWidth - 8;
        Top = area.Bottom - ActualHeight - 8;
    }

    private void FadeOut()
    {
        _hide.Stop();
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(260));
        fade.Completed += (_, _) => Close();
        BeginAnimation(OpacityProperty, fade);
    }

    private void OnClick(object sender, MouseButtonEventArgs e) => FadeOut();

    private async void OnAction(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        ActionButton.IsEnabled = false;
        bool copied = await CopyAsync(_copy!);
        ActionButton.IsEnabled = true;
        if (!copied)
        {
            // Another program holds the clipboard; say so rather than pretend.
            ActionButton.Content = "Clipboard busy, try again";
            return;
        }
        ActionButton.Content = "Copied";
        // A moment to read "Copied", then out of the way.
        _hide.Stop();
        _hide.Interval = TimeSpan.FromSeconds(1.5);
        _hide.Start();
    }

    /// <summary>
    /// Puts text on the clipboard from a thread of its own, giving up after two seconds: a
    /// clipboard manager that doesn't answer must not freeze the notice (or the launch window it
    /// shares a thread with).
    /// </summary>
    public static Task<bool> CopyAsync(string text)
    {
        var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                Clipboard.SetDataObject(text, copy: true);
                done.TrySetResult(true);
            }
            catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or System.Runtime.InteropServices.ExternalException)
            {
                Logger.Warn("ToastWindow::Copy", ex.Message);
                done.TrySetResult(false);
            }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return done.Task.WaitAsync(TimeSpan.FromSeconds(2)).ContinueWith(t => t.IsCompletedSuccessfully && t.Result, TaskScheduler.Default);
    }

    // WS_EX_NOACTIVATE keeps keyboard focus in Roblox; WS_EX_TOOLWINDOW keeps it out of Alt+Tab.
    private void MakeNonActivating()
    {
        const int GwlExStyle = -20, WsExNoActivate = 0x08000000, WsExToolWindow = 0x00000080;
        var hwnd = new WindowInteropHelper(this).Handle;
        SetWindowLong(hwnd, GwlExStyle, GetWindowLong(hwnd, GwlExStyle) | WsExNoActivate | WsExToolWindow);
    }

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hwnd, int index);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hwnd, int index, int value);
}
