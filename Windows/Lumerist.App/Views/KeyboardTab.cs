using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Lumerist.App.Design;
using Lumerist.App.Design.Controls;
using Lumerist.Core;

namespace Lumerist.App.Views;

/// <summary>
/// A drawing of the keyboard for one module (KeyboardTab.swift): every key
/// coloured by what it does here (Dial Key, Nudge − / +, Command Key, System
/// Key), with the combos on a second layer. Below it: conflicts, keys that
/// are not on the drawing, and the Clear… menu. Key names follow the user's
/// keyboard layout.
/// </summary>
internal static class KeyboardTab
{
    public static FrameworkElement Build(ModulePage page)
    {
        var context = page.Context;
        var controller = context.Controller;
        var keymap = controller.Keymap;
        var scope = page.Scope;
        var state = page.State;
        var all = KeyAssignment.All(keymap, scope);
        var conflicts = new ConflictIndex(keymap, scope);
        var spots = KeySpot.Spots(KeySpot.ShapeFor(LayoutLanguage()));
        var drawn = spots.Select(spot => spot.ScanCode).OfType<int>().ToHashSet();
        var comboKey = keymap.SystemKeys.ComboKey;
        var moduleName = scope == ParameterScope.Develop ? "Develop" : "Library";

        var layers = SynthControls.Segmented([KeyboardLayer.Plain, KeyboardLayer.Combos], state.Layer,
            layer => layer == KeyboardLayer.Plain ? "Plain keys" : comboKey is { } combo ? $"Combos (hold {combo.DisplayText})" : "Combos",
            layer =>
            {
                state.Layer = layer;
                page.Refresh();
            }, name: "Layer");

        var onLayer = all.Where(item => item.Chord.IsCombo == (state.Layer == KeyboardLayer.Combos))
            .GroupBy(item => item.Chord.ScanCode).ToDictionary(group => group.Key, group => KeyAssignment.Sorted(group));
        var drawing = new KeyboardDrawing();
        foreach (var spot in spots)
        {
            var assignments = spot.ScanCode is { } code && onLayer.TryGetValue(code, out var list) ? list : [];
            var conflict = assignments.Select(item => conflicts[item.Usage]).FirstOrDefault(message => message is not null);
            var isHeld = spot.ScanCode is not null && state.Layer == KeyboardLayer.Combos && spot.ScanCode == comboKey?.ScanCode;
            var face = KeyFace.Create(spot, assignments, conflict, isHeld, comboKey, page.Reveal);
            KeyboardDrawing.SetSpot(face, spot);
            drawing.Children.Add(face);
        }
        AutomationProperties.SetName(drawing, state.Layer == KeyboardLayer.Plain ? "Keyboard, plain keys" : "Keyboard, combos");

        FrameworkElement card = drawing;
        if (Theme.IsFlat)
        {
            // Galerist: the keyboard sits in a white card (charcoal in dark) on the grey page.
            card = new Border
            {
                Padding = new Thickness(Theme.Space.XL),
                Background = Ink.Panel,
                BorderBrush = Ink.GridLine,
                BorderThickness = new Thickness(1),
                Child = drawing,
            };
        }

        var stack = Layout.VStack(Theme.Space.L,
            Layout.Bar(Theme.Space.M, [Tooltips.Explained(layers, KeyTypeHelp.Combos(comboKey))], [Legend(scope)]),
            card);
        if (state.Layer == KeyboardLayer.Combos && comboKey is null)
            stack.Children.Add(SynthText.Body("There is no Combo Key, so combos are off. Set one to use them.", 12, color: Ink.Amber));
        var hint = SynthText.Body("Click a coloured key to open its slider or command.", 12, color: Ink.Steel);
        stack.Children.Add(Layout.Bar(Theme.Space.M, [hint], [ClearMenu(page, keymap, moduleName)]));
        if (ConflictList(keymap, scope) is { } conflictList) stack.Children.Add(conflictList);
        if (OffDrawingList(all.Where(item => !drawn.Contains(item.Chord.ScanCode)).ToList(), keymap, conflicts) is { } others)
            stack.Children.Add(others);
        return stack;
    }

