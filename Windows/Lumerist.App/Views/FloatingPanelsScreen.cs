using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Lumerist.App.Design;
using Lumerist.App.Design.Controls;
using Lumerist.App.Services;
using Lumerist.Core;

namespace Lumerist.App.Views;

/// <summary>
/// The three panels that float over Lightroom: Value HUD, Palette and Key
/// Reference (FloatingPanelsScreen.swift, HUDScreen.swift,
/// KeysPaletteScreen.swift, KeyReferenceScreen.swift).
/// </summary>
internal sealed class FloatingPanelsScreen(ScreenContext context) : Screen(context)
{
    private IPanelSettings? subscribed;
    private bool askedForPresets;

    public override void Refresh()
    {
        if (Context.Panels is { } panels && !ReferenceEquals(panels, subscribed))
        {
            panels.StateChanged += Refresh;
            subscribed = panels;
        }
        if (!askedForPresets && !Context.IsSnapshot && Controller.Presets.Count == 0)
        {
            askedForPresets = true;
            Controller.RequestPresets();
        }
        base.Refresh();
    }

    public override void Leave()
    {
        if (subscribed is not null) subscribed.StateChanged -= Refresh;
        subscribed = null;
        askedForPresets = false;
        // The sample, and a placing in progress, end with the page.
        Context.Panels?.HideHudPreview();
    }

    protected override FrameworkElement Build() =>
        Layout.VStack(Theme.Space.XL,
            Surfaces.ScreenHeader("Floating panels", "above Lightroom · never take focus"),
            HudSection(),
            PaletteDemo(),
            PaletteSection(),
            Favourites(),
            KeyReferenceSection());

    private OverlaySettings Overlay => Controller.Settings.Overlay;

    /// <summary>Saves the HUD settings and shows the sample with them.</summary>
    private void SaveOverlay(bool preview = true)
    {
        Controller.SaveSettings();
        if (preview) Context.Panels?.PreviewHud();
    }

    // Value HUD ------------------------------------------------------------------

    private FrameworkElement HudSection()
    {
        var overlay = Overlay;
        var panels = Context.Panels;
        var isPlacing = panels?.IsPlacingHud ?? false;
        var never = SettingsChoices.HideAfterNeverValue(OverlaySettings.DurationRange.Maximum);

        var place = SynthControls.Button(isPlacing ? "Done" : "Place the HUD",
            isPlacing ? SynthControls.Kind.Primary : SynthControls.Kind.Secondary, () =>
            {
                if (panels is null) return;
                if (panels.IsPlacingHud) panels.EndPlacingHud();
                else panels.BeginPlacingHud();
            });
        AutomationProperties.SetHelpText(place, isPlacing ? "Keeps the HUD where it is now" : "Shows a sample HUD you can drag");
        place.IsEnabled = panels is not null;
        var reset = SynthControls.Button("Reset position", SynthControls.Kind.Secondary, () =>
        {
            Overlay.Placement = OverlaySettings.PlacementBand.Bottom;
            Overlay.VerticalOffset = 0;
            Overlay.HorizontalOffset = 0;
            SaveOverlay();
        });
        AutomationProperties.SetHelpText(reset, "Puts the HUD back at the bottom centre of the screen");

        return Surfaces.SettingsSection("Value HUD", Icons.Gauge,
            "The HUD never takes focus from Lightroom. Colour is only a help: the slider's name and the key are always written out.",
            Surfaces.SettingRow("Show the value while adjusting",
                "Every Dial Key or Nudge Key change shows the slider, its new value and where it sits in its range.",
                SynthControls.Toggle(overlay.IsEnabled, value =>
                {
                    Overlay.IsEnabled = value;
                    SaveOverlay(preview: false);
                }, "Show the value while adjusting")),
            StyleRow(overlay.Style),
            Surfaces.SettingRow("Panel opacity", "How much of the photo shows through the dark panel behind the value.",
                SynthControls.Slider(overlay.ClampedBackgroundOpacity * 100, 0, 100, 5,
                    value => $"{(int)Math.Round(value, MidpointRounding.AwayFromZero)} %",
                    _ => SaveOverlay(),
                    "HUD panel opacity",
                    onLive: value =>
                    {
                        Overlay.BackgroundOpacity = Math.Round(value / 100, 2, MidpointRounding.AwayFromZero);
                        Context.Panels?.PreviewHud();
                    })),
            Surfaces.SettingRow("Hide after", "How long the HUD stays after your last change. Never keeps it on screen all the time.",
                SynthControls.Slider(overlay.KeepVisible ? never : overlay.ClampedDuration, OverlaySettings.DurationRange.Minimum, never, 0.1,
                    value => SettingsChoices.HideAfterIsNever(value, OverlaySettings.DurationRange.Maximum) ? "Never" : SettingsChoices.Seconds(value),
                    _ => SaveOverlay(),
                    "Hide the HUD after",
                    SettingsChoices.Seconds(OverlaySettings.DurationRange.Minimum), "Never",
                    onLive: value =>
                    {
                        if (SettingsChoices.HideAfterIsNever(value, OverlaySettings.DurationRange.Maximum))
                        {
                            Overlay.KeepVisible = true;
                        }
                        else
                        {
                            Overlay.KeepVisible = false;
                            Overlay.Duration = value;
                        }
                        Context.Panels?.PreviewHud();
                    })),
            Surfaces.SettingRow("Display",
                "Only matters with two or more displays. With Lightroom: the HUD always appears on the display that shows Lightroom's window. With the pointer: it appears on whichever display the pointer is on.",
                SynthControls.Segmented([true, false], overlay.FollowsLightroomWindow, OverlaySettings.ScreenName, value =>
                {
                    Overlay.FollowsLightroomWindow = value;
                    SaveOverlay();
                }, name: "Display for the HUD")),
            Surfaces.SettingRow("Position",
                isPlacing
                    ? "Drag the sample anywhere on the screen, then click Done here or under the sample."
                    : "Where the HUD sits on that display. Place the HUD shows a sample you drag to the spot you like; Reset position puts it back at the bottom centre.",
                Layout.HStack(Theme.Space.S, place, reset)));
    }

