using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using Lumerist.App.Design;
using Lumerist.Core;

namespace Lumerist.App.Panels;

/// <summary>What the Palette shows: Lightroom's module, a selected mask, the modifiers held, the favourites.</summary>
/// <param name="Held">What a click would do with the modifiers held right now; drives the highlight only.</param>
internal sealed record PaletteState(Keymap Keymap, PaletteOptions Options, string Module, bool Masking, PaletteMaskMode Held);

/// <summary>What the Palette's controls do.</summary>
/// <param name="RunTool">A mask tool under "Create New Mask" (its argument); the click reads the modifiers again.</param>
/// <param name="RunFavourite">A favourite row.</param>
internal sealed record PaletteHandlers(Action<string> RunTool, Action<PaletteEntry> RunFavourite, Action TogglePin, Action Close);

/// <summary>
/// The Palette (the Mac's PaletteView): Lightroom's mask tools under "Create
/// New Mask", then the user's favourite presets and tools. Picking an item
/// runs it in Lightroom and closes the Palette unless it is pinned.
/// </summary>
internal static class PaletteView
{
    public const double Width = 330;

    /// <summary>Galerist's mask tool cells: 4 per row, 74 px wide, 1 px hairlines between them.</summary>
    private const double CellWidth = 74;

    public static PanelFrame Build(PaletteState state, PaletteHandlers handlers)
    {
        var palette = PanelLook.Palette;
        var keepVisible = state.Options.KeepVisible;
        var pin = PanelChrome.TitleButton(keepVisible ? "pin.fill" : "pin",
            PanelLook.Brush(keepVisible ? palette.Magenta : palette.Steel), PaletteMenu.PinLabel, handlers.TogglePin,
            keepVisible ? PaletteMenu.PinnedHelp : PaletteMenu.PinHelp);
        AutomationProperties.SetItemStatus(pin, keepVisible ? "On" : "Off");
        var close = PanelChrome.TitleButton("xmark", PanelLook.Brush(palette.Steel), PaletteMenu.CloseLabel, handlers.Close);
        var subtitle = PaletteMenu.Subtitle(new LightroomContext(state.Module, state.Masking ? "masking" : ""));
        var content = PanelLook.IsFlat ? FlatContent(state, handlers) : NeonContent(state, handlers);
        return PanelChrome.Build(PaletteMenu.Title, subtitle, palette.Magenta, [pin, close], content, Width);
    }

    // Miami -----------------------------------------------------------------------

