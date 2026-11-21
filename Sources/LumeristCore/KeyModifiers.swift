/// Modifier keys that matter to Dial Key, Nudge Key and Command Key decisions.
public struct KeyModifiers: OptionSet, Codable, Sendable, Hashable {
    public let rawValue: Int
    public init(rawValue: Int) { self.rawValue = rawValue }

    public static let shift = KeyModifiers(rawValue: 1 << 0)
    public static let control = KeyModifiers(rawValue: 1 << 1)
    public static let option = KeyModifiers(rawValue: 1 << 2)
    public static let command = KeyModifiers(rawValue: 1 << 3)
    /// Not a macOS modifier: a chord with it is a *combo*, which works only
    /// while the Combo Key is held ("Tab + K"). Keyboard events never carry
    /// it; only bindings do.
    public static let combo = KeyModifiers(rawValue: 1 << 8)

    /// Command, Control, and Option combinations belong to Lightroom and macOS
    /// (Cmd+R, Ctrl+Z, ...). Shift alone is allowed: it selects the coarse step.
    var reservesKey: Bool {
        !isDisjoint(with: [.command, .control, .option])
    }
}