    /// <summary>A full-width row: the two styles side by side, each with a miniature.</summary>
    private FrameworkElement StyleRow(OverlaySettings.HudStyle selected)
    {
        var cards = new UniformGrid { Columns = 2, Rows = 1 };
        foreach (var style in new[] { OverlaySettings.HudStyle.Ruler, OverlaySettings.HudStyle.Compact })
        {
            var card = StyleCard(style, selected == style, () =>
            {
                Overlay.Style = style;
                SaveOverlay();
            });
            card.Margin = new Thickness(style == OverlaySettings.HudStyle.Ruler ? 0 : Theme.Space.M / 2, 0,
                style == OverlaySettings.HudStyle.Ruler ? Theme.Space.M / 2 : 0, 0);
            cards.Children.Add(card);
        }
        var content = Layout.VStack(Theme.Space.S, SynthText.Body("Style", 13, FontWeights.SemiBold, Ink.Ice), cards);
        var row = Surfaces.Row(content, 12, 14);
        row.BorderBrush = Ink.Faded(Ink.GridLine, 0.6);
        AutomationProperties.SetName(row, "HUD style");
        return row;
    }

    /// <summary>A selectable card with a miniature of the HUD style, dark as the real HUD always is.</summary>
    private static FrameworkElement StyleCard(OverlaySettings.HudStyle style, bool isSelected, Action select)
    {
        var name = OverlaySettings.StyleName(style);
        var detail = OverlaySettings.StyleDetail(style);
        FrameworkElement face;
        if (Theme.IsFlat)
        {
            var radio = SynthText.Icon(isSelected ? Icons.RadioOn : Icons.RadioOff, 12, isSelected ? Ink.Ice : Ink.Steel);
            radio.VerticalAlignment = VerticalAlignment.Top;
            radio.Margin = new Thickness(0, 3, 0, 0);
            var text = Layout.VStack(4, SynthText.Body(name, 15, FontWeights.SemiBold, Ink.Ice), SynthText.Body(detail, 13, color: Ink.Steel));
            var caption = Layout.Bar(8, [radio, text], []);
            caption.ColumnDefinitions[1].Width = new GridLength(1, GridUnitType.Star);
            caption.ColumnDefinitions[2].Width = new GridLength(0);
            face = new Border
            {
                Padding = new Thickness(18),
                Background = Ink.Panel,
                BorderBrush = isSelected ? Ink.Ice : Ink.GridLine,
                BorderThickness = new Thickness(isSelected ? 2 : 1),
                Child = Layout.VStack(14, FlatMiniature(style), caption),
            };
        }
        else
        {
            var title = Layout.HStack(6,
                SynthText.Icon(isSelected ? Icons.RadioOn : Icons.RadioOff, 11, isSelected ? Ink.Magenta : Ink.Steel),
                SynthText.Caps(name, Theme.DisplayFont, 13, FontWeights.Bold, 1, isSelected ? Ink.Ice : Ink.Body));
            var card = new Border
            {
                Padding = new Thickness(12),
                CornerRadius = new CornerRadius(6),
                Background = isSelected ? Ink.Faded(Ink.Magenta, 0.08) : Ink.PanelRaised,
                BorderBrush = isSelected ? Ink.Magenta : Ink.GridLine,
                BorderThickness = new Thickness(1),
                Child = Layout.VStack(Theme.Space.S, NeonMiniature(style), title, SynthText.Body(detail, 11, color: Ink.Steel)),
            };
            if (isSelected) card.Effect = SynthControls.Glow(Ink.Magenta.Color, 8, 0.35);
            face = card;
        }
        var button = SynthControls.Plain(face, select);
        button.VerticalAlignment = VerticalAlignment.Stretch;
        AutomationProperties.SetName(button, $"{name} style");
        AutomationProperties.SetItemStatus(button, isSelected ? "Selected" : "");
        return button;
    }