    private static FrameworkElement NeonContent(PaletteState state, PaletteHandlers handlers)
    {
        var palette = PanelLook.Palette;
        const double padding = 12;
        var stack = new StackPanel { Margin = new Thickness(padding) };
        var header = new StackPanel();
        header.Children.Add(PanelLook.Label(PaletteMenu.CreateNewMask, 10));
        if (state.Masking)
        {
            var hint = ModeHint(state.Held);
            hint.Margin = new Thickness(0, 6, 0, 0);
            header.Children.Add(hint);
        }
        stack.Children.Add(header);

        var tools = new Grid { Margin = new Thickness(0, Theme.Space.M, 0, 0) };
        for (var column = 0; column < 4; column++) tools.ColumnDefinitions.Add(new ColumnDefinition());
        var rows = (PaletteMenu.MenuToolIds.Count + 3) / 4;
        for (var row = 0; row < rows; row++) tools.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (var index = 0; index < PaletteMenu.MenuToolIds.Count; index++)
        {
            var cell = ToolButton(PaletteMenu.MenuToolIds[index], state, handlers);
            cell.Margin = new Thickness(0, 0, index % 4 < 3 ? 6 : 0, index / 4 < rows - 1 ? 6 : 0);
            Grid.SetColumn(cell, index % 4);
            Grid.SetRow(cell, index / 4);
            tools.Children.Add(cell);
        }
        stack.Children.Add(tools);

        var favourites = state.Options.Favourites;
        if (favourites.Count > 0)
        {
            var title = PanelLook.Label(PaletteMenu.Favourites, 10);
            title.Margin = new Thickness(0, Theme.Space.M, 0, 0);
            stack.Children.Add(title);
            var list = new StackPanel { Margin = new Thickness(0, Theme.Space.M, 0, 0) };
            foreach (var entry in favourites)
            {
                var row = new Grid { Height = 34, Margin = new Thickness(8, 0, 8, 0), Background = Brushes.Transparent };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var preset = entry.Kind == PaletteEntryKind.Preset;
                row.Children.Add(PanelLook.Icon(preset ? "slider.horizontal.3" : "circle.dashed", 13,
                    PanelLook.Brush(preset ? palette.MagentaLight : palette.Teal)));
                var name = PanelLook.BodyLine(entry.Title, 13, PanelLook.Brush(palette.Ice), FontWeights.Medium);
                name.Margin = new Thickness(Theme.Space.S, 0, 0, 0);
                name.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(name, 1);
                row.Children.Add(name);
                if (PaletteMenu.IsMaskTool(entry) && state.Masking && state.Held.Badge() is { } badge)
                {
                    var mark = PanelLook.Mono(badge, 12, FontWeights.Bold, PanelLook.Brush(palette.Magenta));
                    mark.VerticalAlignment = VerticalAlignment.Center;
                    mark.Margin = new Thickness(Theme.Space.S, 0, 0, 0);
                    Grid.SetColumn(mark, 2);
                    row.Children.Add(mark);
                }
                var cell = new Grid();
                cell.Children.Add(row);
                var divider = PanelLook.Hairline(PanelLook.Brush(palette.GridLine, 0.6));
                divider.VerticalAlignment = VerticalAlignment.Bottom;
                cell.Children.Add(divider);
                list.Children.Add(new PanelButton(cell, () => handlers.RunFavourite(entry), label: PaletteMenu.EntryLabel(entry)));
            }
            stack.Children.Add(list);
        }
        else
        {
            var empty = PanelLook.Body(PaletteMenu.NoFavourites, 11, PanelLook.Brush(palette.Steel));
            empty.Margin = new Thickness(0, Theme.Space.M, 0, 0);
            stack.Children.Add(empty);
        }

        var footer = PanelLook.WrappingLabel(PaletteMenu.FooterText(state.Keymap, state.Options.KeepVisible), 9, null,
            Width - 2 * padding, caps: false);
        footer.Margin = new Thickness(0, Theme.Space.M, 0, 0);
        stack.Children.Add(footer);
        return stack;
    }

    /// <summary>"Shift Add · Alt Subtract · Shift+Alt Intersect", the held one lit.</summary>
    private static FrameworkElement ModeHint(PaletteMaskMode held)
    {
        var palette = PanelLook.Palette;
        // Longer key names than the Mac's symbols: the hint wraps when it does not fit.
        var hint = new WrapPanel();
        foreach (var (mode, index) in PaletteMaskModes.Modifying.Select((mode, index) => (mode, index)))
        {
            if (index > 0) hint.Children.Add(PanelLook.Label(" · ", 9));
            hint.Children.Add(PanelLook.Label(mode.HintText(), 9, PanelLook.Brush(mode == held ? palette.Magenta : palette.Steel)));
        }
        AutomationProperties.SetName(hint, PaletteMenu.ModeHintLabel);
        return hint;
    }

    private static FrameworkElement ToolButton(string id, PaletteState state, PaletteHandlers handlers)
    {
        var palette = PanelLook.Palette;
        var tool = PaletteMenu.MaskTool(id);
        var title = tool?.Title ?? id;
        var badge = state.Masking ? state.Held.Badge() : null;
        var teal = PanelLook.Brush(palette.Teal);
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        stack.Children.Add(PanelLook.Icon(tool?.Symbol ?? "circle.dashed", 15, teal));
        var name = PanelLook.Mono(title.ToUpperInvariant(), 9, FontWeights.SemiBold, teal);
        stack.Children.Add(new Viewbox
        {
            Child = name,
            StretchDirection = StretchDirection.DownOnly,
            MaxWidth = (Width - 24 - 18) / 4 - 4,
            Margin = new Thickness(0, 4, 0, 0),
        });
        var face = new Grid { MinHeight = 54, Background = Brushes.Transparent };
        face.Children.Add(new Border
        {
            BorderBrush = badge is null ? PanelLook.Brush(palette.Outline) : PanelLook.Brush(palette.Magenta, 0.7),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(PanelLook.ControlRadius),
        });
        face.Children.Add(stack);
        if (badge is not null)
        {
            var mark = PanelLook.Mono(badge, 11, FontWeights.Bold, PanelLook.Brush(palette.Magenta));
            mark.HorizontalAlignment = HorizontalAlignment.Right;
            mark.VerticalAlignment = VerticalAlignment.Top;
            mark.Margin = new Thickness(0, 3, 5, 0);
            face.Children.Add(mark);
        }
        return ToolCell(face, id, title, state.Masking, handlers);
    }

