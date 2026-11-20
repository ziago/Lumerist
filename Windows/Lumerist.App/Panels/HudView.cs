using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using Lumerist.App.Design;
using Lumerist.App.Services;
using Lumerist.Core;

namespace Lumerist.App.Panels;

/// <summary>What the sample does while it is being placed (the Mac's OverlayView.Placing).</summary>
/// <param name="OnDragStart">The read-out was pressed: start following the pointer.</param>
/// <param name="OnDone">The caption's Done button.</param>
internal sealed record HudPlacingHandlers(Action<UIElement> OnDragStart, Action OnDone);

/// <summary>
/// The Value HUD's look (the Mac's OverlayView and its read-outs): Ruler or
/// Compact, in Miami (neon frame and glow) or Galerist (charcoal, hairline,
/// red needle), inside a transparent margin for the glow.
/// </summary>
internal static class HudView
{
    /// <summary>Height of the strip with the Done button under a sample being placed.</summary>
    public const double PlacingCaptionHeight = 42;

    public static double ReadoutWidth(OverlaySettings.HudStyle style) => style == OverlaySettings.HudStyle.Compact ? 240 : 290;

    /// <summary>The whole HUD: the read-out in its glow margin, and the caption while it is placed.</summary>
    public static FrameworkElement Build(HudReadout content, OverlaySettings.HudStyle style, double backgroundOpacity,
        HudPlacingHandlers? placing = null)
    {
        var opacity = Math.Max(backgroundOpacity, 0.15);
        FrameworkElement readout = (style, PanelLook.IsFlat) switch
        {
            (OverlaySettings.HudStyle.Compact, true) => FlatCompact(content, opacity),
            (OverlaySettings.HudStyle.Compact, false) => NeonCompact(content, opacity),
            (_, true) => FlatRuler(content, opacity),
            _ => NeonRuler(content, opacity),
        };
        // Transparent room for the glow (Miami); same spot in every theme.
        var padded = new Grid { Margin = new Thickness(HudPlacement.GlowMargin) };
        padded.Children.Add(readout);
        var area = new Grid();
        area.Children.Add(padded);
        var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        stack.Children.Add(area);
        if (placing is not null)
        {
            area.Children.Add(new Rectangle
            {
                Stroke = PanelLook.Brush(PanelLook.Palette.Magenta),
                StrokeThickness = 1.5,
                StrokeDashArray = [5 / 1.5, 4 / 1.5],
                RadiusX = PanelLook.ContainerRadius,
                RadiusY = PanelLook.ContainerRadius,
                Margin = new Thickness(7),
                IsHitTestVisible = false,
            });
            // The drag area: the whole padded read-out follows the pointer.
            var drag = new Border { Background = Brushes.Transparent, Cursor = System.Windows.Input.Cursors.SizeAll };
            AutomationProperties.SetName(drag, "Sample HUD: drag it where you want the HUD");
            drag.MouseLeftButtonDown += (_, e) =>
            {
                e.Handled = true;
                placing.OnDragStart(drag);
            };
            area.Children.Add(drag);
            var caption = PlacingCaption(ReadoutWidth(style), placing.OnDone);
            caption.Height = PlacingCaptionHeight - 10;
            caption.Margin = new Thickness(0, 0, 0, 10);
            caption.HorizontalAlignment = HorizontalAlignment.Center;
            stack.Children.Add(caption);
        }
        return stack;
    }

    // Colours -------------------------------------------------------------------

    private static uint FrameColor(HudReadout content) => content.Accent switch
    {
        HudAccent.Teal => PanelLook.Palette.Teal,
        HudAccent.Magenta => PanelLook.Palette.Magenta,
        HudAccent.Neutral => PanelLook.Palette.Steel,
        _ => PanelLook.Palette.Error,
    };

    private static uint NameColor(HudReadout content) => content.Accent switch
    {
        HudAccent.Teal => PanelLook.Palette.Teal,
        HudAccent.Magenta => PanelLook.Palette.MagentaLight,
        HudAccent.Neutral => PanelLook.Palette.Ice,
        _ => PanelLook.Palette.Error,
    };

