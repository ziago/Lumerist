namespace Lumerist.App.Design;

/// <summary>
/// Every colour of a theme, as 0xRRGGBB: the same values as the Mac's
/// <c>SynthPalette</c> (Sources/LumeristApp/Design/Theme.swift), checked by
/// Scripts/check-parity.py.
/// </summary>
/// <remarks>
/// Token names are Miami's; their roles: <c>Abyss</c> window background,
/// <c>Panel</c> cards and tables, <c>GridLine</c> dividers, <c>Outline</c>
/// control borders, <c>Teal</c> primary / active / connected, <c>Magenta</c>
/// selection fills, <c>OnMagenta</c> text on them, <c>Amber</c> attention,
/// <c>Ice</c>/<c>Body</c>/<c>Steel</c> text, <c>Live</c> the one accent for
/// something happening now, <c>Chrome</c> the window's header and tab band,
/// <c>OnTeal</c> text on a teal fill, <c>Tint</c> a soft tint behind Command
/// Keys on the drawn keyboard.
/// </remarks>
internal sealed record ThemePalette(
    uint Abyss, uint Night, uint Dusk, uint Panel, uint PanelRaised, uint GridLine, uint Outline,
    uint Teal, uint TealLight, uint Magenta, uint MagentaLight, uint OnMagenta, uint Amber,
    uint Ice, uint Body, uint Steel, uint Disabled, uint Error, uint Success, uint Live,
    uint Chrome, uint OnTeal, uint Tint)
{
    /// <summary>The original neon theme. Text colours are ≥ 4.5:1 on Abyss, Panel and PanelRaised.</summary>
    public static readonly ThemePalette Miami = new(
        Abyss: 0x070B18, Night: 0x0B0F24, Dusk: 0x1A0C2A, Panel: 0x0D1428, PanelRaised: 0x111A33,
        GridLine: 0x1B2A4A, Outline: 0x2A3B63,
        Teal: 0x2DE2E6, TealLight: 0x9BF6F8, Magenta: 0xFF3EA5, MagentaLight: 0xFF8FCB, OnMagenta: 0x12030B,
        Amber: 0xFFB347,
        Ice: 0xE6F6FF, Body: 0xC3D3EA, Steel: 0x8FA6C8, Disabled: 0x6B80A3, Error: 0xFF6B81, Success: 0x5CF2A6,
        Live: 0xFF3EA5,
        Chrome: 0x070B18,
        OnTeal: 0x070B18, Tint: 0x1B2A4A);

    /// <summary>
    /// Galerist, light: grey page, white surfaces, ink text; a Lightroom-like
    /// blue for keys and secondary actions, the red for selections, attention
    /// and what is live.
    /// </summary>
    public static readonly ThemePalette GaleristLight = new(
        Abyss: 0xF4F4F4, Night: 0xF4F4F4, Dusk: 0xF4F4F4, Panel: 0xFFFFFF, PanelRaised: 0xFAFAFA,
        GridLine: 0xE7E7E7, Outline: 0xDCDCDC,
        Teal: 0x1A66D9, TealLight: 0x1557BA, Magenta: 0xD0141F, MagentaLight: 0xD0141F, OnMagenta: 0xFFFFFF,
        // A shade deeper than the REC red: as text it stays ≥ 4.5:1 on the grey page too.
        Amber: 0xD0141F,
        Ice: 0x0A0A0A, Body: 0x1A1A1A, Steel: 0x525252, Disabled: 0x8A8A8A, Error: 0xD0141F, Success: 0x0A0A0A,
        Live: 0xE11D2A,
        Chrome: 0xFAFAFA,
        OnTeal: 0xFFFFFF, Tint: 0xFCE8EA);

    /// <summary>
    /// Galerist, dark: Lightroom-like charcoal greys, light text, the same blue
    /// (lighter) and red. Also the look of the panels that float over Lightroom.
    /// </summary>
    public static readonly ThemePalette GaleristDark = new(
        Abyss: 0x161616, Night: 0x161616, Dusk: 0x161616, Panel: 0x1E1E1E, PanelRaised: 0x262626,
        GridLine: 0x333333, Outline: 0x3D3D3D,
        Teal: 0x5B9BFF, TealLight: 0x8AB8FF, Magenta: 0xE11D2A, MagentaLight: 0xFF5A63, OnMagenta: 0xFFFFFF,
        Amber: 0xFF5A63,
        Ice: 0xF5F5F5, Body: 0xE5E5E5, Steel: 0xA3A3A3, Disabled: 0x6B6B6B, Error: 0xFF5A63, Success: 0xF5F5F5,
        Live: 0xE11D2A,
        Chrome: 0x1C1C1C,
        OnTeal: 0x0A0A0A, Tint: 0x3A1C20);
}
