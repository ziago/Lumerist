using System.Globalization;

namespace Lumerist.Core;

// The Commands tab and the Command Key editor without the drawing
// (Views/CommandsTab.swift and Views/KeymapEditing.swift): command titles and
// groups, mask tools, one-line summaries, the editable draft of an action,
// and the search and grouping of the list.

public static class CommandTitles
{
    public static string Title(this LightroomCommandName command) => command switch
    {
        LightroomCommandName.Undo => "Undo",
        LightroomCommandName.Redo => "Redo",
        LightroomCommandName.AutoTone => "Auto Tone",
        LightroomCommandName.AutoWhiteBalance => "Auto White Balance",
        LightroomCommandName.ResetAll => "Reset all Develop settings",
        LightroomCommandName.NextPhoto => "Next photo",
        LightroomCommandName.PreviousPhoto => "Previous photo",
        LightroomCommandName.FlagPick => "Flag as Pick",
        LightroomCommandName.FlagReject => "Flag as Rejected",
        LightroomCommandName.FlagRemove => "Remove flag",
        LightroomCommandName.Rating => "Set star rating",
        LightroomCommandName.ColorLabel => "Set colour label",
        LightroomCommandName.SwitchModule => "Switch module",
        LightroomCommandName.SelectTool => "Select Develop tool",
        LightroomCommandName.RevealPanel => "Open a slider's panel",
        LightroomCommandName.ZoomToggle => "Toggle zoom",
        LightroomCommandName.ShowClipping => "Show clipping",
        LightroomCommandName.CreateMask => "New mask",
        LightroomCommandName.AddToMask => "Add to mask",
        LightroomCommandName.SubtractFromMask => "Subtract from mask",
        LightroomCommandName.IntersectWithMask => "Intersect mask",
        LightroomCommandName.InvertMask => "Invert mask",
        LightroomCommandName.ToggleMaskOverlay => "Toggle mask overlay",
        LightroomCommandName.ApplyPreset => "Apply preset",
        LightroomCommandName.CopySettings => "Copy settings",
        _ => "Paste settings",
    };

    public static string Group(this LightroomCommandName command) => command switch
    {
        LightroomCommandName.Undo or LightroomCommandName.Redo or LightroomCommandName.CopySettings
            or LightroomCommandName.PasteSettings or LightroomCommandName.ResetAll => "Edit",
        LightroomCommandName.AutoTone or LightroomCommandName.AutoWhiteBalance or LightroomCommandName.ShowClipping
            or LightroomCommandName.ZoomToggle or LightroomCommandName.RevealPanel or LightroomCommandName.SelectTool => "Develop",
        LightroomCommandName.NextPhoto or LightroomCommandName.PreviousPhoto or LightroomCommandName.FlagPick
            or LightroomCommandName.FlagReject or LightroomCommandName.FlagRemove or LightroomCommandName.Rating
            or LightroomCommandName.ColorLabel or LightroomCommandName.SwitchModule => "Photos",
        LightroomCommandName.ApplyPreset => "Presets",
        _ => "Masking",
    };

    /// <summary>The command menu's groups, in order (Apply preset has its own type, Develop preset).</summary>
    public static IReadOnlyList<string> MenuGroups { get; } = ["Develop", "Edit", "Photos", "Masking"];

    /// <summary>Commands whose argument is a mask "type/subtype".</summary>
    public static bool TakesMask(this LightroomCommandName command) => command is LightroomCommandName.CreateMask
        or LightroomCommandName.AddToMask or LightroomCommandName.SubtractFromMask or LightroomCommandName.IntersectWithMask;

    /// <summary>A sensible default argument for a freshly picked command.</summary>
    public static string? DefaultArgument(this LightroomCommandName command) => command switch
    {
        LightroomCommandName.Rating => "5",
        LightroomCommandName.ColorLabel => "red",
        LightroomCommandName.SwitchModule => "develop",
        LightroomCommandName.SelectTool => "crop",
        LightroomCommandName.RevealPanel => "Exposure",
        _ when command.TakesMask() => "aiSelection/subject",
        _ => null,
    };

    /// <summary>Swift's <c>capitalized</c>: each word starts upper case, the rest lower case.</summary>
    public static string Capitalized(string text)
    {
        var characters = text.ToCharArray();
        var startOfWord = true;
        for (var index = 0; index < characters.Length; index++)
        {
            var character = characters[index];
            if (char.IsWhiteSpace(character))
            {
                startOfWord = true;
                continue;
            }
            characters[index] = startOfWord ? char.ToUpperInvariant(character) : char.ToLowerInvariant(character);
            startOfWord = false;
        }
        return new string(characters);
    }

