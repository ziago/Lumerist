using System.Windows;

namespace Lumerist.App.Views;

/// <summary>
/// What the Floating panels page needs from the panels (the Mac's
/// <c>store.overlay</c>, <c>FloatingPanels.shared</c> and
/// <c>store.isKeyReferenceVisible</c>). The panel host implements it next to
/// <see cref="Panels.IPanelHost"/>; without one (snapshots), the page shows
/// those controls disabled. Called on the UI thread.
/// </summary>
internal interface IPanelSettings
{
    /// <summary>Raised when <see cref="IsPlacingHud"/> or <see cref="IsKeyReferenceVisible"/> changed.</summary>
    event Action? StateChanged;

    /// <summary>
    /// Shows the Value HUD sample with <see cref="Services.AppSettings.Overlay"/>
    /// as it is now (style, opacity, duration, display), then hides it as
    /// the HUD normally would. While placing, updates the sample instead.
    /// </summary>
    void PreviewHud();

    /// <summary>Hides the sample if it is showing (the page closes); ends placing.</summary>
    void HideHudPreview();

    /// <summary>A sample the user drags into place is on screen.</summary>
    bool IsPlacingHud { get; }

    /// <summary>
    /// Shows a sample HUD to drag anywhere on the screen; its position is
    /// written to the overlay settings (and saved) as it moves.
    /// </summary>
    void BeginPlacingHud();

    /// <summary>Stops placing: the sample shows once more at its new spot, then hides.</summary>
    void EndPlacingHud();

    bool IsKeyReferenceVisible { get; }

    void SetKeyReferenceVisible(bool visible);

    /// <summary>Sends the Key Reference back to the top-right corner of the screen.</summary>
    void ResetKeyReferencePosition();

    /// <summary>The Palette opens under the pointer again.</summary>
    void ResetPalettePosition();

    /// <summary>
    /// The Palette as it looks over Lightroom, with the user's favourites, for
    /// the page's demo: clicks do nothing. Null when it cannot be drawn.
    /// </summary>
    FrameworkElement? CreatePalettePreview();
}
