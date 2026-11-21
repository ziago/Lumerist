using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Lumerist.App.Design;
using Lumerist.App.Services;
using Shapes = System.Windows.Shapes;

namespace Lumerist.App.Guide;

/// <summary>The Mac's NeonButtonStyle kinds used by these windows.</summary>
internal enum GuideButtonKind
{
    Primary,
    Secondary,
    Quiet,
}

/// <summary>Line icons drawn on a 16-unit grid (the Mac uses SF Symbols of the same meaning).</summary>
internal enum GuideIcon
{
    Check,
    Envelope,
    ExclamationCircle,
    CheckCircleFill,
    Heart,
    ArrowDownCircle,
    Cup,
    Gift,
    Star,
}

/// <summary>
/// The pieces of the Mac's Components.swift these windows need (NeonPanel,
/// SettingsSection, SettingRow, StatusPill, the button styles), built in code
/// for the theme in effect. Colours and fonts are resource references, so they
/// follow a theme change; shapes and upper-case titles need a rebuild
/// (<see cref="LiveView"/>).
/// </summary>
internal static class GuideKit
{
    private static readonly Lazy<ResourceDictionary> Styles = new(() => new ResourceDictionary
    {
        Source = new Uri("pack://application:,,,/Lumerist;component/Guide/GuideStyles.xaml"),
    });

    public static Style StyleNamed(string key) => (Style)Styles.Value[key];

    /// <summary>Left and right inset of a row inside a panel (the Mac's Synth.rowInset).</summary>
    public static double RowInset => Theme.IsFlat ? 18 : 14;

    // Text -----------------------------------------------------------------------

    /// <summary>Body copy (the Mac's synthBody).</summary>
    public static TextBlock Text(string text, double size, string color = nameof(ThemePalette.Body),
                                 FontWeight? weight = null, string font = "Text")
    {
        var block = new TextBlock
        {
            Text = text,
            FontSize = size,
            FontWeight = weight ?? FontWeights.Normal,
            TextWrapping = TextWrapping.Wrap,
        };
        block.SetResourceReference(TextBlock.FontFamilyProperty, $"Synth.Font.{font}");
        block.SetResourceReference(TextBlock.ForegroundProperty, $"Synth.{color}");
        return block;
    }

    /// <summary>A title (the Mac's synthTitle): display font, upper case in Miami. Screen readers hear the words as written.</summary>
    public static TextBlock Title(string text, double size)
    {
        var block = Text(Theme.TitleText(text), size, nameof(ThemePalette.Ice), Theme.IsFlat ? FontWeights.SemiBold : FontWeights.Bold, "Display");
        block.TextWrapping = TextWrapping.NoWrap;
        AutomationProperties.SetName(block, text);
        AutomationProperties.SetHeadingLevel(block, AutomationHeadingLevel.Level2);
        return block;
    }

    /// <summary>Small upper-case monospaced label (the Mac's synthLabel).</summary>
    public static TextBlock Label(string text, double size, string color = nameof(ThemePalette.Steel))
    {
        var block = Text(text.ToUpperInvariant(), size, color, Theme.IsFlat ? FontWeights.Medium : FontWeights.SemiBold, "Mono");
        AutomationProperties.SetName(block, text);
        return block;
    }

    // Controls ---------------------------------------------------------------------

    /// <summary>A button in the Mac's style for the theme in effect: the label is drawn in capitals, read as written.</summary>
    public static Button Button(string label, GuideButtonKind kind, Action onClick, GuideIcon? icon = null)
    {
        var text = new TextBlock { Text = label.ToUpperInvariant(), VerticalAlignment = VerticalAlignment.Center };
        object content = text;
        if (icon is { } symbol)
        {
            var glyph = Icon(symbol, 12);
            glyph.Margin = new Thickness(0, 0, 6, 0);
            content = new StackPanel { Orientation = Orientation.Horizontal, Children = { glyph, text } };
        }
        var button = new Button
        {
            Content = content,
            Style = StyleNamed($"Guide.Button.{(Theme.IsFlat ? "Flat" : "Neon")}.{kind}"),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
        };
        AutomationProperties.SetName(button, label);
        button.Click += (_, _) => onClick();
        return button;
    }

