using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Lumerist.App.Design;

namespace Lumerist.App.Panels;

/// <summary>
/// Colours, type and symbols of the floating panels and the Value HUD. They
/// are always dark (they sit over Lightroom's dark interface), so the theme
/// tokens resolve to their dark side: Miami's neon, or Galerist's charcoal
/// greys, whatever the main window's light/dark choice.
/// </summary>
internal static class PanelLook
{
    /// <summary>The palette in effect for panels: Miami, or Galerist's dark side.</summary>
    public static ThemePalette Palette => Theme.Current == AppTheme.Miami ? ThemePalette.Miami : ThemePalette.GaleristDark;

    /// <summary>Galerist: no glow, gradients or rounded corners.</summary>
    public static bool IsFlat => Theme.IsFlat;

    /// <summary>The Mac's Synth.Radius.control / container (Miami only).</summary>
    public static double ControlRadius => IsFlat ? 0 : 4;

    public static double ContainerRadius => IsFlat ? 0 : 6;

    public static Color Color(uint hex, double opacity = 1) =>
        Theme.FromHex(hex, (byte)Math.Round(Math.Clamp(opacity, 0, 1) * 255));

    public static SolidColorBrush Brush(uint hex, double opacity = 1)
    {
        var brush = new SolidColorBrush(Color(hex, opacity));
        brush.Freeze();
        return brush;
    }

    /// <summary>The Mac's synthLabel: small uppercase mono; Galerist spaces it wider.</summary>
    public static TrackedText Label(string text, double size = 10, Brush? color = null) =>
        new(text.ToUpperInvariant(), Theme.MonoFont, size, IsFlat ? FontWeights.Medium : FontWeights.SemiBold,
            color ?? Brush(Palette.Steel), IsFlat ? 0.18 * size : 1.5);

    /// <summary>The Mac's panelCaps: Galerist's mono small caps, tracked <paramref name="tracking"/> em.</summary>
    public static TrackedText Caps(string text, double size = 10, Brush? color = null, FontWeight? weight = null,
        double tracking = 0.18) =>
        new(text.ToUpperInvariant(), Theme.MonoFont, size, weight ?? FontWeights.Medium, color ?? Brush(Palette.Steel), tracking * size);

    /// <summary>
    /// A label that may wrap (the Palette's last line): <see cref="Caps"/>
    /// (<paramref name="caps"/>) or <see cref="Label"/> on one line when it
    /// fits <paramref name="width"/>, else the same words on several lines
    /// without the letter spacing (WPF cannot space letters in wrapped text).
    /// </summary>
    public static FrameworkElement WrappingLabel(string text, double size, Brush? color, double width, bool caps)
    {
        var line = caps ? Caps(text, size, color) : Label(text, size, color);
        line.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        if (line.DesiredSize.Width <= width) return line;
        return new TextBlock
        {
            Text = text.ToUpperInvariant(),
            FontFamily = Theme.MonoFont,
            FontSize = size,
            FontWeight = caps || IsFlat ? FontWeights.Medium : FontWeights.SemiBold,
            Foreground = color ?? Brush(Palette.Steel),
            TextWrapping = TextWrapping.Wrap,
        };
    }

    /// <summary>The Mac's synthBody on one line, cut with "…" (lineLimit 1).</summary>
    public static TrackedText BodyLine(string text, double size = 13, Brush? color = null, FontWeight? weight = null) =>
        new(text, Theme.TextFont, size, weight ?? FontWeights.Normal, color ?? Brush(Palette.Body));

