// MARK: - Import keys

/// Keys for culling in Lightroom Classic's Import window, where the Develop
/// and Library keys pause. Each one presses one of Lightroom's own Import
/// shortcuts (`ImportShortcuts`), so the photo is ticked or unticked by
/// Lightroom itself.
///
/// None is assigned in the standard layout: Lumerist takes over a key only
/// when the user picks one (a preset Z would hide Lightroom's zoom).
public struct ImportKeys: Codable, Sendable, Hashable {
    public enum Role: String, Codable, Sendable, CaseIterable {
        case include, exclude, toggle
    }

    /// Ticks the photo, so Lightroom imports it (Lightroom's P).
    public var include: KeyChord?
    /// Unticks the photo, so Lightroom leaves it out (Lightroom's X).
    public var exclude: KeyChord?
    /// Ticks or unticks the photo (Lightroom's ` key; the < key left of Z on European keyboards).
    public var toggle: KeyChord?
    /// After Include or Exclude, Lumerist also presses → so the next photo
    /// is selected.
    public var advance: Bool

    public init(include: KeyChord? = nil, exclude: KeyChord? = nil, toggle: KeyChord? = nil, advance: Bool = false) {
        self.include = include
        self.exclude = exclude
        self.toggle = toggle
        self.advance = advance
    }

    public subscript(role: Role) -> KeyChord? {
        get {
            switch role {
            case .include: include
            case .exclude: exclude
            case .toggle: toggle
            }
        }
        set {
            switch role {
            case .include: include = newValue
            case .exclude: exclude = newValue
            case .toggle: toggle = newValue
            }
        }
    }

    /// The Import key pressed, by exact chord: Shift + Z is not Z.
    public func role(for chord: KeyChord) -> Role? {
        Role.allCases.first { self[$0] == chord }
    }

    /// Cleared keys are written as null; fields missing from a file (or the
    /// whole section, in keymaps saved before Import keys existed) take the
    /// defaults, which assign nothing.
    enum CodingKeys: String, CodingKey, CaseIterable {
        case include, exclude, toggle, advance
    }

    public init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        let defaults = ImportKeys()
        func read(_ key: CodingKeys, _ fallback: KeyChord?) throws -> KeyChord? {
            guard c.contains(key) else { return fallback }
            if try c.decodeNil(forKey: key) { return nil }
            return try c.decode(KeyChord.self, forKey: key)
        }
        include = try read(.include, defaults.include)
        exclude = try read(.exclude, defaults.exclude)
        toggle = try read(.toggle, defaults.toggle)
        advance = try c.decodeIfPresent(Bool.self, forKey: .advance) ?? defaults.advance
    }

    public func encode(to encoder: Encoder) throws {
        var c = encoder.container(keyedBy: CodingKeys.self)
        func write(_ value: KeyChord?, _ key: CodingKeys) throws {
            if let value { try c.encode(value, forKey: key) } else { try c.encodeNil(forKey: key) }
        }
        try write(include, .include)
        try write(exclude, .exclude)
        try write(toggle, .toggle)
        try c.encode(advance, forKey: .advance)
    }
}

// MARK: - Lightroom's Import shortcuts

/// Lightroom Classic's own keys in its Import window, which the Import keys
/// press: P includes, X excludes, key code 50 toggles (` on US keyboards,
/// the < key left of Z on European ISO ones, as tested on an Italian Mac), and
/// → selects the next photo.
///
/// `standard` holds the US (ANSI) key codes, which the shared test cases
/// expect. The app may replace them with the keys that type the same
/// characters on the active keyboard layout, as it does for Lightroom's
/// other shortcuts.
public struct ImportShortcuts: Sendable, Hashable {
    public var include: KeyChord
    public var exclude: KeyChord
    public var toggle: KeyChord
    public var nextPhoto: KeyChord

    public init(include: KeyChord, exclude: KeyChord, toggle: KeyChord, nextPhoto: KeyChord) {
        self.include = include
        self.exclude = exclude
        self.toggle = toggle
        self.nextPhoto = nextPhoto
    }

    public static let standard = ImportShortcuts(
        include: KeyChord(35),                      // P
        exclude: KeyChord(7),                       // X
        toggle: .grave,                             // `, key code 50
        nextPhoto: KeyChord(KeyChord.rightArrow)    // →
    )

    public func chord(for role: ImportKeys.Role) -> KeyChord {
        switch role {
        case .include: include
        case .exclude: exclude
        case .toggle: toggle
        }
    }
}
