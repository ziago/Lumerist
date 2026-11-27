using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Lumerist.App.Design;
using Lumerist.App.Design.Controls;
using Lumerist.Core;

namespace Lumerist.App.Views;

/// <summary>
/// Every catalog parameter of a scope, grouped by panel (SlidersTab.swift):
/// its Dial Key (hold), its Nudge Keys (tap − / +) and the steps they move
/// by. A Nudge Key can be a combo on its own.
/// </summary>
internal static class SlidersTab
{
    private const double ColumnSpacing = 10;
    private const double DialColumnWidth = 86;
    /// <summary>Wide enough for a combo such as "Tab + Shift+]".</summary>
    private const double NudgeColumnWidth = 96;
    /// <summary>Wide enough for "0.05 · Shift 0.33" (the Mac's "⇧" is a word here).</summary>
    private const double StepColumnWidth = 132;

    public static FrameworkElement Build(ModulePage page)
    {
        var context = page.Context;
        var controller = context.Controller;
        var keymap = controller.Keymap;
        var scope = page.Scope;
        var state = page.State;
        var conflicts = new ConflictIndex(keymap, scope);
        var sections = SlidersTabText.FilteredSections(keymap, scope, state.Search);
        var swapped = keymap.Input.SwapNormalAndCoarse;
        var unsupported = controller.CatalogCheck?.Unsupported.ToHashSet(StringComparer.Ordinal) ?? [];

        var legend = SynthText.Label(SlidersTabText.Legend(keymap.SystemKeys.ComboKey), 10, wrap: true);
        var stack = Layout.VStack(Theme.Space.S,
            LargeStepToggle(controller, swapped),
            Layout.Bar(Theme.Space.M, [legend], [Surfaces.ExpandCollapseButtons(
                () =>
                {
                    state.ExpandedSections.UnionWith(ParameterCatalog.Sections(scope).Select(section => section.Section));
                    page.Refresh();
                },
                () =>
                {
                    state.ExpandedSections.Clear();
                    page.Refresh();
                })], stretchFirst: true));

        if (sections.Count == 0)
        {
            var none = SynthText.Body($"No slider or key matches “{state.Search}”.", 13, color: Ink.Steel);
            none.Margin = new Thickness(0, 20, 0, 20);
            stack.Children.Add(none);
        }

        FrameworkElement Header(string section, IReadOnlyList<ParameterDescriptor> parameters, bool isOpen) =>
            Surfaces.CollapsibleHeader(section, SlidersTabText.AssignedCount(parameters, keymap), isOpen, () =>
            {
                if (!state.ExpandedSections.Remove(section)) state.ExpandedSections.Add(section);
                page.Refresh();
            });

        FrameworkElement Table(IReadOnlyList<ParameterDescriptor> parameters)
        {
            var rows = Layout.VStack(0, TableHeader(scope));
            foreach (var descriptor in parameters)
                rows.Children.Add(ParameterRow(page, descriptor, keymap.Slot(descriptor.Key), conflicts, swapped, unsupported.Contains(descriptor.Id)));
            return Surfaces.Panel(rows);
        }

        var searching = state.Search.Trim().Length > 0;
        if (Theme.IsFlat)
        {
            // Galerist: open groups stand apart; closed ones form one hairline list (rows share their lines).
            var list = Layout.VStack(0);
            foreach (var (section, parameters) in sections)
            {
                var isOpen = searching || state.ExpandedSections.Contains(section);
                if (isOpen)
                {
                    var group = Layout.VStack(10, Header(section, parameters, true), Table(parameters));
                    group.Margin = new Thickness(0, 10, 0, 10);
                    list.Children.Add(group);
                }
                else
                {
                    var header = Header(section, parameters, false);
                    header.Margin = new Thickness(0, -1, 0, 0);
                    list.Children.Add(header);
                }
            }
            stack.Children.Add(list);
        }
        else
        {
            foreach (var (section, parameters) in sections)
            {
                var isOpen = searching || state.ExpandedSections.Contains(section);
                stack.Children.Add(Header(section, parameters, isOpen));
                if (isOpen) stack.Children.Add(Table(parameters));
            }
        }
        if (KeymapEditor.LastError is { } error) stack.Children.Add(SynthText.ErrorLine(error));
        // A Nudge Key just assigned on one of Lightroom's keys: never blocking, shown while it lasts.
        else if (KeymapEditor.CurrentWarning(keymap, page.Scope) is { } warning) stack.Children.Add(SynthText.WarningLine(warning));
        return stack;
    }

