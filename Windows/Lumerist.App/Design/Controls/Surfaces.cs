using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

namespace Lumerist.App.Design.Controls;

/// <summary>
/// Panels, headers and settings rows (the Mac's NeonPanel, ScreenHeader,
/// SettingsSection, SettingRow, SettingText, SectionTitle, CollapsibleHeader).
/// </summary>
internal static class Surfaces
{
    /// <summary>Left and right inset of a row inside a panel (as in <see cref="SettingRow"/>).</summary>
    public static double RowInset => Theme.IsFlat ? 18 : 14;

    /// <summary>A table or card: panel fill (translucent in Miami) with a 1 px grid-line outline.</summary>
    public static Border Panel(UIElement? content = null, double padding = 0) => new()
    {
        Background = Theme.IsFlat ? Ink.Panel : Ink.Faded(Ink.Panel, 0.82),
        BorderBrush = Ink.GridLine,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(Theme.IsFlat ? 0 : 6),
        Padding = new Thickness(padding),
        Child = content,
        SnapsToDevicePixels = true,
    };

    /// <summary>
    /// Screen title. Galerist: the read-out as a small-caps eyebrow above a
    /// large sentence-case title. Miami: uppercase title with the read-out beside it.
    /// </summary>
    public static FrameworkElement ScreenHeader(string title, string? readout = null)
    {
        var heading = SynthText.Title(title, Theme.IsFlat ? 30 : 24);
        AutomationProperties.SetHeadingLevel(heading, AutomationHeadingLevel.Level1);
        if (Theme.IsFlat)
            return Layout.VStack(Theme.Space.S, readout is null ? null : SynthText.Label(readout, 11), heading);
        var row = Layout.HStack(Theme.Space.M, heading);
        heading.VerticalAlignment = VerticalAlignment.Bottom;
        if (readout is not null)
        {
            var label = SynthText.Label(readout, 12);
            label.VerticalAlignment = VerticalAlignment.Bottom;
            label.Margin = new Thickness(0, 0, 0, 4);
            row.Children.Add(label);
        }
        return row;
    }

    /// <summary>Galerist section title: an optional icon and the name in mono small caps.</summary>
    public static FrameworkElement SectionTitle(string title, string? icon = null, double size = 12)
    {
        var text = SynthText.Caps(title, Theme.MonoFont, size, FontWeights.SemiBold, 0.16 * size, Ink.Ice);
        if (icon is null) return text;
        var glyph = SynthText.Icon(icon, size - 0.5, Ink.Teal);
        glyph.Width = 16;
        return Layout.HStack(Theme.Space.S, glyph, text);
    }

    /// <summary>A titled group of settings rows inside a panel, with an optional footer.</summary>
    public static FrameworkElement SettingsSection(string title, string? icon, string? footer, params UIElement?[] rows)
    {
        var heading = Theme.IsFlat ? SectionTitle(title, icon) : SynthText.Title(title, 15);
        AutomationProperties.SetHeadingLevel(heading, AutomationHeadingLevel.Level2);
        var content = Layout.VStack(0, rows);
        return Layout.VStack(Theme.IsFlat ? 10 : Theme.Space.S,
            heading,
            Panel(content),
            footer is null ? null : SynthText.Body(footer, 12, color: Ink.Steel));
    }

    /// <summary>A setting's title and description.</summary>
    public static FrameworkElement SettingText(string title, string? detail = null, bool isEnabled = true) =>
        Layout.VStack(3,
            SynthText.Body(title, Theme.IsFlat ? 14 : 13, FontWeights.SemiBold, isEnabled ? Ink.Ice : Ink.Disabled),
            detail is null ? null : SynthText.Body(detail, Theme.IsFlat ? 12.5 : 12, color: isEnabled ? Ink.Steel : Ink.Disabled));