    /// <summary>The cursor uses the opposite neon so it stands out from the frame.</summary>
    private static uint CursorColor(HudReadout content) =>
        content.Accent == HudAccent.Magenta ? PanelLook.Palette.Teal : PanelLook.Palette.Magenta;

    private static uint ValueAccent(HudReadout content) =>
        content.Accent == HudAccent.Magenta ? PanelLook.Palette.MagentaLight : PanelLook.Palette.Teal;

    /// <summary>Galerist: white names; a rejection keeps the error colour.</summary>
    private static uint FlatNameColor(HudReadout content) =>
        content.Accent == HudAccent.Error ? PanelLook.Palette.Error : PanelLook.Palette.Ice;

    private static string? NoticeSymbol(HudReadout content) => content.NoticeIcon switch
    {
        HudNoticeIcon.Info => "info.circle",
        HudNoticeIcon.Checkmark => "checkmark",
        HudNoticeIcon.Cross => "xmark",
        _ => null,
    };

    // Miami ---------------------------------------------------------------------

    /// <summary>Abyss fill at the chosen opacity, a neon outline and its glow.</summary>
    private static FrameworkElement NeonFrame(HudReadout content, double opacity, FrameworkElement body, double width)
    {
        var frame = FrameColor(content);
        var grid = new Grid { Width = width };
        grid.Children.Add(new Border
        {
            Background = PanelLook.Brush(PanelLook.Palette.Abyss, opacity),
            BorderBrush = PanelLook.Brush(frame),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(PanelLook.ControlRadius),
            Effect = new DropShadowEffect { Color = PanelLook.Color(frame), Opacity = 0.45, BlurRadius = 22, ShadowDepth = 0 },
        });
        grid.Children.Add(body);
        return grid;
    }

    /// <summary>The value in bold mono with a soft glow, and its unit in the accent (the Mac's ValueReadout).</summary>
    private static BaselineRow ValueReadout(string value, string? unit, double size, uint accent)
    {
        var number = new TrackedText(value, Theme.MonoFont, size, FontWeights.Bold, Brushes.White)
        {
            Effect = new DropShadowEffect { Color = PanelLook.Color(accent), Opacity = 0.5, BlurRadius = 16, ShadowDepth = 0 },
        };
        var row = new BaselineRow(number) { Spacing = 4 };
        if (!string.IsNullOrEmpty(unit))
            row.Children.Add(new TrackedText(unit, Theme.MonoFont, size * 0.44, FontWeights.SemiBold, PanelLook.Brush(accent)));
        AutomationProperties.SetName(row, unit is null ? value : $"{value} {unit}");
        return row;
    }

    private static FrameworkElement NeonRuler(HudReadout content, double opacity)
    {
        const double width = 290, horizontal = 14;
        var body = new StackPanel { Margin = new Thickness(horizontal, 12, horizontal, 12) };
        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var name = PanelLook.Brush(NameColor(content));
        if (NoticeSymbol(content) is { } symbol)
        {
            var icon = PanelLook.Icon(symbol, 10, name);
            icon.Margin = new Thickness(0, 0, 8, 0);
            header.Children.Add(icon);
        }
        var title = PanelLook.Label(content.DisplayTitle, 11, name);
        title.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(title, 1);
        header.Children.Add(title);
        if (content.KeyLabel is { } key)
        {
            var keyText = PanelLook.Label($"[{key}] {content.StepLabel ?? ""}", 11);
            keyText.Margin = new Thickness(24, 0, 0, 0);
            keyText.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(keyText, 2);
            header.Children.Add(keyText);
        }
        body.Children.Add(header);
        if (content.ValueText is { } value)
        {
            var readout = ValueReadout(value, content.Unit, 34, ValueAccent(content));
            readout.Margin = new Thickness(0, 8, 0, 0);
            body.Children.Add(readout);
        }
        if (content.Detail is { } detail)
        {
            var text = PanelLook.Body(detail, 12, PanelLook.Brush(content.Accent == HudAccent.Error ? PanelLook.Palette.Error : PanelLook.Palette.Body),
                maxLines: 2);
            text.Margin = new Thickness(0, 8, 0, 0);
            body.Children.Add(text);
        }
        if (content.Bar is { } bar)
        {
            var scale = ScaleMarks(bar, CursorColor(content), width - 2 * horizontal, tickHeight: 16, labelSize: 11, thinBar: false);
            scale.Height = 34;
            scale.Margin = new Thickness(0, 8, 0, 0);
            body.Children.Add(scale);
            if (content.RangeText is { } range && !bar.HasLabels)
                body.Children.Add(RangeRow(range, 10, new Thickness(0, 8, 0, 0)));
        }
        return NeonFrame(content, opacity, body, width);
    }

