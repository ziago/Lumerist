// MARK: - What the keyboard layout types

/// What the active keyboard layout types, as far as Lumerist needs it: the
/// names printed on the keys, and the keys that type Lightroom's own
/// shortcuts. Lightroom finds its shortcuts by the character a key types, so
/// Lumerist presses them by character too, never by position.
///
/// The app builds it from the input source (UCKeyTranslate); tests build it
/// by hand.
public struct LayoutCharacters: Sendable, Hashable {
    /// The character each key types with no modifier. Dead keys are left
    /// out: alone they type nothing.
    public var plain: [Int64: String]
    /// The character each key types with ⌘, from the layout's own ⌘ table:
    /// QWERTY on "Dvorak – QWERTY ⌘" and on most non-Latin layouts.
    public var command: [Int64: String]
    /// What each key shows: its plain character, or a dead key's own accent.
    public var display: [Int64: String]

    /// `display` defaults to `plain`.
    public init(plain: [Int64: String], command: [Int64: String] = [:], display: [Int64: String]? = nil) {
        self.plain = plain
        self.command = command
        self.display = display ?? plain
    }

    /// The keys searched, in this order: the main keys by key code, then the
    /// JIS ¥ and _ keys.
    public static let searchOrder: [Int64] = KeyNames.layoutDependentKeyCodes.sorted()

    /// The first key that types `character` (in either case) alone, or with
    /// ⌘ when `withCommand`; nil when no key does.
    public func keyCode(typing character: String, withCommand: Bool = false) -> Int64? {
        let table = withCommand ? command : plain
        let wanted = character.lowercased()
        return Self.searchOrder.first { table[$0]?.lowercased() == wanted }
    }

    /// The key names for `KeyNames.layoutName` (see `KeyNames.label`).
    public var names: [Int64: String] {
        let hasDotlessI = display.values.contains("ı")
        var names: [Int64: String] = [:]
        for (keyCode, text) in display {
            if let label = KeyNames.label(forTyped: text, layoutHasDotlessI: hasDotlessI) { names[keyCode] = label }
        }
        return names
    }
}

// MARK: - Lightroom's own keys, by character

/// One of Lightroom's own shortcuts named by the letter it types, such as
/// ⌘Z: Lumerist presses whichever key types that letter on the active layout.
public struct CharacterShortcut: Sendable, Hashable {
    /// Lowercase.
    public let character: String
    public let modifiers: KeyModifiers
    /// The key on a US keyboard, used when no layout is known.
    public let usKeyCode: Int64

    public init(_ character: String, _ modifiers: KeyModifiers = [], usKeyCode: Int64) {
        self.character = character
        self.modifiers = modifiers
        self.usKeyCode = usKeyCode
    }

    /// The key to press. With ⌘ the layout's ⌘ table decides (the keys of
    /// "Dvorak – QWERTY ⌘" move under ⌘), without it the plain one. When no
    /// key types the letter, the ASCII-capable layout macOS keeps for
    /// shortcuts decides, and failing that the US position.
    public func chord(layout: LayoutCharacters?, asciiCapable: LayoutCharacters?) -> KeyChord {
        let withCommand = modifiers.contains(.command)
        let keyCode = layout?.keyCode(typing: character, withCommand: withCommand)
            ?? asciiCapable?.keyCode(typing: character, withCommand: withCommand)
            ?? usKeyCode
        return KeyChord(keyCode, modifiers)
    }
}

/// A key Lumerist presses in Lightroom: a key that is the same on every
/// layout (arrows), or the key that types a character.
public enum LightroomKeyPress: Sendable, Hashable {
    case key(KeyChord)
    case character(CharacterShortcut)

