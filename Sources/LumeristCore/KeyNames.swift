/// Human-readable names for macOS hardware key codes.
///
/// Keys are stored as key codes, so a binding follows the physical key. The
/// app installs `layoutName`, which reads the character the active keyboard
/// layout prints on a key (AZERTY, QWERTZ, Italian…); without it, or for keys
/// it does not name, the US (ANSI) names below are used.
public enum KeyNames {
    /// Layout-aware name for a key code, or nil to use the ANSI name.
    /// Installed once by the app at launch; read from the main thread.
    nonisolated(unsafe) public static var layoutName: (@Sendable (Int64) -> String?)?

    /// Key codes whose printed character depends on the keyboard layout
    /// (letters, digits, punctuation, and the ISO key next to left Shift).
    public static let layoutDependentKeyCodes: Set<Int64> = Set((0...50).map(Int64.init))
        .subtracting([36, 48, 49]) // Return, Tab, Space

    private static let names: [Int64: String] = [
        0: "A", 1: "S", 2: "D", 3: "F", 4: "H", 5: "G", 6: "Z", 7: "X", 8: "C", 9: "V",
        11: "B", 12: "Q", 13: "W", 14: "E", 15: "R", 16: "Y", 17: "T",
        18: "1", 19: "2", 20: "3", 21: "4", 22: "6", 23: "5", 24: "=", 25: "9", 26: "7",
        27: "-", 28: "8", 29: "0", 30: "]", 31: "O", 32: "U", 33: "[", 34: "I", 35: "P",
        36: "Return", 37: "L", 38: "J", 39: "'", 40: "K", 41: ";", 42: "\\", 43: ",",
        44: "/", 45: "N", 46: "M", 47: ".", 48: "Tab", 49: "Space", 50: "`",
        51: "Delete", 53: "Esc",
        96: "F5", 97: "F6", 98: "F7", 99: "F3", 100: "F8", 101: "F9", 103: "F11",
        109: "F10", 111: "F12", 118: "F4", 120: "F2", 122: "F1",
        123: "←", 124: "→", 125: "↓", 126: "↑",
        // Numeric keypad: distinct physical keys from the main row.
        65: "Num .", 67: "Num *", 69: "Num +", 75: "Num /", 76: "Num Enter", 78: "Num -", 81: "Num =",
        82: "Num 0", 83: "Num 1", 84: "Num 2", 85: "Num 3", 86: "Num 4", 87: "Num 5",
        88: "Num 6", 89: "Num 7", 91: "Num 8", 92: "Num 9",
    ]

    /// macOS order and symbols for modifier keys: ⌃ ⌥ ⇧ ⌘.
    public static func modifierSymbols(_ modifiers: KeyModifiers) -> String {
        var text = ""
        if modifiers.contains(.control) { text += "⌃" }
        if modifiers.contains(.option) { text += "⌥" }
        if modifiers.contains(.shift) { text += "⇧" }
        if modifiers.contains(.command) { text += "⌘" }
        return text
    }

    /// The key's name on the active layout: "Q", "Tab", "Num *".
    public static func name(for keyCode: Int64) -> String {
        name(for: keyCode, layout: layoutName)
    }

    /// Name using an explicit layout lookup (testable without global state).
    static func name(for keyCode: Int64, layout: ((Int64) -> String?)?) -> String {
        if layoutDependentKeyCodes.contains(keyCode),
           let name = layout?(keyCode), !name.isEmpty {
            return name
        }
        return ansiName(for: keyCode)
    }

    /// The US (ANSI) name, independent of the active layout.
    static func ansiName(for keyCode: Int64) -> String {
        names[keyCode] ?? (keyCode == 10 ? "§" : "Key \(keyCode)")
    }
}