    /// <summary>The language of Lightroom's keyboard layout (ANSI or ISO drawing).</summary>
    private static int LayoutLanguage()
    {
        try
        {
            return (int)(Input.KeyboardLayout.OfLightroom() & 0xFFFF);
        }
        catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException)
        {
            return 0x0409;
        }
    }

    private static FrameworkElement ClearMenu(ModulePage page, Keymap keymap, string moduleName)
    {
        var controller = page.Context.Controller;
        var targets = ClearTargets.Available(keymap, page.Scope);
        var menu = SynthControls.MenuButton<ClearTarget?>("Clear…", $"Clear keys in {moduleName}", targets.Cast<ClearTarget?>().ToList(), null,
            target => target?.Title() ?? "", target =>
            {
                if (target is not { } chosen) return;
                if (!SynthDialog.Confirm(page.View, chosen.Question(page.Scope), "Only those keys go: steps and Command Keys stay as they are.",
                        "Clear", isDestructive: true)) return;
                KeymapEditor.Edit(controller, edited =>
                {
                    if (chosen == ClearTarget.Dial) edited.ClearDialKeys(page.Scope);
                    else edited.ClearNudgeKeys(page.Scope);
                });
            });
        menu.IsEnabled = targets.Count > 0;
        return menu;
    }

    private static FrameworkElement? ConflictList(Keymap keymap, ParameterScope scope)
    {
        var conflicts = keymap.Conflicts().Where(conflict => conflict.Scope == scope || conflict.Scope is null).ToList();
        if (conflicts.Count == 0) return null;
        var rows = Layout.VStack(0);
        foreach (var conflict in conflicts)
        {
            var isError = conflict.Severity == ConflictSeverity.Error;
            var icon = SynthText.Icon(isError ? Icons.Error : Icons.Warning, 12, isError ? Ink.Error : Ink.Amber);
            icon.VerticalAlignment = VerticalAlignment.Top;
            icon.Margin = new Thickness(0, 2, 0, 0);
            AutomationProperties.SetName(icon, isError ? "Error" : "Warning");
            var text = Layout.VStack(2,
                SynthText.Body(conflict.Message, 13, FontWeights.Medium, Ink.Ice),
                SynthText.Body("Involves " + string.Join(", ", conflict.Usages.Select(usage => usage.Label())), 12, color: Ink.Steel));
            var row = Layout.Bar(Theme.Space.S, [icon, text], []);
            row.ColumnDefinitions[1].Width = new GridLength(1, GridUnitType.Star);
            row.ColumnDefinitions[2].Width = new GridLength(0);
            rows.Children.Add(ListRow(row));
        }
        return Section("Conflicts", rows);
    }

    private static FrameworkElement? OffDrawingList(IReadOnlyList<KeyAssignment> assignments, Keymap keymap, ConflictIndex conflicts)
    {
        if (assignments.Count == 0) return null;
        var rows = Layout.VStack(0);
        foreach (var assignment in assignments)
        {
            var cap = KeyCap.Create(keymap.DisplayText(assignment.Chord),
                conflicts[assignment.Usage] is null ? KeyCapState.Assigned : KeyCapState.Conflict, 54);
            var kind = SynthText.Label(KeyAssignment.Title(assignment.Type), 9, Theme.IsFlat ? Ink.Steel : KeyFace.TextColor(assignment.Type));
            var row = ListRow(Layout.Bar(Theme.Space.M, [cap, SynthText.Body(assignment.Name, 13, FontWeights.Medium, Ink.Ice)], [kind]));
            row.ToolTip = assignment.Description(keymap);
            AutomationProperties.SetName(row, assignment.Description(keymap));
            rows.Children.Add(row);
        }
        return Section("Other keys", rows);
    }

    private static FrameworkElement Section(string title, UIElement content)
    {
        var heading = SynthText.Title(title, 15);
        AutomationProperties.SetHeadingLevel(heading, AutomationHeadingLevel.Level2);
        return Layout.VStack(Theme.Space.S, heading, Surfaces.Panel(content));
    }

    private static Border ListRow(UIElement content) => new()
    {
        Child = content,
        Padding = new Thickness(14, 6, 14, 6),
        MinHeight = 38,
        BorderBrush = Ink.Faded(Ink.GridLine, 0.6),
        BorderThickness = new Thickness(0, 0, 0, 1),
    };

    /// <summary>Colour key for the drawing; each kind is explained on hover.</summary>
    private static FrameworkElement Legend(ParameterScope scope)
    {
        FrameworkElement Item(UIElement swatch, string title) =>
            Tooltips.Explained(Layout.HStack(Theme.IsFlat ? 6 : 4, swatch, SynthText.Label(title, 9)), title switch
            {
                "Dial Key" => KeyTypeHelp.Dial(scope),
                "Nudge − / +" => KeyTypeHelp.NudgeKeys(scope),
                "Command Key" => KeyTypeHelp.CommandKey,
                "System key" => KeyTypeHelp.SystemKey,
                _ => KeyTypeHelp.Conflict,
            });

        if (Theme.IsFlat)
        {
            // Galerist: the same fills and borders as the keys.
            return Layout.HStack(Theme.Space.M,
                Item(new Border { Width = 12, Height = 12, Background = Ink.Teal }, "Dial Key"),
                Item(new Border { Width = 12, Height = 12, BorderBrush = Ink.Teal, BorderThickness = new Thickness(2) }, "Nudge − / +"),
                Item(new Border { Width = 12, Height = 12, Background = Ink.Tint, BorderBrush = Ink.Faded(Ink.Magenta, 0.45), BorderThickness = new Thickness(1) },
                    "Command Key"),
                Item(new Border { Width = 12, Height = 12, Background = Ink.Panel, BorderBrush = Ink.Ice, BorderThickness = new Thickness(1) }, "System key"),
                Item(new Grid { Width = 12, Height = 12, Children = { new Ellipse { Width = 7, Height = 7, Fill = Ink.Live } } }, "Conflict"));
        }
        static FrameworkElement Swatch(SolidColorBrush color) => new Border
        {
            Width = 12,
            Height = 12,
            CornerRadius = new CornerRadius(2),
            Background = Ink.Faded(color, 0.18),
            BorderBrush = color,
            BorderThickness = new Thickness(1),
        };
        return Layout.HStack(Theme.Space.M,
            Item(Swatch(KeyFace.Color(KeyAssignment.Kind.Dial)), "Dial Key"),
            Item(Swatch(KeyFace.Color(KeyAssignment.Kind.NudgeUp)), "Nudge − / +"),
            Item(Swatch(KeyFace.Color(KeyAssignment.Kind.Command)), "Command Key"),
            Item(Swatch(KeyFace.Color(KeyAssignment.Kind.System)), "System key"),
            Item(SynthText.Icon(Icons.Warning, 9, Ink.Error), "Conflict"));
    }
}

