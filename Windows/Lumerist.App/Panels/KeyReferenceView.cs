using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Lumerist.App.Design;
using Lumerist.Core;

namespace Lumerist.App.Panels;

/// <summary>
/// The floating list of the current module's keys (the Mac's
/// KeyReferenceView): System Keys, Dial Keys, Nudge Keys and Command Keys,
/// then the combos in their own group ("Combos (hold Tab)"). Searchable and
/// paged. The search and the page live here, so the rows can change while
/// the search field keeps the keyboard.
/// </summary>
internal sealed class KeyReferenceView
{
    public const double Width = 340;

    private readonly Action hide;
    private readonly Action? startTyping;
    private Keymap keymap;
    private LightroomContext context;
    private string search = "";
    private int page;
    private StackPanel? rows;
    private Grid? pager;

    /// <param name="hide">The × button.</param>
    /// <param name="startTyping">The search field was clicked: the panel may now take the keyboard.</param>
    public KeyReferenceView(Keymap keymap, LightroomContext context, Action hide, Action? startTyping = null)
    {
        this.keymap = keymap;
        this.context = context;
        this.hide = hide;
        this.startTyping = startTyping;
    }

    /// <summary>
    /// Builds the whole panel. A new theme starts from an empty search, as
    /// the Mac's views start again in a new look.
    /// </summary>
    public PanelFrame Build(bool resetSearch = false)
    {
        if (resetSearch)
        {
            search = "";
            page = 0;
        }
        var palette = PanelLook.Palette;
        var flat = PanelLook.IsFlat;
        var close = PanelChrome.TitleButton("xmark", PanelLook.Brush(palette.Steel), KeyReferenceList.HideLabel, hide);
        var stack = new StackPanel { Margin = new Thickness(flat ? 14 : 12, 12, flat ? 14 : 12, 12) };
        stack.Children.Add(SearchField());
        rows = new StackPanel { Margin = new Thickness(0, flat ? 10 : Theme.Space.S, 0, 0) };
        stack.Children.Add(rows);
        pager = new Grid();
        stack.Children.Add(pager);
        FillRows();
        return PanelChrome.Build(KeyReferenceList.Title, KeyReferenceList.Subtitle(context), palette.Teal, [close], stack, Width);
    }

    /// <summary>The keymap or Lightroom's module changed.</summary>
    public void Update(Keymap newKeymap, LightroomContext newContext)
    {
        keymap = newKeymap;
        context = newContext;
    }

    private void FillRows()
    {
        if (rows is null || pager is null) return;
        var flat = PanelLook.IsFlat;
        var palette = PanelLook.Palette;
        var all = KeyReferenceList.Rows(keymap, KeyReferenceList.Scope(context), search);
        var current = KeyReferenceList.Page(all, page);
        rows.Children.Clear();
        if (current.Rows.Count == 0)
        {
            var empty = PanelLook.Body(search.Length == 0 ? KeyReferenceList.NoKeys : KeyReferenceList.NothingMatches, 12,
                PanelLook.Brush(palette.Steel));
            empty.Margin = new Thickness(0, 10, 0, 10);
            rows.Children.Add(empty);
        }
        foreach (var entry in current.Rows)
        {
            if (entry.IsHeader) rows.Children.Add(Header(entry));
            else rows.Children.Add(flat ? FlatRow(entry) : NeonRow(entry));
        }

        pager.Children.Clear();
        pager.ColumnDefinitions.Clear();
        pager.Visibility = current.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        pager.Margin = new Thickness(0, (flat ? 10 : Theme.Space.S) + (flat ? 2 : 0), 0, 0);
        if (current.Count <= 1) return;
        pager.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        pager.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        pager.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var previous = QuietButton("chevron.left", "Previous page", current.Index > 0, () => ShowPage(Math.Max(0, current.Index - 1)));
        pager.Children.Add(previous);
        var text = PanelLook.Label(KeyReferenceList.PageText(current), 10);
        text.HorizontalAlignment = HorizontalAlignment.Center;
        text.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(text, 1);
        pager.Children.Add(text);
        var next = QuietButton("chevron.right", "Next page", current.Index < current.Count - 1,
            () => ShowPage(Math.Min(current.Count - 1, current.Index + 1)));
        Grid.SetColumn(next, 2);
        pager.Children.Add(next);
    }

