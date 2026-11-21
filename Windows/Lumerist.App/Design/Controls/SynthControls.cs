using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Lumerist.Core;

namespace Lumerist.App.Design.Controls;

/// <summary>
/// The interactive controls (the Mac's NeonButtonStyle, NeonToggleStyle,
/// SynthSegmented, NeonMenu, MoreButton, StatusPill, NeonFieldStyle,
/// SynthSearchField and NeonSlider). Each is built for the theme in effect;
/// build again after <see cref="Theme.Changed"/>.
/// </summary>
internal static class SynthControls
{
    /// <summary>Button kinds: magenta fill, outline, error outline (Miami), text only.</summary>
    public enum Kind
    {
        Primary,
        Secondary,
        Destructive,
        Quiet,
    }

    /// <summary>The look of a <see cref="StatusPill"/>.</summary>
    public enum Tone
    {
        Good,
        Waiting,
        Off,
    }

    private static ControlStyles? styles;

    /// <summary>The control templates (ControlStyles.xaml), loaded once.</summary>
    public static ControlStyles Styles => styles ??= new ControlStyles();

    public static Style Style(string key) => (Style)Styles[key];

    private static string Look => Theme.IsFlat ? "Flat" : "Neon";

    // Buttons ---------------------------------------------------------------------

    /// <summary>A text button in small caps.</summary>
    public static Button Button(string title, Kind kind, Action onClick, string? tooltip = null, string? icon = null)
    {
        UIElement label = new SpacedText { Text = title.ToUpperInvariant(), Tracking = Theme.IsFlat ? 1.6 : 1, VerticalAlignment = VerticalAlignment.Center };
        // An icon before the title, as in the Mac's Label(title, systemImage:).
        if (icon is not null) label = Layout.HStack(6, SynthText.Icon(icon, 10), label);
        var button = new Button
        {
            Style = Style($"Synth.Button.{Look}.{kind}"),
            Content = label,
        };
        button.Click += (_, _) => onClick();
        AutomationProperties.SetName(button, title);
        if (tooltip is not null) button.ToolTip = tooltip;
        return button;
    }

    /// <summary>A button showing an icon (arrows, remove…); <paramref name="name"/> is read by screen readers.</summary>
    public static Button IconButton(string glyph, Kind kind, Action onClick, string name)
    {
        var button = new Button
        {
            Style = Style($"Synth.Button.{Look}.{kind}"),
            Content = SynthText.Icon(glyph, 11),
        };
        button.Click += (_, _) => onClick();
        AutomationProperties.SetName(button, name);
        return button;
    }

    /// <summary>A button with no chrome around <paramref name="content"/>.</summary>
    public static Button Plain(UIElement content, Action onClick)
    {
        var button = new Button { Style = Style("Synth.Button.Plain"), Content = content };
        button.Click += (_, _) => onClick();
        return button;
    }

    // Switches --------------------------------------------------------------------

    /// <summary>
    /// A switch (the Mac's toggles): Galerist's square switch, Miami's
    /// OFF | ON. The row around it shows the title; <paramref name="name"/>
    /// is for screen readers.
    /// </summary>
    public static CheckBox Toggle(bool isOn, Action<bool> onChange, string name)
    {
        var toggle = new CheckBox { Style = Style($"Synth.Switch.{Look}"), IsChecked = isOn };
        toggle.Checked += (_, _) => onChange(true);
        toggle.Unchecked += (_, _) => onChange(false);
        AutomationProperties.SetName(toggle, name);
        return toggle;
    }

