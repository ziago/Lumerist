import Foundation
import Testing
@testable import LumeristCore

struct LightroomShortcutsTests {
    /// The generated table is Shared/lightroom-shortcuts.json, entry for entry.
    @Test func theTableMatchesTheSharedFile() throws {
        let url = URL(fileURLWithPath: #filePath).deletingLastPathComponent().deletingLastPathComponent()
            .deletingLastPathComponent().appendingPathComponent("Shared/lightroom-shortcuts.json")
        let file = try #require(try JSONSerialization.jsonObject(with: Data(contentsOf: url)) as? [String: Any])
        let sections = try #require(file["sections"] as? [[String: Any]])
        var expected: [String] = []
        for section in sections {
            let contexts = section["contexts"] as? [String] ?? []
            let community = section["communityReported"] as? Bool ?? false
            for shortcut in section["shortcuts"] as? [[String: Any]] ?? [] {
                let mac = shortcut["mac"] as? [String] ?? []
                guard !mac.isEmpty else { continue }
                let name = shortcut["name"] as? String ?? shortcut["action"] as? String ?? ""
                let own = shortcut["contexts"] as? [String] ?? contexts
                expected.append([name, mac.joined(separator: " "), own.joined(separator: " "), "\(community)",
                                 shortcut["importRole"] as? String ?? "-"].joined(separator: " | "))
            }
        }
        let actual = LightroomShortcuts.table.map { entry in
            [entry.name, entry.mac.joined(separator: " "), entry.contexts.map(\.rawValue).joined(separator: " "),
             "\(entry.isCommunityReported)", entry.importRole?.rawValue ?? "-"].joined(separator: " | ")
        }
        #expect(actual == expected, "run python3 Scripts/generate-lightroom-shortcuts.py")
        // Every chord can be read.
        for entry in LightroomShortcuts.table {
            for chord in entry.mac { #expect(LightroomShortcuts.logicalChord(chord) != nil, "\(chord)") }
        }
    }

    @Test func lettersFollowTheLayoutAndPunctuationIsNeverMatched() {
        let us: (Int64) -> String? = { _ in nil }
        // Key code 6 is Z on US keyboards and Y on German ones.
        let german: (Int64) -> String? = { [6: "Y", 16: "Z", 33: "Ü", 24: "´"][$0] }
        let key6 = KeyChord(6)
        #expect(LightroomShortcuts.match(key6, in: .library, layout: us)?.name == "Toggle Zoom view")
        #expect(LightroomShortcuts.match(key6, in: .develop, layout: german)?.name == "View Before and After left/right")
        #expect(LightroomShortcuts.match(KeyChord(16), in: .library, layout: german)?.name == "Toggle Zoom view")
        // [ and = on US keyboards: Lightroom uses both, but they are not matched.
        #expect(LightroomShortcuts.match(KeyChord(33), in: .develop, layout: us) == nil)
        #expect(LightroomShortcuts.match(KeyChord(24), in: .library, layout: us) == nil)
        #expect(LightroomShortcuts.match(KeyChord(33), in: .develop, layout: german) == nil)
        // The key left of 1 (Cycle flag settings) is never matched either.
        #expect(LightroomShortcuts.match(.grave, in: .library, layout: us) == nil)
        // Fixed keys match on any layout; combos never.
        #expect(LightroomShortcuts.match(KeyChord(124), in: .importWindow, layout: german)?.name == "Move between photos")
        #expect(LightroomShortcuts.match(KeyChord(96), in: .develop, layout: german)?.name == "Show/hide Module Picker")
        #expect(LightroomShortcuts.match(KeyChord(6, .combo), in: .library, layout: us) == nil)
        // Modifiers must match exactly.
        #expect(LightroomShortcuts.match(KeyChord(32, .command), in: .develop, layout: us)?.name == "Auto tone")
        #expect(LightroomShortcuts.match(KeyChord(32, [.command, .option]), in: .develop, layout: us) == nil)
    }

    @Test func importWindowKeysAreCommunityReported() {
        let match = LightroomShortcuts.match(KeyChord(35), in: .importWindow, layout: nil)
        #expect(match?.name == "Include" && match?.isCommunityReported == true && match?.importRole == .include)
        #expect(LightroomShortcuts.match(KeyChord(35), in: .library, layout: nil)?.isCommunityReported == false)
        #expect(LightroomShortcuts.match(KeyChord(6), in: .importWindow, layout: nil) == nil)
    }
}