/// <summary>
/// Lays out key faces on the 15 × 5 key-unit drawing, scaled to the width
/// it gets (at most 960 px), each inset by 2 px.
/// </summary>
internal sealed class KeyboardDrawing : Panel
{
    private const double MaximumWidth = 960;

    public static readonly DependencyProperty SpotProperty = DependencyProperty.RegisterAttached(
        "Spot", typeof(KeySpot), typeof(KeyboardDrawing), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsParentArrange));

    public static void SetSpot(UIElement element, KeySpot spot) => element.SetValue(SpotProperty, spot);

    public static KeySpot? GetSpot(UIElement element) => (KeySpot?)element.GetValue(SpotProperty);

    private static double DrawingWidth(Size available) =>
        double.IsFinite(available.Width) ? Math.Min(MaximumWidth, available.Width) : MaximumWidth;

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = DrawingWidth(availableSize);
        var unit = width / KeySpot.TotalWidth;
        foreach (UIElement child in InternalChildren)
        {
            if (GetSpot(child) is not { } spot) continue;
            child.Measure(new Size(Math.Max(0, spot.Width * unit - 4), Math.Max(0, spot.Height * unit - 4)));
        }
        return new Size(width, unit * KeySpot.TotalHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var width = Math.Min(MaximumWidth, finalSize.Width);
        var unit = width / KeySpot.TotalWidth;
        var left = Math.Max(0, (finalSize.Width - width) / 2);
        foreach (UIElement child in InternalChildren)
        {
            if (GetSpot(child) is not { } spot) continue;
            child.Arrange(new Rect(left + spot.X * unit + 2, spot.Y * unit + 2,
                Math.Max(0, spot.Width * unit - 4), Math.Max(0, spot.Height * unit - 4)));
        }
        return finalSize;
    }
}