    /// <summary>"Large step by default": the keys use the large step and Shift the small one.</summary>
    private static FrameworkElement LargeStepToggle(AppController controller, bool isOn)
    {
        var text = Layout.VStack(Theme.IsFlat ? 3 : 2,
            SynthText.Body("Large step by default", Theme.IsFlat ? 14 : 13, FontWeights.SemiBold, Ink.Ice),
            SynthText.Body("When on, Dial and Nudge Keys move by the large step and Shift gives the small one.",
                Theme.IsFlat ? 12.5 : 12, color: Ink.Steel));
        var toggle = SynthControls.Toggle(isOn, value => KeymapEditor.Edit(controller, keymap => keymap.Input.SwapNormalAndCoarse = value),
            "Large step by default");
        var row = Layout.Bar(Theme.Space.L, [text], [toggle], stretchFirst: true);
        row.Margin = new Thickness(Surfaces.RowInset, Theme.IsFlat ? 14 : 10, Surfaces.RowInset, Theme.IsFlat ? 14 : 10);
        return Surfaces.Panel(row);
    }

    /// <summary>A grid with the table's columns: name (the rest), Dial Key, Nudge −, Nudge +, Step.</summary>
    private static Grid Columns()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        foreach (var width in new[] { DialColumnWidth, NudgeColumnWidth, NudgeColumnWidth, StepColumnWidth })
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(width + ColumnSpacing) });
        return grid;
    }

    private static void Place(Grid grid, UIElement element, int column)
    {
        if (column > 0 && element is FrameworkElement framework)
        {
            framework.Margin = new Thickness(ColumnSpacing, 0, 0, 0);
            framework.HorizontalAlignment = HorizontalAlignment.Left;
        }
        if (element is FrameworkElement aligned) aligned.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(element, column);
        grid.Children.Add(element);
    }

    private static FrameworkElement TableHeader(ParameterScope scope)
    {
        var grid = Columns();
        Place(grid, SynthText.Label(scope == ParameterScope.Develop ? "Adjustment" : "Quick Develop", 10), 0);
        // The key types, each explained on hover.
        var column = 1;
        foreach (var role in SlotRoles.All)
            Place(grid, Tooltips.Explained(SynthText.Label(role.Title(), 10), KeyTypeHelp.For(role, scope)), column++);
        Place(grid, Tooltips.Explained(SynthText.Label("Step", 10), KeyTypeHelp.Step(scope)), column);
        var header = new Border
        {
            Child = grid,
            Padding = new Thickness(Surfaces.RowInset, Theme.IsFlat ? 11 : 8, Surfaces.RowInset, Theme.IsFlat ? 11 : 8),
            // Galerist: a soft band behind the column names.
            Background = Theme.IsFlat ? Ink.PanelRaised : Brushes.Transparent,
            BorderBrush = Ink.GridLine,
            BorderThickness = new Thickness(0, 0, 0, 1),
        };
        return header;
    }

    private static FrameworkElement ParameterRow(ModulePage page, ParameterDescriptor descriptor, ParameterSlot? slot,
        ConflictIndex conflicts, bool swapped, bool isUnsupported)
    {
        var context = page.Context;
        var key = descriptor.Key;
        var grid = Columns();

        var label = SynthText.Body(descriptor.Label, 13, FontWeights.Medium, Ink.Ice);
        label.TextWrapping = TextWrapping.NoWrap;
        label.TextTrimming = TextTrimming.CharacterEllipsis;
        var name = Layout.HStack(6, label, SynthText.Label(SlidersTabText.UnitLabel(descriptor.Unit), 9));
        if (isUnsupported)
        {
            var warning = SynthText.Icon(Icons.Warning, 11, Ink.Amber);
            warning.ToolTip = $"This Lightroom did not accept {descriptor.Id} in the last catalog check.";
            AutomationProperties.SetName(warning, "not in this Lightroom");
            name.Children.Add(warning);
        }
        Place(grid, name, 0);

        var column = 1;
        foreach (var role in SlotRoles.All)
        {
            var chord = role.Chord(slot);
            KeySlot? box = null;
            box = new KeySlot($"{key}.{role.WireName()}", chord, chord is null ? null : conflicts[role.Usage(key)],
                role == SlotRole.Dial ? 52 : 40, $"{descriptor.Label} {role.Title()}", context.ComboKey,
                recorded => KeymapEditor.Assign(context.Controller, recorded, role, key, box),
                () => KeymapEditor.Assign(context.Controller, null, role, key, box));
            Place(grid, box, column++);
        }
        Place(grid, StepCell(page, descriptor, slot, swapped), column);

        return new Border
        {
            Child = grid,
            Padding = new Thickness(Surfaces.RowInset, 0, Surfaces.RowInset, 0),
            MinHeight = Theme.IsFlat ? 44 : 40,
            BorderBrush = Ink.RowDivider,
            BorderThickness = new Thickness(0, 0, 0, 1),
        };
    }

    /// <summary>
    /// The steps as the keys use them now: the key alone, then with Shift
    /// ("1 · Shift 5"). Highlighted when they differ from the defaults; a
    /// click opens the step editor.
    /// </summary>
    private static FrameworkElement StepCell(ModulePage page, ParameterDescriptor descriptor, ParameterSlot? slot, bool swapped)
    {
        if (SlidersTabText.IsChoice(descriptor))
        {
            // Choice parameters move one option at a time.
            var option = SynthText.Label("1 option", 9);
            option.ToolTip = "Each step moves to the next option.";
            return option;
        }
        var steps = StepPair.For(descriptor, slot, swapped);
        var weight = Theme.IsFlat && !steps.IsCustom ? FontWeights.Normal : FontWeights.SemiBold;
        var color = steps.IsCustom ? Ink.MagentaLight : Ink.Body;
        var text = new SpacedText
        {
            Text = $"{StepPair.Text(steps.Plain)} · Shift {StepPair.Text(steps.Shifted)}",
            FontFamily = Theme.MonoFont,
            FontSize = 12,
            FontWeight = weight,
            Foreground = color,
            MinimumScale = 0.8,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var face = new Border
        {
            Child = text,
            Padding = new Thickness(8, 0, 8, 0),
            MinHeight = 26,
            CornerRadius = new CornerRadius(Theme.IsFlat ? 0 : 4),
            Background = steps.IsCustom && !Theme.IsFlat ? Ink.Faded(Ink.Magenta, 0.10) : Brushes.Transparent,
            // Galerist: your own steps get the ink border of a key; the defaults a hairline.
            BorderBrush = steps.IsCustom ? Ink.Magenta : Theme.IsFlat ? Ink.Outline : Ink.GridLine,
            BorderThickness = new Thickness(1),
        };
        Button? button = null;
        button = SynthControls.Plain(face, () => StepEditor.Open(page, descriptor, button!));
        button.HorizontalAlignment = HorizontalAlignment.Left;
        button.MaxWidth = StepColumnWidth;
        button.ToolTip = steps.IsCustom ? "Your own steps. Click to change them." : "Click to change the steps";
        AutomationProperties.SetName(button, $"{descriptor.Label} steps");
        AutomationProperties.SetItemStatus(button,
            $"{StepPair.Text(steps.Plain)}, with Shift {StepPair.Text(steps.Shifted)}" + (steps.IsCustom ? ", changed" : ""));
        AutomationProperties.SetHelpText(button, "Opens the step editor");
        return button;
    }
}

