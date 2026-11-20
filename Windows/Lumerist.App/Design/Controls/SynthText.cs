using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Lumerist.App.Design.Controls;

/// <summary>
/// Text in the theme's type (the Mac's <c>synthTitle</c>, <c>synthLabel</c>,
/// <c>synthBody</c>): build it when the screen is built, and build again on
/// <see cref="Theme.Changed"/>.
/// </summary>
internal static class SynthText
{
    /// <summary>
    /// A title: Outfit semibold in sentence case (Galerist), or uppercase
    /// Chakra Petch bold with 1 px tracking (Miami).
    /// </summary>
    public static FrameworkElement Title(string text, double size = 24)
    {
        if (Theme.IsFlat)
        {
            return new TextBlock
            {
                Text = text,
                FontFamily = Theme.DisplayFont,
                FontSize = size,
                FontWeight = FontWeights.SemiBold,
                Foreground = Ink.Ice,
                TextWrapping = TextWrapping.Wrap,
            };
        }
        return new SpacedText
        {
            Text = text.ToUpperInvariant(),
            FontFamily = Theme.DisplayFont,
            FontSize = size,
            FontWeight = FontWeights.Bold,
            Tracking = 1,
            Foreground = Ink.Ice,
            Wrap = true,
        };
    }

    /// <summary>Small uppercase monospaced label (table headers, read-outs); Galerist spaces it wider.</summary>
    public static SpacedText Label(string text, double size = 10, Brush? color = null, bool wrap = false) => new()
    {
        Text = text.ToUpperInvariant(),
        FontFamily = Theme.MonoFont,
        FontSize = size,
        FontWeight = Theme.IsFlat ? FontWeights.Medium : FontWeights.SemiBold,
        Tracking = Theme.IsFlat ? 0.18 * size : 1.5,
        Foreground = color ?? Ink.Steel,
        Wrap = wrap,
    };

    /// <summary>Body copy in the text font (Inter in Miami, Outfit in Galerist); wraps.</summary>
    public static TextBlock Body(string text, double size = 13, FontWeight? weight = null, Brush? color = null) => new()
    {
        Text = text,
        FontFamily = Theme.TextFont,
        FontSize = size,
        FontWeight = weight ?? FontWeights.Normal,
        Foreground = color ?? Ink.Body,
        TextWrapping = TextWrapping.Wrap,
    };

    /// <summary>Monospaced text (keys, values, steps).</summary>
    public static TextBlock Mono(string text, double size, FontWeight weight, Brush color) => new()
    {
        Text = text,
        FontFamily = Theme.MonoFont,
        FontSize = size,
        FontWeight = weight,
        Foreground = color,
    };

    /// <summary>Uppercase spaced text in any font (tabs, pills, buttons).</summary>
    public static SpacedText Caps(string text, FontFamily font, double size, FontWeight weight, double tracking, Brush? color = null)
    {
        var caps = new SpacedText
        {
            Text = text.ToUpperInvariant(),
            FontFamily = font,
            FontSize = size,
            FontWeight = weight,
            Tracking = tracking,
        };
        // Without a colour it inherits one (a button's foreground).
        if (color is not null) caps.Foreground = color;
        return caps;
    }

    /// <summary>A glyph of the Windows icon font (the Mac's SF Symbols).</summary>
    public static TextBlock Icon(string glyph, double size, Brush? color = null)
    {
        var icon = new Glyph
        {
            Text = glyph,
            FontFamily = Icons.Font,
            FontSize = size,
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Center,
        };
        if (color is not null) icon.Foreground = color;
        return icon;
    }

    /// <summary>A warning line in the error colour ("Label(error, systemImage: exclamationmark.triangle.fill)").</summary>
    public static FrameworkElement ErrorLine(string text, double size = 12)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var icon = Icon(Icons.Warning, size - 1, Ink.Error);
        icon.VerticalAlignment = VerticalAlignment.Top;
        icon.Margin = new Thickness(0, 2, 6, 0);
        var body = Body(text, size, color: Ink.Error);
        Grid.SetColumn(body, 1);
        grid.Children.Add(icon);
        grid.Children.Add(body);
        return grid;
    }
}

/// <summary>An icon: decoration only, so screen readers skip it (the control around it carries the name).</summary>
internal sealed class Glyph : TextBlock
{
    protected override System.Windows.Automation.Peers.AutomationPeer OnCreateAutomationPeer() => null!;
}

/// <summary>
/// Glyphs of Windows' own icon font (Segoe Fluent Icons on Windows 11,
/// Segoe MDL2 Assets on Windows 10: same code points), standing in for the
/// Mac's SF Symbols.
/// </summary>
internal static class Icons
{
    public static readonly FontFamily Font = new("Segoe Fluent Icons, Segoe MDL2 Assets");

    public const string Add = "\uE710";
    public const string Minus = "\uE738";
    public const string Cancel = "\uE711";
    public const string More = "\uE712";
    public const string Settings = "\uE713";
    public const string Search = "\uE721";
    public const string CheckMark = "\uE73E";
    public const string ChevronDown = "\uE70D";
    public const string ChevronRight = "\uE76C";
    public const string ArrowUp = "\uE74A";
    public const string ArrowDown = "\uE74B";
    public const string Warning = "\uE7BA";
    public const string Error = "\uE783";
    public const string Lock = "\uE72E";
    public const string Link = "\uE71B";
    public const string Power = "\uE7E8";
    public const string Palette = "\uE790";
    public const string Puzzle = "\uEA86";
    public const string Document = "\uE8A5";
    public const string Diagnostic = "\uE9D9";
    public const string Keyboard = "\uE765";
    public const string Sliders = "\uE9E9";
    public const string Grid = "\uE80A";
    public const string Mouse = "\uE962";
    public const string Windows = "\uE737";
    public const string Gauge = "\uEC4A";
    public const string List = "\uE8FD";
    public const string Wheel = "\uEC8F";
    public const string Touchpad = "\uEFA5";
    public const string Drag = "\uE7C2";
    public const string Hourglass = "\uE916";
    public const string Heart = "\uEB51";
    public const string Download = "\uE896";
    public const string Circle = "\uEA3A";
    public const string CircleFilled = "\uEA3B";
    public const string DashedCircle = "\uF16B";
    public const string CircleCheck = "\uEC61";
    public const string Shield = "\uEA18";
    public const string RadioOn = "\uECCB";
    public const string RadioOff = "\uECCA";
    public const string Folder = "\uE838";
}