    private void ShowPage(int index)
    {
        page = index;
        FillRows();
    }

    /// <summary>The Mac's search field: a magnifier, the text, and a clear button while there is text.</summary>
    private Border SearchField()
    {
        var palette = PanelLook.Palette;
        var flat = PanelLook.IsFlat;
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(PanelLook.Icon("magnifyingglass", 11, PanelLook.Brush(palette.Steel)));
        var placeholder = new TextBlock
        {
            Text = KeyReferenceList.SearchPrompt,
            FontFamily = Theme.TextFont,
            FontSize = 13,
            Foreground = PanelLook.Brush(palette.Steel),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0),
            IsHitTestVisible = false,
            Visibility = search.Length == 0 ? Visibility.Visible : Visibility.Collapsed,
        };
        var field = new TextBox
        {
            Text = search,
            FontFamily = Theme.TextFont,
            FontSize = 13,
            Foreground = PanelLook.Brush(palette.Ice),
            CaretBrush = PanelLook.Brush(palette.Ice),
            SelectionBrush = PanelLook.Brush(palette.Teal),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            Margin = new Thickness(6, 0, 0, 0),
            VerticalContentAlignment = VerticalAlignment.Center,
            FocusVisualStyle = null,
        };
        AutomationProperties.SetName(field, KeyReferenceList.SearchPrompt);
        // Typing needs the keyboard: only now may the panel take it from Lightroom.
        field.PreviewMouseLeftButtonDown += (_, _) => startTyping?.Invoke();
        var clear = new PanelButton(PanelLook.Icon("xmark.circle.fill", 10, PanelLook.Brush(palette.Steel)), () => field.Text = "",
            label: "Clear search")
        {
            Width = 18,
            Height = 18,
            Visibility = search.Length == 0 ? Visibility.Collapsed : Visibility.Visible,
        };
        field.TextChanged += (_, _) =>
        {
            search = field.Text;
            page = 0;
            placeholder.Visibility = search.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            clear.Visibility = search.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
            FillRows();
        };
        Grid.SetColumn(placeholder, 1);
        Grid.SetColumn(field, 1);
        Grid.SetColumn(clear, 2);
        grid.Children.Add(placeholder);
        grid.Children.Add(field);
        grid.Children.Add(clear);
        return new Border
        {
            Height = flat ? 32 : 30,
            Padding = new Thickness(flat ? 12 : 10, 0, flat ? 12 : 10, 0),
            Background = PanelLook.Brush(flat ? palette.Panel : palette.Abyss),
            BorderBrush = PanelLook.Brush(palette.Outline),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(PanelLook.ControlRadius),
            Cursor = Cursors.IBeam,
            Child = grid,
        };
    }

    private static FrameworkElement Header(KeyReferenceEntry entry)
    {
        var palette = PanelLook.Palette;
        var flat = PanelLook.IsFlat;
        var title = flat
            ? PanelLook.Caps(entry.Title, 9, PanelLook.Brush(palette.Ice), FontWeights.SemiBold)
            : PanelLook.Label(entry.Title, 9, PanelLook.Brush(palette.Amber));
        title.VerticalAlignment = VerticalAlignment.Bottom;
        title.HorizontalAlignment = HorizontalAlignment.Left;
        var cell = new Grid { MinHeight = flat ? 30 : 26 };
        if (flat) cell.Margin = new Thickness(0, 0, 0, 4);
        cell.Children.Add(title);
        AutomationProperties.SetName(cell, entry.Title);
        AutomationProperties.SetHeadingLevel(cell, AutomationHeadingLevel.Level2);
        return cell;
    }

    /// <summary>Galerist: hairline rows, mono key caps with a light 1 px border.</summary>
    private static FrameworkElement FlatRow(KeyReferenceEntry entry)
    {
        var palette = PanelLook.Palette;
        var ice = PanelLook.Brush(palette.Ice);
        var keys = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        foreach (var key in entry.Keys)
        {
            var text = PanelLook.Mono(key, 11, FontWeights.Medium, ice);
            text.HorizontalAlignment = HorizontalAlignment.Center;
            text.VerticalAlignment = VerticalAlignment.Center;
            keys.Children.Add(new Border
            {
                BorderBrush = PanelLook.Brush(palette.Ice, 0.85),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(6, 0, 6, 0),
                MinWidth = 30,
                MinHeight = 20,
                Margin = new Thickness(keys.Children.Count > 0 ? 4 : 0, 0, 0, 0),
                SnapsToDevicePixels = true,
                Child = text,
            });
        }
        return Row(entry, keys, 88, 10, 30, PanelLook.BodyLine(entry.Title, 13, ice), PanelLook.Caps(entry.Kind, 8),
            PanelLook.Hairline(PanelLook.Brush(palette.GridLine)));
    }

    private static FrameworkElement NeonRow(KeyReferenceEntry entry)
    {
        var palette = PanelLook.Palette;
        var teal = PanelLook.Brush(palette.Teal);
        var keys = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        foreach (var key in entry.Keys)
        {
            var text = PanelLook.Mono(key, 11, FontWeights.Bold, teal);
            text.HorizontalAlignment = HorizontalAlignment.Center;
            text.VerticalAlignment = VerticalAlignment.Center;
            keys.Children.Add(new Border
            {
                BorderBrush = PanelLook.Brush(palette.Teal, 0.8),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(5, 0, 5, 0),
                MinWidth = 22,
                MinHeight = 20,
                Margin = new Thickness(keys.Children.Count > 0 ? 3 : 0, 0, 0, 0),
                Child = text,
            });
        }
        return Row(entry, keys, 104, 6, 26,
            PanelLook.BodyLine(entry.Title, 12, PanelLook.Brush(palette.Ice), FontWeights.Medium), PanelLook.Label(entry.Kind, 8), null);
    }

    /// <summary>Keys in a fixed column, the title, and the kind at the end.</summary>
    private static Grid Row(KeyReferenceEntry entry, FrameworkElement keys, double keysWidth, double spacing, double height,
        FrameworkElement title, FrameworkElement kind, FrameworkElement? topLine)
    {
        var row = new Grid { Height = height };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(keysWidth) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        keys.HorizontalAlignment = HorizontalAlignment.Left;
        keys.ClipToBounds = true;
        row.Children.Add(keys);
        title.Margin = new Thickness(spacing, 0, 0, 0);
        title.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(title, 1);
        row.Children.Add(title);
        kind.Margin = new Thickness(4 + spacing, 0, 0, 0);
        kind.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(kind, 2);
        row.Children.Add(kind);
        if (topLine is not null)
        {
            topLine.VerticalAlignment = VerticalAlignment.Top;
            Grid.SetColumnSpan(topLine, 3);
            row.Children.Add(topLine);
        }
        AutomationProperties.SetName(row, $"{string.Join(", ", entry.Keys)} {entry.Title} {entry.Kind}");
        return row;
    }

    /// <summary>The Mac's neonQuiet button: a symbol in a quiet colour, dimmed when it cannot be used.</summary>
    private static PanelButton QuietButton(string symbol, string label, bool enabled, Action action)
    {
        var palette = PanelLook.Palette;
        var flat = PanelLook.IsFlat;
        var color = !enabled ? palette.Disabled : flat ? palette.Body : palette.Steel;
        var face = new Grid { MinHeight = 26, MinWidth = 24, Background = Brushes.Transparent };
        face.Children.Add(PanelLook.Icon(symbol, 11, PanelLook.Brush(color)));
        face.Margin = new Thickness(flat ? 4 : 6, 0, flat ? 4 : 6, 0);
        return new PanelButton(face, action, label: label) { IsEnabled = enabled };
    }
}