/// <summary>
/// The popover under a step cell: the step of the key alone and with Shift,
/// within the parameter's allowed range, and a way back to the defaults.
/// The page holds its rebuild while it is open, so the popover keeps its place.
/// </summary>
internal static class StepEditor
{
    public static void Open(ModulePage page, ParameterDescriptor descriptor, FrameworkElement anchor)
    {
        var controller = page.Context.Controller;
        var frame = new Border
        {
            Width = 280,
            Padding = new Thickness(14),
            Background = Theme.IsFlat ? Ink.Panel : Ink.PanelRaised,
            BorderBrush = Ink.Outline,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(Theme.IsFlat ? 0 : 6),
        };
        var popup = new Popup
        {
            PlacementTarget = anchor,
            Placement = PlacementMode.Bottom,
            VerticalOffset = 4,
            StaysOpen = false,
            AllowsTransparency = true,
            Child = frame,
        };
        var update = Content(controller, descriptor, frame);
        page.Hold();
        controller.KeymapChanged += update;
        popup.Closed += (_, _) =>
        {
            controller.KeymapChanged -= update;
            page.Release();
        };
        popup.IsOpen = true;
    }

    /// <summary>
    /// Builds the popover's content once and returns how to bring it up to
    /// date: the fields keep the focus while a change is saved.
    /// </summary>
    private static Action Content(AppController controller, ParameterDescriptor descriptor, Border frame)
    {
        var swapped = controller.Keymap.Input.SwapNormalAndCoarse;
        var defaults = SynthText.Body("", 11, color: Ink.Steel);
        var back = SynthControls.Button("Back to default", SynthControls.Kind.Quiet, () =>
            KeymapEditor.Edit(controller, edited => edited.UpdateSlot(descriptor.Key, slot => slot with { NormalStep = null, CoarseStep = null })));
        AutomationProperties.SetName(back, $"Back to the default steps of {descriptor.Label}");
        var (alone, aloneBox) = Field(controller, descriptor, "Key alone", normal: !swapped);
        var shifted = Field(controller, descriptor, "With Shift", normal: swapped);
        frame.Child = Layout.VStack(Theme.Space.M,
            Layout.VStack(2,
                SynthText.Title(descriptor.Label, 14),
                SynthText.Label($"From {StepPair.Text(descriptor.StepMinimum)} to {StepPair.Text(descriptor.StepMaximum)} {SlidersTabText.EditorUnit(descriptor.Unit)}", 9)),
            alone,
            shifted.Row,
            Layout.Bar(Theme.Space.S, [defaults], [back], stretchFirst: true));

        void Update()
        {
            var keymap = controller.Keymap;
            var slot = keymap.Slot(descriptor.Key);
            var steps = StepPair.For(descriptor, slot, swapped);
            defaults.Text = $"Default {StepPair.Text(steps.DefaultPlain)} · Shift {StepPair.Text(steps.DefaultShifted)}";
            back.IsEnabled = steps.IsCustom;
            Show(aloneBox, slot, normal: !swapped);
            Show(shifted.Box, slot, normal: swapped);
        }

        void Show(TextBox box, ParameterSlot? slot, bool normal)
        {
            if (box.IsKeyboardFocusWithin) return;
            var current = (normal ? slot?.NormalStep : slot?.CoarseStep) ?? (normal ? descriptor.NormalStep : descriptor.CoarseStep);
            box.Text = StepPair.FieldText(current);
        }

        Update();
        return Update;
    }

    /// <summary>One step field; <paramref name="normal"/> picks the stored value it edits (the small step, or the large one).</summary>
    private static (FrameworkElement Row, TextBox Box) Field(AppController controller, ParameterDescriptor descriptor, string title, bool normal)
    {
        var slot = controller.Keymap.Slot(descriptor.Key);
        var current = (normal ? slot?.NormalStep : slot?.CoarseStep) ?? (normal ? descriptor.NormalStep : descriptor.CoarseStep);
        var field = SynthControls.Field(StepPair.FieldText(current), 84, text =>
        {
            if (StepPair.Parse(text) is not { } value) return;
            // Clamped into the allowed range; the default value means "no override".
            var stored = StepPair.Stored(descriptor, value, normal);
            KeymapEditor.Edit(controller, keymap => keymap.UpdateSlot(descriptor.Key, existing =>
                normal ? existing with { NormalStep = stored } : existing with { CoarseStep = stored }));
        }, $"{descriptor.Label} step, {(title == "With Shift" ? "with Shift" : "key alone")}");
        return (Layout.Bar(Theme.Space.S, [SynthText.Body(title, 13, FontWeights.Medium, Ink.Ice)], [field], stretchFirst: true), field);
    }
}