    private static FrameworkElement NeonCompact(HudReadout content, double opacity)
    {
        const double width = 240, horizontal = 12;
        var body = new StackPanel { Margin = new Thickness(horizontal, 9, horizontal, 9) };
        var header = new BaselineRow(PanelLook.Label(content.DisplayTitle, 11, PanelLook.Brush(NameColor(content))))
        {
            Spacing = 10,
            MinimumGap = 28,
            TrailingFrom = 1,
        };
        if (content.ValueText is { } value)
            header.Children.Add(ValueReadout(value, content.Unit, 18, ValueAccent(content)));
        else if (content.Detail is { } detail)
            header.Children.Add(PanelLook.BodyLine(detail, 12, PanelLook.Brush(PanelLook.Palette.Body)));
        body.Children.Add(header);
        if (content.Bar is { } bar)
        {
            var scale = ScaleMarks(bar, CursorColor(content), width - 2 * horizontal, tickHeight: 8, labelSize: 9, thinBar: true);
            scale.Height = 22;
            scale.Margin = new Thickness(0, 6, 0, 0);
            body.Children.Add(scale);
        }
        return NeonFrame(content, opacity, body, width);
    }

    /// <summary>The ends of the range under a scale that has no numbers of its own.</summary>
    private static Grid RangeRow((string Low, string High) range, double size, Thickness margin)
    {
        var row = new Grid { Margin = margin };
        var steel = PanelLook.Brush(PanelLook.Palette.Steel);
        row.Children.Add(PanelLook.Mono(range.Low, size, FontWeights.Medium, steel));
        var high = PanelLook.Mono(range.High, size, FontWeights.Medium, steel);
        high.HorizontalAlignment = HorizontalAlignment.Right;
        row.Children.Add(high);
        return row;
    }

    /// <summary>
    /// Scale marks (the Mac's ScaleMarks, and ThinBar for Compact): small
    /// ticks, big ticks with numbers, a bright zero, a glowing cursor; Compact
    /// adds a slim track filled from zero to the value.
    /// </summary>
    private static Canvas ScaleMarks(HudBar bar, uint cursor, double totalWidth, double tickHeight, double labelSize, bool thinBar)
    {
        const double inset = 10;
        var width = totalWidth - inset * 2;
        var canvas = new Canvas { Width = totalWidth, ClipToBounds = false, IsHitTestVisible = false };
        canvas.Children.Add(new NeonMarks(bar, width, tickHeight, labelSize) { Width = totalWidth, Height = tickHeight });
        var cursorBrush = PanelLook.Brush(cursor);
        if (thinBar)
        {
            var origin = bar.Zero ?? 0;
            var start = Math.Min(origin, bar.Fraction) * width;
            var end = Math.Max(origin, bar.Fraction) * width;
            var track = new Rectangle { Width = width, Height = 2, Fill = PanelLook.Brush(PanelLook.Palette.Steel, 0.3) };
            Canvas.SetLeft(track, inset);
            Canvas.SetTop(track, 8);
            canvas.Children.Add(track);
            var fill = new Rectangle
            {
                Width = Math.Max(end - start, 2),
                Height = 2,
                Fill = cursorBrush,
                Effect = new DropShadowEffect { Color = PanelLook.Color(cursor), BlurRadius = 6, ShadowDepth = 0, Opacity = 1 },
            };
            Canvas.SetLeft(fill, inset + Math.Min(start, width - 2));
            Canvas.SetTop(fill, 8);
            canvas.Children.Add(fill);
        }
        var needle = new Rectangle
        {
            Width = 3,
            Height = tickHeight + 8,
            Fill = cursorBrush,
            Effect = new DropShadowEffect { Color = PanelLook.Color(cursor), BlurRadius = 10, ShadowDepth = 0, Opacity = 1 },
        };
        Canvas.SetLeft(needle, inset + Math.Min(Math.Max(bar.Fraction * width - 1.5, 0), width - 3));
        Canvas.SetTop(needle, -4);
        canvas.Children.Add(needle);
        return canvas;
    }

