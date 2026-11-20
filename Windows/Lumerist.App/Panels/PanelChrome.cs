using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Lumerist.App.Design;

namespace Lumerist.App.Panels;

/// <summary>A floating panel's frame and the part of it that moves the panel.</summary>
internal sealed record PanelFrame(FrameworkElement Root, FrameworkElement TitleBar, double CornerRadius);

/// <summary>
/// The shared frame of the floating panels (the Mac's PanelChrome), with a
/// title bar row: drag handle, title, context, trailing buttons.
/// </summary>
/// <remarks>
/// Miami: abyss fill, rounded corners, a neon outline in the panel's accent.
/// Galerist: charcoal fill, square corners, a 1 px hairline, mono small caps
/// in the title bar, no glow.
/// </remarks>
internal static class PanelChrome
{
    public const string DragHelp = "Drag to move";

    public static PanelFrame Build(string title, string? subtitle, uint accent, IReadOnlyList<UIElement> trailing,
        FrameworkElement content, double width)
    {
        var palette = PanelLook.Palette;
        var flat = PanelLook.IsFlat;
        var bar = new Grid
        {
            Height = flat ? 40 : 34,
            Background = Brushes.Transparent,
            ToolTip = DragHelp,
        };
        var row = new Grid { Margin = flat ? new Thickness(14, 0, 8, 0) : new Thickness(12, 0, 12, 0) };
        var spacing = flat ? 10 : Theme.Space.S;
        var labels = new List<FrameworkElement>
        {
            PanelLook.Icon("line.3.horizontal", flat ? 11 : 10, PanelLook.Brush(palette.Steel)),
            flat
                ? PanelLook.Caps(title, 11, PanelLook.Brush(palette.Ice), FontWeights.SemiBold)
                : PanelLook.Label(title, 11, PanelLook.Brush(accent)),
        };
        if (subtitle is not null) labels.Add(flat ? PanelLook.Caps(subtitle, 10) : PanelLook.Label(subtitle, 9));
        var column = 0;
        foreach (var label in labels)
        {
            // The labels let presses through to the title bar, which moves the panel.
            label.IsHitTestVisible = false;
            label.VerticalAlignment = VerticalAlignment.Center;
            label.Margin = new Thickness(column == 0 ? 0 : spacing, 0, 0, 0);
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(label, column++);
            row.Children.Add(label);
        }
        // The last label (the context) gives way when the title bar is full.
        row.ColumnDefinitions[^1].Width = new GridLength(1, GridUnitType.Star);
        labels[^1].HorizontalAlignment = HorizontalAlignment.Left;
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        foreach (var button in trailing)
        {
            if (button is FrameworkElement element && buttons.Children.Count > 0) element.Margin = new Thickness(6, 0, 0, 0);
            buttons.Children.Add(button);
        }
        buttons.Margin = new Thickness(spacing + Theme.Space.S, 0, 0, 0);
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(buttons, column);
        row.Children.Add(buttons);
        bar.Children.Add(row);
        var line = PanelLook.Hairline(PanelLook.Brush(palette.GridLine));
        line.VerticalAlignment = VerticalAlignment.Bottom;
        line.IsHitTestVisible = false;
        bar.Children.Add(line);

        var stack = new StackPanel();
        stack.Children.Add(bar);
        stack.Children.Add(content);
        var radius = PanelLook.ContainerRadius;
        // The outline is drawn over the content, as the Mac's strokeBorder overlay: no 1 px inset.
        var root = new Grid { Width = width, SnapsToDevicePixels = true };
        root.Children.Add(new Border
        {
            Background = flat ? PanelLook.Brush(palette.Panel) : PanelLook.Brush(palette.Abyss, 0.96),
            CornerRadius = new CornerRadius(radius),
        });
        root.Children.Add(stack);
        root.Children.Add(new Border
        {
            BorderBrush = flat ? PanelLook.Brush(palette.GridLine) : PanelLook.Brush(accent),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(radius),
            IsHitTestVisible = false,
        });
        return new PanelFrame(root, bar, radius);
    }

    /// <summary>A 22 × 22 symbol button for the title bar (pin, close).</summary>
    public static PanelButton TitleButton(string symbol, Brush color, string label, Action action, string? help = null)
    {
        var face = new Grid { Width = 22, Height = 22, Background = Brushes.Transparent };
        face.Children.Add(PanelLook.Icon(symbol, 11, color));
        var button = new PanelButton(face, action, label: label);
        if (help is not null) button.ToolTip = help;
        return button;
    }
}