/// <summary>One key on the drawing: its printed name, what it does, and where a click leads.</summary>
internal static class KeyFace
{
    /// <summary>Miami: outline and fill of each kind.</summary>
    public static SolidColorBrush Color(KeyAssignment.Kind kind) => kind switch
    {
        KeyAssignment.Kind.Dial => Ink.Teal,
        KeyAssignment.Kind.NudgeDown or KeyAssignment.Kind.NudgeUp => Ink.Magenta,
        KeyAssignment.Kind.Command => Ink.Success,
        _ => Ink.Amber,
    };

    /// <summary>Miami: text on the dark key (≥ 4.5:1 on panel colours).</summary>
    public static SolidColorBrush TextColor(KeyAssignment.Kind kind) => kind switch
    {
        KeyAssignment.Kind.Dial => Ink.TealLight,
        KeyAssignment.Kind.NudgeDown or KeyAssignment.Kind.NudgeUp => Ink.MagentaLight,
        KeyAssignment.Kind.Command => Ink.Success,
        _ => Ink.Amber,
    };

    private sealed record Style(Brush Fill, Brush Border, double BorderWidth, Brush Glyph, Brush Name);

    public static FrameworkElement Create(KeySpot spot, IReadOnlyList<KeyAssignment> assignments, string? conflict, bool isHeld,
        KeyChord? comboKey, Action<KeyboardReveal> reveal)
    {
        var primary = assignments.Count > 0 ? assignments[0] : null;
        var isInert = spot.ScanCode is null;
        var keyName = spot.Caption ?? (spot.ScanCode is { } code ? KeyNames.Name(code) : "");

        var style = Theme.IsFlat ? FlatStyle(primary, isInert, isHeld) : NeonStyle(primary, isInert, isHeld);
        var outline = KeyOutline(spot, style);
        var content = Content(spot, keyName, assignments, primary, conflict, isHeld, isInert, style);
        var face = new Grid { Background = Brushes.Transparent, ClipToBounds = false };
        face.Children.Add(outline);
        face.Children.Add(content);
        if (!Theme.IsFlat && (primary is not null || isHeld))
        {
            var glowColor = isHeld ? Ink.Amber.Color : Color(primary!.Type).Color;
            outline.Effect = SynthControls.Glow(glowColor, 5, 0.3);
        }

        var tooltip = Tooltip(keyName, assignments, conflict, isHeld, isInert, comboKey);
        FrameworkElement result = face;
        if (primary?.Reveal is { } target)
        {
            var button = SynthControls.Plain(face, () => reveal(target));
            AutomationProperties.SetHelpText(button, target.Hint);
            // Hover: Galerist lightens the fill, Miami brightens the glow.
            button.MouseEnter += (_, _) => outline.Opacity = 0.85;
            button.MouseLeave += (_, _) => outline.Opacity = 1;
            result = button;
        }
        if (tooltip.Length > 0) result.ToolTip = tooltip;
        AutomationProperties.SetName(result, isInert ? keyName : $"Key {keyName}");
        AutomationProperties.SetItemStatus(result, AccessibilityValue(assignments, conflict, isHeld, comboKey));
        if (isInert) result.IsHitTestVisible = false;
        return result;
    }

