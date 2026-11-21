using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Lumerist.App.Design;
using Lumerist.App.Design.Controls;
using Lumerist.Core;

namespace Lumerist.App.Views;

/// <summary>
/// Edits one Command Key (the Mac's ActionEditor sheet): name, group, key,
/// where it works and what it does. Validates on a copy of the keymap
/// before saving. The form scrolls; the buttons stay at the bottom.
/// </summary>
internal sealed class ActionEditor : Window
{
    private readonly AppController controller;
    private readonly CommandKey original;
    private readonly ActionDraft draft;
    private readonly TextBox titleField;
    private readonly TextBox groupField;
    private readonly ContentControl keyRow = new() { Focusable = false, IsTabStop = false };
    private readonly ContentControl scopeRow = new() { Focusable = false, IsTabStop = false };
    private readonly ContentControl actionHost = new() { Focusable = false, IsTabStop = false };
    private readonly ContentControl errorHost = new() { Focusable = false, IsTabStop = false };
    private readonly ContentControl buttonBar = new() { Focusable = false, IsTabStop = false };
    /// <summary>Preset searches of each draft (a macro has one per step), kept across rebuilds.</summary>
    private readonly Dictionary<ActionDraft, string> presetSearches = [];
    private KeyChord? chord;
    private CommandScope scope;
    private string? error;

    /// <summary>Opens the editor over the main window, modally.</summary>
    /// <param name="keepKey">False for a new or duplicated key: the user picks a free key.</param>
    public static void Open(DependencyObject from, AppController controller, CommandKey commandKey, bool isNew, bool? keepKey = null)
    {
        var editor = new ActionEditor(controller, commandKey, isNew, keepKey ?? !isNew) { Owner = GetWindow(from) };
        editor.ShowDialog();
    }

    private ActionEditor(AppController controller, CommandKey original, bool isNew, bool keepKey)
    {
        this.controller = controller;
        this.original = original;
        chord = keepKey ? original.Chord : null;
        scope = original.Scope;
        draft = new ActionDraft(original.Action);

        Title = isNew ? "Add Command Key" : "Edit Command Key";
        Width = 720;
        Height = 680;
        MinHeight = 420;
        MinWidth = 600;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetResourceReference(StyleProperty, "Synth.Window");
        Resources.MergedDictionaries.Add(new ControlStyles());
        SourceInitialized += (_, _) => Theme.ApplyTitleBar(this);

        titleField = SynthControls.Field(original.Title, 260, _ => { }, "Name", TextAlignment.Left);
        titleField.TextChanged += (_, _) => BuildButtons();
        groupField = SynthControls.Field(original.Group, 180, _ => { }, "Group", TextAlignment.Left);

        var groups = controller.Keymap.CommandKeys.Select(key => key.Group).Append("Lightroom")
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        var existing = SynthControls.MenuButton("Existing", "Existing groups", groups, original.Group, group => group,
            group => groupField.Text = group);
        existing.Width = 110;

        var form = Layout.VStack(Theme.Space.L,
            SynthText.Title(isNew ? "Add Command Key" : "Edit Command Key", 20),
            Surfaces.Panel(Layout.VStack(0,
                Surfaces.SettingRow("Name", null, titleField),
                Surfaces.SettingRow("Group", "Command Keys are listed under their group.", Layout.HStack(Theme.Space.S, groupField, existing)),
                keyRow,
                scopeRow)),
            SynthText.Title("What it does", 15),
            actionHost,
            errorHost);
        var scroller = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = new Border { Padding = new Thickness(Theme.Space.XXL), Child = form },
        };

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.Children.Add(scroller);
        Grid.SetRow(buttonBar, 1);
        root.Children.Add(buttonBar);
        Content = root;

