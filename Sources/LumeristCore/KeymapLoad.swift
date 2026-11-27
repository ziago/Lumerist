import CryptoKit
import Foundation

// MARK: - Loading the stored keymap

/// What to do with the keymap a Mac stored, decided without touching
/// storage. A keymap this build can't read (a newer version wrote it, or it
/// is damaged) is never written over: the app keeps a copy, starts from the
/// standard layout for this session and leaves the stored data where it is,
/// so going back to the newer version finds the keys again. Only a change
/// the user makes replaces it.
public struct KeymapLoad: Sendable {
    /// The stored data this build could not read, and why.
    public struct Unreadable: Sendable, Hashable {
        public let data: Data
        public let reason: String
    }

    public let keymap: Keymap
    /// Write `keymap` now: only on the first run, when nothing was stored.
    public let saveNow: Bool
    public let unreadable: Unreadable?

    public static func decide(stored data: Data?, standard: Keymap) -> KeymapLoad {
        guard let data else { return KeymapLoad(keymap: standard, saveNow: true, unreadable: nil) }
        do {
            return KeymapLoad(keymap: try Keymap.decodeStored(data), saveNow: false, unreadable: nil)
        } catch {
            return KeymapLoad(keymap: standard, saveNow: false,
                              unreadable: Unreadable(data: data, reason: error.localizedDescription))
        }
    }
}

/// Copies of stored keymaps this build could not read.
public enum KeymapBackup {
    /// "keymap-<first 12 hex digits of the SHA-256>.json": the same data
    /// always gets the same name, so relaunching never piles up copies.
    public static func fileName(for data: Data) -> String {
        let digest = SHA256.hash(data: data).map { String(format: "%02x", $0) }.joined()
        return "keymap-\(digest.prefix(12)).json"
    }
}
