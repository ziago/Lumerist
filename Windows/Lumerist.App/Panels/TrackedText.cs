using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Lumerist.App.Panels;

/// <summary>An element whose first text baseline is known, for <see cref="BaselineRow"/>.</summary>
internal interface IHasBaseline
{
    /// <summary>Distance from the element's top to its first baseline, after measuring.</summary>
    double Baseline { get; }
}

/// <summary>
/// One line of text with letter spacing (the Mac's <c>.tracking</c>, which
/// WPF text lacks) and optional tabular figures, cut with "…" when it does
/// not fit. Built once in code: the panels build their views again when
/// something changes, as the Mac rebuilds its SwiftUI views.
/// </summary>
internal sealed class TrackedText : FrameworkElement, IHasBaseline
{
    private readonly string text;
    private readonly Typeface typeface;
    private readonly double size;
    private readonly Brush brush;
    private readonly double tracking;
    private readonly bool tabular;
    private GlyphRun? run;
    private FormattedText? formatted;
    private double baseline;

    /// <param name="tracking">Extra space after each character but the last, in pixels (may be negative).</param>
    /// <param name="tabular">Every digit as wide as the widest, so a changing number does not shift sideways.</param>
    public TrackedText(string text, FontFamily family, double size, FontWeight weight, Brush brush, double tracking = 0,
        bool tabular = false)
    {
        this.text = text;
        typeface = new Typeface(family, FontStyles.Normal, weight, FontStretches.Normal);
        this.size = size;
        this.brush = brush;
        this.tracking = tracking;
        this.tabular = tabular;
        SnapsToDevicePixels = true;
        System.Windows.Automation.AutomationProperties.SetName(this, text);
    }

    public double Baseline => baseline;

    protected override Size MeasureOverride(Size availableSize)
    {
        var pixelsPerDip = (float)VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var reference = new FormattedText(text.Length == 0 ? " " : text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            typeface, size, brush, pixelsPerDip);
        baseline = reference.Baseline;
        var height = reference.Height;
        run = null;
        formatted = null;
        if (text.Length == 0) return new Size(0, height);
        if ((tracking != 0 || tabular) && BuildRun(availableSize.Width, pixelsPerDip) is { } built)
        {
            run = built.Run;
            return new Size(built.Width, height);
        }
        // No letter spacing to add (or a character the font lacks): WPF's own text, with kerning and fallback fonts.
        reference.MaxLineCount = 1;
        reference.Trimming = TextTrimming.CharacterEllipsis;
        if (!double.IsInfinity(availableSize.Width)) reference.MaxTextWidth = Math.Max(1, availableSize.Width);
        formatted = reference;
        return new Size(Math.Min(reference.Width, availableSize.Width), height);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        if (run is not null) drawingContext.DrawGlyphRun(brush, run);
        else if (formatted is not null) drawingContext.DrawText(formatted, new Point(0, 0));
    }

    private (GlyphRun Run, double Width)? BuildRun(double available, float pixelsPerDip)
    {
        if (!typeface.TryGetGlyphTypeface(out var glyphs)) return null;
        var map = glyphs.CharacterToGlyphMap;
        var digitAdvance = 0.0;
        if (tabular)
        {
            foreach (var digit in "0123456789")
            {
                if (map.TryGetValue(digit, out var index)) digitAdvance = Math.Max(digitAdvance, glyphs.AdvanceWidths[index] * size);
            }
        }
        var indices = new List<ushort>(text.Length);
        var advances = new List<double>(text.Length);
        var offsets = new List<Point>(text.Length);
        foreach (var character in text)
        {
            if (!map.TryGetValue(character, out var index)) return null;
            var advance = glyphs.AdvanceWidths[index] * size;
            var offset = 0.0;
            if (tabular && char.IsAsciiDigit(character) && digitAdvance > 0)
            {
                offset = (digitAdvance - advance) / 2;
                advance = digitAdvance;
            }
            indices.Add(index);
            advances.Add(advance + tracking);
            offsets.Add(new Point(offset, 0));
        }
        advances[^1] -= tracking;
        var width = advances.Sum();
        if (width > available + 0.5 && map.TryGetValue('…', out var ellipsis))
        {
            // Too long: drop characters from the end and close with "…".
            var ellipsisAdvance = glyphs.AdvanceWidths[ellipsis] * size;
            while (indices.Count > 0 && advances.Sum() + tracking + ellipsisAdvance > available)
            {
                indices.RemoveAt(indices.Count - 1);
                advances.RemoveAt(advances.Count - 1);
                offsets.RemoveAt(offsets.Count - 1);
            }
            if (indices.Count > 0) advances[^1] += tracking;
            indices.Add(ellipsis);
            advances.Add(ellipsisAdvance);
            offsets.Add(new Point(0, 0));
            width = advances.Sum();
        }
        var glyphRun = new GlyphRun(glyphs, 0, false, size, pixelsPerDip, indices, new Point(0, baseline), advances, offsets,
            null, null, null, null, null);
        return (glyphRun, Math.Max(0, width));
    }
}