    /// <summary>The Mac's synthBody, wrapping, up to <paramref name="maxLines"/> lines (0 = no limit).</summary>
    public static TextBlock Body(string text, double size = 13, Brush? color = null, FontWeight? weight = null, int maxLines = 0)
    {
        var block = new TextBlock
        {
            Text = text,
            FontFamily = Theme.TextFont,
            FontSize = size,
            FontWeight = weight ?? FontWeights.Normal,
            Foreground = color ?? Brush(Palette.Body),
            TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        if (maxLines > 0) block.MaxHeight = Math.Ceiling(size * Theme.TextFont.LineSpacing * maxLines) + 1;
        return block;
    }

    /// <summary>Plain mono text (key caps, range ends).</summary>
    public static TextBlock Mono(string text, double size, FontWeight weight, Brush color) => new()
    {
        Text = text,
        FontFamily = Theme.MonoFont,
        FontSize = size,
        FontWeight = weight,
        Foreground = color,
        TextTrimming = TextTrimming.CharacterEllipsis,
    };

    /// <summary>
    /// A symbol in place of the Mac's SF Symbols: Windows' own icon font
    /// (Segoe Fluent Icons on Windows 11, Segoe MDL2 Assets on Windows 10),
    /// or a small drawing for the shapes that font lacks.
    /// </summary>
    public static FrameworkElement Icon(string symbol, double size, Brush color)
    {
        FrameworkElement icon = symbol switch
        {
            "circle.dashed" => new Ellipse { Stroke = color, StrokeThickness = 1.2, StrokeDashArray = [2.2, 1.6], Width = size, Height = size },
            "circle.dotted" => new Ellipse
            {
                Stroke = color, StrokeThickness = 1.6, StrokeDashArray = [0.1, 1.9], StrokeDashCap = PenLineCap.Round,
                Width = size, Height = size,
            },
            "rectangle.dashed" => new Rectangle
            {
                Stroke = color, StrokeThickness = 1.2, StrokeDashArray = [2.2, 1.6], Width = size * 1.15, Height = size * 0.85,
            },
            "square.bottomhalf.filled" => HalfFilledSquare(size, color),
            _ => new TextBlock
            {
                Text = Glyph(symbol),
                FontFamily = Design.Controls.Icons.Font,
                FontSize = size,
                Foreground = color,
                TextAlignment = TextAlignment.Center,
            },
        };
        icon.HorizontalAlignment = HorizontalAlignment.Center;
        icon.VerticalAlignment = VerticalAlignment.Center;
        icon.SnapsToDevicePixels = true;
        return icon;
    }

    private static string Glyph(string symbol) => symbol switch
    {
        "info.circle" => "",
        "checkmark" => "",
        "xmark" or "xmark.circle.fill" => "",
        "arrow.up.and.down.and.arrow.left.and.right" => "",
        "line.3.horizontal" => "",
        "pin" => "",
        "pin.fill" => "",
        "slider.horizontal.3" => "",
        "magnifyingglass" => "",
        "chevron.left" => "",
        "chevron.right" => "",
        "person.crop.rectangle" => "",
        "cloud.sun" => "",
        "person.2" => "",
        "cube" or "cube.transparent" => "",
        "mountain.2" => "",
        "paintbrush.pointed" => "",
        "eyedropper" => "",
        "sun.max" => "",
        _ => "",
    };

    private static FrameworkElement HalfFilledSquare(double size, Brush color)
    {
        var square = new Grid { Width = size, Height = size };
        square.Children.Add(new Rectangle { Stroke = color, StrokeThickness = 1.2 });
        square.Children.Add(new Rectangle { Fill = color, Height = size / 2, VerticalAlignment = VerticalAlignment.Bottom });
        return square;
    }

    /// <summary>A horizontal hairline.</summary>
    public static Rectangle Hairline(Brush color, double height = 1) => new()
    {
        Height = height,
        Fill = color,
        SnapsToDevicePixels = true,
        HorizontalAlignment = HorizontalAlignment.Stretch,
    };
}

/// <summary>
/// A button that never takes the keyboard focus (the panels must leave it
/// with Lightroom): no chrome of its own, the given fill, and another fill
/// while pressed (the Mac's PanelCellButtonStyle).
/// </summary>
internal sealed class PanelButton : Button
{
    private static readonly ControlTemplate PlainTemplate = CreateTemplate();
    private readonly Brush normal;
    private readonly Brush? pressed;

    public PanelButton(UIElement content, Action onClick, Brush? fill = null, Brush? pressedFill = null, string? label = null)
    {
        Template = PlainTemplate;
        Content = content;
        Focusable = false;
        IsTabStop = false;
        normal = fill ?? Brushes.Transparent;
        pressed = pressedFill;
        Background = normal;
        BorderThickness = new Thickness(0);
        Padding = new Thickness(0);
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Stretch;
        if (label is not null) AutomationProperties.SetName(this, label);
        Click += (_, _) => onClick();
    }

    protected override void OnIsPressedChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnIsPressedChanged(e);
        Background = IsPressed && pressed is not null ? pressed : normal;
    }

    private static ControlTemplate CreateTemplate()
    {
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(BackgroundProperty));
        border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(BorderBrushProperty));
        border.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(BorderThicknessProperty));
        border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(PaddingProperty));
        border.SetValue(UIElement.SnapsToDevicePixelsProperty, true);
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(HorizontalAlignmentProperty, new TemplateBindingExtension(HorizontalContentAlignmentProperty));
        presenter.SetValue(VerticalAlignmentProperty, new TemplateBindingExtension(VerticalContentAlignmentProperty));
        border.AppendChild(presenter);
        var template = new ControlTemplate(typeof(Button)) { VisualTree = border };
        template.Seal();
        return template;
    }
}
