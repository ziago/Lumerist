using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using static Lumerist.App.Interop.Native;

namespace Lumerist.App.Panels;

/// <summary>
/// The Win32 calls the floating panels need: window styles that never take
/// the focus from Lightroom, placing in screen pixels, monitors and their DPI.
/// </summary>
internal static partial class PanelNative
{
    private const int GWL_EXSTYLE = -20;
    public const long WS_EX_TOPMOST = 0x0000_0008, WS_EX_TRANSPARENT = 0x0000_0020, WS_EX_TOOLWINDOW = 0x0000_0080;
    public const long WS_EX_LAYERED = 0x0008_0000, WS_EX_NOACTIVATE = 0x0800_0000;

    private const uint SWP_NOSIZE = 0x0001, SWP_NOMOVE = 0x0002, SWP_NOZORDER = 0x0004, SWP_NOACTIVATE = 0x0010;
    private static readonly nint HWND_TOPMOST = -1;

    private const uint MONITOR_DEFAULTTONULL = 0, MONITOR_DEFAULTTOPRIMARY = 1, MONITOR_DEFAULTTONEAREST = 2;
    private const int MDT_EFFECTIVE_DPI = 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public uint cbSize;
        public RECT rcMonitor, rcWork;
        public uint dwFlags;
    }

    /// <summary>A monitor's work area (without the taskbar) in screen pixels, and its scale (1 = 96 dpi).</summary>
    public readonly record struct Monitor(nint Handle, Rect WorkArea, Rect Bounds, double Scale)
    {
        /// <summary>The work area in this monitor's device-independent pixels (screen pixels ÷ scale).</summary>
        public Rect WorkAreaDips => new(WorkArea.X / Scale, WorkArea.Y / Scale, WorkArea.Width / Scale, WorkArea.Height / Scale);
    }

    public static nint Handle(Window window) => new WindowInteropHelper(window).EnsureHandle();

    /// <summary>Adds and removes extended styles.</summary>
    public static void SetExtendedStyle(Window window, long add, long remove)
    {
        var handle = Handle(window);
        var style = GetWindowLong(handle);
        var updated = (style | add) & ~remove;
        if (updated != style) SetWindowLong(handle, updated);
    }

    public static bool HasExtendedStyle(Window window, long style) => (GetWindowLong(Handle(window)) & style) == style;

    /// <summary>The window's frame in screen pixels.</summary>
    public static Rect Frame(Window window)
    {
        GetWindowRect(Handle(window), out var rect);
        return new Rect(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
    }

    /// <summary>
    /// Moves (and sizes, when <paramref name="width"/> is given) a window in
    /// screen pixels, above the other topmost windows, without activating it.
    /// A move onto a monitor with another DPI is done twice: the first move
    /// lets WPF rescale the window, the second puts it exactly where asked.
    /// </summary>
    public static void Place(Window window, int x, int y, int? width = null, int? height = null)
    {
        var handle = Handle(window);
        var flags = SWP_NOACTIVATE | (width is null ? SWP_NOSIZE : 0);
        var before = MonitorFromWindow(handle, MONITOR_DEFAULTTONEAREST);
        SetWindowPos(handle, HWND_TOPMOST, x, y, width ?? 0, height ?? 0, flags);
        if (MonitorFromWindow(handle, MONITOR_DEFAULTTONEAREST) != before)
            SetWindowPos(handle, HWND_TOPMOST, x, y, width ?? 0, height ?? 0, flags);
    }

    /// <summary>Moves a window during a drag (no z-order change, no activation).</summary>
    public static void Move(Window window, int x, int y) =>
        SetWindowPos(Handle(window), 0, x, y, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);

    /// <summary>Puts a window back above the other topmost windows (the HUD above the panels).</summary>
    public static void BringToTop(Window window) =>
        SetWindowPos(Handle(window), HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);

    public static (int X, int Y) Cursor()
    {
        GetCursorPos(out var point);
        return (point.X, point.Y);
    }

    /// <summary>The monitor under the pointer (else the primary one).</summary>
    public static Monitor PointerMonitor()
    {
        GetCursorPos(out var point);
        return Describe(MonitorFromPoint(point, MONITOR_DEFAULTTOPRIMARY));
    }

    /// <summary>The monitor that holds most of a window.</summary>
    public static Monitor WindowMonitor(nint window) => Describe(MonitorFromWindow(window, MONITOR_DEFAULTTONEAREST));

    public static Monitor PrimaryMonitor() => Describe(MonitorFromPoint(default, MONITOR_DEFAULTTOPRIMARY));

    /// <summary>The monitor that holds the point, or null.</summary>
    public static Monitor? MonitorAt(int x, int y)
    {
        var handle = MonitorFromPoint(new POINT { X = x, Y = y }, MONITOR_DEFAULTTONULL);
        return handle == 0 ? null : Describe(handle);
    }

    /// <summary>The monitor that holds the centre of a frame, else the one with most of it.</summary>
    public static Monitor MonitorFor(Rect frame)
    {
        if (MonitorAt((int)Math.Round(frame.X + frame.Width / 2), (int)Math.Round(frame.Y + frame.Height / 2)) is { } centre)
            return centre;
        var rect = ToRect(frame);
        return Describe(MonitorFromRect(ref rect, MONITOR_DEFAULTTONEAREST));
    }

    /// <summary>True when the square of ±<paramref name="margin"/> pixels around the point touches a monitor.</summary>
    public static bool IsNearAMonitor(int x, int y, int margin)
    {
        var rect = new RECT { Left = x - margin, Top = y - margin, Right = x + margin + 1, Bottom = y + margin + 1 };
        return MonitorFromRect(ref rect, MONITOR_DEFAULTTONULL) != 0;
    }

    public static bool IsUsableWindow(nint window) => window != 0 && IsWindow(window) && IsWindowVisible(window) && !IsIconic(window);

    private static Monitor Describe(nint handle)
    {
        var info = new MONITORINFO { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() };
        if (handle == 0 || !GetMonitorInfoW(handle, ref info))
        {
            var area = SystemParameters.WorkArea;
            return new Monitor(handle, area, area, 1);
        }
        var scale = 1.0;
        try
        {
            if (GetDpiForMonitor(handle, MDT_EFFECTIVE_DPI, out var dpiX, out _) == 0 && dpiX > 0) scale = dpiX / 96.0;
        }
        catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException)
        {
            // Before Windows 8.1: one DPI for every monitor.
        }
        return new Monitor(handle, FromRect(info.rcWork), FromRect(info.rcMonitor), scale);
    }

    private static Rect FromRect(RECT rect) => new(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);

    private static RECT ToRect(Rect rect) => new()
    {
        Left = (int)Math.Round(rect.Left),
        Top = (int)Math.Round(rect.Top),
        Right = (int)Math.Round(rect.Right),
        Bottom = (int)Math.Round(rect.Bottom),
    };

    private static long GetWindowLong(nint window) =>
        IntPtr.Size == 8 ? GetWindowLongPtrW(window, GWL_EXSTYLE) : GetWindowLongW(window, GWL_EXSTYLE);

    private static void SetWindowLong(nint window, long value)
    {
        if (IntPtr.Size == 8) SetWindowLongPtrW(window, GWL_EXSTYLE, (nint)value);
        else SetWindowLongW(window, GWL_EXSTYLE, (int)value);
    }

    [LibraryImport("user32.dll")]
    private static partial nint GetWindowLongPtrW(nint hWnd, int nIndex);

    [LibraryImport("user32.dll")]
    private static partial int GetWindowLongW(nint hWnd, int nIndex);

    [LibraryImport("user32.dll")]
    private static partial nint SetWindowLongPtrW(nint hWnd, int nIndex, nint dwNewLong);

    [LibraryImport("user32.dll")]
    private static partial int SetWindowLongW(nint hWnd, int nIndex, int dwNewLong);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowPos(nint hWnd, nint hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetWindowRect(nint hWnd, out RECT lpRect);

    [LibraryImport("user32.dll")]
    private static partial nint MonitorFromPoint(POINT pt, uint dwFlags);

    [LibraryImport("user32.dll")]
    private static partial nint MonitorFromWindow(nint hwnd, uint dwFlags);

    [LibraryImport("user32.dll")]
    private static partial nint MonitorFromRect(ref RECT lprc, uint dwFlags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetMonitorInfoW(nint hMonitor, ref MONITORINFO lpmi);

    [LibraryImport("shcore.dll")]
    private static partial int GetDpiForMonitor(nint hmonitor, int dpiType, out uint dpiX, out uint dpiY);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsWindow(nint hWnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsWindowVisible(nint hWnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsIconic(nint hWnd);
}