    /// <summary>Miami's ticks, zero and numbers, drawn in one pass.</summary>
    private sealed class NeonMarks(HudBar bar, double width, double tickHeight, double labelSize) : FrameworkElement
    {
        private const double Inset = 10;

        protected override void OnRender(DrawingContext context)
        {
            var palette = PanelLook.Palette;
            var major = PanelLook.Brush(palette.Ice, 0.9);
            var minor = PanelLook.Brush(palette.Steel, 0.6);
            foreach (var tick in bar.Ticks)
            {
                var x = Inset + Round(tick.Fraction * width) + 0.5;
                var height = tick.IsMajor ? tickHeight : tickHeight * 0.45;
                var lineWidth = tick.IsMajor ? 2.0 : 1.0;
                context.DrawRectangle(tick.IsMajor ? major : minor, null,
                    new Rect(x - lineWidth / 2, tickHeight - height, lineWidth, height));
            }
            if (bar.Zero is { } zero)
            {
                var x = Inset + Round(zero * width);
                context.DrawRectangle(PanelLook.Brush(palette.Ice), null, new Rect(x - 1.5, -3, 3, tickHeight + 3));
            }
            var dip = (float)VisualTreeHelper.GetDpi(this).PixelsPerDip;
            foreach (var tick in bar.Ticks)
            {
                if (tick.Label is not { } label) continue;
                var text = new FormattedText(label, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    new Typeface(Theme.MonoFont, FontStyles.Normal, FontWeights.Bold, FontStretches.Normal), labelSize,
                    PanelLook.Brush(label == "0" ? palette.Ice : palette.Body), dip);
                // Centred on the mark, under the ticks.
                var centre = new Point(Inset + tick.Fraction * width, tickHeight + labelSize * 0.75 + 2);
                context.DrawText(text, new Point(centre.X - text.Width / 2, centre.Y - text.Height / 2));
            }
        }
    }

    // Galerist ------------------------------------------------------------------

    /// <summary>The darkest charcoal at the chosen opacity, a 1 px hairline, no glow.</summary>
    private static FrameworkElement FlatFrame(HudReadout content, double opacity, FrameworkElement body, double width)
    {
        var grid = new Grid { Width = width, SnapsToDevicePixels = true };
        grid.Children.Add(new Border { Background = PanelLook.Brush(PanelLook.Palette.Abyss, opacity) });
        grid.Children.Add(body);
        grid.Children.Add(new Border
        {
            BorderBrush = PanelLook.Brush(content.Accent == HudAccent.Error ? PanelLook.Palette.Error : PanelLook.Palette.GridLine),
            BorderThickness = new Thickness(1),
            IsHitTestVisible = false,
        });
        return grid;
    }

    /// <summary>The value in Outfit semibold with tabular figures; the unit in mono small caps.</summary>
    private static BaselineRow FlatValue(string value, string? unit, double size)
    {
        var row = new BaselineRow(new TrackedText(value, Theme.TextFont, size, FontWeights.SemiBold, PanelLook.Brush(PanelLook.Palette.Ice),
            -0.02 * size, tabular: true))
        {
            Spacing = size * 0.16,
        };
        if (!string.IsNullOrEmpty(unit)) row.Children.Add(PanelLook.Caps(unit, Math.Max(10, size * 0.3), tracking: 0.1));
        AutomationProperties.SetName(row, unit is null ? value : $"{value} {unit}");
        return row;
    }

