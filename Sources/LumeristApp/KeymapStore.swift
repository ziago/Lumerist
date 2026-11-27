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
    /// Set when the stored keymap could not be read: why, and where its copy
    /// went (nil if the copy could not be written either).
    @ObservationIgnored private(set) var unreadable: (reason: String, backup: URL?)?

    init(defaults: UserDefaults = .standard) {
        self.defaults = defaults
        // Key names and Lightroom's fallback shortcuts follow the active
        // keyboard layout; created at launch, so this runs before any UI.
        KeyboardLayout.install()
        // First run: the standard layout, saved. A stored keymap this build
        // cannot read (decoding is strict, so a newer version's file may be
        // one) is copied and never written over; the standard layout is used
        // until the user changes a key (`KeymapLoad`).
        let load = KeymapLoad.decide(stored: defaults.data(forKey: Self.storageKey), standard: Self.standardLayout)
        keymap = load.keymap
        if let unreadable = load.unreadable {
            self.unreadable = (unreadable.reason, Self.backUp(unreadable.data, defaults: defaults))
        }
        if load.saveNow { try? save() }
    }

    /// Keeps a copy of a stored keymap this build could not read, in
    /// ~/Library/Application Support/Lumerist/keymap-backups (or, if that
    /// fails, under a UserDefaults key of its own).
    private static func backUp(_ data: Data, defaults: UserDefaults) -> URL? {
        let manager = FileManager.default
        if let support = manager.urls(for: .applicationSupportDirectory, in: .userDomainMask).first {
            let folder = support.appending(path: "Lumerist/keymap-backups", directoryHint: .isDirectory)
            let file = folder.appending(path: KeymapBackup.fileName(for: data))
            do {
                try manager.createDirectory(at: folder, withIntermediateDirectories: true)
                if !manager.fileExists(atPath: file.path) { try data.write(to: file, options: .atomic) }
                return file
            } catch {}
        }
        defaults.set(data, forKey: storageKey + ".unreadable")
        return nil
    }

    /// Validates, persists, and only then publishes a change.
    func mutate(_ change: (inout Keymap) throws -> Void) throws {
        var updated = keymap
        try change(&updated)
        try updated.validate()
        defaults.set(try updated.encoded(), forKey: Self.storageKey)
        keymap = updated
    }

    /// The standard layout for this Mac's keyboard (ANSI, ISO or JIS), taken
    /// on the first run and by "Restore standard layout". The Palette Key is
    /// not moved later when another keyboard is used: `LMGetKbdType` follows
    /// the last keyboard typed on, so a Mac with an ISO built-in keyboard and
    /// an ANSI external one would flip it back and forth, and the other spot
    /// may hold a key the user assigned. General shows the key, and changing
    /// it is one click.
    static var standardLayout: Keymap {
        Keymap.standardLayout(for: KeyboardLayout.physicalShape, layout: KeyboardLayout.characters)
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