    /// A chord of Shared/lightroom-shortcuts.json ("Alt+Primary+V"): a key
    /// that types a character by that character, a named key by its code.
    init?(logicalChord text: String) {
        guard let chord = LightroomShortcuts.logicalChord(text) else { return nil }
        if let code = LightroomShortcuts.fixedKeys.first(where: { $0.value == chord.key })?.key {
            self = .key(KeyChord(code, chord.modifiers))
        } else if chord.key.count == 1, let code = KeyNames.usKeyCode(for: chord.key) {
            self = .character(CharacterShortcut(chord.key.lowercased(), chord.modifiers, usKeyCode: code))
        } else {
            return nil
        }
    }

    public func chord(layout: LayoutCharacters?, asciiCapable: LayoutCharacters?) -> KeyChord {
        switch self {
        case .key(let chord): chord
        case .character(let shortcut): shortcut.chord(layout: layout, asciiCapable: asciiCapable)
        }
    }
}

/// Lightroom's own shortcuts for the commands the SDK cannot run in every
/// module (Auto Tone in Library, flags…). Lumerist presses one when the
/// plug-in refuses the command.
public enum FallbackShortcuts {
    public static let flagPick = CharacterShortcut("p", usKeyCode: 35)                 // P
    public static let flagReject = CharacterShortcut("x", usKeyCode: 7)                // X

    /// The key for a command in Lightroom's language (nil: English): the
    /// English key unless that language's help gives another one marked as
    /// safe to press (none is, today; Shared/lightroom-shortcuts.json).
    public static func press(for command: LightroomCommandName, lightroomLanguage: String?) -> LightroomKeyPress? {
        if let chord = LightroomShortcuts.pressedChord(for: command.rawValue, language: lightroomLanguage),
           let press = LightroomKeyPress(logicalChord: chord) {
            return press
        }
        return press(for: command)
    }

    public static func press(for command: LightroomCommandName) -> LightroomKeyPress? {
        switch command {
        case .autoTone: .character(CharacterShortcut("u", .command, usKeyCode: 32))             // ⌘U
        case .undo: .character(CharacterShortcut("z", .command, usKeyCode: 6))                  // ⌘Z
        case .redo: .character(CharacterShortcut("z", [.command, .shift], usKeyCode: 6))        // ⇧⌘Z
        case .flagPick: .character(flagPick)
        case .flagReject: .character(flagReject)
        case .flagRemove: .character(CharacterShortcut("u", usKeyCode: 32))                     // U
        case .showClipping: .character(CharacterShortcut("j", usKeyCode: 38))                   // J
        case .nextPhoto: .key(KeyChord(KeyChord.rightArrow))                                    // →
        case .previousPhoto: .key(KeyChord(KeyChord.leftArrow))                                 // ←
        default: nil
        }
    }
}

// MARK: - Commands Lumerist runs itself

/// Commands the SDK has no call for, which Lumerist carries out itself by
/// pressing Lightroom's own shortcut; nothing goes to the plug-in. Keymaps
/// store them like Lightroom's commands, `{"command": "pasteSettingsFromPrevious"}`,
/// and their names never clash with `LightroomCommandName`.
public enum AppCommand: String, Sendable, Hashable, CaseIterable {
    /// Develop's Paste Settings from Previous, Lightroom's ⌥⌘V.
    case pasteSettingsFromPrevious

    /// The shortcut it presses.
    public var shortcut: CharacterShortcut {
        switch self {
        case .pasteSettingsFromPrevious: CharacterShortcut("v", [.option, .command], usKeyCode: 9)
        }
    }

    /// The key it presses in Lightroom's language (nil: English), like
    /// `FallbackShortcuts.press(for:lightroomLanguage:)`.
    public func press(lightroomLanguage: String?) -> LightroomKeyPress {
        if let chord = LightroomShortcuts.pressedChord(for: rawValue, language: lightroomLanguage),
           let press = LightroomKeyPress(logicalChord: chord) {
            return press
        }
        return .character(shortcut)
    }

    /// The Command Key that is one of these, if any.
    public init?(_ action: CommandKey.Action) {
        guard case .command(let name, nil) = action, let command = AppCommand(rawValue: name) else { return nil }
        self = command
    }
}
