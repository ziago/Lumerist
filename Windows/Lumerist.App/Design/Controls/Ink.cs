using System.Windows.Media;

namespace Lumerist.App.Design.Controls;

/// <summary>
/// The theme's brushes for code-built UI (the Mac's <c>Synth.teal</c>…),
/// cached per palette. Code-built content is rebuilt on
/// <see cref="Theme.Changed"/>, so a brush taken here is always current.
/// </summary>
internal static class Ink
{
    private static ThemePalette? cachedPalette;
    private static readonly Dictionary<string, SolidColorBrush> Cache = [];

    public static SolidColorBrush Get(string token)
    {
        var palette = Theme.Palette;
        if (!ReferenceEquals(palette, cachedPalette))
        {
            Cache.Clear();
            cachedPalette = palette;
        }
        if (!Cache.TryGetValue(token, out var brush)) Cache[token] = brush = Theme.Brush(token);
        return brush;
    }

    /// <summary>A token at an opacity (SwiftUI's <c>color.opacity(x)</c>).</summary>
    public static SolidColorBrush Faded(SolidColorBrush brush, double opacity)
    {
        var faded = new SolidColorBrush(brush.Color) { Opacity = opacity };
        faded.Freeze();
        return faded;
    }

    public static SolidColorBrush Abyss => Get(nameof(ThemePalette.Abyss));
    public static SolidColorBrush Night => Get(nameof(ThemePalette.Night));
    public static SolidColorBrush Dusk => Get(nameof(ThemePalette.Dusk));
    public static SolidColorBrush Panel => Get(nameof(ThemePalette.Panel));
    public static SolidColorBrush PanelRaised => Get(nameof(ThemePalette.PanelRaised));
    public static SolidColorBrush GridLine => Get(nameof(ThemePalette.GridLine));
    public static SolidColorBrush Outline => Get(nameof(ThemePalette.Outline));
    public static SolidColorBrush Teal => Get(nameof(ThemePalette.Teal));
    public static SolidColorBrush TealLight => Get(nameof(ThemePalette.TealLight));
    public static SolidColorBrush Magenta => Get(nameof(ThemePalette.Magenta));
    public static SolidColorBrush MagentaLight => Get(nameof(ThemePalette.MagentaLight));
    public static SolidColorBrush OnMagenta => Get(nameof(ThemePalette.OnMagenta));
    public static SolidColorBrush Amber => Get(nameof(ThemePalette.Amber));
    public static SolidColorBrush Ice => Get(nameof(ThemePalette.Ice));
    public static SolidColorBrush Body => Get(nameof(ThemePalette.Body));
    public static SolidColorBrush Steel => Get(nameof(ThemePalette.Steel));
    public static SolidColorBrush Disabled => Get(nameof(ThemePalette.Disabled));
    public static SolidColorBrush Error => Get(nameof(ThemePalette.Error));
    public static SolidColorBrush Success => Get(nameof(ThemePalette.Success));
    public static SolidColorBrush Live => Get(nameof(ThemePalette.Live));
    public static SolidColorBrush Chrome => Get(nameof(ThemePalette.Chrome));
    public static SolidColorBrush OnTeal => Get(nameof(ThemePalette.OnTeal));
    public static SolidColorBrush Tint => Get(nameof(ThemePalette.Tint));

    /// <summary>The line between rows inside a panel: faint in Miami, a full hairline in Galerist.</summary>
    public static SolidColorBrush RowDivider => Theme.IsFlat ? GridLine : Faded(GridLine, 0.6);
}
