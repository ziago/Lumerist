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
///
/// Lightroom takes its shortcuts from its interface language, so each
/// language Lightroom ships has the keys its help page lists differently
/// (`Language`): `activeLanguage` picks one, English when unknown.
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

    /// Lightroom's interface language as the plug-in reports it ("it",
    /// "zh_cn"…), or nil when not known yet. Installed by the app (main
    /// thread); tests pass a language explicitly instead.
    nonisolated(unsafe) public static var activeLanguage: String?

    /// Lightroom's default use of `chord` in `context`, if Lumerist can tell,
    /// in Lightroom's language (`activeLanguage`). Combos are Lumerist's own
    /// keys and never match.
    public static func match(_ chord: KeyChord, in context: Context) -> Match? {
        match(chord, in: context, layout: KeyNames.layoutName, language: activeLanguage)
    }

    /// Like `match`, in a given Lightroom language (nil: English).
    public static func match(_ chord: KeyChord, in context: Context, language: String?) -> Match? {
        match(chord, in: context, layout: KeyNames.layoutName, language: language)
    }

    /// `layout`: the active layout's key names, as `KeyNames.layoutName`.
    static func match(_ chord: KeyChord, in context: Context, layout: ((Int64) -> String?)?,
                      language: String? = nil) -> Match? {
        guard !chord.isCombo, let key = matchableKey(chord.keyCode, layout: layout) else { return nil }
        return indexes[Language.forCode(language).id]?[context]?[LogicalChord(key: key, modifiers: chord.modifiers)]
    }

    // MARK: Languages

    /// One of the languages Lightroom Classic ships, from
    /// Shared/lightroom-shortcuts.json.
    public struct Language: Sendable, Hashable {
        /// "it", "zh_cn"…
        public let id: String
        /// How General names it: "Italiano".
        public let name: String
        /// The `LrLocalization` codes that select it.
        let codes: [String]
        /// The character Lightroom's Import window toggles with: < in
        /// Italian and Swedish, ` otherwise.
        public let importToggle: String
        let overrides: [Override]

        public static var english: Language { languages[0] }

        /// The language a code names, as the apps write codes: an exact code,
        /// then the part before "_" ("de_ch" is German; never for zh, whose
        /// regions are separate languages). nil for a language Lightroom does
        /// not ship.
        public static func matching(_ code: String) -> Language? {
            if let exact = languages.first(where: { $0.codes.contains(code) }) { return exact }
            let base = String(code.prefix { $0 != "_" })
            guard base != code, base != "zh" else { return nil }
            return languages.first { $0.codes.contains(base) }
        }

        /// The language whose keys Lumerist uses: English when the code is
        /// unknown or nil.
        public static func forCode(_ code: String?) -> Language {
            code.flatMap(matching) ?? english
        }
    }

    /// An entry of `table` with other keys in one language. `press`: Lumerist
    /// may press it (only for an entry with `fallbackFor`).
    struct Override: Sendable, Hashable {
        let entry: Int
        let mac: [String]
        let press: Bool
    }

    /// The chord Lumerist presses for one of its commands (`Entry.fallbackFor`)
    /// in `language`, when that language's help gives another key that may
    /// be pressed; nil to keep the English key.
    static func pressedChord(for command: String, language: String?) -> String? {
        let language = Language.forCode(language)
        for override in language.overrides where override.press && table[override.entry].fallbackFor == command {
            return override.mac.first
        }
        return nil
    }

    // MARK: Table

    struct Entry: Sendable {
        let name: String
        /// Logical chords ("Primary+Shift+Z"), as in the shared file.
        let mac: [String]
        let contexts: [Context]
        var isCommunityReported = false
        var importRole: ImportKeys.Role? = nil
        /// The Lumerist command that presses this key (`FallbackShortcuts`, `AppCommand`).
        var fallbackFor: String? = nil
    }

    struct LogicalChord: Sendable, Hashable {
        let key: String
        let modifiers: KeyModifiers
    }

    /// Per language: the first entry for a chord wins, in the order of
    /// Adobe's help, with the language's own keys in place of English ones.
    private static let indexes: [String: [Context: [LogicalChord: Match]]] = {
        Dictionary(uniqueKeysWithValues: languages.map { ($0.id, index(for: $0)) })
    }()

    private static func index(for language: Language) -> [Context: [LogicalChord: Match]] {
        var chords = table.map(\.mac)
        for override in language.overrides { chords[override.entry] = override.mac }
        var index: [Context: [LogicalChord: Match]] = [:]
        for (entry, mac) in zip(table, chords) {
            for text in mac {
                guard let chord = logicalChord(text) else { continue }
                for context in entry.contexts where index[context]?[chord] == nil {
                    index[context, default: [:]][chord] = Match(name: entry.name, context: context,
                                                               isCommunityReported: entry.isCommunityReported,
                                                               importRole: entry.importRole)
                }
            }
        }
        return index
    }

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
    static let fixedKeys: [Int64: String] = [
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
