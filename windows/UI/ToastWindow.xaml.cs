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

    private ToastWindow(string title, string body, TimeSpan duration)
    {
        InitializeComponent();
        TitleText.Text = title;
        BodyText.Text = body;
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

    /// <summary>Shows the notice, replacing one that is still on screen.</summary>
    public static void Show(string title, string body, TimeSpan? duration = null)
    {
        _current?.Close();
        _current = new ToastWindow(title, body, duration ?? TimeSpan.FromSeconds(7));
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