    /// <summary>One line describing the action: "Auto Tone", "Set star rating: 5 ★", "Preset: Matte", "Macro · 2 steps".</summary>
    public static string Summary(this CommandAction action)
    {
        switch (action)
        {
            case CommandAction.Command(var name, var argument):
                var command = LightroomCommandNames.FromWireName(name);
                var title = command?.Title() ?? name;
                if (argument is null) return title;
                if (command?.TakesMask() == true) return $"{title}: {MaskTool.TitleFor(argument)}";
                if (command == LightroomCommandName.Rating) return $"{title}: {argument} ★";
                if (command == LightroomCommandName.RevealPanel)
                    return $"{title}: {ParameterCatalog.Descriptor(ParameterScope.Develop, argument)?.Label ?? argument}";
                return $"{title}: {Capitalized(argument)}";
            case CommandAction.Preset(_, var presetName):
                return $"Preset: {presetName}";
            case CommandAction.Shortcut(var chord):
                return $"Lightroom shortcut {chord.DisplayText}"; // never a combo
            case CommandAction.Macro(var steps):
                return $"Macro · {steps.Count} step{(steps.Count == 1 ? "" : "s")}";
            default:
                return "";
        }
    }

    public static string KindTitle(this CommandAction action) => action switch
    {
        CommandAction.Command => "Command",
        CommandAction.Preset => "Preset",
        CommandAction.Shortcut => "Shortcut",
        _ => "Macro",
    };

    /// <summary>"Develop", "Library", "All modules".</summary>
    public static string Title(this CommandScope scope) => scope switch
    {
        CommandScope.Develop => "Develop",
        CommandScope.Library => "Library",
        _ => "All modules",
    };

    /// <summary>The Commands tab's explanation, with how to record a combo with the current Combo Key.</summary>
    public static string Hint(KeyChord? comboKey)
    {
        const string intro = "One press runs a Lightroom command, a preset, a Lightroom shortcut or a macro.";
        if (comboKey is not { } combo) return intro;
        var example = new KeyChord(0x25, KeyModifiers.Combo).DisplayTextWith(combo); // the Mac's key code 40, K
        return intro + $" For a combo, hold {combo.DisplayText} while you press the key: {example}.";
    }

    /// <summary>The editor's Key row: how to record, and a combo with the current Combo Key.</summary>
    public static string KeyDetail(KeyChord? comboKey)
    {
        const string text = "Click, then press the key (modifiers allowed). Esc cancels.";
        return comboKey is { } combo ? text + $" For a combo, hold {combo.DisplayText} and press the key." : text;
    }

    /// <summary>Command Keys whose title, group, key or summary contains <paramref name="search"/>.</summary>
    public static IReadOnlyList<CommandKey> Filter(IEnumerable<CommandKey> keys, string search, Keymap keymap)
    {
        var query = search.Trim();
        if (query.Length == 0) return keys.ToList();
        return keys.Where(key => Contains(key.Title, query) || Contains(key.Group, query)
            || Contains(keymap.DisplayText(key.Chord), query) || Contains(key.Action.Summary(), query)).ToList();
    }

    /// <summary>Groups in the order they first appear, each with its keys in list order.</summary>
    public static IReadOnlyList<(string Group, IReadOnlyList<CommandKey> Keys)> OrderedGroups(IEnumerable<CommandKey> keys) =>
        keys.GroupBy(key => key.Group, StringComparer.Ordinal)
            .Select(group => (group.Key, (IReadOnlyList<CommandKey>)group.ToList()))
            .ToList();

    /// <summary>A copy without a key, for the editor: the user picks a free one.</summary>
    public static CommandKey Duplicate(CommandKey original)
    {
        var title = original.Title + " copy";
        var info = new StringInfo(title);
        if (info.LengthInTextElements > CommandKey.MaximumTitleLength) title = info.SubstringByTextElements(0, CommandKey.MaximumTitleLength);
        return original with { Id = Guid.NewGuid(), Title = title };
    }

    /// <summary>The title as saved: trimmed, at most <see cref="CommandKey.MaximumTitleLength"/> characters.</summary>
    public static string SavedTitle(string title)
    {
        var trimmed = title.Trim();
        var info = new StringInfo(trimmed);
        return info.LengthInTextElements > CommandKey.MaximumTitleLength
            ? info.SubstringByTextElements(0, CommandKey.MaximumTitleLength)
            : trimmed;
    }

    /// <summary>The group as saved: "Lightroom" when left empty.</summary>
    public static string SavedGroup(string group) => group.Trim().Length == 0 ? "Lightroom" : group;

    /// <summary>Adds the Command Key, or replaces the one with the same id.</summary>
    public static void Upsert(this Keymap keymap, CommandKey commandKey)
    {
        var index = keymap.CommandKeys.FindIndex(existing => existing.Id == commandKey.Id);
        if (index >= 0) keymap.CommandKeys[index] = commandKey;
        else keymap.CommandKeys.Add(commandKey);
    }