    /// <summary>
    /// Galerist key colours: Dial Key filled blue with the opposite text;
    /// Command Key a soft tint; System Key with a 1 px ink border; Nudge Keys
    /// with a 2 px blue border; plain keys white with a hairline; modifiers on
    /// the page grey.
    /// </summary>
    private static Style FlatStyle(KeyAssignment? primary, bool isInert, bool isHeld)
    {
        if (isHeld) return new Style(Ink.Magenta, Ink.Magenta, 1, Ink.OnMagenta, Ink.OnMagenta);
        if (isInert) return new Style(Ink.Abyss, Ink.GridLine, 1, Ink.Steel, Ink.Steel);
        return primary?.Type switch
        {
            KeyAssignment.Kind.Dial => new Style(Ink.Teal, Ink.Teal, 1, Ink.OnTeal, Ink.OnTeal),
            KeyAssignment.Kind.Command => new Style(Ink.Tint, Ink.Faded(Ink.Magenta, 0.45), 1, Ink.Ice, Ink.Ice),
            KeyAssignment.Kind.System => new Style(Ink.Panel, Ink.Ice, 1, Ink.Ice, Ink.Ice),
            KeyAssignment.Kind.NudgeDown or KeyAssignment.Kind.NudgeUp => new Style(Ink.Panel, Ink.Teal, 2, Ink.Teal, Ink.Ice),
            _ => new Style(Ink.Panel, Ink.Outline, 1, Ink.Steel, Ink.Steel),
        };
    }

    private static Style NeonStyle(KeyAssignment? primary, bool isInert, bool isHeld)
    {
        var color = isHeld ? Ink.Amber : primary is not null ? Color(primary.Type) : isInert ? Ink.GridLine : Ink.Outline;
        Brush fill = isHeld ? Ink.Faded(Ink.Amber, 0.22)
            : primary is not null ? Ink.Faded(Color(primary.Type), 0.11)
            : isInert ? Brushes.Transparent
            : Ink.Faded(Ink.Abyss, 0.55);
        Brush stroke = isInert ? Ink.Faded(color, 0.6) : color;
        var glyph = primary is null && !isHeld ? Ink.Steel : Ink.Ice;
        var name = isHeld ? Ink.Amber : primary is not null ? TextColor(primary.Type) : Ink.Steel;
        return new Style(fill, stroke, 1, isInert || (primary is null && !isHeld) ? Ink.Steel : glyph, name);
    }

    /// <summary>A rounded rectangle, or the ISO Enter's shape (narrower at the bottom on the left).</summary>
    private static Shape KeyOutline(KeySpot spot, Style style)
    {
        if (spot.IsIsoEnter)
        {
            return new IsoEnterOutline
            {
                Fill = style.Fill,
                Stroke = style.Border,
                StrokeThickness = style.BorderWidth,
            };
        }
        return new Rectangle
        {
            Fill = style.Fill,
            Stroke = style.Border,
            StrokeThickness = style.BorderWidth,
            RadiusX = Theme.IsFlat ? 0 : 4,
            RadiusY = Theme.IsFlat ? 0 : 4,
            SnapsToDevicePixels = true,
        };
    }

