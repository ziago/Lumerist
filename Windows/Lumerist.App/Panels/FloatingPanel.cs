using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Lumerist.App.Services;

namespace Lumerist.App.Panels;

/// <summary>
/// A window that floats over Lightroom without taking the focus (the Mac's
/// non-activating NSPanel): topmost, no taskbar button, not in Alt+Tab, and
/// never activated by a click, so Lightroom keeps the keyboard and the Dial
/// Keys keep working.
/// </summary>
internal class NonActivatingWindow : Window
{
    private const int WM_MOUSEACTIVATE = 0x0021, MA_NOACTIVATE = 3;
    private bool takesFocus;

    public NonActivatingWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        ShowActivated = false;
        Focusable = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Ideal);
        SourceInitialized += (_, _) =>
        {
            PanelNative.SetExtendedStyle(this, PanelNative.WS_EX_NOACTIVATE | PanelNative.WS_EX_TOOLWINDOW, 0);
            HwndSource.FromHwnd(PanelNative.Handle(this))?.AddHook(WindowProcedure);
        };
        Deactivated += (_, _) => StopTakingFocus();
        IsVisibleChanged += (_, _) =>
        {
            if (!IsVisible) StopTakingFocus();
        };
    }

    /// <summary>True while the window may hold the keyboard (the Key Reference's search field).</summary>
    protected bool TakesFocus => takesFocus;

    /// <summary>
    /// Lets this window take the keyboard, for typing in a text field the
    /// user clicked; the next time it loses the focus it goes back to never
    /// taking it.
    /// </summary>
    public void StartTakingFocus()
    {
        if (takesFocus) return;
        takesFocus = true;
        PanelNative.SetExtendedStyle(this, 0, PanelNative.WS_EX_NOACTIVATE);
        // The click just went to this window, so Windows normally lets it come
        // to the front; if it refuses, Deactivated never comes, so undo now.
        if (!Activate()) StopTakingFocus();
    }

    private void StopTakingFocus()
    {
        if (!takesFocus) return;
        takesFocus = false;
        PanelNative.SetExtendedStyle(this, PanelNative.WS_EX_NOACTIVATE, 0);
    }

    /// <summary>A click never activates the window (WS_EX_NOACTIVATE alone leaves child windows a way in).</summary>
    private nint WindowProcedure(nint window, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == WM_MOUSEACTIVATE && !takesFocus)
        {
            handled = true;
            return MA_NOACTIVATE;
        }
        return 0;
    }

    /// <summary>The size of the window's content in device-independent pixels, measured now.</summary>
    public Size MeasureContent()
    {
        if (Content is not UIElement content) return new Size(0, 0);
        content.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return content.DesiredSize;
    }

    /// <summary>Shows the window without activating it; true when it was hidden.</summary>
    public bool ShowWithoutActivating()
    {
        if (IsVisible) return false;
        Show();
        return true;
    }
}

/// <summary>
/// Moves a window with the pointer after a press on one of its parts (a
/// title bar, the HUD sample). Screen pixels throughout, so it keeps up
/// across monitors with different scales, and the window is never activated.
/// </summary>
internal static class WindowDrag
{
    /// <param name="dropped">Runs once, after the button is released.</param>
    public static void Begin(Window window, UIElement handle, Action? dropped = null)
    {
        var start = PanelNative.Cursor();
        var frame = PanelNative.Frame(window);
        if (!handle.CaptureMouse()) return;

        void Move(object sender, MouseEventArgs e)
        {
            var (x, y) = PanelNative.Cursor();
            PanelNative.Move(window, (int)frame.X + x - start.X, (int)frame.Y + y - start.Y);
        }

        void Up(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            handle.ReleaseMouseCapture();
        }

        void Lost(object sender, MouseEventArgs e)
        {
            handle.MouseMove -= Move;
            handle.MouseLeftButtonUp -= Up;
            handle.LostMouseCapture -= Lost;
            dropped?.Invoke();
        }

        handle.MouseMove += Move;
        handle.MouseLeftButtonUp += Up;
        handle.LostMouseCapture += Lost;
    }
}

