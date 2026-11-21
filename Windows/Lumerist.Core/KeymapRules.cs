using System.Globalization;

namespace Lumerist.Core;

// Validation, conflicts and the key overview. The same rules, order of checks
// and messages as Sources/LumeristCore/Keymap.swift; Shared/fixtures/keymap-rules
// pins them down for both apps.

/// <summary>What went wrong with a keymap; <see cref="KeymapException"/> carries the details.</summary>
public enum KeymapErrorKind
{
    UnsupportedFormat,
    UnknownFields,
    UnknownParameter,
    InvalidStep,
    InvalidOption,
    InvalidCommandKey,
    InvalidCombo,
    Conflicts,
}

/// <summary>A keymap that can't be stored or used. Only the details of its <see cref="Kind"/> are set.</summary>
public sealed class KeymapException : Exception
{
    private KeymapException(KeymapErrorKind kind, string message) : base(message) => Kind = kind;

    public KeymapErrorKind Kind { get; }

    /// <summary><see cref="KeymapErrorKind.UnsupportedFormat"/>: the stored format, 0 when there is none.</summary>
    public int? Format { get; private init; }

    /// <summary><see cref="KeymapErrorKind.UnknownFields"/>: the field names, sorted.</summary>
    public IReadOnlyList<string> Fields { get; private init; } = [];

    /// <summary><see cref="KeymapErrorKind.UnknownParameter"/>, <see cref="KeymapErrorKind.InvalidStep"/>.</summary>
    public ParameterKey? Parameter { get; private init; }

    /// <summary><see cref="KeymapErrorKind.InvalidOption"/>: "drag sensitivity", "trackpad sensitivity".</summary>
    public string? Option { get; private init; }

    /// <summary><see cref="KeymapErrorKind.InvalidCommandKey"/>: "empty command", "macros cannot contain macros"…</summary>
    public string? Reason { get; private init; }

    /// <summary><see cref="KeymapErrorKind.InvalidCombo"/>: where the combo is not allowed.</summary>
    public ComboMisuse? Misuse { get; private init; }

    /// <summary><see cref="KeymapErrorKind.Conflicts"/>: the conflicts of severity error.</summary>
    public IReadOnlyList<KeyConflict> Conflicts { get; private init; } = [];

    public static KeymapException UnsupportedFormat(int format) =>
        new(KeymapErrorKind.UnsupportedFormat, $"Unsupported keymap format: {format}.") { Format = format };

    public static KeymapException UnknownFields(IEnumerable<string> fields)
    {
        var sorted = fields.Distinct().Order(StringComparer.Ordinal).ToList();
        return new(KeymapErrorKind.UnknownFields, $"Unknown keymap fields: {string.Join(", ", sorted)}.") { Fields = sorted };
    }

    public static KeymapException UnknownParameter(ParameterKey key) =>
        new(KeymapErrorKind.UnknownParameter, $"Unknown parameter {key}.") { Parameter = key };

    public static KeymapException InvalidStep(ParameterKey key) =>
        new(KeymapErrorKind.InvalidStep, $"Steps for {key} are outside the allowed range.") { Parameter = key };

    public static KeymapException InvalidOption(string name) =>
        new(KeymapErrorKind.InvalidOption, $"The {name} setting is out of range.") { Option = name };

    public static KeymapException InvalidCommandKey(string reason) =>
        new(KeymapErrorKind.InvalidCommandKey, $"Invalid command key: {reason}.") { Reason = reason };

    public static KeymapException InvalidCombo(ComboMisuse misuse) =>
        new(KeymapErrorKind.InvalidCombo, misuse.Message) { Misuse = misuse };

    public static KeymapException ConflictsFound(IReadOnlyList<KeyConflict> conflicts) =>
        new(KeymapErrorKind.Conflicts, conflicts.FirstOrDefault()?.Message ?? "Conflicting keys.") { Conflicts = conflicts };
}

/// <summary>Places where a combo (a chord with <see cref="KeyModifiers.Combo"/>) is not allowed.</summary>
public abstract record ComboMisuse
{
    private ComboMisuse() { }

    /// <summary>A Dial Key is held on its own while you scroll.</summary>
    public sealed record DialKey(ParameterKey Parameter) : ComboMisuse;

    /// <summary>Lumerist sends a Lightroom shortcut as a plain key press.</summary>
    public sealed record LightroomShortcut : ComboMisuse;

