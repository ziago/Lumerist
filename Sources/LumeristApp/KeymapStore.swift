import Foundation
import LumeristCore
import Observation

/// Owns the persisted Keymap (format 3): Dial Keys, Nudge Keys, Command Keys,
/// system keys, Import keys, Palette and input options.
@MainActor
@Observable
final class KeymapStore {
    static let storageKey = "org.lumerist.keymap.v4"

    @ObservationIgnored private let defaults: UserDefaults
    private(set) var keymap: Keymap

    init(defaults: UserDefaults = .standard) {
        self.defaults = defaults
        // Key names and Lightroom's fallback shortcuts follow the active
        // keyboard layout; created at launch, so this runs before any UI.
        KeyboardLayout.install()
        if let data = defaults.data(forKey: Self.storageKey),
           let stored = try? Keymap.decodeStored(data) {
            keymap = stored
            return
        }
        // First run, or a stored keymap this build cannot read (decoding is
        // strict): start from the standard layout.
        keymap = Self.standardLayout
        try? save()
    }

    /// Validates, persists, and only then publishes a change.
    func mutate(_ change: (inout Keymap) throws -> Void) throws {
        var updated = keymap
        try change(&updated)
        try updated.validate()
        defaults.set(try updated.encoded(), forKey: Self.storageKey)
        keymap = updated
    }

    /// The standard layout for this Mac's keyboard (ISO or ANSI).
    static var standardLayout: Keymap {
        Keymap.standardLayout(isoKeyboard: KeyboardLayout.physicalShape == .iso)
    }

    /// Puts back the standard keys (no Import keys); input options, the
    /// Palette and "Move to the next photo" are kept.
    func restoreStandardLayout() throws {
        let (input, palette, advance) = (keymap.input, keymap.palette, keymap.importKeys.advance)
        try mutate {
            $0 = Self.standardLayout
            $0.input = input
            $0.palette = palette
            $0.importKeys.advance = advance
        }
    }

    func export(to url: URL) throws {
        try keymap.encoded().write(to: url, options: .atomic)
    }

    /// Accepts a keymap exported by this version of Lumerist.
    func importFile(from url: URL) throws {
        let imported = try Keymap.decodeStored(try Data(contentsOf: url))
        try mutate { $0 = imported }
    }

    private func save() throws {
        defaults.set(try keymap.encoded(), forKey: Self.storageKey)
    }
}