/// <summary>
/// The Palette's and the Key Reference's window (the Mac's PanelHost in
/// FloatingPanels.swift): dragged by its title bar, it remembers where it
/// was left and opens there; it follows its content's size and keeps its top
/// edge in place.
/// </summary>
internal sealed class FloatingPanel : NonActivatingWindow
{
    /// <summary>Room around the panel for its shadow (the Mac panel's window shadow).</summary>
    public const double ShadowMargin = 14;

    private readonly Func<PanelOrigin?> savedOrigin;
    private readonly Action<PanelOrigin> savePosition;

    /// <param name="savedOrigin">Where the panel was left (top-left in screen pixels), or null.</param>
    /// <param name="savePosition">Stores where the user dragged it.</param>
    public FloatingPanel(string title, Func<PanelOrigin?> savedOrigin, Action<PanelOrigin> savePosition)
    {
        Title = title;
        this.savedOrigin = savedOrigin;
        this.savePosition = savePosition;
    }

    /// <summary>
    /// Puts new content in: the panel's frame and, behind it, a shadow of the
    /// same shape. Presses on <paramref name="titleBar"/> move the panel.
    /// </summary>
    public void SetContent(FrameworkElement chrome, FrameworkElement titleBar, double cornerRadius)
    {
        var shadow = new Border
        {
            Background = Brushes.Black,
            CornerRadius = new CornerRadius(cornerRadius),
            Effect = new DropShadowEffect { Color = Colors.Black, Opacity = 0.5, BlurRadius = 18, ShadowDepth = 4, Direction = 270 },
        };
        var grid = new Grid { Margin = new Thickness(ShadowMargin) };
        grid.Children.Add(shadow);
        grid.Children.Add(chrome);
        titleBar.MouseLeftButtonDown += (_, e) =>
        {
            e.Handled = true;
            WindowDrag.Begin(this, titleBar, () =>
            {
                if (!IsVisible) return;
                var frame = PanelNative.Frame(this);
                savePosition(new PanelOrigin((int)frame.X, (int)frame.Y));
            });
        };
        Content = grid;
    }

    /// <summary>
    /// Shows the panel at its saved place, or with its top-left corner at
    /// <paramref name="fallback"/> (screen pixels, the panel's own corner
    /// without the shadow), kept inside the monitor's work area.
    /// </summary>
    public void Present((int X, int Y) fallback)
    {
        var size = MeasureContent();
        var probe = PanelNative.MonitorAt(fallback.X, fallback.Y) ?? PanelNative.PointerMonitor();
        var margin = (int)Math.Round(ShadowMargin * probe.Scale);
        var origin = (X: fallback.X - margin, Y: fallback.Y - margin);
        // Ignore a position on a monitor that is no longer connected.
        if (savedOrigin() is { } saved && PanelNative.IsNearAMonitor(saved.X, saved.Y, 20)) origin = (saved.X, saved.Y);
        var monitor = PanelNative.MonitorAt(origin.X + margin + 10, origin.Y + margin + 10) ?? PanelNative.PrimaryMonitor();
        var scale = monitor.Scale;
        var shadow = ShadowMargin * scale;
        var width = size.Width * scale;
        var height = size.Height * scale;
        var work = monitor.WorkArea;
        // The panel itself stays inside the work area; its shadow may not.
        var x = Math.Min(Math.Max(origin.X, work.Left - shadow), work.Right - width + shadow);
        var y = Math.Min(Math.Max(origin.Y, work.Top - shadow), work.Bottom - height + shadow);
        PanelNative.Place(this, (int)Math.Round(x), (int)Math.Round(y));
        ShowWithoutActivating();
        PanelNative.Place(this, (int)Math.Round(x), (int)Math.Round(y));
    }
}