        BuildKeyRow();
        BuildScopeRow();
        BuildAction();
        BuildButtons();
        controller.PresetsChanged += BuildAction;
        PreviewKeyDown += (_, e) =>
        {
            // Esc closes the editor, unless a key box is recording (then Esc cancels the recording).
            if (e.Key != Key.Escape || ChordRecorder.Shared.RecordingId is not null) return;
            Close();
            e.Handled = true;
        };
        Closed += (_, _) =>
        {
            controller.PresetsChanged -= BuildAction;
            ChordRecorder.Shared.Stop();
        };
    }

    private KeyChord? ComboKey => controller.Keymap.SystemKeys.ComboKey;

    private void BuildKeyRow()
    {
        var slot = new KeySlot("command-editor.key", chord, null, 60, "Command Key", ComboKey,
            recorded =>
            {
                chord = recorded;
                BuildKeyRow();
                BuildButtons();
            },
            () =>
            {
                chord = null;
                BuildKeyRow();
                BuildButtons();
            });
        keyRow.Content = Surfaces.SettingRow("Key", CommandTitles.KeyDetail(ComboKey), slot);
    }

    private void BuildScopeRow()
    {
        scopeRow.Content = Surfaces.SettingRow("Works in", null,
            SynthControls.Segmented([CommandScope.Develop, CommandScope.Library, CommandScope.Any], scope, item => item.Title(),
                item => scope = item, name: "Works in"));
    }

    private void BuildAction()
    {
        actionHost.Content = DraftEditor(draft, allowsMacro: true, slotId: "command-editor");
        errorHost.Content = error is null ? null : SynthText.ErrorLine(error);
    }

    /// <summary>Something in the action changed: draw it again and update Save.</summary>
    private void Changed()
    {
        BuildAction();
        BuildButtons();
    }

    private void BuildButtons()
    {
        var runNow = SynthControls.Button("Run now", SynthControls.Kind.Quiet, () =>
        {
            if (draft.Action is { } action) controller.Run(action);
        });
        runNow.IsEnabled = draft.Action is not null;
        var cancel = SynthControls.Button("Cancel", SynthControls.Kind.Secondary, Close);
        cancel.IsCancel = true;
        var save = SynthControls.Button("Save", SynthControls.Kind.Primary, Save);
        save.IsEnabled = chord is not null && titleField.Text.Trim().Length > 0 && draft.Action is not null;
        buttonBar.Content = new Border
        {
            Padding = new Thickness(Theme.Space.XXL, Theme.Space.L, Theme.Space.XXL, Theme.Space.L),
            Background = Theme.IsFlat ? Ink.Chrome : Ink.Abyss,
            BorderBrush = Ink.GridLine,
            BorderThickness = new Thickness(0, 1, 0, 0),
            Child = Layout.Bar(Theme.Space.S, [runNow], [cancel, save]),
        };
    }

    private void Save()
    {
        if (chord is not { } key || draft.Action is not { } action) return;
        var updated = original with
        {
            Title = CommandTitles.SavedTitle(titleField.Text),
            Group = CommandTitles.SavedGroup(groupField.Text),
            Chord = key,
            Scope = scope,
            Action = action,
        };
        var proposed = controller.Keymap.Clone();
        proposed.Upsert(updated);
        try
        {
            proposed.Validate();
        }
        catch (KeymapException failure)
        {
            ShowError(failure.Message);
            return;
        }
        if (controller.UpdateKeymap(proposed) is { } saveError)
        {
            ShowError(saveError);
            return;
        }
        Close();
    }

    private void ShowError(string message)
    {
        error = message;
        errorHost.Content = SynthText.ErrorLine(message);
    }

    // What it does ---------------------------------------------------------------

    /// <summary>The type picker plus the controls for the chosen type (the Mac's ActionDraftEditor).</summary>
    private FrameworkElement DraftEditor(ActionDraft editing, bool allowsMacro, string slotId)
    {
        ActionDraft.Kind[] kinds = allowsMacro
            ? [ActionDraft.Kind.Command, ActionDraft.Kind.Preset, ActionDraft.Kind.Shortcut, ActionDraft.Kind.Macro]
            : [ActionDraft.Kind.Command, ActionDraft.Kind.Preset, ActionDraft.Kind.Shortcut];
        var stack = Layout.VStack(Theme.Space.M,
            SynthControls.Segmented(kinds, editing.Type, ActionDraft.Title, kind =>
            {
                editing.Type = kind;
                Changed();
            }, name: "What it does"));
        switch (editing.Type)
        {
            case ActionDraft.Kind.Command:
                stack.Children.Add(CommandPicker(editing));
                break;
            case ActionDraft.Kind.Preset:
                stack.Children.Add(PresetPicker(editing));
                break;
            case ActionDraft.Kind.Shortcut:
                ShortcutPicker(editing, slotId, stack);
                break;
            default:
                stack.Children.Add(MacroEditor(editing));
                break;
        }
        return stack;
    }

    private FrameworkElement CommandPicker(ActionDraft editing)
    {
        var commands = LightroomCommandNames.All.Where(command => command != LightroomCommandName.ApplyPreset).ToList();
        var menu = SynthControls.MenuButton(editing.Command.Title(), "Command",
            CommandTitles.MenuGroups.Select(group => new SynthControls.MenuGroup<LightroomCommandName>(group,
                commands.Where(command => command.Group() == group).ToList())).ToList(),
            editing.Command, command => command.Title(), command =>
            {
                editing.Choose(command);
                Changed();
            });
        menu.Width = 320;
        menu.HorizontalAlignment = HorizontalAlignment.Left;
        return Layout.VStack(Theme.Space.S, menu, ArgumentPicker(editing));
    }

    private FrameworkElement ArgumentPicker(ActionDraft editing)
    {
        void Set(string argument)
        {
            editing.Argument = argument;
            Changed();
        }

        FrameworkElement Choice(IReadOnlyList<string> items, string name, string fallback, Func<string, string> title)
        {
            // A command picked earlier may have no argument yet: take the first sensible one.
            editing.Argument ??= fallback;
            var menu = SynthControls.MenuButton(title(editing.Argument), name, items, editing.Argument, title, Set);
            menu.Width = 320;
            menu.HorizontalAlignment = HorizontalAlignment.Left;
            return menu;
        }

        switch (editing.Command)
        {
            case LightroomCommandName.Rating:
                return SynthControls.Segmented(["0", "1", "2", "3", "4", "5"], editing.Argument ?? "5",
                    stars => stars == "0" ? "None" : $"{stars} ★", Set, name: "Rating");
            case LightroomCommandName.ColorLabel:
                return SynthControls.Segmented(["red", "yellow", "green", "blue", "purple", "none"], editing.Argument ?? "red",
                    CommandTitles.Capitalized, Set, name: "Colour label");
            case LightroomCommandName.SwitchModule:
                return Choice(LightroomCommandNames.Modules.Order(StringComparer.Ordinal).ToList(), "Module", "develop", CommandTitles.Capitalized);
            case LightroomCommandName.SelectTool:
                return Choice(LightroomCommandNames.Tools.Order(StringComparer.Ordinal).ToList(), "Tool", "crop", CommandTitles.Capitalized);
            case LightroomCommandName.RevealPanel:
                var sliders = SynthControls.MenuButton(
                    ParameterCatalog.Descriptor(ParameterScope.Develop, editing.Argument ?? "")?.Label ?? "Choose a slider", "Slider",
                    ParameterCatalog.Sections(ParameterScope.Develop).Select(section =>
                        new SynthControls.MenuGroup<string>(section.Section, section.Parameters.Select(parameter => parameter.Id).ToList())).ToList(),
                    editing.Argument, id => ParameterCatalog.Descriptor(ParameterScope.Develop, id)?.Label ?? id, Set);
                sliders.Width = 320;
                sliders.HorizontalAlignment = HorizontalAlignment.Left;
                return sliders;
            case var command when command.TakesMask():
                return Choice(MaskTool.All.Select(tool => tool.Id).ToList(), "Mask tool", "aiSelection/subject", MaskTool.TitleFor);
            default:
                return SynthText.Body("No options.", 12, color: Ink.Steel);
        }
    }

    /// <summary>Develop presets reported by the plug-in, searchable, with refresh.</summary>
    private FrameworkElement PresetPicker(ActionDraft editing)
    {
        var list = new ContentControl { Focusable = false, IsTabStop = false };
        void Fill()
        {
            var search = presetSearches.GetValueOrDefault(editing, "");
            var presets = controller.Presets.Where(preset => search.Length == 0
                || preset.Name.Contains(search, StringComparison.CurrentCultureIgnoreCase)
                || preset.Group.Contains(search, StringComparison.CurrentCultureIgnoreCase)).ToList();
            var rows = Layout.VStack(0);
            if (presets.Count == 0)
            {
                var none = SynthText.Body(controller.Presets.Count == 0 ? "No presets loaded. Connect Lightroom and press Refresh." : "No preset matches.",
                    12, color: Ink.Steel);
                none.Margin = new Thickness(12);
                rows.Children.Add(none);
            }
            foreach (var preset in presets)
            {
                var selected = editing.PresetId == preset.Id;
                var name = SynthText.Body(preset.Name, 13, color: Ink.Ice);
                name.TextWrapping = TextWrapping.NoWrap;
                var trailing = new List<UIElement> { SynthText.Label(preset.Group, 9) };
                if (selected) trailing.Add(SynthText.Icon(Icons.CheckMark, 12, Ink.Magenta));
                var face = new Border
                {
                    Padding = new Thickness(12, 0, 12, 0),
                    MinHeight = 30,
                    Background = selected ? (Theme.IsFlat ? Ink.GridLine : Ink.Faded(Ink.Magenta, 0.1)) : Brushes.Transparent,
                    Child = Layout.Bar(Theme.Space.S, [name], trailing),
                };
                var row = SynthControls.Plain(face, () =>
                {
                    editing.PresetId = preset.Id;
                    editing.PresetName = preset.Name;
                    Changed();
                });
                AutomationProperties.SetName(row, preset.Name);
                AutomationProperties.SetItemStatus(row, selected ? "Selected" : "");
                rows.Children.Add(row);
            }
            list.Content = new ScrollViewer { Height = 180, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = rows };
        }
        Fill();

        var search = SynthControls.SearchField("Search presets", presetSearches.GetValueOrDefault(editing, ""), text =>
        {
            presetSearches[editing] = text;
            Fill();
        });
        var refresh = SynthControls.Button("Refresh", SynthControls.Kind.Secondary, controller.RequestPresets,
            "Ask Lightroom for the current preset list");
        var stack = Layout.VStack(Theme.Space.S, Layout.Bar(Theme.Space.S, [search], [refresh], stretchFirst: true));
        if (editing.PresetId is not null)
        {
            stack.Children.Add(Layout.HStack(6, SynthText.Icon(Icons.CheckMark, 11, Ink.Teal),
                SynthText.Body($"Selected: {editing.PresetName}", 12, color: Ink.Teal)));
        }
        stack.Children.Add(Surfaces.Panel(list));
        if (controller.Presets.Count == 0 && !presetsAsked)
        {
            presetsAsked = true;
            controller.RequestPresets();
        }
        return stack;
    }

    private bool presetsAsked;

    /// <summary>Why the last recorded shortcut was refused (a combo can't be sent).</summary>
    private readonly Dictionary<ActionDraft, string> shortcutErrors = [];

    private void ShortcutPicker(ActionDraft editing, string slotId, Stack stack)
    {
        var slot = new KeySlot($"{slotId}.shortcut", editing.Shortcut, null, 70, "Lightroom shortcut", ComboKey,
            recorded =>
            {
                if (recorded.IsCombo) shortcutErrors[editing] = new ComboMisuse.LightroomShortcut().Message;
                else
                {
                    editing.Shortcut = recorded;
                    shortcutErrors.Remove(editing);
                }
                Changed();
            },
            () =>
            {
                editing.Shortcut = null;
                shortcutErrors.Remove(editing);
                Changed();
            });
        stack.Children.Add(Layout.HStack(Theme.Space.M, slot,
            SynthText.Body("Lumerist sends this key combination to Lightroom, as if you pressed it.", 12, color: Ink.Steel)));
        if (shortcutErrors.TryGetValue(editing, out var message)) stack.Children.Add(SynthText.ErrorLine(message));
    }

    /// <summary>Ordered macro steps, each with a pause after it.</summary>
    private FrameworkElement MacroEditor(ActionDraft macro)
    {
        var stack = Layout.VStack(Theme.Space.S);
        for (var index = 0; index < macro.Steps.Count; index++)
        {
            var step = macro.Steps[index];
            var position = index;
            var delay = SynthControls.Field(step.Delay.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture), 56, text =>
            {
                if (StepPair.Parse(text) is { } seconds) step.Delay = ActionDraft.ClampDelay(seconds);
                Changed();
            }, $"Seconds to wait after step {index + 1}");
            var up = SynthControls.IconButton(Icons.ArrowUp, SynthControls.Kind.Quiet, () =>
            {
                macro.MoveStep(position, -1);
                Changed();
            }, "Move step up");
            up.IsEnabled = index > 0;
            var down = SynthControls.IconButton(Icons.ArrowDown, SynthControls.Kind.Quiet, () =>
            {
                macro.MoveStep(position, 1);
                Changed();
            }, "Move step down");
            down.IsEnabled = index < macro.Steps.Count - 1;
            var remove = SynthControls.IconButton(Icons.Cancel, SynthControls.Kind.Quiet, () =>
            {
                macro.Steps.RemoveAt(position);
                Changed();
            }, $"Remove step {index + 1}");
            var header = Layout.Bar(Theme.Space.S, [SynthText.Label($"Step {index + 1}", 10, Ink.Teal)],
                [SynthText.Label("Then wait", 9), delay, SynthText.Label("s", 9), up, down, remove]);
            var content = Layout.VStack(Theme.Space.S, header, DraftEditor(step.Action, allowsMacro: false, slotId: $"macro.{step.Id}"));
            content.Margin = new Thickness(12);
            stack.Children.Add(Surfaces.Panel(content));
        }
        var add = SynthControls.Button("Add step", SynthControls.Kind.Secondary, () =>
        {
            macro.Steps.Add(new ActionDraft.StepDraft(new ActionDraft()));
            Changed();
        }, icon: Icons.Add);
        add.IsEnabled = macro.Steps.Count < CommandKey.MaximumMacroSteps;
        add.HorizontalAlignment = HorizontalAlignment.Left;
        stack.Children.Add(add);
        return stack;
    }
}