    internal static bool Contains(string text, string query) => text.Contains(query, StringComparison.CurrentCultureIgnoreCase);
}

/// <summary>A mask tool offered by the Palette and the Command Key editor.</summary>
/// <param name="Id">The mask argument: "aiSelection/subject", "brush"…</param>
public sealed record MaskTool(string Id, string Title)
{
    public static IReadOnlyList<MaskTool> All { get; } =
    [
        new("aiSelection/subject", "Subject"),
        new("aiSelection/sky", "Sky"),
        new("aiSelection/background", "Background"),
        new("aiSelection/people", "People"),
        new("aiSelection/objects", "Objects"),
        new("aiSelection/landscape", "Landscape"),
        new("brush", "Brush"),
        new("gradient", "Linear"),
        new("radialGradient", "Radial"),
        new("rangeMask/color", "Color range"),
        new("rangeMask/luminance", "Luminance range"),
        new("rangeMask/depth", "Depth range"),
    ];

    public static string TitleFor(string? argument) =>
        argument is null ? "" : All.FirstOrDefault(tool => tool.Id == argument)?.Title ?? argument;
}

/// <summary>The editable form of a Command Key's action, and of one macro step.</summary>
public sealed class ActionDraft
{
    public enum Kind
    {
        Command,
        Preset,
        Shortcut,
        Macro,
    }

    /// <summary>One macro step and the pause after it.</summary>
    public sealed class StepDraft(ActionDraft action, double delay = 0)
    {
        /// <summary>Stable identity, so the step's key box records on its own.</summary>
        public Guid Id { get; } = Guid.NewGuid();

        public ActionDraft Action { get; set; } = action;

        public double Delay { get; set; } = delay;
    }

    public Kind Type { get; set; } = Kind.Command;

    public LightroomCommandName Command { get; set; } = LightroomCommandName.AutoTone;

    public string? Argument { get; set; }

    public string? PresetId { get; set; }

    public string PresetName { get; set; } = "";

    public KeyChord? Shortcut { get; set; }

    public List<StepDraft> Steps { get; } = [];

    public ActionDraft() { }

    public ActionDraft(CommandAction action)
    {
        switch (action)
        {
            case CommandAction.Command(var name, var argument):
                Type = Kind.Command;
                Command = LightroomCommandNames.FromWireName(name) ?? LightroomCommandName.AutoTone;
                Argument = argument;
                break;
            case CommandAction.Preset(var uuid, var name):
                Type = Kind.Preset;
                PresetId = uuid;
                PresetName = name;
                break;
            case CommandAction.Shortcut(var chord):
                Type = Kind.Shortcut;
                Shortcut = chord;
                break;
            case CommandAction.Macro(var steps):
                Type = Kind.Macro;
                Steps.AddRange(steps.Select(step => new StepDraft(new ActionDraft(step.Action), step.Delay)));
                break;
        }
    }

    /// <summary>"Command", "Develop preset", "LR shortcut", "Macro".</summary>
    public static string Title(Kind kind) => kind switch
    {
        Kind.Command => "Command",
        Kind.Preset => "Develop preset",
        Kind.Shortcut => "LR shortcut",
        _ => "Macro",
    };

    /// <summary>Picks a command and its default argument.</summary>
    public void Choose(LightroomCommandName command)
    {
        Command = command;
        Argument = command.DefaultArgument();
    }

    /// <summary>The action, or null while incomplete.</summary>
    public CommandAction? Action
    {
        get
        {
            switch (Type)
            {
                case Kind.Command:
                    return Command.Accepts(Argument) ? new CommandAction.Command(Command.WireName(), Argument) : null;
                case Kind.Preset:
                    return PresetId is null ? null : new CommandAction.Preset(PresetId, PresetName.Length == 0 ? PresetId : PresetName);
                case Kind.Shortcut:
                    return Shortcut is { } chord ? new CommandAction.Shortcut(chord) : null;
                default:
                    var actions = Steps.Select(step => step.Action.Action is { } stepAction ? new MacroStep(stepAction, step.Delay) : null)
                        .OfType<MacroStep>().ToList();
                    return actions.Count == 0 || actions.Count != Steps.Count ? null : new CommandAction.Macro(actions);
            }
        }
    }

    /// <summary>A macro step's pause, clamped to 0–10 s.</summary>
    public static double ClampDelay(double seconds) =>
        double.IsFinite(seconds) ? Math.Clamp(seconds, 0, MacroStep.MaximumDelay) : 0;

    /// <summary>Swaps a step with its neighbour (offset −1 or +1); nothing at the ends.</summary>
    public void MoveStep(int index, int offset)
    {
        var target = index + offset;
        if (index < 0 || index >= Steps.Count || target < 0 || target >= Steps.Count) return;
        (Steps[index], Steps[target]) = (Steps[target], Steps[index]);
    }
}