    /// <summary>
    /// Galerist's square switch as a drawing only (the header's Shortcuts
    /// switch): ink with the knob right when on, grey outline with the knob
    /// left when off, the knob centred while pending.
    /// </summary>
    public static FrameworkElement FlatSwitch(bool isOn, bool isEnabled = true, bool isPending = false)
    {
        var ink = isEnabled ? Ink.Magenta : Ink.Disabled;
        var off = isEnabled ? Ink.Steel : Ink.Disabled;
        var grid = new Grid { Width = 38, Height = 22, SnapsToDevicePixels = true };
        grid.Children.Add(new Border { Background = isOn && !isPending ? ink : Brushes.Transparent });
        grid.Children.Add(new Border { BorderBrush = isOn || isPending ? ink : off, BorderThickness = new Thickness(1) });
        grid.Children.Add(new Border
        {
            Width = 14,
            Height = 14,
            Margin = new Thickness(4, 0, 4, 0),
            HorizontalAlignment = isPending ? HorizontalAlignment.Center : isOn ? HorizontalAlignment.Right : HorizontalAlignment.Left,
            Background = isOn && !isPending ? (isEnabled ? Ink.OnMagenta : Ink.Panel) : isPending ? ink : off,
        });
        return grid;
    }

    // Segmented control -------------------------------------------------------------

    /// <summary>
    /// Small uppercase segmented control (sub-tabs, modes). Galerist: hairline
    /// cells side by side, the selected one filled with ink. Miami: one
    /// outlined capsule, the selected cell magenta.
    /// </summary>
    public static FrameworkElement Segmented<T>(IReadOnlyList<T> items, T selection, Func<T, string> title, Action<T> onSelect,
        bool isEnabled = true, string? name = null)
    {
        var group = Guid.NewGuid().ToString("N");
        // Galerist: cells overlap by 1 px so neighbouring borders read as one hairline.
        var row = new Stack { Orientation = Orientation.Horizontal, Spacing = Theme.IsFlat ? -1 : 0 };
        foreach (var item in items)
        {
            var text = title(item);
            var cell = new RadioButton
            {
                Style = Style($"Synth.Segment.{Look}"),
                GroupName = group,
                IsChecked = EqualityComparer<T>.Default.Equals(item, selection),
                Content = new SpacedText { Text = text.ToUpperInvariant(), Tracking = Theme.IsFlat ? 1.6 : 1 },
            };
            cell.Checked += (_, _) => onSelect(item);
            AutomationProperties.SetName(cell, text);
            row.Children.Add(cell);
        }
        FrameworkElement result = row;
        if (!Theme.IsFlat)
        {
            result = new Border
            {
                Child = row,
                BorderBrush = Ink.Outline,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                ClipToBounds = true,
                SnapsToDevicePixels = true,
            };
        }
        result.IsEnabled = isEnabled;
        result.HorizontalAlignment = HorizontalAlignment.Left;
        if (name is not null) AutomationProperties.SetName(result, name);
        return result;
    }

    // Menus -------------------------------------------------------------------------

    /// <summary>One titled group of menu items (null title: no header).</summary>
    public sealed record MenuGroup<T>(string? Title, IReadOnlyList<T> Items);

    /// <summary>
    /// A button with a chevron that pops up a menu below it, the current item
    /// checked (the Mac's NeonMenu): to pick a slider, a group, an option.
    /// </summary>
    public static Button MenuButton<T>(string title, string name, IReadOnlyList<MenuGroup<T>> groups, T? selection,
        Func<T, string> itemTitle, Action<T> onSelect)
    {
        var label = SynthText.Body(title, 13, FontWeights.Medium, Ink.Ice);
        label.TextWrapping = TextWrapping.NoWrap;
        label.TextTrimming = TextTrimming.CharacterEllipsis;
        var chevron = SynthText.Icon(Icons.ChevronDown, 10, Theme.IsFlat ? Ink.Steel : Ink.Teal);
        var face = new Border
        {
            Height = 30,
            Padding = new Thickness(10, 0, 10, 0),
            BorderBrush = Ink.Outline,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(Theme.IsFlat ? 0 : 4),
            Background = Brushes.Transparent,
            Child = Layout.Bar(Theme.Space.S, [label], [chevron], stretchFirst: true),
            SnapsToDevicePixels = true,
        };
        Button? button = null;
        button = Plain(face, () =>
        {
            var sections = groups.Select(group => (group.Title, group.Items.Select(item =>
                (itemTitle(item), selection is not null && EqualityComparer<T>.Default.Equals(item, selection), (Action)(() => onSelect(item))))
                .ToList())).ToList();
            PopUp(button!, sections);
        });
        AutomationProperties.SetName(button, name);
        AutomationProperties.SetHelpText(button, $"{title}. Opens a menu");
        return button;
    }