    public sealed record SystemKey(SystemKeyRole Role) : ComboMisuse;

    public string Message => this switch
    {
        DialKey =>
            "A Dial Key can't be a combo: you hold it on its own while you scroll. Combos work for Nudge Keys and Command Keys.",
        LightroomShortcut =>
            "A Lightroom shortcut can't be a combo: Lumerist sends it to Lightroom as a plain key press.",
        _ => "Only the Key Reference toggle can be a combo. Press a single key, with modifiers if you like.",
    };
}

/// <summary>Where a chord is used, for conflict reports and the key overview.</summary>
public abstract record KeyUsage
{
    private KeyUsage() { }

    public sealed record Dial(ParameterKey Parameter) : KeyUsage;

    public sealed record NudgeDown(ParameterKey Parameter) : KeyUsage;

    public sealed record NudgeUp(ParameterKey Parameter) : KeyUsage;

    /// <summary>A Command Key, by id; the title is for display.</summary>
    public sealed record Action(Guid Id, string Title) : KeyUsage;

    public sealed record SystemKey(SystemKeyRole Role) : KeyUsage;

    internal bool IsNudge => this is NudgeDown or NudgeUp;
}

public enum ConflictSeverity
{
    Error,
    Warning,
}

/// <summary>A problem with one chord.</summary>
/// <param name="Chord">The chord in conflict; combos keep their <see cref="KeyModifiers.Combo"/> modifier.</param>
/// <param name="Scope">Null for conflicts among System Keys.</param>
public sealed record KeyConflict(
    ConflictSeverity Severity,
    KeyChord Chord,
    ParameterScope? Scope,
    IReadOnlyList<KeyUsage> Usages,
    string Message)
{
    public bool Equals(KeyConflict? other) =>
        other is not null && Severity == other.Severity && Chord == other.Chord && Scope == other.Scope
        && Message == other.Message && Usages.SequenceEqual(other.Usages);

    public override int GetHashCode() => HashCode.Combine(Severity, Chord, Scope, Message, Usages.Count);
}

public sealed partial class Keymap
{
    /// <summary>Keys that type or confirm: Enter, Space, Backspace, Esc.</summary>
    private static readonly HashSet<int> ReservedTypingKeys =
        [ScanCodes.Enter, ScanCodes.Space, ScanCodes.Backspace, ScanCodes.Escape];

    /// <summary>
    /// Throws <see cref="KeymapException"/> for the first problem: an unknown
    /// parameter, a Dial Key combo, steps or options out of range, a System Key
    /// combo other than the Key Reference toggle, an invalid Command Key, or
    /// conflicts of severity error.
    /// </summary>
    public void Validate()
    {
        foreach (var scope in ParameterScopes.All)
        {
            foreach (var (id, slot) in Slots(scope).OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                var key = new ParameterKey(scope, id);
                var descriptor = ParameterCatalog.Descriptor(key) ?? throw KeymapException.UnknownParameter(key);
                if (slot.Dial is { IsCombo: true }) throw KeymapException.InvalidCombo(new ComboMisuse.DialKey(key));
                foreach (var step in new[] { slot.NormalStep, slot.CoarseStep })
                {
                    if (step is not { } value) continue;
                    if (!double.IsFinite(value) || value <= 0 || !descriptor.StepRangeContains(value))
                        throw KeymapException.InvalidStep(key);
                }
            }
        }
        if (!InRange(Input.TrackpadPointsPerStep, InputOptions.TrackpadPointsPerStepMinimum, InputOptions.TrackpadPointsPerStepMaximum))
            throw KeymapException.InvalidOption("trackpad sensitivity");
        if (!InRange(Input.DragSensitivity, InputOptions.DragSensitivityMinimum, InputOptions.DragSensitivityMaximum))
            throw KeymapException.InvalidOption("drag sensitivity");
        // Only the Key Reference toggle may be a combo: the other System Keys
        // are held on their own or must work everywhere.
        foreach (var role in SystemKeyRoles.All)
        {
            if (role != SystemKeyRole.KeyReferenceToggle && SystemKeys.ChordFor(role) is { IsCombo: true })
                throw KeymapException.InvalidCombo(new ComboMisuse.SystemKey(role));
        }
        foreach (var commandKey in CommandKeys)
        {
            var title = commandKey.Title.Trim();
            if (title.Length == 0 || new StringInfo(title).LengthInTextElements > CommandKey.MaximumTitleLength)
                throw KeymapException.InvalidCommandKey($"a title of 1–{CommandKey.MaximumTitleLength} characters is required");
            ValidateAction(commandKey.Action, nested: false);
        }
        var errors = Conflicts().Where(conflict => conflict.Severity == ConflictSeverity.Error).ToList();
        if (errors.Count > 0) throw KeymapException.ConflictsFound(errors);
    }