    /// <summary>A mask tool's button, with the screen reader's Add / Subtract / Intersect in its help text.</summary>
    private static PanelButton ToolCell(FrameworkElement face, string id, string title, bool masking, PaletteHandlers handlers,
        Brush? fill = null, Brush? pressed = null)
    {
        var button = new PanelButton(face, () => handlers.RunTool(id), fill, pressed, PaletteMenu.ToolLabel(title, masking));
        if (masking) AutomationProperties.SetHelpText(button, PaletteMenu.MaskingHint);
        return button;
    }

    // Galerist --------------------------------------------------------------------

    private static FrameworkElement FlatContent(PaletteState state, PaletteHandlers handlers)
    {
        var palette = PanelLook.Palette;
        const double padding = 14;
        var stack = new StackPanel { Margin = new Thickness(padding) };
        var header = new StackPanel();
        header.Children.Add(PanelLook.Caps(PaletteMenu.CreateNewMask, 10));
        if (state.Masking)
        {
            var hint = FlatModeHint(state.Held);
            hint.Margin = new Thickness(0, Theme.Space.S, 0, 0);
            header.Children.Add(hint);
        }
        stack.Children.Add(header);

        var grid = new Grid();
        for (var column = 0; column < 4; column++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var rows = (PaletteMenu.MenuToolIds.Count + 3) / 4;
        for (var row = 0; row < rows; row++) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (var index = 0; index < PaletteMenu.MenuToolIds.Count; index++)
        {
            var cell = FlatToolCell(PaletteMenu.MenuToolIds[index], state, handlers);
            cell.Margin = new Thickness(0, 0, index % 4 < 3 ? 1 : 0, index / 4 < rows - 1 ? 1 : 0);
            Grid.SetColumn(cell, index % 4);
            Grid.SetRow(cell, index / 4);
            grid.Children.Add(cell);
        }
        // The hairlines are the grid's background between the cells.
        stack.Children.Add(new Border
        {
            Background = PanelLook.Brush(palette.GridLine),
            Padding = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, Theme.Space.M, 0, 0),
            SnapsToDevicePixels = true,
            Child = grid,
        });

        var favourites = state.Options.Favourites;
        if (favourites.Count > 0)
        {
            var title = PanelLook.Caps(PaletteMenu.Favourites, 10);
            title.Margin = new Thickness(0, Theme.Space.M, 0, 0);
            stack.Children.Add(title);
            var list = new StackPanel();
            for (var index = 0; index < favourites.Count; index++)
            {
                var entry = favourites[index];
                var row = new Grid { Height = 34, Margin = new Thickness(12, 0, 12, 0) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.Children.Add(PanelLook.Icon(PaletteMenu.Symbol(entry), 12, PanelLook.Brush(palette.Steel)));
                var name = PanelLook.BodyLine(entry.Title, 13, PanelLook.Brush(palette.Ice));
                name.Margin = new Thickness(10, 0, 0, 0);
                name.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(name, 1);
                row.Children.Add(name);
                if (PaletteMenu.IsMaskTool(entry) && state.Masking && state.Held.Badge() is { } badge)
                {
                    var mark = PanelLook.Caps(badge, 11, PanelLook.Brush(palette.Ice), FontWeights.SemiBold, 0);
                    mark.VerticalAlignment = VerticalAlignment.Center;
                    mark.Margin = new Thickness(6, 0, 0, 0);
                    Grid.SetColumn(mark, 2);
                    row.Children.Add(mark);
                }
                var cell = new Grid();
                cell.Children.Add(row);
                if (index > 0)
                {
                    var divider = PanelLook.Hairline(PanelLook.Brush(palette.GridLine));
                    divider.VerticalAlignment = VerticalAlignment.Top;
                    cell.Children.Add(divider);
                }
                list.Children.Add(new PanelButton(cell, () => handlers.RunFavourite(entry), Brushes.Transparent,
                    PanelLook.Brush(palette.PanelRaised), PaletteMenu.EntryLabel(entry)));
            }
            stack.Children.Add(new Border
            {
                BorderBrush = PanelLook.Brush(palette.GridLine),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, Theme.Space.M, 0, 0),
                SnapsToDevicePixels = true,
                Child = list,
            });
        }
        else
        {
            var empty = PanelLook.Body(PaletteMenu.NoFavourites, 12, PanelLook.Brush(palette.Steel));
            empty.Margin = new Thickness(0, Theme.Space.M, 0, 0);
            stack.Children.Add(empty);
        }

        var footer = PanelLook.WrappingLabel(PaletteMenu.FooterText(state.Keymap, state.Options.KeepVisible), 9, null,
            Width - 2 * padding, caps: true);
        footer.Margin = new Thickness(0, Theme.Space.M, 0, 0);
        stack.Children.Add(footer);
        return stack;
    }