    /// <summary>Galerist: a small HUD in the charcoal of the real one, with the red needle.</summary>
    private static FrameworkElement FlatMiniature(OverlaySettings.HudStyle style)
    {
        var dark = ThemePalette.GaleristDark;
        SolidColorBrush Dark(uint hex, double opacity = 1) => Ink.Faded(new SolidColorBrush(Theme.FromHex(hex)), opacity);
        SpacedText Caps(string text, double size, double tracking = 0.18) =>
            SynthText.Caps(text, Theme.MonoFont, size, FontWeights.Medium, tracking * size, Dark(dark.Steel));
        TextBlock Value(string text, double size) => new()
        {
            Text = text,
            FontFamily = Theme.TextFont,
            FontSize = size,
            FontWeight = FontWeights.SemiBold,
            Foreground = Dark(dark.Ice),
        };

        FrameworkElement content;
        if (style == OverlaySettings.HudStyle.Ruler)
        {
            var value = Layout.HStack(5, Value("+0.35", 24), Caps("EV", 10, 0.1));
            var scale = new MiniScale(Dark(dark.GridLine), Dark(dark.Ice), Dark(dark.Disabled), Dark(dark.Live)) { Height = 14 };
            content = Layout.VStack(8, Layout.Bar(8, [Caps("Exposure", 9)], [Caps("[Q] Normal", 9)]), value, scale);
        }
        else
        {
            var bar = new Grid { Height = 2 };
            bar.Children.Add(new Border { Background = Dark(dark.Steel, 0.35) });
            bar.Children.Add(new ProportionalMark(0.5, 0.06, Dark(dark.Live)));
            content = Layout.VStack(8, Layout.Bar(8, [Caps("Contrast", 9)], [Value("+12", 17)]), bar);
        }
        return new Border
        {
            Padding = new Thickness(14, 12, 14, 12),
            Background = Dark(dark.Abyss),
            BorderBrush = Dark(dark.GridLine),
            BorderThickness = new Thickness(1),
            Child = content,
        };
    }

