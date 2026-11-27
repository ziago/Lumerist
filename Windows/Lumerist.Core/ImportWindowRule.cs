namespace Lumerist.Core;

/// <summary>
/// What the Windows app learns about Lightroom's foreground window: its
/// style bits and size, and its owner's window class and size. Never a
/// title or any other text (the Mac's WindowShape, Sources/LumeristCore/ImportWindowRule.swift).
/// </summary>
/// <param name="Style"><c>GWL_STYLE</c> of the foreground window.</param>
/// <param name="OwnerClass">The window class of its owner (<c>GW_OWNER</c>), null when it has none.</param>
public sealed record WindowShape(uint Style, int Width, int Height, string? OwnerClass, int OwnerWidth, int OwnerHeight);

/// <summary>
/// Recognises Lightroom Classic's Import window on Windows from the shape of
/// the foreground window alone, whatever Lightroom's language.
/// <para>
/// Seen in Lightroom Classic 16.0.1 on Windows 11 (27 Nov 2026): the Import
/// window is a popup without a title bar (style 0x94000000), owned by the
/// main window (class <c>AgWinMainFrame</c>), almost as large as it
/// (2484×1293 against 2500×1332); the main window stays enabled. The first
/// Assisted Culling guide is a transparent popup of the same size over it,
/// so it counts as the Import window too. Export (1066×610) and Preferences
/// (1166×901) have a title bar and are much smaller, so they never match.
/// </para>
/// </summary>
public static class ImportWindowRule
{
    /// <summary>The window class of Lightroom Classic's main window.</summary>
    public const string MainFrameClass = "AgWinMainFrame";

    public const double MinimumWidthRatio = 0.9;
    public const double MinimumHeightRatio = 0.8;

    public const uint WS_POPUP = 0x8000_0000;
    /// <summary>A title bar: WS_BORDER | WS_DLGFRAME.</summary>
    public const uint WS_CAPTION = 0x00C0_0000;

    public static bool IsImportWindow(WindowShape? window) =>
        window is { OwnerClass: MainFrameClass, OwnerWidth: > 0, OwnerHeight: > 0 }
        && (window.Style & WS_POPUP) != 0
        && (window.Style & WS_CAPTION) == 0
        && window.Width >= MinimumWidthRatio * window.OwnerWidth
        && window.Height >= MinimumHeightRatio * window.OwnerHeight;
}
