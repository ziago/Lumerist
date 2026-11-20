using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Lumerist.App.Design;
using Lumerist.App.Design.Controls;
using Lumerist.Core;

namespace Lumerist.App.Views;

/// <summary>Command Keys of a module, grouped, with add / edit / duplicate / delete / run (CommandsTab.swift).</summary>
internal static class CommandsTab
{
    public static FrameworkElement Build(ModulePage page)
    {
        var context = page.Context;
        var controller = context.Controller;
        var keymap = controller.Keymap;
        var scope = page.Scope;
        var state = page.State;
        var conflicts = new ConflictIndex(keymap, scope);
        var keys = CommandTitles.Filter(keymap.CommandKeysIn(scope), state.Search, keymap);
        var groups = CommandTitles.OrderedGroups(keys);

        var add = SynthControls.Button("Add Command Key", SynthControls.Kind.Primary, () =>
        {
            var blank = new CommandKey(Guid.NewGuid(), scope == ParameterScope.Develop ? CommandScope.Develop : CommandScope.Library,
                default, "Lightroom", "", new CommandAction.Command(LightroomCommandName.AutoTone.WireName()));
            ActionEditor.Open(page.View, controller, blank, isNew: true);
        }, icon: Icons.Add);
        var hint = SynthText.Body(CommandTitles.Hint(keymap.SystemKeys.ComboKey), 12, color: Ink.Steel);
        var stack = Layout.VStack(Theme.Space.S,
            Layout.Bar(Theme.Space.M, [add, hint], [Surfaces.ExpandCollapseButtons(
                () =>
                {
                    state.CollapsedGroups.Clear();
                    page.Refresh();
                },
                () =>
                {
                    state.CollapsedGroups.UnionWith(groups.Select(group => group.Group));
                    page.Refresh();
                })]));
        // The hint takes the free width and wraps.
        if (stack.Children[0] is Grid bar)
        {
            bar.ColumnDefinitions[1].Width = new GridLength(1, GridUnitType.Star);
            bar.ColumnDefinitions[2].Width = new GridLength(0);
        }

        if (keys.Count == 0)
        {
            var empty = SynthText.Body(state.Search.Trim().Length == 0
                ? $"No Command Keys in {(scope == ParameterScope.Develop ? "Develop" : "Library")} yet."
                : $"Nothing matches “{state.Search}”.", 13, color: Ink.Steel);
            empty.Margin = new Thickness(14);
            stack.Children.Add(Surfaces.Panel(empty));
        }

        FrameworkElement Header(string group, int count, bool isOpen) =>
            Surfaces.CollapsibleHeader(group, count, isOpen, () =>
            {
                if (!state.CollapsedGroups.Remove(group)) state.CollapsedGroups.Add(group);
                page.Refresh();
            });

        FrameworkElement GroupPanel(IReadOnlyList<CommandKey> items)
        {
            var rows = Layout.VStack(0);
            foreach (var commandKey in items)
                rows.Children.Add(Row(page, commandKey, conflicts[new KeyUsage.Action(commandKey.Id, commandKey.Title)]));
            return Surfaces.Panel(rows);
        }

        if (Theme.IsFlat)
        {
            // Galerist: open groups stand apart; closed ones form one hairline list.
            var list = Layout.VStack(0);
            foreach (var (group, items) in groups)
            {
                if (!state.CollapsedGroups.Contains(group))
                {
                    var open = Layout.VStack(10, Header(group, items.Count, true), GroupPanel(items));
                    open.Margin = new Thickness(0, 10, 0, 10);
                    list.Children.Add(open);
                }
                else
                {
                    var closed = Header(group, items.Count, false);
                    closed.Margin = new Thickness(0, -1, 0, 0);
                    list.Children.Add(closed);
                }
            }
            stack.Children.Add(list);
        }
        else
        {
            foreach (var (group, items) in groups)
            {
                var isOpen = !state.CollapsedGroups.Contains(group);
                stack.Children.Add(Header(group, items.Count, isOpen));
                if (isOpen) stack.Children.Add(GroupPanel(items));
            }
        }
        return stack;
    }

    private static FrameworkElement Row(ModulePage page, CommandKey commandKey, string? conflict)
    {
        var controller = page.Context.Controller;
        var keymap = controller.Keymap;
        var keyText = keymap.DisplayText(commandKey.Chord);
        var cap = KeyCap.Create(keyText, conflict is null ? KeyCapState.Assigned : KeyCapState.Conflict, 44);
        cap.HorizontalAlignment = HorizontalAlignment.Left;
        cap.ToolTip = conflict ?? keyText;
        var capCell = new Border { Width = 96, Child = cap };

        var summary = SynthText.Body(commandKey.Action.Summary(), Theme.IsFlat ? 12.5 : 12, color: Ink.Steel);
        summary.TextWrapping = TextWrapping.NoWrap;
        summary.TextTrimming = TextTrimming.CharacterEllipsis;
        var text = Layout.VStack(2,
            SynthText.Body(commandKey.Title, Theme.IsFlat ? 14 : 13, FontWeights.SemiBold, Ink.Ice),
            summary);

        var run = SynthControls.Button("Run", SynthControls.Kind.Quiet, () => controller.Run(commandKey.Action), "Run it now in Lightroom");
        var edit = SynthControls.Button("Edit", SynthControls.Kind.Secondary, () => ActionEditor.Open(page.View, controller, commandKey, isNew: false));
        AutomationProperties.SetName(edit, $"Edit {commandKey.Title}");
        var more = SynthControls.MoreButton($"More for {commandKey.Title}",
        [
            ("Duplicate", () => ActionEditor.Open(page.View, controller, CommandTitles.Duplicate(commandKey), isNew: true, keepKey: false)),
            ("Run now", () => controller.Run(commandKey.Action)),
            ("Delete…", () =>
            {
                if (SynthDialog.Confirm(page.View, $"Delete “{commandKey.Title}”?", null, "Delete", isDestructive: true))
                    KeymapEditor.Edit(controller, edited => edited.CommandKeys.RemoveAll(key => key.Id == commandKey.Id));
            }),
        ]);
        var trailing = new List<UIElement>();
        if (commandKey.Scope == CommandScope.Any) trailing.Add(SynthText.Label("All modules", 9));
        trailing.AddRange([run, edit, more]);
        var row = Layout.Bar(Theme.Space.M, [capCell, text], trailing);
        // The title and summary take the free width.
        row.ColumnDefinitions[1].Width = new GridLength(1, GridUnitType.Star);
        row.ColumnDefinitions[2].Width = new GridLength(0);
        return new Border
        {
            Child = row,
            Padding = new Thickness(Surfaces.RowInset, 6, Surfaces.RowInset, 6),
            MinHeight = Theme.IsFlat ? 52 : 48,
            BorderBrush = Ink.RowDivider,
            BorderThickness = new Thickness(0, 0, 0, 1),
        };
    }
}
