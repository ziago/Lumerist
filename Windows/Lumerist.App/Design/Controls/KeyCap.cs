using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Lumerist.App.Design.Controls;

/// <summary>A key box's state.</summary>
internal enum KeyCapState
{
    Assigned,
    Empty,
    Recording,
    Conflict,
}

/// <summary>
/// A keyboard key (the Mac's KeyCap). Galerist: a white key with a 1 px
/// border and mono text; an empty slot is a hairline box with "+";
/// recording is an ink key with the live dot. Miami: a neon outline, dashed
/// when empty.
/// </summary>
internal static class KeyCap
{
    public static FrameworkElement Create(string text, KeyCapState state = KeyCapState.Assigned, double minWidth = 36) =>
        Theme.IsFlat ? Flat(text, state, minWidth) : Neon(text, state, minWidth);

    private static FrameworkElement Flat(string text, KeyCapState state, double minWidth)
    {
        var foreground = state switch
        {
            KeyCapState.Assigned => Ink.Teal,
            KeyCapState.Conflict => Ink.Ice,
            KeyCapState.Empty => Ink.Steel,
            _ => Ink.OnMagenta,
        };
        var border = state switch
        {
            KeyCapState.Assigned => Ink.Teal,
            KeyCapState.Empty => Ink.Outline,
            KeyCapState.Recording => Ink.Magenta,
            _ => Ink.Amber,
        };
        var row = Layout.HStack(5);
        if (state == KeyCapState.Conflict) row.Children.Add(SynthText.Icon(Icons.Warning, 9, Ink.Amber));
        if (state == KeyCapState.Recording)
            row.Children.Add(new Ellipse { Width = 6, Height = 6, Fill = Ink.Live, VerticalAlignment = VerticalAlignment.Center });
        row.Children.Add(Label(state == KeyCapState.Empty ? "+" : text,
            state == KeyCapState.Recording ? 11 : state == KeyCapState.Empty ? 14 : 13,
            state == KeyCapState.Empty ? FontWeights.Normal : FontWeights.Medium,
            state == KeyCapState.Recording ? 1 : 0, foreground));
        row.HorizontalAlignment = HorizontalAlignment.Center;
        return new Border
        {
            MinWidth = minWidth,
            MinHeight = 28,
            Padding = new Thickness(8, 0, 8, 0),
            Background = state == KeyCapState.Recording ? Ink.Magenta : state == KeyCapState.Empty ? Brushes.Transparent : Ink.Panel,
            BorderBrush = border,
            BorderThickness = new Thickness(1),
            Child = row,
            SnapsToDevicePixels = true,
        };
    }

    private static FrameworkElement Neon(string text, KeyCapState state, double minWidth)
    {
        var color = state switch
        {
            KeyCapState.Assigned => Ink.Teal,
            KeyCapState.Empty => Ink.Steel,
            KeyCapState.Recording => Ink.Magenta,
            _ => Ink.Amber,
        };
        var row = Layout.HStack(4);
        if (state == KeyCapState.Conflict) row.Children.Add(SynthText.Icon(Icons.Warning, 9, color));
        row.Children.Add(Label(state == KeyCapState.Empty ? "+" : text, state == KeyCapState.Recording ? 11 : 14, FontWeights.Bold, 0, color));
        row.HorizontalAlignment = HorizontalAlignment.Center;
        var grid = new Grid
        {
            MinWidth = minWidth,
            MinHeight = 28,
            // A clear fill still answers clicks everywhere on the key.
            Background = Brushes.Transparent,
            SnapsToDevicePixels = true,
        };
        grid.Children.Add(new Rectangle
        {
            RadiusX = 4,
            RadiusY = 4,
            Fill = state == KeyCapState.Recording ? Ink.Faded(Ink.Magenta, 0.12) : Brushes.Transparent,
            Stroke = color,
            StrokeThickness = 1,
            StrokeDashArray = state == KeyCapState.Empty ? new DoubleCollection { 3, 3 } : null,
        });
        row.Margin = new Thickness(8, 0, 8, 0);
        grid.Children.Add(row);
        if (state != KeyCapState.Empty) grid.Effect = SynthControls.Glow(color.Color, 6, 0.35);
        return grid;
    }

    private static SpacedText Label(string text, double size, FontWeight weight, double tracking, Brush color) => new()
    {
        Text = text,
        FontFamily = Theme.MonoFont,
        FontSize = size,
        FontWeight = weight,
        Tracking = tracking,
        Foreground = color,
        MinimumScale = 0.7,
        VerticalAlignment = VerticalAlignment.Center,
    };
}