    private static bool InRange(double value, double minimum, double maximum) => value >= minimum && value <= maximum;

    private static void ValidateAction(CommandAction action, bool nested)
    {
        switch (action)
        {
            case CommandAction.Command command:
                if (command.Name.Length == 0) throw KeymapException.InvalidCommandKey("empty command");
                break;
            case CommandAction.Preset preset:
                if (preset.Uuid.Length == 0) throw KeymapException.InvalidCommandKey("missing preset");
                break;
            case CommandAction.Shortcut shortcut:
                if (shortcut.Chord.IsCombo) throw KeymapException.InvalidCombo(new ComboMisuse.LightroomShortcut());
                break;
            case CommandAction.Macro macro:
                if (nested) throw KeymapException.InvalidCommandKey("macros cannot contain macros");
                if (macro.Steps.Count == 0 || macro.Steps.Count > CommandKey.MaximumMacroSteps)
                    throw KeymapException.InvalidCommandKey($"a macro needs 1–{CommandKey.MaximumMacroSteps} steps");
                foreach (var step in macro.Steps)
                {
                    if (!double.IsFinite(step.Delay) || !InRange(step.Delay, 0, MacroStep.MaximumDelay))
                        throw KeymapException.InvalidCommandKey("macro delays must be 0–10 s");
                    ValidateAction(step.Action, nested: true);
                }
                break;
        }
    }

    /// <summary>
    /// Every problem, errors first. Allowed on purpose: one Dial Key and one
    /// tap Command Key on the same chord (hold adjusts, tap runs the action).
    /// A combo is simply another chord: "Tab + K" and "K" never conflict.
    /// </summary>
    public IReadOnlyList<KeyConflict> Conflicts()
    {
        var result = new List<KeyConflict>();
        string Name(KeyChord chord) => DisplayText(chord);

        // System Keys among themselves.
        var systemUsages = new Dictionary<KeyChord, List<KeyUsage>>();
        foreach (var role in SystemKeyRoles.All)
        {
            if (SystemKeys.ChordFor(role) is not { } chord) continue;
            if (!systemUsages.TryGetValue(chord, out var list)) systemUsages[chord] = list = [];
            list.Add(new KeyUsage.SystemKey(role));
        }
        foreach (var (chord, usages) in systemUsages)
        {
            if (usages.Count > 1)
                result.Add(new(ConflictSeverity.Error, chord, null, usages, $"{Name(chord)} is used by more than one system key."));
        }
        if (SystemKeys.KeyReferenceToggle is { IsCombo: true } toggle)
        {
            if (SystemKeys.ComboKey is not { } comboKey)
            {
                result.Add(new(ConflictSeverity.Warning, toggle, null, [new KeyUsage.SystemKey(SystemKeyRole.KeyReferenceToggle)],
                    $"{Name(toggle)} does nothing until you set a Combo Key."));
            }
            else if (toggle.ScanCode == comboKey.ScanCode)
            {
                result.Add(new(ConflictSeverity.Error, toggle, null,
                    [new KeyUsage.SystemKey(SystemKeyRole.KeyReferenceToggle), new KeyUsage.SystemKey(SystemKeyRole.ComboKey)],
                    $"{Name(toggle)} can't work: a combo can't use the Combo Key itself."));
            }
        }

        foreach (var scope in ParameterScopes.All)
        {
            var byChord = new Dictionary<KeyChord, List<KeyUsage>>();
            void Add(KeyChord chord, KeyUsage usage)
            {
                if (!byChord.TryGetValue(chord, out var list)) byChord[chord] = list = [];
                list.Add(usage);
            }
            foreach (var (id, slot) in Slots(scope).OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                var key = new ParameterKey(scope, id);
                if (slot.Dial is { } dial) Add(dial, new KeyUsage.Dial(key));
                if (slot.NudgeDown is { } down) Add(down, new KeyUsage.NudgeDown(key));
                if (slot.NudgeUp is { } up) Add(up, new KeyUsage.NudgeUp(key));
            }
            foreach (var commandKey in CommandKeysIn(scope))
                Add(commandKey.Chord, new KeyUsage.Action(commandKey.Id, commandKey.Title));

            foreach (var (chord, usages) in byChord.OrderBy(pair => Name(pair.Key), StringComparer.Ordinal))
            {
                var text = Name(chord);
                var dials = usages.Count(usage => usage is KeyUsage.Dial);
                var nudges = usages.Count(usage => usage.IsNudge);
                var actions = usages.Count(usage => usage is KeyUsage.Action);
                string? problem = null;
                if (dials > 1) problem = $"{text} is the Dial Key of more than one slider.";
                else if (nudges > 1) problem = $"{text} is used by more than one Nudge Key.";
                else if (nudges > 0 && (dials > 0 || actions > 0)) problem = $"{text} is both a Nudge Key and another key.";
                else if (actions > 1) problem = $"{text} runs more than one action.";
                if (problem is not null) result.Add(new(ConflictSeverity.Error, chord, scope, usages, problem));

                if (systemUsages.TryGetValue(chord, out var roles))
                {
                    result.Add(new(ConflictSeverity.Error, chord, scope, [.. roles, .. usages],
                        $"{text} is a system key and cannot be assigned here."));
                }
                if (chord.IsCombo)
                {
                    result.AddRange(ComboProblems(chord, text, scope, usages));
                }
                else if (ReservedTypingKeys.Contains(chord.ScanCode) || chord.UsesReservedModifiers)
                {
                    result.Add(new(ConflictSeverity.Warning, chord, scope, usages,
                        $"{text} is also used for typing or by Lightroom and Windows."));
                }
            }
        }
        return result.OrderBy(conflict => conflict.Severity == ConflictSeverity.Error ? 0 : 1).ToList();
    }