    private static FrameworkElement FlatRuler(HudReadout content, double opacity)
    {
        const double width = 290, horizontal = 16;
        var body = new StackPanel { Margin = new Thickness(horizontal, 13, horizontal, 10) };
        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var name = PanelLook.Brush(FlatNameColor(content));
        if (NoticeSymbol(content) is { } symbol)
        {
            var icon = PanelLook.Icon(symbol, 11, name);
            icon.Margin = new Thickness(0, 0, 8, 0);
            header.Children.Add(icon);
        }
        var title = PanelLook.Caps(content.DisplayTitle, 11, name, FontWeights.SemiBold);
        title.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(title, 1);
        header.Children.Add(title);
        if (content.KeyLabel is { } key)
        {
            var keyText = PanelLook.Caps($"[{key}] {content.StepLabel ?? ""}", 10);
            keyText.Margin = new Thickness(24, 0, 0, 0);
            keyText.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(keyText, 2);
            header.Children.Add(keyText);
        }
        body.Children.Add(header);
        if (content.ValueText is { } value)
        {
            var readout = FlatValue(value, content.Unit, 38);
            readout.Margin = new Thickness(0, 6, 0, 0);
            body.Children.Add(readout);
        }
        if (content.Detail is { } detail)
        {
            var text = PanelLook.Body(detail, 12, PanelLook.Brush(content.Accent == HudAccent.Error ? PanelLook.Palette.Error : PanelLook.Palette.Body),
                maxLines: 2);
            text.Margin = new Thickness(0, 6, 0, 0);
            body.Children.Add(text);
        }
        if (content.Bar is { } bar)
        {
            var scale = new FlatScale(bar, ruler: true) { Height = 32, Margin = new Thickness(0, 6 + 4, 0, 0) };
            body.Children.Add(scale);
            if (content.RangeText is { } range && !bar.HasLabels)
                body.Children.Add(RangeRow(range, 9, new Thickness(0, 6, 0, 0)));
        }
        return FlatFrame(content, opacity, body, width);
    }

    private static FrameworkElement FlatCompact(HudReadout content, double opacity)
    {
        const double width = 240, horizontal = 12;
        var body = new StackPanel { Margin = new Thickness(horizontal, 9, horizontal, 8) };
        var header = new BaselineRow(PanelLook.Caps(content.DisplayTitle, 10, PanelLook.Brush(FlatNameColor(content)), FontWeights.SemiBold))
        {
            Spacing = 10,
            MinimumGap = 28,
            TrailingFrom = 1,
        };
        if (content.ValueText is { } value)
            header.Children.Add(FlatValue(value, content.Unit, 20));
        else if (content.Detail is { } detail)
            header.Children.Add(PanelLook.BodyLine(detail, 12, PanelLook.Brush(PanelLook.Palette.Body)));
        body.Children.Add(header);
        if (content.Bar is { } bar)
            body.Children.Add(new FlatScale(bar, ruler: false) { Height = 20, Margin = new Thickness(0, 7, 0, 0) });
        return FlatFrame(content, opacity, body, width);
    }

    /// <summary>
    /// The Galerist scale. Ruler: ticks hang from a hairline, the needle is a
    /// 2 px red line. Compact: a 2 px track filled in red from zero (or the
    /// start) to the value, small ticks below. Numbers in mono either way.
    /// </summary>
    private sealed class FlatScale(HudBar bar, bool ruler) : FrameworkElement
    {
        private const double Inset = 10;

        private double ZeroHeight => ruler ? 13 : 7;
        private double MajorHeight => ruler ? 8 : 5;
        private double MinorHeight => ruler ? 4 : 3;
        private double LabelSize => ruler ? 9 : 8;
        /// <summary>Where the ticks start: under the hairline (ruler) or the track (compact).</summary>
        private double TickTop => ruler ? 1 : 3;