    /// <summary>A menu with one group and no header.</summary>
    public static Button MenuButton<T>(string title, string name, IReadOnlyList<T> items, T? selection,
        Func<T, string> itemTitle, Action<T> onSelect) =>
        MenuButton(title, name, [new MenuGroup<T>(null, items)], selection, itemTitle, onSelect);

    /// <summary>"⋯" button that pops a small menu.</summary>
    public static Button MoreButton(string name, IReadOnlyList<(string Title, Action Action)> entries)
    {
        var face = new Border
        {
            Width = 28,
            Height = 28,
            BorderBrush = Theme.IsFlat ? Ink.Outline : Ink.GridLine,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(Theme.IsFlat ? 0 : 4),
            Background = Brushes.Transparent,
            Child = SynthText.Icon(Icons.More, 12, Ink.Steel),
        };
        Button? button = null;
        button = Plain(face, () => PopUp(button!, [(null, entries.Select(entry => (entry.Title, false, entry.Action)).ToList())]));
        AutomationProperties.SetName(button, name);
        return button;
    }

    /// <summary>Opens a themed menu below <paramref name="anchor"/>: sections separated by lines, optional disabled headers.</summary>
    public static void PopUp(FrameworkElement anchor, IReadOnlyList<(string? Title, List<(string Title, bool IsChecked, Action Action)> Entries)> sections)
    {
        var menu = new ContextMenu
        {
            Style = Style("Synth.ContextMenu"),
            PlacementTarget = anchor,
            Placement = PlacementMode.Bottom,
            VerticalOffset = 4,
            MinWidth = anchor.ActualWidth,
            MaxHeight = SystemParameters.WorkArea.Height * 0.8,
        };
        for (var index = 0; index < sections.Count; index++)
        {
            var (title, entries) = sections[index];
            if (index > 0) menu.Items.Add(new Separator { Style = Style("Synth.MenuSeparator") });
            if (title is not null) menu.Items.Add(new MenuItem { Style = Style("Synth.MenuItem"), Header = title, IsEnabled = false });
            foreach (var (entryTitle, isChecked, action) in entries)
            {
                var item = new MenuItem { Style = Style("Synth.MenuItem"), Header = entryTitle, IsChecked = isChecked };
                item.Click += (_, _) => action();
                menu.Items.Add(item);
            }
        }
        menu.IsOpen = true;
    }

    // Status ------------------------------------------------------------------------

    /// <summary>An outlined chip that always spells its state; Miami's glow is decoration only.</summary>
    public static FrameworkElement StatusPill(string text, string icon, Tone tone)
    {
        if (Theme.IsFlat)
        {
            var strong = tone == Tone.Good;
            var color = strong ? Ink.Ice : Ink.Steel;
            return new Border
            {
                Height = 28,
                Padding = new Thickness(10, 0, 10, 0),
                BorderBrush = strong ? Ink.Ice : Ink.Outline,
                BorderThickness = new Thickness(1),
                SnapsToDevicePixels = true,
                Child = Layout.HStack(6, SynthText.Icon(icon, 10, color),
                    SynthText.Caps(text, Theme.MonoFont, 10.5, FontWeights.Medium, 1.6, color)),
            };
        }
        var neon = tone switch
        {
            Tone.Good => Ink.Teal,
            Tone.Waiting => Ink.Amber,
            _ => Ink.Steel,
        };
        var pill = new Border
        {
            Height = 28,
            Padding = new Thickness(10, 0, 10, 0),
            BorderBrush = neon,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            SnapsToDevicePixels = true,
            Child = Layout.HStack(6, SynthText.Icon(icon, 10, neon), SynthText.Caps(text, Theme.MonoFont, 11, FontWeights.SemiBold, 1, neon)),
        };
        if (tone != Tone.Off) pill.Effect = Glow(neon.Color, 6, 0.3);
        return pill;
    }