    private static FrameworkElement NeonMiniature(OverlaySettings.HudStyle style)
    {
        var value = SynthText.Mono("+0.35", style == OverlaySettings.HudStyle.Ruler ? 16 : 11, FontWeights.Bold, Brushes.White);
        FrameworkElement content;
        if (style == OverlaySettings.HudStyle.Ruler)
        {
            var line = new Grid { Height = 8 };
            line.Children.Add(new Border { Height = 1, Background = Ink.Faded(Ink.Steel, 0.5), VerticalAlignment = VerticalAlignment.Center });
            line.Children.Add(new Border { Width = 2, Height = 8, Background = Ink.Magenta, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(46, 0, 0, 0) });
            content = Layout.VStack(4, SynthText.Label("Exposure", 8, Ink.Teal), value, line);
            content.Margin = new Thickness(8);
        }
        else
        {
            var row = Layout.Bar(8, [SynthText.Label("Exposure", 8, Ink.Teal)], [value]);
            var grid = new Grid();
            row.Margin = new Thickness(6);
            grid.Children.Add(row);
            grid.Children.Add(new Border { Height = 2, Background = Ink.Magenta, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(6, 0, 6, 2) });
            content = grid;
        }
        return new Border
        {
            MinHeight = 64,
            CornerRadius = new CornerRadius(3),
            BorderBrush = Ink.Teal,
            BorderThickness = new Thickness(1),
            Child = content,
            VerticalAlignment = VerticalAlignment.Center,
        };
    }

    // Palette --------------------------------------------------------------------

    /// <summary>
    /// What the Palette looks like over Lightroom: the real panel with your
    /// favourites, for reference only (clicks do nothing), beside the words.
    /// </summary>
    private FrameworkElement PaletteDemo()
    {
        var keymap = Controller.Keymap;
        var key = keymap.SystemKeys.PaletteKey is { } chord ? keymap.DisplayText(chord) : null;
        var flat = Theme.IsFlat;
        var heading = SynthText.Title("This is the Palette", flat ? 20 : 15);
        AutomationProperties.SetHeadingLevel(heading, AutomationHeadingLevel.Level2);
        var words = Layout.VStack(flat ? Theme.Space.M : Theme.Space.S,
            heading,
            SynthText.Body(key is null
                    ? "Set a Palette Key below and this panel appears under the pointer in Develop."
                    : $"In Lightroom's Develop module, hold {key} and this panel appears under the pointer.",
                flat ? 15 : 13, color: Ink.Ice),
            SynthText.Body("Click a tool under Create New Mask to start a mask, or one of your favourite presets to apply it. With a mask already selected, hold Shift to add to it, Alt to subtract or both to intersect.",
                flat ? 13 : 12, color: Ink.Steel),
            SynthText.Body("Hold the key for a quick look: it closes when you let go. Tap it instead and the Palette stays open until you press the key again. The key never reaches Lightroom, so it does nothing else. Your favourites below show up in it straight away.",
                flat ? 13 : 12, color: Ink.Steel));

        var preview = Context.Panels?.CreatePalettePreview();
        if (preview is not null)
        {
            preview.IsHitTestVisible = false;
            preview.Focusable = false;
        }
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        if (flat)
        {
            // Galerist: the words on white, the Palette in its own grey well on the right, dark as over Lightroom.
            words.Margin = new Thickness(Theme.Space.XXL);
            grid.Children.Add(words);
            if (preview is not null)
            {
                var well = new Border
                {
                    Padding = new Thickness(Theme.Space.XL),
                    Background = Ink.Abyss,
                    BorderBrush = Ink.GridLine,
                    BorderThickness = new Thickness(1, 0, 0, 0),
                    Child = preview,
                };
                preview.VerticalAlignment = VerticalAlignment.Top;
                Grid.SetColumn(well, 1);
                grid.Children.Add(well);
            }
            return new Border { Background = Ink.Panel, BorderBrush = Ink.GridLine, BorderThickness = new Thickness(1), Child = grid };
        }
        grid.Children.Add(words);
        if (preview is not null)
        {
            preview.Margin = new Thickness(Theme.Space.XL, 0, 0, 0);
            preview.VerticalAlignment = VerticalAlignment.Top;
            Grid.SetColumn(preview, 1);
            grid.Children.Add(preview);
        }
        grid.Margin = new Thickness(Theme.Space.L);
        return Surfaces.Panel(grid);
    }

