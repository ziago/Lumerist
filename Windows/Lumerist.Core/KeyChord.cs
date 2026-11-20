namespace Lumerist.Core;

/// <summary>Modifier keys that matter to Dial, Nudge and Command Key decisions.</summary>
[Flags]
public enum KeyModifiers
{
    None = 0,
    Shift = 1 << 0,
    /// <summary>Ctrl, left or right.</summary>
    Control = 1 << 1,
    /// <summary>Alt, left or right. AltGr (left Ctrl + right Alt) is a character key, not this.</summary>
    Alt = 1 << 2,
    /// <summary>
    /// Not a keyboard modifier: a chord with it is a combo, which works only
    /// while the Combo Key is held ("Tab + K"). Key events never carry it;
    /// only bindings do.
    /// </summary>
    Combo = 1 << 8,
}

public static class KeyModifiersExtensions
{
    /// <summary>
    /// Ctrl and Alt combinations belong to Lightroom and Windows (Ctrl+Z, Alt
    /// menus…). Shift alone is allowed: it selects the large step.
    /// </summary>
    public static bool ReservesKey(this KeyModifiers modifiers) =>
        (modifiers & (KeyModifiers.Control | KeyModifiers.Alt)) != 0;
}

/// <summary>
/// A physical key plus the modifiers held with it. Stored as a Set 1 scan
/// code (extended keys carry 0xE000, e.g. → is 0xE04D), so a chord follows the
/// physical key whatever the keyboard layout.
/// </summary>
public readonly record struct KeyChord(int ScanCode, KeyModifiers Modifiers = KeyModifiers.None)
{
    /// <summary>"Shift+Tab", "Num *", "Q"; a combo reads "Combo + K".</summary>
    public string DisplayText => DisplayTextWith(null);

    /// <summary>Like <see cref="DisplayText"/>, but a combo names the Combo Key: "Tab + K".</summary>
    public string DisplayTextWith(KeyChord? comboKey)
    {
        var text = KeyNames.ModifierPrefix(Modifiers) + KeyNames.Name(ScanCode);
        if (!IsCombo) return text;
        return $"{comboKey?.WithoutCombo.DisplayText ?? "Combo"} + {text}";
    }

    public override string ToString() => DisplayText;

    /// <summary>The same key without modifiers.</summary>
    public KeyChord Plain => new(ScanCode);

    /// <summary>True when the chord works only while the Combo Key is held.</summary>
    public bool IsCombo => (Modifiers & KeyModifiers.Combo) != 0;

    public KeyChord AsCombo => this with { Modifiers = Modifiers | KeyModifiers.Combo };

    /// <summary>The key and real modifiers of a combo, without the Combo Key.</summary>
    public KeyChord WithoutCombo => this with { Modifiers = Modifiers & ~KeyModifiers.Combo };

    /// <summary>True when Ctrl or Alt is part of the chord.</summary>
    public bool UsesReservedModifiers => Modifiers.ReservesKey();

    // Common keys.
    public static readonly KeyChord Tab = new(ScanCodes.Tab);
    /// <summary>The key left of 1, on ANSI and ISO keyboards alike.</summary>
    public static readonly KeyChord LeftOf1 = new(ScanCodes.LeftOf1);
    /// <summary>Combo Key + H ("H for help"): the default Key Reference toggle.</summary>
    public static readonly KeyChord ComboH = new(ScanCodes.H, KeyModifiers.Combo);
    public static readonly KeyChord ControlEscape = new(ScanCodes.Escape, KeyModifiers.Control);
}

/// <summary>Set 1 scan codes of the keys Lumerist refers to by name.</summary>
public static class ScanCodes
{
    public const int Escape = 0x01, Backspace = 0x0E, Tab = 0x0F, Enter = 0x1C, Space = 0x39;
    public const int LeftOf1 = 0x29;
    public const int Q = 0x10, W = 0x11, E = 0x12, R = 0x13, T = 0x14, P = 0x19;
    public const int A = 0x1E, S = 0x1F, D = 0x20, F = 0x21, H = 0x23;
    public const int X = 0x2D, C = 0x2E, V = 0x2F, B = 0x30, M = 0x32;
    public const int Digit1 = 0x02, Digit2 = 0x03, Digit3 = 0x04, Digit4 = 0x05, Digit5 = 0x06, Digit6 = 0x07;
    public const int LeftArrow = 0xE04B, RightArrow = 0xE04D, UpArrow = 0xE048, DownArrow = 0xE050;
}

/// <summary>
/// Looks a chord up in a table where Shift may be "extra": a key bound without
/// Shift (alone or as a combo) still matches when Shift is held, because Shift
/// selects the large step. An exact match always wins.
/// </summary>
public static class ChordLookup
{
    public static bool TryMatch<TValue>(KeyChord chord, IReadOnlyDictionary<KeyChord, TValue> table,
        out TValue value, out bool extraShift)
    {
        extraShift = false;
        if (table.TryGetValue(chord, out value!)) return true;
        if ((chord.Modifiers & ~KeyModifiers.Combo) == KeyModifiers.Shift &&
            table.TryGetValue(chord with { Modifiers = chord.Modifiers & ~KeyModifiers.Shift }, out value!))
        {
            extraShift = true;
            return true;
        }
        value = default!;
        return false;
    }
}