    /// <summary>
    /// A combo can't use the Combo Key itself (that key is already held) or
    /// the pause key (which always pauses); without a Combo Key it does nothing.
    /// </summary>
    private IEnumerable<KeyConflict> ComboProblems(KeyChord chord, string text, ParameterScope scope, List<KeyUsage> usages)
    {
        if (SystemKeys.ComboKey is not { } comboKey)
        {
            return [new(ConflictSeverity.Warning, chord, scope, usages, $"{text} does nothing until you set a Combo Key.")];
        }
        if (chord.ScanCode == comboKey.ScanCode)
        {
            return [new(ConflictSeverity.Error, chord, scope, [.. usages, new KeyUsage.SystemKey(SystemKeyRole.ComboKey)],
                $"{text} can't work: a combo can't use the Combo Key itself.")];
        }
        if (SystemKeys.PauseToggle is { } pause && chord.WithoutCombo == pause)
        {
            return [new(ConflictSeverity.Error, chord, scope, [.. usages, new KeyUsage.SystemKey(SystemKeyRole.PauseToggle)],
                $"{text} can't work: {pause.DisplayText} always pauses Lumerist.")];
        }
        return [];
    }

    /// <summary>The Dial Keys and Nudge Keys of one scope, in catalog order, for the key overview.</summary>
    public AssignmentSummary Summary(ParameterScope scope)
    {
        var slots = Slots(scope);
        var entries = ParameterCatalog.All
            .Where(descriptor => descriptor.Scope == scope && slots.ContainsKey(descriptor.Id))
            .Select(descriptor => new AssignmentSummary.Entry(descriptor, slots[descriptor.Id]))
            .ToList();
        return new AssignmentSummary(
            scope,
            [.. entries.Where(entry => entry.Slot.Dial is not null)],
            [.. entries.Where(entry => entry.Slot.NudgeDown is not null || entry.Slot.NudgeUp is not null)]);
    }
}

/// <summary>The Dial Keys and Nudge Keys of one scope, for the key overview.</summary>
public sealed record AssignmentSummary(
    ParameterScope Scope,
    IReadOnlyList<AssignmentSummary.Entry> DialKeys,
    IReadOnlyList<AssignmentSummary.Entry> NudgeKeys)
{
    public sealed record Entry(ParameterDescriptor Descriptor, ParameterSlot Slot);
}