    private static FrameworkElement Content(KeySpot spot, string keyName, IReadOnlyList<KeyAssignment> assignments, KeyAssignment? primary,
        string? conflict, bool isHeld, bool isInert, Style style)
    {
        var mono = Theme.MonoFont;
        if (isInert || spot.IsCompact)
        {
            var label = new SpacedText
            {
                Text = keyName,
                FontFamily = mono,
                FontSize = spot.IsCompact ? 10 : 9,
                FontWeight = Theme.IsFlat ? FontWeights.Medium : FontWeights.SemiBold,
                Foreground = Theme.IsFlat || primary is null ? style.Glyph : TextColor(primary.Type),
                MinimumScale = 0.6,
                HorizontalAlignment = spot.IsCompact ? HorizontalAlignment.Center : HorizontalAlignment.Left,
                VerticalAlignment = spot.IsCompact ? VerticalAlignment.Center : VerticalAlignment.Bottom,
                Margin = new Thickness(spot.IsCompact ? 0 : Theme.IsFlat ? 7 : 6),
            };
            return label;
        }

        var grid = new Grid { Margin = Theme.IsFlat ? new Thickness(6, 5, 6, 5) : new Thickness(5, 4, 5, 4) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // The key's size decides the type size, as on the Mac (unit = key width).
        var name = new ScaledKeyName
        {
            Text = keyName,
            IsCaption = spot.Caption is not null,
            FontFamily = mono,
            FontWeight = Theme.IsFlat ? FontWeights.Medium : spot.Caption is null ? FontWeights.Bold : FontWeights.SemiBold,
            Foreground = style.Glyph,
            UnitsWide = spot.Width,
        };
        FrameworkElement? badge = null;
        if (conflict is not null)
        {
            // Galerist: the one red, a key that clashes with another. Miami: a warning sign.
            badge = Theme.IsFlat
                ? new Ellipse { Width = 7, Height = 7, Fill = Ink.Live, Margin = new Thickness(0, 2, 0, 0), VerticalAlignment = VerticalAlignment.Top }
                : SynthText.Icon(Icons.Warning, 9, Ink.Error);
        }
        else if (assignments.Count > 1)
        {
            badge = SynthText.Mono($"+{assignments.Count - 1}", 9, Theme.IsFlat ? FontWeights.Medium : FontWeights.Bold,
                Theme.IsFlat ? Ink.Faded((SolidColorBrush)style.Name, 0.7) : Ink.Steel);
            badge.VerticalAlignment = VerticalAlignment.Top;
        }
        var top = Layout.Bar(2, [name], badge is null ? [] : [badge], stretchFirst: true);
        top.VerticalAlignment = VerticalAlignment.Top;
        grid.Children.Add(top);

        FrameworkElement? bottom = null;
        if (isHeld)
        {
            bottom = SynthText.Body("held", 10, Theme.IsFlat ? FontWeights.Medium : FontWeights.SemiBold, Theme.IsFlat ? style.Name : Ink.Amber);
        }
        else if (primary is not null)
        {
            bottom = new ScaledFaceText
            {
                Text = primary.FaceText,
                FontFamily = Theme.TextFont,
                FontWeight = Theme.IsFlat ? FontWeights.Medium : FontWeights.SemiBold,
                Foreground = style.Name,
                UnitsWide = spot.Width,
            };
        }
        if (bottom is not null)
        {
            Grid.SetRow(bottom, 2);
            grid.Children.Add(bottom);
        }
        return grid;
    }

    private static string Tooltip(string keyName, IReadOnlyList<KeyAssignment> assignments, string? conflict, bool isHeld, bool isInert, KeyChord? comboKey)
    {
        if (isHeld) return $"{keyName}: the Combo Key. Hold it, then press one of the coloured keys.";
        if (assignments.Count == 0) return isInert ? "" : $"{keyName}: nothing here";
        var lines = assignments.Select(assignment => assignment.Line(comboKey)).ToList();
        if (conflict is not null) lines.Add("⚠ " + conflict);
        return string.Join("\n", lines);
    }

    private static string AccessibilityValue(IReadOnlyList<KeyAssignment> assignments, string? conflict, bool isHeld, KeyChord? comboKey)
    {
        if (isHeld) return "Combo Key, held";
        if (assignments.Count == 0) return "Not assigned";
        var parts = assignments.Select(assignment =>
            $"{assignment.Chord.DisplayTextWith(comboKey)}, {KeyAssignment.Title(assignment.Type)} of {assignment.Name}: {assignment.Detail}").ToList();
        if (conflict is not null) parts.Add("Conflict: " + conflict);
        return string.Join("; ", parts);
    }
}

/// <summary>The printed key name, sized from the key's width (10–14 px for characters, 9 px for captions).</summary>
internal sealed class ScaledKeyName : ContentControl
{
    private readonly SpacedText text = new() { MinimumScale = 0.6 };

    public ScaledKeyName()
    {
        Content = text;
        Focusable = false;
        IsTabStop = false;
    }

    public string Text
    {
        get => text.Text;
        set => text.Text = value;
    }

