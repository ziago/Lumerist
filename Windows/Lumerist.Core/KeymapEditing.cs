namespace Lumerist.Core;

// The rules behind key editing in the main window (Views/KeymapEditing.swift):
// slot roles, how a key's use is named, the System Key texts, and the decision
// to commit an edit, refuse it, or ask to move a key that is already in use.

/// <summary>Which key of a parameter slot is being edited.</summary>
public enum SlotRole
{
    Dial,
    NudgeDown,
    NudgeUp,
}

public static class SlotRoles
{
    public static IReadOnlyList<SlotRole> All { get; } = [SlotRole.Dial, SlotRole.NudgeDown, SlotRole.NudgeUp];

    /// <summary>"Dial Key", "Nudge −", "Nudge +".</summary>
    public static string Title(this SlotRole role) => role switch
    {
        SlotRole.Dial => "Dial Key",
        SlotRole.NudgeDown => "Nudge −",
        _ => "Nudge +",
    };

    /// <summary>"dial", "nudgeDown", "nudgeUp": part of a key box's identity.</summary>
    public static string WireName(this SlotRole role) => role switch
    {
        SlotRole.Dial => "dial",
        SlotRole.NudgeDown => "nudgeDown",
        _ => "nudgeUp",
    };

    public static KeyChord? Chord(this SlotRole role, ParameterSlot? slot) => role switch
    {
        SlotRole.Dial => slot?.Dial,
        SlotRole.NudgeDown => slot?.NudgeDown,
        _ => slot?.NudgeUp,
    };

    /// <summary>The slot with this role's key replaced (null clears it).</summary>
    public static ParameterSlot With(this SlotRole role, ParameterSlot slot, KeyChord? chord) => role switch
    {
        SlotRole.Dial => slot with { Dial = chord },
        SlotRole.NudgeDown => slot with { NudgeDown = chord },
        _ => slot with { NudgeUp = chord },
    };

    public static KeyUsage Usage(this SlotRole role, ParameterKey key) => role switch
    {
        SlotRole.Dial => new KeyUsage.Dial(key),
        SlotRole.NudgeDown => new KeyUsage.NudgeDown(key),
        _ => new KeyUsage.NudgeUp(key),
    };
}

/// <summary>Names for where a key is used, and the System Keys' texts.</summary>
public static class KeyUsageText
{
    /// <summary>The Mac's key code 40 (K): the example combo in the Combo Key's text.</summary>
    private const int ExampleComboScanCode = 0x25;

    /// <summary>"the Dial Key of Exposure", "the Nudge + key of Contrast", "the Command Key “Auto Tone”", "the Combo Key".</summary>
    public static string Label(this KeyUsage usage) => usage switch
    {
        KeyUsage.Dial dial => $"the Dial Key of {ParameterLabel(dial.Parameter)}",
        KeyUsage.NudgeDown down => $"the Nudge − key of {ParameterLabel(down.Parameter)}",
        KeyUsage.NudgeUp up => $"the Nudge + key of {ParameterLabel(up.Parameter)}",
        KeyUsage.Action action => $"the Command Key “{action.Title}”",
        KeyUsage.SystemKey system => $"the {system.Role.Title()}",
        _ => "",
    };

    private static string ParameterLabel(ParameterKey key) => ParameterCatalog.Descriptor(key)?.Label ?? key.Id;

    public static string Title(this SystemKeyRole role) => role switch
    {
        SystemKeyRole.ComboKey => "Combo Key",
        SystemKeyRole.PaletteKey => "Palette Key",
        SystemKeyRole.KeyReferenceToggle => "Key Reference toggle",
        _ => "Pause toggle",
    };

