/// Lightroom Classic's own default keys where Lumerist can take a key over:
/// Library, Develop and the Import window. Lumerist warns, without
/// blocking, when a tap key (Command Key, Nudge Key, Import key) is one of
/// them in its own place. The list is Shared/lightroom-shortcuts.json, from
/// Adobe's public help (the Import window's keys are community reports);
/// `LightroomShortcutData.swift` is generated from it.
///
/// Adobe writes the keys for US keyboards. Letters and digits match by the
/// character the key types on the active layout, as Lightroom's own
/// shortcuts do; arrows, F-keys and named keys (Tab, Space, Return…) match
/// by key. Punctuation never matches: which key types "[" or "`" differs
/// from layout to layout, and a missing warning is better than a wrong one.
public enum LightroomShortcuts {
    public enum Context: String, Sendable, Hashable, CaseIterable {
        case library, develop
        case importWindow = "import"

        public init(_ scope: ParameterScope) {
            switch scope {
            case .develop: self = .develop
            case .library: self = .library
            }
        }

        /// "Library", "Develop", "the Import window", as in "… in Library."
        public var place: String {
            switch self {
            case .library: "Library"
            case .develop: "Develop"
            case .importWindow: "the Import window"
            }
        }
    }

    /// One of Lightroom's keys found for a chord.
    public struct Match: Sendable, Hashable {
        /// What the key does in Lightroom, for the warning: "Toggle Zoom view".
        public let name: String
        public let context: Context
        /// From community reports rather than Adobe's help.
        public let isCommunityReported: Bool
        /// In the Import window, the key that Import key itself sends.
        public let importRole: ImportKeys.Role?
    }

    /// Lightroom's default use of `chord` in `context`, if Lumerist can tell.
    /// Combos are Lumerist's own keys and never match.
    public static func match(_ chord: KeyChord, in context: Context) -> Match? {
        match(chord, in: context, layout: KeyNames.layoutName)
    }

    /// `layout`: the active layout's key names, as `KeyNames.layoutName`.
    static func match(_ chord: KeyChord, in context: Context, layout: ((Int64) -> String?)?) -> Match? {
        guard !chord.isCombo, let key = matchableKey(chord.keyCode, layout: layout) else { return nil }
        return index[context]?[LogicalChord(key: key, modifiers: chord.modifiers)]
    }

    // MARK: Table

    struct Entry: Sendable {
        let name: String
        /// Logical chords ("Primary+Shift+Z"), as in the shared file.
        let mac: [String]
        let contexts: [Context]
        var isCommunityReported = false
        var importRole: ImportKeys.Role? = nil
    }

    struct LogicalChord: Sendable, Hashable {
        let key: String
        let modifiers: KeyModifiers
    }

    /// The first entry for a chord wins, in the order of Adobe's help.
    private static let index: [Context: [LogicalChord: Match]] = {
        var index: [Context: [LogicalChord: Match]] = [:]
        for entry in table {
            for text in entry.mac {
                guard let chord = logicalChord(text) else { continue }
                for context in entry.contexts where index[context]?[chord] == nil {
                    index[context, default: [:]][chord] = Match(name: entry.name, context: context,
                                                               isCommunityReported: entry.isCommunityReported,
                                                               importRole: entry.importRole)
                }
            }
        }
        return index
    }()

    /// "Primary+Shift+Z" → Z with ⌘⇧; nil for an unknown modifier.
    static func logicalChord(_ text: String) -> LogicalChord? {
        var parts = text.components(separatedBy: "+")
        guard let key = parts.popLast(), !key.isEmpty else { return nil }
        var modifiers: KeyModifiers = []
        for part in parts {
            switch part {
            case "Shift": modifiers.insert(.shift)
            case "Alt": modifiers.insert(.option)
            case "Primary": modifiers.insert(.command)
            case "Control": modifiers.insert(.control)
            default: return nil
            }
        }
        return LogicalChord(key: key, modifiers: modifiers)
    }

    // MARK: Keys

    /// Keys that are the same on every layout, by macOS key code.
    private static let fixedKeys: [Int64: String] = [
        123: "ArrowLeft", 124: "ArrowRight", 125: "ArrowDown", 126: "ArrowUp",
        48: "Tab", 49: "Space", 36: "Return", 53: "Esc", 51: "Backspace", 117: "ForwardDelete",
        115: "Home", 119: "End", 116: "PageUp", 121: "PageDown",
        122: "F1", 120: "F2", 99: "F3", 118: "F4", 96: "F5", 97: "F6",
        98: "F7", 100: "F8", 101: "F9", 109: "F10", 103: "F11", 111: "F12",
    ]

    /// The logical name to look up, or nil for a key that is never matched:
    /// a letter or digit by the character it types on the active layout
    /// (`KeyNames`; a Turkish "i" reads lowercase and still is I, the dotless
    /// ı is no I), a fixed key by its code, punctuation never.
    static func matchableKey(_ keyCode: Int64, layout: ((Int64) -> String?)?) -> String? {
        if let fixed = fixedKeys[keyCode] { return fixed }
        guard KeyNames.layoutDependentKeyCodes.contains(keyCode) else { return nil }
        let name = KeyNames.name(for: keyCode, layout: layout)
        guard name.unicodeScalars.count == 1, let scalar = name.unicodeScalars.first, scalar.isASCII,
              scalar.properties.isAlphabetic || ("0"..."9").contains(scalar) else { return nil }
        return name.uppercased()
    }
}