/// <summary>
/// Lays its children out left to right on a shared first baseline (SwiftUI's
/// <c>HStack(alignment: .firstTextBaseline)</c>). Children from
/// <see cref="TrailingFrom"/> on sit at the right end, after the leading ones
/// (a <c>Spacer</c> between them); the leading ones get what is left.
/// </summary>
internal sealed class BaselineRow : Panel, IHasBaseline
{
    private double baseline;

    public double Spacing { get; init; }

    /// <summary>Space the leading and trailing groups keep between them, at least.</summary>
    public double MinimumGap { get; init; }

    /// <summary>The index of the first trailing child; null keeps every child on the left.</summary>
    public int? TrailingFrom { get; init; }

    public double Baseline => baseline;

    public BaselineRow(params UIElement[] children)
    {
        foreach (var child in children) Children.Add(child);
    }

    private static double BaselineOf(UIElement child) =>
        child is IHasBaseline text ? text.Baseline : child.DesiredSize.Height;

    protected override Size MeasureOverride(Size availableSize)
    {
        var split = TrailingFrom ?? Children.Count;
        var unlimited = new Size(double.PositiveInfinity, availableSize.Height);
        // Trailing children first: the leading ones (a title) give way.
        var trailingWidth = 0.0;
        for (var index = split; index < Children.Count; index++)
        {
            Children[index].Measure(unlimited);
            trailingWidth += Children[index].DesiredSize.Width + (index > split ? Spacing : 0);
        }
        var gap = split < Children.Count && split > 0 ? Math.Max(MinimumGap, Spacing) : 0;
        var left = double.IsInfinity(availableSize.Width) ? double.PositiveInfinity : Math.Max(0, availableSize.Width - trailingWidth - gap);
        var leadingWidth = 0.0;
        for (var index = 0; index < split; index++)
        {
            var spacing = index > 0 ? Spacing : 0;
            var room = double.IsInfinity(left) ? left : Math.Max(0, left - leadingWidth - spacing);
            Children[index].Measure(new Size(room, availableSize.Height));
            leadingWidth += Children[index].DesiredSize.Width + spacing;
        }
        baseline = Children.Cast<UIElement>().Select(BaselineOf).DefaultIfEmpty(0).Max();
        var height = Children.Cast<UIElement>().Select(child => baseline - BaselineOf(child) + child.DesiredSize.Height).DefaultIfEmpty(0).Max();
        return new Size(leadingWidth + gap + trailingWidth, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var split = TrailingFrom ?? Children.Count;
        var x = 0.0;
        for (var index = 0; index < split; index++)
        {
            var child = Children[index];
            if (index > 0) x += Spacing;
            child.Arrange(new Rect(x, baseline - BaselineOf(child), child.DesiredSize.Width, child.DesiredSize.Height));
            x += child.DesiredSize.Width;
        }
        var right = finalSize.Width;
        for (var index = Children.Count - 1; index >= split; index--)
        {
            var child = Children[index];
            right -= child.DesiredSize.Width;
            child.Arrange(new Rect(right, baseline - BaselineOf(child), child.DesiredSize.Width, child.DesiredSize.Height));
            right -= Spacing;
        }
        return finalSize;
    }
}
