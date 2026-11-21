/// A physical key plus the modifiers held with it. Stored as a hardware key
/// code, so a chord follows the physical key whatever the keyboard layout.
public struct KeyChord: Codable, Sendable, Hashable, CustomStringConvertible {
    public var keyCode: Int64
    public var modifiers: KeyModifiers

    public init(_ keyCode: Int64, _ modifiers: KeyModifiers = []) {
        self.keyCode = keyCode
        self.modifiers = modifiers
    }

    /// "⇧Tab", "Num *", "Q"; a combo reads "Combo + K".
    public var displayText: String {
        displayText(comboKey: nil)
    }

    /// Like `displayText`, but a combo names the Combo Key: "Tab + K".
    /// Without a Combo Key it reads "Combo + K".
    public func displayText(comboKey: KeyChord?) -> String {
        let text = KeyNames.modifierSymbols(modifiers) + KeyNames.name(for: keyCode)
        guard isCombo else { return text }
        return "\(comboKey?.withoutCombo.displayText ?? "Combo") + \(text)"
    }

    public var description: String { displayText }

    /// True when the chord works only while the Combo Key is held.
    public var isCombo: Bool { modifiers.contains(.combo) }

    /// The same chord as a combo (Combo Key held).
    public var asCombo: KeyChord { KeyChord(keyCode, modifiers.union(.combo)) }

    /// The key and real modifiers of a combo, without the Combo Key.
    public var withoutCombo: KeyChord { KeyChord(keyCode, modifiers.subtracting(.combo)) }

    /// True when ⌘, ⌃ or ⌥ is part of the chord (these belong to macOS and
    /// Lightroom unless a binding names them explicitly).
    var usesReservedModifiers: Bool { modifiers.reservesKey }

    // MARK: Common keys

    public static let tab = KeyChord(48)
    /// The key left of 1 on US keyboards; next to the left Shift on ISO ones.
    public static let grave = KeyChord(50)
    /// Left of 1 on ISO keyboards (on ANSI ones that key is `grave`).
    public static let isoSection = KeyChord(10)
    /// Combo Key + H ("H for help"): the default Key Reference toggle.
    public static let comboH = KeyChord(4, .combo)
    public static let controlEscape = KeyChord(53, .control)
    public static let leftArrow: Int64 = 123
    public static let rightArrow: Int64 = 124
}

/// Looks a chord up in a table where Shift may be "extra": a key bound
/// without Shift (alone or as a combo) still matches when Shift is held,
/// because Shift selects the coarse step. An exact match always wins.
enum ChordLookup {
    static func match<Value>(_ chord: KeyChord, in table: [KeyChord: Value]) -> (value: Value, extraShift: Bool)? {
        if let exact = table[chord] { return (exact, false) }
        if chord.modifiers.subtracting(.combo) == .shift,
           let unshifted = table[KeyChord(chord.keyCode, chord.modifiers.subtracting(.shift))] {
            return (unshifted, true)
        }
        return nil
    }
}
