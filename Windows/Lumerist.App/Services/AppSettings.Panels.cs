namespace Lumerist.App.Services;

/// <summary>The top-left corner of a floating panel's window, in screen pixels.</summary>
internal sealed record PanelOrigin(int X, int Y);

/// <summary>The floating panels' preferences (the Mac keeps them in UserDefaults).</summary>
internal sealed partial class AppSettings
{
    /// <summary>Where the Palette was left; null opens it at the pointer.</summary>
    public PanelOrigin? PaletteOrigin { get; set; }

    /// <summary>Where the Key Reference was left; null opens it near the top right of the screen.</summary>
    public PanelOrigin? KeyReferenceOrigin { get; set; }
}