    /// <summary>The longer explanation; combos are written with the current Combo Key.</summary>
    public static string Detail(this SystemKeyRole role, KeyChord? comboKey)
    {
        switch (role)
        {
            case SystemKeyRole.ComboKey:
                var combo = new KeyChord(ExampleComboScanCode, KeyModifiers.Combo).DisplayTextWith(comboKey); // "Tab + K"
                return $"Gives every key a second job: hold the Combo Key, then press the other key (shown as {combo}). "
                    + "To assign a combo, click a key box and press the two keys together. Tapped alone, it still reaches Lightroom.";
            case SystemKeyRole.PaletteKey:
                return "Hold it in Develop to open the Palette under the pointer: pick a Create New Mask tool (Subject, Sky, "
                    + "People…) or a favourite preset. Hold it for a quick look; tap it and the Palette stays open until you press the key again. Clear the key to turn the Palette off.";
            case SystemKeyRole.KeyReferenceToggle:
                var standard = KeyChord.ComboH.DisplayTextWith(comboKey); // "Tab + H"
                return $"Shows or hides the Key Reference, the floating list of your keys. The standard layout uses the combo {standard}.";
            default:
                return "Hands every key back to Lightroom until you press it again. Handy when you need to type.";
        }
    }
}

/// <summary>Usage → conflict message for one scope (and the System Keys), computed once per drawing.</summary>
public sealed class ConflictIndex
{
    private readonly Dictionary<KeyUsage, string> messages = [];

    public ConflictIndex(Keymap keymap, ParameterScope scope)
    {
        foreach (var conflict in keymap.Conflicts())
        {
            if (conflict.Scope != scope && conflict.Scope is not null) continue;
            foreach (var usage in conflict.Usages) messages.TryAdd(usage, conflict.Message);
        }
    }

    public string? this[KeyUsage usage] => messages.GetValueOrDefault(usage);
}

/// <summary>What to do with a key assignment (KeymapEditor.commit on the Mac).</summary>
public abstract record KeymapEdit
{
    private KeymapEdit() { }

    /// <summary>Valid: put this keymap in use.</summary>
    public sealed record Commit(Keymap Keymap) : KeymapEdit;

    /// <summary>Moving keys can't fix it (a combo on a Dial Key…): say why, under the title "This key can't go here".</summary>
    public sealed record Refuse(string Message) : KeymapEdit;

    /// <summary>
    /// The key is already used elsewhere: ask before moving it. When
    /// <paramref name="CanMove"/> is false only an OK is offered (a System Key
    /// is in the way). <paramref name="Moved"/> is the keymap with the key
    /// moved, or null with <paramref name="MoveError"/> when even that fails.
    /// </summary>
    public sealed record AskToMove(string Title, string Message, bool CanMove, Keymap? Moved, string? MoveError) : KeymapEdit;
}

public static class KeymapEditing
{
    public const string RefuseTitle = "This key can't go here";

    /// <summary>
    /// Removes <paramref name="chord"/> from every slot and Command Key of
    /// <paramref name="scope"/>, except the usage being assigned. A combo and
    /// the plain key are different chords.
    /// </summary>
    public static void RemoveChord(this Keymap keymap, KeyChord chord, ParameterScope scope, KeyUsage? keeping)
    {
        foreach (var id in keymap.Slots(scope).Keys.ToList())
        {
            var key = new ParameterKey(scope, id);
            keymap.UpdateSlot(key, slot =>
            {
                if (slot.Dial == chord && keeping != new KeyUsage.Dial(key)) slot = slot with { Dial = null };
                if (slot.NudgeDown == chord && keeping != new KeyUsage.NudgeDown(key)) slot = slot with { NudgeDown = null };
                if (slot.NudgeUp == chord && keeping != new KeyUsage.NudgeUp(key)) slot = slot with { NudgeUp = null };
                return slot;
            });
        }
        keymap.CommandKeys.RemoveAll(commandKey =>
            commandKey.Scope.Includes(scope) && commandKey.Chord == chord
            && !(keeping is KeyUsage.Action action && action.Id == commandKey.Id));
    }