    private FrameworkElement PaletteSection()
    {
        var keymap = Controller.Keymap;
        var note = keymap.SystemKeys.PaletteKey is { } key
            ? $"In Develop, hold {keymap.DisplayText(key)} and the Palette opens under the pointer. Clear the key to switch the Palette off."
            : "The Palette is off: it has no key. Set one above.";
        var reset = SynthControls.Button("Reset position", SynthControls.Kind.Secondary, () => Context.Panels?.ResetPalettePosition());
        reset.IsEnabled = Context.Panels is not null;
        AutomationProperties.SetHelpText(reset, "The Palette opens under the pointer again");
        return Surfaces.SettingsSection("Palette", Icons.Palette, note,
            Surfaces.SettingRow("Palette Key",
                "Hold it in Develop to open the Palette under the pointer; let go to close it. Click the box and press another key to change it.",
                SystemKeySlot(SystemKeyRole.PaletteKey)),
            Surfaces.SettingRow("Position", "If you dragged the Palette somewhere, it opens there. Reset sends it back under the pointer.", reset));
    }

    private KeySlot SystemKeySlot(SystemKeyRole role)
    {
        KeySlot? slot = null;
        slot = new KeySlot($"system.{role.WireName()}", Controller.Keymap.SystemKeys.ChordFor(role), null, 64, role.Title(), Context.ComboKey,
            chord => KeymapEditor.SetSystem(Controller, role, chord, slot),
            () => KeymapEditor.SetSystem(Controller, role, null, slot));
        return slot;
    }

    private FrameworkElement Favourites()
    {
        var favourites = Controller.Keymap.Palette.Favourites;
        var heading = SynthText.Title("Palette favourites", 15);
        AutomationProperties.SetHeadingLevel(heading, AutomationHeadingLevel.Level2);
        var refresh = SynthControls.Button("Refresh presets", SynthControls.Kind.Quiet, Controller.RequestPresets);
        AutomationProperties.SetHelpText(refresh, "Reloads the Develop presets from Lightroom");

        var rows = Layout.VStack(0);
        if (favourites.Count == 0)
        {
            var empty = SynthText.Body("Nothing here yet. The main mask tools are always in the Palette under Create New Mask; add the presets and other mask tools you use most.",
                12, color: Ink.Steel);
            empty.Margin = new Thickness(14);
            rows.Children.Add(empty);
        }
        for (var index = 0; index < favourites.Count; index++) rows.Children.Add(FavouriteRow(favourites[index], index, favourites.Count));

        var presets = Controller.Presets;
        var groups = presets.GroupBy(preset => preset.Group, StringComparer.Ordinal).OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => new SynthControls.MenuGroup<PresetItem>(group.Key, group.ToList())).ToList();
        var addPreset = SynthControls.MenuButton(presets.Count == 0 ? "Add preset… (refresh first)" : "Add preset…",
            "Add a Develop preset to the Palette favourites", groups, null, preset => preset.Name, preset =>
                KeymapEditor.Edit(Controller, keymap => keymap.Palette.Favourites.Add(
                    new PaletteEntry(PaletteEntryKind.Preset, preset.Id, null, preset.Name))));
        addPreset.Width = 240;
        var addTool = SynthControls.MenuButton("Add mask tool…", "Add a mask tool to the Palette favourites", MaskTool.All, null,
            tool => tool.Title, tool => KeymapEditor.Edit(Controller, keymap => keymap.Palette.Favourites.Add(
                new PaletteEntry(PaletteEntryKind.Tool, LightroomCommandName.CreateMask.WireName(), tool.Id, tool.Title))));
        addTool.Width = 200;
        var adders = Layout.HStack(Theme.Space.S, addPreset, addTool);
        adders.Margin = new Thickness(12);
        rows.Children.Add(adders);

