using System.Globalization;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Documents;
using System.Windows.Media;

namespace Lumerist.App.Design.Controls;

/// <summary>
/// Text with letter spacing (SwiftUI's <c>tracking</c>), which TextBlock
/// cannot do: the mono small caps of labels, tabs and buttons. Font and
/// colour are inherited like a TextBlock's, so a button's style reaches it.
/// </summary>
/// <remarks>
/// Characters are drawn one by one, so there is no kerning: meant for short
/// labels, mostly in the monospaced font. <see cref="Wrap"/> breaks lines at
/// spaces; <see cref="MinimumScale"/> shrinks a single line that does not fit
/// (SwiftUI's minimumScaleFactor).
/// </remarks>
internal sealed class SpacedText : FrameworkElement
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(SpacedText),
        new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Extra space after each character, in device-independent pixels (negative tightens).</summary>
    public static readonly DependencyProperty TrackingProperty = DependencyProperty.Register(
        nameof(Tracking), typeof(double), typeof(SpacedText),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty WrapProperty = DependencyProperty.Register(
        nameof(Wrap), typeof(bool), typeof(SpacedText),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>1 = never shrink; 0.6 = may shrink to 60 % to fit on one line.</summary>
    public static readonly DependencyProperty MinimumScaleProperty = DependencyProperty.Register(
        nameof(MinimumScale), typeof(double), typeof(SpacedText),
        new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TextAlignmentProperty = DependencyProperty.Register(
        nameof(TextAlignment), typeof(TextAlignment), typeof(SpacedText),
        new FrameworkPropertyMetadata(TextAlignment.Left, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ForegroundProperty = TextElement.ForegroundProperty.AddOwner(typeof(SpacedText),
        new FrameworkPropertyMetadata(SystemColors.ControlTextBrush, FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FontFamilyProperty = TextElement.FontFamilyProperty.AddOwner(typeof(SpacedText),
        new FrameworkPropertyMetadata(SystemFonts.MessageFontFamily, FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FontSizeProperty = TextElement.FontSizeProperty.AddOwner(typeof(SpacedText),
        new FrameworkPropertyMetadata(SystemFonts.MessageFontSize, FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FontWeightProperty = TextElement.FontWeightProperty.AddOwner(typeof(SpacedText),
        new FrameworkPropertyMetadata(FontWeights.Normal, FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    private readonly List<(FormattedText Glyph, Point Origin)> layout = [];
    private double scale = 1;
    private Size natural;

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public double Tracking
    {
        get => (double)GetValue(TrackingProperty);
        set => SetValue(TrackingProperty, value);
    }

    public bool Wrap
    {
        get => (bool)GetValue(WrapProperty);
        set => SetValue(WrapProperty, value);
    }

    public double MinimumScale
    {
        get => (double)GetValue(MinimumScaleProperty);
        set => SetValue(MinimumScaleProperty, value);
    }

    public TextAlignment TextAlignment
    {
        get => (TextAlignment)GetValue(TextAlignmentProperty);
        set => SetValue(TextAlignmentProperty, value);
    }

    public Brush Foreground
    {
        get => (Brush)GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    public FontFamily FontFamily
    {
        get => (FontFamily)GetValue(FontFamilyProperty);
        set => SetValue(FontFamilyProperty, value);
    }

    public double FontSize
    {
        get => (double)GetValue(FontSizeProperty);
        set => SetValue(FontSizeProperty, value);
    }

    public FontWeight FontWeight
    {
        get => (FontWeight)GetValue(FontWeightProperty);
        set => SetValue(FontWeightProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        layout.Clear();
        scale = 1;
        var text = Text ?? "";
        var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var typeface = new Typeface(FontFamily, FontStyles.Normal, FontWeight, FontStretches.Normal);
        FormattedText Make(string piece) => new(piece, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface,
            FontSize, Foreground, pixelsPerDip);

        var lineHeight = Make("Xg").Height;
        if (text.Length == 0) return natural = new Size(0, lineHeight);

        var glyphs = new List<(FormattedText Glyph, double Advance, bool IsSpace)>();
        var elements = StringInfo.GetTextElementEnumerator(text);
        while (elements.MoveNext())
        {
            var piece = elements.GetTextElement();
            var glyph = Make(piece);
            glyphs.Add((glyph, glyph.WidthIncludingTrailingWhitespace + Tracking, piece == " "));
        }

        double x = 0, y = 0, widest = 0;
        if (Wrap && double.IsFinite(availableSize.Width))
        {
            // Greedy line breaking at spaces.
            var index = 0;
            while (index < glyphs.Count)
            {
                var wordEnd = index;
                while (wordEnd < glyphs.Count && !glyphs[wordEnd].IsSpace) wordEnd++;
                var wordWidth = 0.0;
                for (var i = index; i < wordEnd; i++) wordWidth += glyphs[i].Advance;
                if (x > 0 && x + wordWidth - Tracking > availableSize.Width)
                {
                    widest = Math.Max(widest, x - Tracking);
                    x = 0;
                    y += lineHeight;
                }
                for (var i = index; i < wordEnd; i++)
                {
                    layout.Add((glyphs[i].Glyph, new Point(x, y)));
                    x += glyphs[i].Advance;
                }
                if (wordEnd < glyphs.Count)
                {
                    // The space: drawn only when the line goes on.
                    layout.Add((glyphs[wordEnd].Glyph, new Point(x, y)));
                    x += glyphs[wordEnd].Advance;
                }
                index = wordEnd + 1;
            }
            widest = Math.Max(widest, x - Tracking);
            return natural = new Size(Math.Max(0, widest), y + lineHeight);
        }

        foreach (var (glyph, advance, _) in glyphs)
        {
            layout.Add((glyph, new Point(x, 0)));
            x += advance;
        }
        natural = new Size(Math.Max(0, x - Tracking), lineHeight);
        if (MinimumScale < 1 && double.IsFinite(availableSize.Width) && natural.Width > availableSize.Width && natural.Width > 0)
        {
            scale = Math.Max(MinimumScale, availableSize.Width / natural.Width);
            return new Size(natural.Width * scale, natural.Height * scale);
        }
        return natural;
    }

    protected override void OnRender(DrawingContext drawing)
    {
        if (layout.Count == 0) return;
        var contentWidth = natural.Width * scale;
        var offset = TextAlignment switch
        {
            TextAlignment.Right => Math.Max(0, ActualWidth - contentWidth),
            TextAlignment.Center => Math.Max(0, (ActualWidth - contentWidth) / 2),
            _ => 0,
        };
        // Vertically centred in the space given, like a TextBlock in a taller cell.
        var top = Math.Max(0, (ActualHeight - natural.Height * scale) / 2);
        drawing.PushTransform(new TranslateTransform(offset, top));
        if (scale < 1) drawing.PushTransform(new ScaleTransform(scale, scale));
        foreach (var (glyph, origin) in layout)
        {
            glyph.SetForegroundBrush(Foreground);
            drawing.DrawText(glyph, origin);
        }
        if (scale < 1) drawing.Pop();
        drawing.Pop();
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new SpacedTextAutomationPeer(this);

    /// <summary>Screen readers read it as plain text.</summary>
    private sealed class SpacedTextAutomationPeer(SpacedText owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override string GetNameCore()
        {
            var name = base.GetNameCore();
            return string.IsNullOrEmpty(name) ? owner.Text : name;
        }

        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Text;

        protected override string GetClassNameCore() => "TextBlock";
    }
}