    /// <summary>Error conflicts that involve <paramref name="chord"/> in <paramref name="scope"/> (null: among System Keys).</summary>
    private static IReadOnlyList<KeyConflict> BlockingConflicts(this Keymap keymap, KeyChord chord, ParameterScope? scope) =>
        keymap.Conflicts()
            .Where(conflict => conflict.Severity == ConflictSeverity.Error && conflict.Chord == chord
                && (conflict.Scope is null || conflict.Scope == scope))
            .ToList();

    /// <summary>
    /// Applies <paramref name="change"/> to a copy of <paramref name="current"/>
    /// and validates it: commit, refuse with the reason, or ask to move the
    /// keys in the way. <paramref name="current"/> is never changed.
    /// </summary>
    private static KeymapEdit Plan(Keymap current, KeyChord? chord, ParameterScope? scope, KeyUsage? usage, Action<Keymap> change)
    {
        var proposed = current.Clone();
        change(proposed);
        try
        {
            proposed.Validate();
            return new KeymapEdit.Commit(proposed);
        }
        catch (KeymapException error)
        {
            if (chord is not { } key || error.Kind != KeymapErrorKind.Conflicts) return new KeymapEdit.Refuse(error.Message);
            var others = proposed.BlockingConflicts(key, scope).SelectMany(conflict => conflict.Usages)
                .Where(other => other != usage).ToList();
            if (others.Count == 0) return new KeymapEdit.Refuse(error.Message);
            var system = others.Any(other => other is KeyUsage.SystemKey);
            var names = string.Join(", ", others.Select(other => other.Label()).Distinct().Order(StringComparer.Ordinal));
            var text = proposed.DisplayText(key);
            var message = system
                ? $"{text} is {names}. Change that system key first (General → System Keys)."
                : $"{text} is {names}. Move it here?";
            var canMove = !system && scope is not null;
            Keymap? moved = null;
            string? moveError = null;
            if (canMove)
            {
                moved = current.Clone();
                moved.RemoveChord(key, scope!.Value, usage);
                change(moved);
                try
                {
                    moved.Validate();
                }
                catch (KeymapException moveFailure)
                {
                    moved = null;
                    moveError = moveFailure.Message;
                }
            }
            return new KeymapEdit.AskToMove($"{text} is already in use", message, canMove, moved, moveError);
        }
    }

    /// <summary>A parameter slot's key: <paramref name="chord"/> null clears it.</summary>
    public static KeymapEdit PlanAssign(Keymap current, KeyChord? chord, SlotRole role, ParameterKey key) =>
        Plan(current, chord, key.Scope, role.Usage(key), keymap => keymap.UpdateSlot(key, slot => role.With(slot, chord)));

    /// <summary>A System Key: <paramref name="chord"/> null turns that key off.</summary>
    public static KeymapEdit PlanSystemKey(Keymap current, SystemKeyRole role, KeyChord? chord) =>
        Plan(current, chord, null, new KeyUsage.SystemKey(role), keymap =>
        {
            switch (role)
            {
                case SystemKeyRole.ComboKey: keymap.SystemKeys.ComboKey = chord; break;
                case SystemKeyRole.PaletteKey: keymap.SystemKeys.PaletteKey = chord; break;
                case SystemKeyRole.KeyReferenceToggle: keymap.SystemKeys.KeyReferenceToggle = chord; break;
                default: keymap.SystemKeys.PauseToggle = chord; break;
            }
        });

    /// <summary>
    /// The standard layout with the user's options kept (General → Restore):
    /// mouse, trackpad and Lightroom options and the Palette stay.
    /// </summary>
    public static Keymap StandardLayoutKeeping(Keymap current)
    {
        var restored = Keymap.StandardLayout();
        restored.Input = current.Input with { };
        restored.Palette = current.Palette with { Favourites = [.. current.Palette.Favourites] };
        return restored;
    }
}