    public bool IsCaption { get; set; }

    /// <summary>How many key units wide the key is; the drawing's unit is its width / 15.</summary>
    public double UnitsWide { get; set; } = 1;

    protected override Size MeasureOverride(Size constraint)
    {
        var unit = double.IsFinite(constraint.Width) && UnitsWide > 0 ? (constraint.Width + 16) / UnitsWide : 64;
        text.FontSize = IsCaption ? 9 : Math.Min(14, Math.Max(10, unit * (Theme.IsFlat ? 0.2 : 0.21)));
        return base.MeasureOverride(constraint);
    }
}

/// <summary>What the key does, on two lines at most, sized from the key's width (8.5–11 px).</summary>
internal sealed class ScaledFaceText : ContentControl
{
    private readonly TextBlock text = new()
    {
        TextWrapping = TextWrapping.Wrap,
        TextTrimming = TextTrimming.CharacterEllipsis,
    };

    public ScaledFaceText()
    {
        Content = text;
        Focusable = false;
        IsTabStop = false;
    }

    public string Text
    {
        get => text.Text;
        set => text.Text = value;
    }

    public double UnitsWide { get; set; } = 1;

    /// <summary>
    /// Like the Mac's minimumScaleFactor: a word wider than the key shrinks the
    /// text instead of breaking in two. 0.6, not the Mac's 0.7: Outfit at these
    /// sizes needs it for "Temperature" on a letter key (as the key names do).
    /// </summary>
    private const double MinimumScale = 0.6;

    protected override Size MeasureOverride(Size constraint)
    {
        var unit = double.IsFinite(constraint.Width) && UnitsWide > 0 ? (constraint.Width + 16) / UnitsWide : 64;
        var size = Math.Min(11, Math.Max(8.5, unit * 0.155));
        if (double.IsFinite(constraint.Width) && constraint.Width > 0)
        {
            // "Temperature" or "Saturation" on a letter key: TextWrapping.Wrap would split the word.
            // One pixel to spare: layout rounding can take it from the line.
            var room = constraint.Width - 1;
            var widest = WidestWord(size);
            if (widest > room) size = Math.Max(size * MinimumScale, size * room / widest);
        }
        text.FontSize = size;
        text.MaxHeight = size * 1.3 * 2;
        return base.MeasureOverride(constraint);
    }

    private double WidestWord(double fontSize)
    {
        var typeface = new Typeface(text.FontFamily, text.FontStyle, text.FontWeight, text.FontStretch);
        var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var widest = 0.0;
        foreach (var word in (text.Text ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var formatted = new FormattedText(word, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                typeface, fontSize, Brushes.Black, pixelsPerDip);
            widest = Math.Max(widest, formatted.WidthIncludingTrailingWhitespace);
        }
        return widest;
    }
}

/// <summary>
/// The ISO Enter key's outline: full width on the top row, narrower by a
/// quarter key below. Square corners in both themes.
/// </summary>
internal sealed class IsoEnterOutline : Shape
{
    protected override Geometry DefiningGeometry
    {
        get
        {
            var inset = StrokeThickness / 2;
            var width = ActualWidth;
            var height = ActualHeight;
            if (width <= 0 || height <= 0) return Geometry.Empty;
            // The face spans 1.5 × 2 key units minus 4 px; the notch is a quarter key wide and one row (minus the gap) tall.
            var notchWidth = (width + 4) / 6;
            var notchHeight = (height + 4) / 2 - 4;
            Point[] points =
            [
                new(inset, inset),
                new(width - inset, inset),
                new(width - inset, height - inset),
                new(notchWidth + inset, height - inset),
                new(notchWidth + inset, notchHeight + inset),
                new(inset, notchHeight + inset),
            ];
            var figure = new PathFigure { StartPoint = points[0], IsClosed = true, IsFilled = true };
            for (var index = 1; index < points.Length; index++) figure.Segments.Add(new LineSegment(points[index], isStroked: true));
            var geometry = new PathGeometry([figure]);
            geometry.Freeze();
            return geometry;
        }
    }
}