        return Layout.VStack(Theme.Space.S,
            Layout.Bar(Theme.Space.M, [heading], [refresh]),
            Surfaces.Panel(rows),
            SynthText.Body("With a mask selected in Lightroom, hold Shift to add a mask tool to it, Alt to subtract, or both to intersect.",
                12, color: Ink.Steel));
    }

    private FrameworkElement FavouriteRow(PaletteEntry entry, int index, int count)
    {
        var isPreset = entry.Kind == PaletteEntryKind.Preset;
        var up = SynthControls.IconButton(Icons.ArrowUp, SynthControls.Kind.Quiet, () => Move(index, -1), $"Move {entry.Title} up");
        up.IsEnabled = index > 0;
        var down = SynthControls.IconButton(Icons.ArrowDown, SynthControls.Kind.Quiet, () => Move(index, 1), $"Move {entry.Title} down");
        down.IsEnabled = index < count - 1;
        var remove = SynthControls.IconButton(Icons.Cancel, SynthControls.Kind.Quiet,
            () => KeymapEditor.Edit(Controller, keymap => keymap.Palette.Favourites.RemoveAt(index)), $"Remove {entry.Title}");
        var row = Layout.Bar(Theme.Space.M,
            [
                SynthText.Icon(isPreset ? Icons.Sliders : Icons.DashedCircle, 13, isPreset ? Ink.MagentaLight : Ink.Teal),
                SynthText.Body(entry.Title, 13, FontWeights.Medium, Ink.Ice),
                SynthText.Label(isPreset ? "Preset" : "Mask tool", 9),
            ],
            [up, down, remove]);
        return new Border
        {
            Child = row,
            Padding = new Thickness(14, 0, 14, 0),
            MinHeight = 38,
            BorderBrush = Ink.Faded(Ink.GridLine, 0.6),
            BorderThickness = new Thickness(0, 0, 0, 1),
        };
    }

    private void Move(int index, int offset) => KeymapEditor.Edit(Controller, keymap =>
    {
        var list = keymap.Palette.Favourites;
        var target = index + offset;
        if (target < 0 || target >= list.Count) return;
        (list[index], list[target]) = (list[target], list[index]);
    });

    // Key Reference --------------------------------------------------------------

    private FrameworkElement KeyReferenceSection()
    {
        var panels = Context.Panels;
        var show = SynthControls.Toggle(panels?.IsKeyReferenceVisible ?? false, value => panels?.SetKeyReferenceVisible(value),
            "Show the Key Reference");
        show.IsEnabled = panels is not null;
        var reset = SynthControls.Button("Reset position", SynthControls.Kind.Secondary, () => panels?.ResetKeyReferencePosition());
        reset.IsEnabled = panels is not null;
        return Surfaces.SettingsSection("Key Reference", Icons.List, "Move it by its title bar.",
            Surfaces.SettingRow("Key Reference key",
                "Press it in Develop or Library to show or hide the list. Click the box and press another key, or hold Tab with it for a combo.",
                SystemKeySlot(SystemKeyRole.KeyReferenceToggle)),
            Surfaces.SettingRow("Show the Key Reference",
                "A list of the keys that work in Lightroom's current module, floating above Lightroom. Clicking it leaves Lightroom in front.",
                show),
            Surfaces.SettingRow("Position", "Sends the panel back to the top-right corner of the screen.", reset));
    }
}

/// <summary>The Ruler miniature's scale: a hairline, eleven ticks with a white zero, the red needle.</summary>
internal sealed class MiniScale(Brush line, Brush zero, Brush tick, Brush needle) : FrameworkElement
{
    protected override void OnRender(DrawingContext drawing)
    {
        var width = ActualWidth;
        if (width <= 0) return;
        drawing.DrawRectangle(line, null, new Rect(0, 0, width, 1));
        for (var index = 0; index <= 10; index++)
        {
            var x = Math.Floor(width * index / 10) - (index == 10 ? 1 : 0);
            drawing.DrawRectangle(index == 5 ? zero : tick, null, new Rect(x, 0, 1, index == 5 ? 11 : 5));
        }
        drawing.DrawRectangle(needle, null, new Rect(Math.Round(width * 0.535), 0, 2, 13));
    }
}

/// <summary>A segment placed at a fraction of the width (the Compact miniature's red bar).</summary>
internal sealed class ProportionalMark(double start, double length, Brush fill) : FrameworkElement
{
    protected override void OnRender(DrawingContext drawing)
    {
        var width = ActualWidth;
        if (width <= 0) return;
        drawing.DrawRectangle(fill, null, new Rect(Math.Round(width * start), 0, Math.Round(width * length), ActualHeight));
    }
}
