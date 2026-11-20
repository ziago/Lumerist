namespace Lumerist.Core;

/// <summary>
/// Recording the next key press for a key box (the Mac's ChordRecorder, in
/// Design/KeySlot.swift), without the window. Holding the Combo Key and
/// pressing another key records a combo ("Tab + K"); the Combo Key pressed
/// and released alone records itself. Esc alone cancels; Backspace or Delete
/// alone clears the box. Modifier keys on their own are never fed in.
/// </summary>
public sealed class ChordRecording(KeyChord? comboKey)
{
    /// <summary>What a key event did to the recording.</summary>
    public enum Outcome
    {
        /// <summary>Still recording (a modifier, the Combo Key going down, an auto-repeat).</summary>
        Continue,
        /// <summary>Esc: stop without changing the box.</summary>
        Cancel,
        /// <summary>Backspace or Delete: empty the box.</summary>
        Clear,
        /// <summary><see cref="Chord"/> is the recorded key.</summary>
        Finish,
    }

    /// <summary>Del, the key Windows keyboards print beside Backspace.</summary>
    public const int DeleteScanCode = 0xE053;

    /// <summary>The Combo Key is down: the next key makes a combo.</summary>
    public bool IsComboKeyDown { get; private set; }

    /// <summary>The recorded chord once <see cref="Outcome.Finish"/> was returned.</summary>
    public KeyChord? Chord { get; private set; }

    /// <summary>A key went down (with the modifiers held, never Combo).</summary>
    public Outcome KeyDown(KeyChord chord, bool isRepeat)
    {
        var plain = chord.Modifiers == KeyModifiers.None;
        if (chord.ScanCode == ScanCodes.Escape && plain) return Outcome.Cancel;
        if (IsComboKeyDown)
        {
            if (comboKey is { } held && chord.ScanCode == held.ScanCode) return Outcome.Continue; // auto-repeat
            return Finish(chord.AsCombo);
        }
        if (comboKey is { } combo && chord == combo && !isRepeat)
        {
            IsComboKeyDown = true; // a combo or the Combo Key alone: the next event decides
            return Outcome.Continue;
        }
        if (plain && chord.ScanCode is ScanCodes.Backspace or DeleteScanCode) return Outcome.Clear;
        return Finish(chord);
    }

    /// <summary>A key went up: the Combo Key released alone records itself.</summary>
    public Outcome KeyUp(int scanCode)
    {
        if (IsComboKeyDown && comboKey is { } combo && scanCode == combo.ScanCode) return Finish(combo);
        return Outcome.Continue;
    }

    private Outcome Finish(KeyChord chord)
    {
        Chord = chord;
        return Outcome.Finish;
    }
}