    /// <summary>An outlined chip that spells a state that is done (the Mac's StatusPill, good tone), with a check mark.</summary>
    public static FrameworkElement Pill(string text)
    {
        var color = Theme.IsFlat ? nameof(ThemePalette.Ice) : nameof(ThemePalette.Teal);
        var label = Text(text.ToUpperInvariant(), Theme.IsFlat ? 10.5 : 11, color,
                         Theme.IsFlat ? FontWeights.Medium : FontWeights.SemiBold, "Mono");
        label.TextWrapping = TextWrapping.NoWrap;
        label.VerticalAlignment = VerticalAlignment.Center;
        var check = Icon(GuideIcon.Check, 10, color, thickness: 2);
        check.Margin = new Thickness(0, 0, 6, 0);
        var pill = new Border
        {
            Height = 28,
            Padding = new Thickness(10, 0, 10, 0),
            BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Child = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Children = { check, label } },
        };
        pill.SetResourceReference(Border.BorderBrushProperty, $"Synth.{color}");
        pill.SetResourceReference(Border.CornerRadiusProperty, "Synth.Radius.Control");
        AutomationProperties.SetName(pill, text);
        return pill;
    }

    /// <summary>A small spinning arc for "sending" (still when Windows animations are off, and in renders).</summary>
    public static FrameworkElement Spinner()
    {
        var rotation = new RotateTransform();
        var arc = new Shapes.Path
        {
            Data = Geometry.Parse("M 7,1.5 A 5.5,5.5 0 1 1 1.5,7"),
            Width = 14,
            Height = 14,
            StrokeThickness = 1.8,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = rotation,
            VerticalAlignment = VerticalAlignment.Center,
        };
        arc.SetResourceReference(Shapes.Shape.StrokeProperty, "Synth.Steel");
        AutomationProperties.SetName(arc, "Sending");
        arc.Loaded += (_, _) =>
        {
            if (!SystemParameters.ClientAreaAnimation) return;
            rotation.BeginAnimation(RotateTransform.AngleProperty,
                new DoubleAnimation(0, 360, TimeSpan.FromSeconds(0.9)) { RepeatBehavior = RepeatBehavior.Forever });
        };
        arc.Unloaded += (_, _) => rotation.BeginAnimation(RotateTransform.AngleProperty, null);
        return arc;
    }

    // Surfaces ---------------------------------------------------------------------

    /// <summary>A card: panel fill (translucent in Miami) with a 1 px grid-line outline (the Mac's NeonPanel).</summary>
    public static Border Panel(UIElement child)
    {
        var border = new Border { Child = child, BorderThickness = new Thickness(1) };
        if (Theme.IsFlat) border.SetResourceReference(Border.BackgroundProperty, "Synth.Panel");
        else border.Background = Translucent(nameof(ThemePalette.Panel), 0.82);
        border.SetResourceReference(Border.BorderBrushProperty, "Synth.GridLine");
        border.SetResourceReference(Border.CornerRadiusProperty, "Synth.Radius.Container");
        return border;
    }

    /// <summary>A titled group of rows in a panel, with an optional footer (the Mac's SettingsSection).</summary>
    public static FrameworkElement Section(string title, GuideIcon icon, string? footer, params UIElement[] rows)
    {
        var spacing = Theme.IsFlat ? 10.0 : Theme.Space.S;
        var stack = new StackPanel();
        stack.Children.Add(Theme.IsFlat ? SectionTitle(title, icon) : Title(title, 15));
        var list = new StackPanel();
        foreach (var row in rows) list.Children.Add(row);
        var panel = Panel(list);
        panel.Margin = new Thickness(0, spacing, 0, 0);
        stack.Children.Add(panel);
        if (footer is not null)
        {
            var note = Text(footer, 12, nameof(ThemePalette.Steel));
            note.Margin = new Thickness(0, spacing, 0, 0);
            stack.Children.Add(note);
        }
        return stack;
    }

    /// <summary>Galerist's section title: a teal icon and the name in mono capitals.</summary>
    private static FrameworkElement SectionTitle(string title, GuideIcon icon)
    {
        var glyph = Icon(icon, 11.5, nameof(ThemePalette.Teal));
        glyph.Margin = new Thickness(2, 0, 8, 0);
        var name = Text(title.ToUpperInvariant(), 12, nameof(ThemePalette.Ice), FontWeights.SemiBold, "Mono");
        name.VerticalAlignment = VerticalAlignment.Center;
        AutomationProperties.SetName(name, title);
        AutomationProperties.SetHeadingLevel(name, AutomationHeadingLevel.Level2);
        return new StackPanel { Orientation = Orientation.Horizontal, Children = { glyph, name } };
    }

    /// <summary>One setting: title and description on the left, the control on the right (the Mac's SettingRow).</summary>
    public static Border Row(string title, string? detail, UIElement? control)
    {
        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        texts.Children.Add(Text(title, Theme.IsFlat ? 14 : 13, nameof(ThemePalette.Ice), FontWeights.SemiBold));
        if (detail is not null)
        {
            var line = Text(detail, Theme.IsFlat ? 12.5 : 12, nameof(ThemePalette.Steel));
            line.Margin = new Thickness(0, 3, 0, 0);
            texts.Children.Add(line);
        }
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(texts);
        if (control is FrameworkElement element)
        {
            element.Margin = new Thickness(Theme.Space.L, 0, 0, 0);
            element.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(element, 1);
            grid.Children.Add(element);
        }
        return RowFrame(grid, Theme.IsFlat ? 13 : 12);
    }

    /// <summary>A row's padding and the divider under it.</summary>
    public static Border RowFrame(UIElement child, double vertical)
    {
        var frame = new Border
        {
            Child = child,
            Padding = new Thickness(RowInset, vertical, RowInset, vertical),
            BorderThickness = new Thickness(0, 0, 0, 1),
        };
        if (Theme.IsFlat) frame.SetResourceReference(Border.BorderBrushProperty, "Synth.GridLine");
        else frame.BorderBrush = Translucent(nameof(ThemePalette.GridLine), 0.6);
        return frame;
    }

    /// <summary>Buttons side by side, 8 apart.</summary>
    public static StackPanel Buttons(params UIElement[] buttons)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var button in buttons)
        {
            if (row.Children.Count > 0 && button is FrameworkElement element) element.Margin = new Thickness(Theme.Space.S, 0, 0, 0);
            row.Children.Add(button);
        }
        return row;
    }

    /// <summary>The window background: Galerist's flat page; Miami's abyss → night → dusk.</summary>
    public static Brush WindowBackground()
    {
        if (Theme.IsFlat) return Theme.Brush(nameof(ThemePalette.Abyss));
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(0, 1),
            GradientStops =
            {
                new GradientStop(Theme.ColorOf(nameof(ThemePalette.Abyss)), 0),
                new GradientStop(Theme.ColorOf(nameof(ThemePalette.Night)), 0.7),
                new GradientStop(Theme.ColorOf(nameof(ThemePalette.Dusk)), 1),
            },
        };
        brush.Freeze();
        return brush;
    }

    public static SolidColorBrush Translucent(string token, double opacity)
    {
        var brush = new SolidColorBrush(Theme.ColorOf(token)) { Opacity = opacity };
        brush.Freeze();
        return brush;
    }

    // Icons ------------------------------------------------------------------------

    private static string PathData(GuideIcon icon) => icon switch
    {
        GuideIcon.Check => "M 3,8.5 L 6.5,12 L 13,4.5",
        GuideIcon.Envelope => "M 1.5,3.5 H 14.5 V 12.5 H 1.5 Z M 1.5,3.5 L 8,9 L 14.5,3.5",
        GuideIcon.ExclamationCircle => "M 1.5,8 A 6.5,6.5 0 1 0 14.5,8 A 6.5,6.5 0 1 0 1.5,8 Z M 8,4.6 V 8.8 M 8,11.3 V 11.4",
        GuideIcon.CheckCircleFill => "M 1,8 A 7,7 0 1 0 15,8 A 7,7 0 1 0 1,8 Z",
        GuideIcon.Heart => "M 8,13.5 C 5,11.5 1.5,9 1.5,5.6 C 1.5,3.6 3,2.2 4.8,2.2 C 6.2,2.2 7.3,3 8,4.2 C 8.7,3 9.8,2.2 11.2,2.2 C 13,2.2 14.5,3.6 14.5,5.6 C 14.5,9 11,11.5 8,13.5 Z",
        GuideIcon.ArrowDownCircle => "M 1.5,8 A 6.5,6.5 0 1 0 14.5,8 A 6.5,6.5 0 1 0 1.5,8 Z M 8,4.5 V 11 M 5.3,8.4 L 8,11.1 L 10.7,8.4",
        GuideIcon.Cup => "M 2.5,5.5 H 11 V 9.5 C 11,11.4 9.6,12.8 7.7,12.8 H 5.8 C 3.9,12.8 2.5,11.4 2.5,9.5 Z M 11,6.5 H 12.3 C 13.4,6.5 14.2,7.3 14.2,8.3 C 14.2,9.3 13.4,10.1 12.3,10.1 H 11 M 1.5,14.5 H 12",
        GuideIcon.Gift => "M 1.8,5.5 H 14.2 V 8.5 H 1.8 Z M 2.8,8.5 V 14.2 H 13.2 V 8.5 M 8,5.5 V 14.2 M 8,5.5 C 6.2,5.5 4.2,4.8 4.2,3.4 C 4.2,2.2 5.9,1.8 8,5.5 C 10.1,1.8 11.8,2.2 11.8,3.4 C 11.8,4.8 9.8,5.5 8,5.5",
        GuideIcon.Star => "M 8,1.6 L 9.9,5.7 L 14.4,6.2 L 11,9.2 L 12,13.7 L 8,11.4 L 4,13.7 L 5,9.2 L 1.6,6.2 L 6.1,5.7 Z",
        _ => "",
    };

    /// <summary>
    /// A line icon <paramref name="size"/> wide, in a theme colour, or in the
    /// enclosing button's text colour when <paramref name="color"/> is null.
    /// Decoration only: shapes have no automation peer, so screen readers skip it.
    /// </summary>
    public static FrameworkElement Icon(GuideIcon icon, double size, string? color = null, double thickness = 1.3)
    {
        var geometry = Geometry.Parse(PathData(icon)).Clone();
        geometry.Transform = new ScaleTransform(size / 16, size / 16);
        var path = new Shapes.Path
        {
            Data = geometry,
            Width = size,
            Height = size,
            StrokeThickness = thickness,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            VerticalAlignment = VerticalAlignment.Center,
            SnapsToDevicePixels = false,
        };
        var property = icon == GuideIcon.CheckCircleFill ? Shapes.Shape.FillProperty : Shapes.Shape.StrokeProperty;
        if (color is null)
        {
            path.SetBinding(property, new Binding(nameof(Control.Foreground))
            {
                RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(Control), 1),
            });
        }
        else
        {
            path.SetResourceReference(property, $"Synth.{color}");
        }
        if (icon != GuideIcon.CheckCircleFill) return path;

        // A filled circle with the check cut out in the surface colour.
        var check = (Shapes.Path)Icon(GuideIcon.Check, size * 0.72, Theme.IsFlat ? nameof(ThemePalette.Panel) : nameof(ThemePalette.Abyss), 1.8);
        check.HorizontalAlignment = HorizontalAlignment.Center;
        var badge = new Grid { Width = size, Height = size, VerticalAlignment = VerticalAlignment.Center, Children = { path, check } };
        return badge;
    }

    // Links ------------------------------------------------------------------------

    /// <summary>Opens a web page in the default browser. Nothing is sent by Lumerist itself.</summary>
    public static void Open(Uri url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url.AbsoluteUri) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception error) when (error is Win32Exception or InvalidOperationException or FileNotFoundException)
        {
            Log.Write($"Could not open {url.Host}: {error.Message}");
        }
    }
}

/// <summary>
/// Content built in code for the theme in effect, rebuilt on a theme change
/// and whenever the subclass calls <see cref="Rebuild"/>. Listens only while
/// on screen (Loaded), so a closed window or a render leaves nothing behind.
/// </summary>
internal abstract class LiveView : ContentControl
{
    private readonly Action rebuild;
    private bool isListening;

    protected LiveView()
    {
        rebuild = Rebuild;
        Focusable = false;
        IsTabStop = false;
        // Backgrounds fill the space given (a render taller than the content).
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Stretch;
        Loaded += (_, _) =>
        {
            if (isListening) return;
            isListening = true;
            Theme.Changed += rebuild;
            StartListening();
            Rebuild();
        };
        Unloaded += (_, _) =>
        {
            if (!isListening) return;
            isListening = false;
            Theme.Changed -= rebuild;
            StopListening();
        };
    }

    /// <summary>The content for the current state and theme.</summary>
    protected abstract UIElement Build();

    /// <summary>Subscribe to what changes the content (it is rebuilt right after).</summary>
    protected virtual void StartListening() { }

    protected virtual void StopListening() { }

    public void Rebuild() => Content = Build();
}