    /// <summary>Miami's neon glow (never in Galerist).</summary>
    public static System.Windows.Media.Effects.Effect? Glow(Color color, double radius, double opacity) => Theme.IsFlat
        ? null
        : new System.Windows.Media.Effects.DropShadowEffect { Color = color, BlurRadius = radius * 2, ShadowDepth = 0, Opacity = opacity };

    // Fields ------------------------------------------------------------------------

    /// <summary>
    /// A monospaced field on abyss (Miami) or white (Galerist) with a 1 px
    /// outline. <paramref name="onCommit"/> runs on Enter and when the field
    /// loses the focus.
    /// </summary>
    public static TextBox Field(string text, double width, Action<string> onCommit, string name, TextAlignment alignment = TextAlignment.Right)
    {
        var field = new TextBox
        {
            Style = Style("Synth.Field"),
            Text = text,
            Width = width,
            TextAlignment = alignment,
            Background = Theme.IsFlat ? Ink.Panel : Ink.Abyss,
        };
        var committed = text;
        void Commit()
        {
            if (field.Text == committed) return;
            committed = field.Text;
            onCommit(field.Text);
        }
        field.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            Commit();
            e.Handled = true;
        };
        field.LostKeyboardFocus += (_, _) => Commit();
        AutomationProperties.SetName(field, name);
        return field;
    }

    /// <summary>A search field: magnifier, prompt while empty, a clear button once there is text.</summary>
    public static Border SearchField(string prompt, string text, Action<string> onChange) => SearchField(prompt, text, onChange, out _);

    /// <inheritdoc cref="SearchField(string, string, Action{string})"/>
    /// <param name="box">The text box, to set its text from elsewhere (a click on the drawn keyboard).</param>
    public static Border SearchField(string prompt, string text, Action<string> onChange, out TextBox box)
    {
        box = new TextBox { Style = Style("Synth.SearchText"), Text = text };
        var field = box;
        AutomationProperties.SetName(field, prompt);
        var placeholder = SynthText.Body(prompt, 13, color: Ink.Steel);
        placeholder.TextWrapping = TextWrapping.NoWrap;
        placeholder.IsHitTestVisible = false;
        placeholder.VerticalAlignment = VerticalAlignment.Center;
        placeholder.Margin = new Thickness(2, 0, 0, 0);
        var clear = Plain(SynthText.Icon(Icons.Cancel, 10, Ink.Steel), () => field.Text = "");
        clear.Width = 18;
        clear.Height = 18;
        AutomationProperties.SetName(clear, "Clear search");
        void Update()
        {
            placeholder.Visibility = field.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            clear.Visibility = field.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        }
        field.TextChanged += (_, _) =>
        {
            Update();
            onChange(field.Text);
        };
        Update();

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var magnifier = SynthText.Icon(Icons.Search, 11, Ink.Steel);
        magnifier.Margin = new Thickness(0, 0, 6, 0);
        grid.Children.Add(magnifier);
        Grid.SetColumn(placeholder, 1);
        Grid.SetColumn(field, 1);
        grid.Children.Add(placeholder);
        grid.Children.Add(field);
        Grid.SetColumn(clear, 2);
        grid.Children.Add(clear);
        return new Border
        {
            Height = Theme.IsFlat ? 32 : 30,
            Padding = new Thickness(Theme.IsFlat ? 12 : 10, 0, Theme.IsFlat ? 12 : 10, 0),
            Background = Theme.IsFlat ? Ink.Panel : Ink.Abyss,
            BorderBrush = Ink.Outline,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(Theme.IsFlat ? 0 : 4),
            Child = grid,
            SnapsToDevicePixels = true,
        };
    }

    // Slider ------------------------------------------------------------------------

    private const double SliderTrackWidth = 220;

    /// <summary>
    /// A slider tinted teal with a monospaced read-out (the Mac's NeonSlider),
    /// optional end labels below. The value is rounded to <paramref name="step"/>.
    /// <paramref name="onLive"/> follows every move; <paramref name="onCommit"/>
    /// runs once a drag ends (or at once for a click or a key), so a screen
    /// rebuilt by the commit never loses the drag.
    /// </summary>
    public static FrameworkElement Slider(double value, double minimum, double maximum, double? step, Func<double, string> readout,
        Action<double> onCommit, string name, string? lowLabel = null, string? highLabel = null, Action<double>? onLive = null)
    {
        var slider = new Slider
        {
            Style = Style("Synth.Slider"),
            Minimum = minimum,
            Maximum = maximum,
            Value = value,
            Width = SliderTrackWidth,
            SmallChange = step ?? (maximum - minimum) / 100,
            LargeChange = (step ?? (maximum - minimum) / 100) * 5,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var text = SynthText.Mono(readout(value), 13, FontWeights.SemiBold, Ink.Ice);
        text.MinWidth = 60;
        text.TextAlignment = TextAlignment.Right;
        text.VerticalAlignment = VerticalAlignment.Center;
        AutomationProperties.SetName(slider, name);
        AutomationProperties.SetHelpText(slider, readout(value));

        var dragging = false;
        var current = SettingsChoices.Stepped(value, minimum, maximum, step);
        slider.ValueChanged += (_, e) =>
        {
            current = SettingsChoices.Stepped(e.NewValue, minimum, maximum, step);
            text.Text = readout(current);
            AutomationProperties.SetHelpText(slider, text.Text);
            onLive?.Invoke(current);
            if (!dragging) onCommit(current);
        };
        slider.AddHandler(Thumb.DragStartedEvent, new DragStartedEventHandler((_, _) => dragging = true));
        slider.AddHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler((_, _) =>
        {
            dragging = false;
            onCommit(current);
        }));

        var top = Layout.HStack(Theme.Space.M, slider, text);
        if (lowLabel is null && highLabel is null) return top;
        var ends = new Grid { Width = SliderTrackWidth, HorizontalAlignment = HorizontalAlignment.Left };
        var low = SynthText.Label(lowLabel ?? "", 9);
        var high = SynthText.Label(highLabel ?? "", 9);
        high.HorizontalAlignment = HorizontalAlignment.Right;
        ends.Children.Add(low);
        ends.Children.Add(high);
        return Layout.VStack(2, top, ends);
    }

    // Numbers -----------------------------------------------------------------------

    /// <summary>
    /// A value with − and + buttons (the Mac's Stepper), e.g. "3 s". The
    /// value stays inside the range and moves by <paramref name="step"/>.
    /// </summary>
    public static FrameworkElement Stepper(double value, double minimum, double maximum, double step, Func<double, string> readout,
        Action<double> onChange, string name)
    {
        var text = SynthText.Mono(readout(value), 13, FontWeights.SemiBold, Ink.Ice);
        text.MinWidth = 44;
        text.TextAlignment = TextAlignment.Right;
        var down = IconButton(Icons.Minus, Kind.Secondary, () => onChange(Math.Clamp(value - step, minimum, maximum)), $"{name}, less");
        var up = IconButton(Icons.Add, Kind.Secondary, () => onChange(Math.Clamp(value + step, minimum, maximum)), $"{name}, more");
        foreach (var button in new[] { down, up })
        {
            button.MinHeight = 24;
            button.Padding = new Thickness(6, 0, 6, 0);
        }
        down.IsEnabled = value > minimum;
        up.IsEnabled = value < maximum;
        var stack = Layout.HStack(Theme.Space.S, text, Layout.HStack(-1, down, up));
        AutomationProperties.SetName(stack, name);
        AutomationProperties.SetHelpText(stack, readout(value));
        return stack;
    }
}