        protected override void OnRender(DrawingContext context)
        {
            var palette = PanelLook.Palette;
            var width = Math.Floor(ActualWidth - Inset * 2);
            double X(double fraction) => Inset + Round(fraction * width);
            var live = PanelLook.Brush(palette.Live);
            if (ruler)
            {
                context.DrawRectangle(PanelLook.Brush(palette.Steel, 0.3), null, new Rect(Inset, 0, width + 1, 1));
            }
            else
            {
                context.DrawRectangle(PanelLook.Brush(palette.Steel, 0.35), null, new Rect(Inset, 0, width + 1, 2));
                var origin = X(bar.Zero ?? 0);
                var end = X(bar.Fraction);
                context.DrawRectangle(live, null, new Rect(Math.Min(origin, end), 0, Math.Max(Math.Abs(end - origin), 2), 2));
            }
            var steel = PanelLook.Brush(palette.Steel);
            var disabled = PanelLook.Brush(palette.Disabled);
            foreach (var tick in bar.Ticks)
            {
                context.DrawRectangle(tick.IsMajor ? steel : disabled, null,
                    new Rect(X(tick.Fraction), TickTop, 1, tick.IsMajor ? MajorHeight : MinorHeight));
            }
            var ice = PanelLook.Brush(palette.Ice);
            if (bar.Zero is { } zero) context.DrawRectangle(ice, null, new Rect(X(zero), TickTop, 1, ZeroHeight));
            var dip = (float)VisualTreeHelper.GetDpi(this).PixelsPerDip;
            var typeface = new Typeface(Theme.MonoFont, FontStyles.Normal, FontWeights.Medium, FontStretches.Normal);
            foreach (var tick in bar.Ticks)
            {
                if (tick.Label is not { } label) continue;
                var text = new FormattedText(label, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, LabelSize,
                    label == "0" ? ice : steel, dip);
                var centre = new Point(X(tick.Fraction) + 0.5, TickTop + ZeroHeight + 3 + LabelSize * 0.6);
                context.DrawText(text, new Point(centre.X - text.Width / 2, centre.Y - text.Height / 2));
            }
            if (ruler)
            {
                // The needle: what is live.
                var x = Math.Min(Math.Max(X(bar.Fraction) - 0.5, Inset - 0.5), Inset + width - 1.5);
                context.DrawRectangle(live, null, new Rect(x, 0, 2, ZeroHeight + 3));
            }
        }
    }

    // Placing -------------------------------------------------------------------

    /// <summary>Under the sample being placed: what to do, and the way out.</summary>
    private static FrameworkElement PlacingCaption(double width, Action onDone)
    {
        var palette = PanelLook.Palette;
        var row = new Grid { Margin = new Thickness(10, 0, 0, 0) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var icon = PanelLook.Icon("arrow.up.and.down.and.arrow.left.and.right", 10, PanelLook.Brush(palette.MagentaLight));
        icon.Margin = new Thickness(0, 0, 8, 0);
        row.Children.Add(icon);
        var text = PanelLook.Label("Drag the HUD", 10, PanelLook.Brush(palette.Ice));
        text.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(text, 1);
        row.Children.Add(text);
        var done = PrimaryButton("Done", onDone);
        done.ToolTip = "Keeps the HUD where it is now";
        AutomationProperties.SetHelpText(done, "Keeps the HUD where it is now");
        Grid.SetColumn(done, 2);
        row.Children.Add(done);
        return new Border
        {
            Width = width,
            Background = PanelLook.Brush(palette.Abyss, 0.94),
            BorderBrush = PanelLook.Brush(palette.Magenta),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(PanelLook.ControlRadius),
            Child = row,
        };
    }

    /// <summary>The Mac's neonPrimary button: the main action, filled.</summary>
    private static PanelButton PrimaryButton(string title, Action action)
    {
        var palette = PanelLook.Palette;
        var label = PanelLook.IsFlat
            ? new TrackedText(title.ToUpperInvariant(), Theme.MonoFont, 11, FontWeights.Medium, PanelLook.Brush(palette.OnMagenta), 1.6)
            : new TrackedText(title.ToUpperInvariant(), Theme.DisplayFont, 12, FontWeights.Bold, PanelLook.Brush(palette.OnMagenta), 1);
        label.HorizontalAlignment = HorizontalAlignment.Center;
        label.VerticalAlignment = VerticalAlignment.Center;
        var face = new Border
        {
            Background = PanelLook.Brush(palette.Magenta),
            CornerRadius = new CornerRadius(PanelLook.ControlRadius),
            Padding = new Thickness(14, 0, 14, 0),
            MinHeight = PanelLook.IsFlat ? 30 : 32,
            Child = label,
        };
        if (!PanelLook.IsFlat)
            face.Effect = new DropShadowEffect { Color = PanelLook.Color(palette.Magenta), Opacity = 0.55, BlurRadius = 18, ShadowDepth = 0 };
        return new PanelButton(face, action, label: title) { VerticalAlignment = VerticalAlignment.Center };
    }

    /// <summary>Swift's <c>rounded()</c>: halves away from zero.</summary>
    private static double Round(double value) => Math.Round(value, MidpointRounding.AwayFromZero);
}