    /// <summary>"Shift Add  Alt Subtract  Shift+Alt Intersect": the held one in a light fill.</summary>
    private static FrameworkElement FlatModeHint(PaletteMaskMode held)
    {
        var palette = PanelLook.Palette;
        var hint = new WrapPanel();
        foreach (var mode in PaletteMaskModes.Modifying)
        {
            var lit = mode == held;
            hint.Children.Add(new Border
            {
                Background = lit ? PanelLook.Brush(palette.Ice) : Brushes.Transparent,
                Padding = new Thickness(6, 3, 6, 3),
                Margin = new Thickness(0, 0, 6, 0),
                // The panel's own dark on the light fill: OnMagenta is white here, as the fill nearly is.
                Child = PanelLook.Caps(mode.HintText(), 9, PanelLook.Brush(lit ? palette.Abyss : palette.Steel),
                    lit ? FontWeights.SemiBold : FontWeights.Medium, 0.12),
            });
        }
        AutomationProperties.SetName(hint, PaletteMenu.ModeHintLabel);
        return hint;
    }

    private static FrameworkElement FlatToolCell(string id, PaletteState state, PaletteHandlers handlers)
    {
        var palette = PanelLook.Palette;
        var tool = PaletteMenu.MaskTool(id);
        var title = tool?.Title ?? id;
        var badge = state.Masking ? state.Held.Badge() : null;
        var ice = PanelLook.Brush(palette.Ice);
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        var icon = PanelLook.Icon(tool?.Symbol ?? "circle.dashed", 16, ice);
        var iconBox = new Grid { Height = 18 };
        iconBox.Children.Add(icon);
        stack.Children.Add(iconBox);
        stack.Children.Add(new Viewbox
        {
            Child = PanelLook.Caps(title, 9, ice, tracking: 0.08),
            StretchDirection = StretchDirection.DownOnly,
            MaxWidth = CellWidth - 8,
            Margin = new Thickness(4, 7, 4, 0),
        });
        var face = new Grid { Width = CellWidth, Height = 62 };
        face.Children.Add(stack);
        if (badge is not null)
        {
            var mark = PanelLook.Caps(badge, 11, ice, FontWeights.SemiBold, 0);
            mark.HorizontalAlignment = HorizontalAlignment.Right;
            mark.VerticalAlignment = VerticalAlignment.Top;
            mark.Margin = new Thickness(0, 3, 5, 0);
            face.Children.Add(mark);
        }
        return ToolCell(face, id, title, state.Masking, handlers, PanelLook.Brush(palette.Panel), PanelLook.Brush(palette.PanelRaised));
    }
}