    /// <summary>One setting: title and description on the left, the control on the right, a divider below.</summary>
    public static Border SettingRow(string title, string? detail, UIElement? control, bool isEnabled = true)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var text = SettingText(title, detail, isEnabled);
        text.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(text);
        if (control is FrameworkElement element)
        {
            element.Margin = new Thickness(Theme.Space.L, 0, 0, 0);
            element.VerticalAlignment = VerticalAlignment.Center;
            element.IsEnabled = element.IsEnabled && isEnabled;
            if (AutomationProperties.GetName(element).Length == 0) AutomationProperties.SetName(element, title);
            Grid.SetColumn(element, 1);
            grid.Children.Add(element);
        }
        return Row(grid, Theme.IsFlat ? 13 : 12);
    }

    /// <summary>A full-width row inside a panel: the row inset and a divider below.</summary>
    public static Border Row(UIElement content, double vertical = 12, double? horizontal = null) => new()
    {
        Child = content,
        Padding = new Thickness(horizontal ?? RowInset, vertical, horizontal ?? RowInset, vertical),
        BorderBrush = Ink.RowDivider,
        BorderThickness = new Thickness(0, 0, 0, 1),
        SnapsToDevicePixels = true,
    };

    /// <summary>
    /// A collapsible section header with a counter. Galerist: chevron and mono
    /// caps title with a square ink badge; a closed section is a white hairline
    /// row (stack closed rows with a −1 top margin to share lines).
    /// </summary>
    public static Button CollapsibleHeader(string title, int count, bool isExpanded, Action toggle)
    {
        var chevron = SynthText.Icon(Icons.ChevronRight, 10, Theme.IsFlat ? Ink.Ice : Ink.Teal);
        chevron.RenderTransformOrigin = new Point(0.5, 0.5);
        chevron.RenderTransform = new RotateTransform(isExpanded ? 90 : 0);
        FrameworkElement? badge = null;
        if (count > 0)
        {
            var number = SynthText.Mono($"{count}", 10, Theme.IsFlat ? FontWeights.Medium : FontWeights.Bold, Ink.OnMagenta);
            number.VerticalAlignment = VerticalAlignment.Center;
            badge = new Border
            {
                Child = number,
                Background = Ink.Magenta,
                Padding = new Thickness(6, 0, 6, 0),
                Height = Theme.IsFlat ? 17 : 16,
                CornerRadius = new CornerRadius(Theme.IsFlat ? 0 : 8),
            };
            AutomationProperties.SetName(badge, $"{count} assigned");
        }
        var name = Theme.IsFlat ? SectionTitle(title) : SynthText.Title(title, 14);
        var row = Layout.HStack(Theme.IsFlat ? 10 : Theme.Space.S, chevron, name, badge);
        var face = new Border
        {
            Child = row,
            SnapsToDevicePixels = true,
        };
        if (Theme.IsFlat)
        {
            face.Padding = new Thickness(isExpanded ? 0 : 18, 0, isExpanded ? 0 : 18, 0);
            face.Height = isExpanded ? 32 : 44;
            face.Background = isExpanded ? Brushes.Transparent : Ink.Panel;
            face.BorderBrush = isExpanded ? Brushes.Transparent : Ink.GridLine;
            face.BorderThickness = new Thickness(1);
        }
        else
        {
            face.Padding = new Thickness(0, 8, 0, 8);
            face.Background = Brushes.Transparent;
        }
        var button = SynthControls.Plain(face, toggle);
        AutomationProperties.SetName(button, title);
        AutomationProperties.SetItemStatus(button, isExpanded ? "Expanded" : "Collapsed");
        return button;
    }

    /// <summary>"Open all · Close all".</summary>
    public static FrameworkElement ExpandCollapseButtons(Action expandAll, Action collapseAll) =>
        Layout.HStack(4,
            SynthControls.Button("Open all", SynthControls.Kind.Quiet, expandAll),
            new TextBlock { Text = "·", Foreground = Ink.Steel, VerticalAlignment = VerticalAlignment.Center },
            SynthControls.Button("Close all", SynthControls.Kind.Quiet, collapseAll));

    /// <summary>A 1 px line in the grid-line colour.</summary>
    public static Border Divider(bool vertical = false, double length = double.NaN) => vertical
        ? new Border { Width = 1, Height = length, Background = Ink.GridLine, SnapsToDevicePixels = true }
        : new Border { Height = 1, Width = length, Background = Ink.GridLine, SnapsToDevicePixels = true };
}

/// <summary>
/// The window background. Galerist: the flat grey page. Miami: abyss → night
/// → dusk, a faint perspective grid at the bottom and a magenta horizon line;
/// the grid never moves.
/// </summary>
internal sealed class SynthBackground : FrameworkElement
{
    public SynthBackground()
    {
        IsHitTestVisible = false;
    }

    /// <summary>Decoration only: nothing for screen readers.</summary>
    protected override System.Windows.Automation.Peers.AutomationPeer OnCreateAutomationPeer() => null!;

    protected override void OnRender(DrawingContext drawing)
    {
        var width = ActualWidth;
        var height = ActualHeight;
        if (width <= 0 || height <= 0) return;
        var whole = new Rect(0, 0, width, height);
        if (Theme.IsFlat)
        {
            drawing.DrawRectangle(Ink.Abyss, null, whole);
            return;
        }
        var gradient = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        gradient.GradientStops.Add(new GradientStop(Ink.Abyss.Color, 0));
        gradient.GradientStops.Add(new GradientStop(Ink.Night.Color, 0.7));
        gradient.GradientStops.Add(new GradientStop(Ink.Dusk.Color, 1));
        gradient.Freeze();
        drawing.DrawRectangle(gradient, null, whole);

        // Teal grid lines converging to a vanishing point above the band (opacity 0.22 × 0.45).
        var gridHeight = Math.Min(220, height);
        var top = height - gridHeight;
        var pen = new Pen(new SolidColorBrush(Theme.FromHex(0x2DE2E6, (byte)(255 * 0.22 * 0.45))), 1);
        pen.Freeze();
        var vanishing = new Point(width / 2, -gridHeight * 0.9);
        var t = (0 - gridHeight) / (vanishing.Y - gridHeight);
        for (var x = -width; x <= width * 2; x += 64)
            drawing.DrawLine(pen, new Point(x, top + gridHeight), new Point(x + (vanishing.X - x) * t, top));
        double y = gridHeight, step = 46;
        while (y > 0)
        {
            drawing.DrawLine(pen, new Point(0, top + y), new Point(width, top + y));
            y -= step;
            step *= 0.72;
            if (step < 6) break;
        }

        // The horizon: magenta fading out at both ends, with a soft glow.
        var horizonY = height - 190 - 0.5;
        if (horizonY <= 0) return;
        for (var glow = 4; glow >= 0; glow--)
        {
            var line = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
            var alpha = glow == 0 ? 0.8 * 0.6 : 0.08 * 0.6;
            line.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0xFF, 0x3E, 0xA5), 0));
            line.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(255 * alpha), 0xFF, 0x3E, 0xA5), 0.5));
            line.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0xFF, 0x3E, 0xA5), 1));
            line.Freeze();
            drawing.DrawRectangle(line, null, new Rect(0, horizonY - glow * 2, width, 1 + glow * 4));
        }
    }
}
