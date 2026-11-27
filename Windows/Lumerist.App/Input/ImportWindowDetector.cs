using System.Runtime.InteropServices;
using Lumerist.Core;
using static Lumerist.App.Interop.Native;

namespace Lumerist.App.Input;

/// <summary>
/// Whether Lightroom Classic's Import window is the foreground window, so the
/// Import keys work there and the Develop and Library keys pause (the Mac's
/// ImportWindowDetector). Lightroom's SDK can't tell: with the Import window
/// open its module stays "library".
/// </summary>
/// <remarks>
/// The input hook asks for each event while Lightroom is in front, so the
/// answer is never late, even for the first key after the window opens or
/// closes. It reads only what Windows itself keeps about the foreground
/// window: its style bits and size, and its owner's window class and size.
/// No title, text or file name is read, nothing is stored or sent, and no
/// message goes to Lightroom (these calls never wait for it, unlike UI
/// Automation). <see cref="ImportWindowRule"/> decides from the shape alone.
/// </remarks>
internal static partial class ImportWindowDetector
{
    private const int GWL_STYLE = -16;
    private const uint GW_OWNER = 4;

    /// <param name="window">Lightroom's foreground window.</param>
    public static bool IsImportWindow(nint window) => ImportWindowRule.IsImportWindow(Shape(window));

    /// <summary>The shape of an owned window, or null (Lightroom's main window has no owner).</summary>
    private static WindowShape? Shape(nint window)
    {
        if (window == 0) return null;
        var owner = GetWindow(window, GW_OWNER);
        if (owner == 0) return null;
        if (!GetWindowRect(window, out var bounds) || !GetWindowRect(owner, out var ownerBounds)) return null;
        return new WindowShape(
            Style(window),
            bounds.Right - bounds.Left,
            bounds.Bottom - bounds.Top,
            ClassName(owner),
            ownerBounds.Right - ownerBounds.Left,
            ownerBounds.Bottom - ownerBounds.Top);
    }

    /// <summary>The low 32 bits are the style, whether Windows sign-extends them or not.</summary>
    private static uint Style(nint window) =>
        unchecked((uint)(IntPtr.Size == 8 ? (long)GetWindowLongPtrW(window, GWL_STYLE) : GetWindowLongW(window, GWL_STYLE)));

    [LibraryImport("user32.dll")]
    private static partial nint GetWindow(nint hWnd, uint uCmd);

    [LibraryImport("user32.dll")]
    private static partial nint GetWindowLongPtrW(nint hWnd, int nIndex);

    [LibraryImport("user32.dll")]
    private static partial int GetWindowLongW(nint hWnd, int nIndex);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetWindowRect(nint hWnd, out RECT lpRect);
}
