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

/// Lightroom's keys follow its interface language.
struct LightroomLanguageTests {
    private func id(_ code: String?) -> String { LightroomShortcuts.Language.forCode(code).id }

    @Test func codesPickTheirLanguageThenTheBaseLanguageThenEnglish() {
        #expect(id("it") == "it" && id("ja") == "ja" && id("zh_cn") == "zh_cn" && id("zh_tw") == "zh_tw")
        #expect(id("pt") == "pt" && id("pt_br") == "pt")
        #expect(id("nb") == "no" && id("nn") == "no")
        #expect(id("de_ch") == "de" && id("sv_se") == "sv")
        // zh regions are languages of their own; unknown codes and nil are English.
        #expect(id("zh") == "en" && id("zh_hk") == "en")
        #expect(id("xx") == "en" && id(nil) == "en")
        #expect(LightroomShortcuts.Language.matching("xx") == nil)
        #expect(LightroomShortcuts.Language.matching("de_at")?.name == "Deutsch")
    }

    @Test func everyLanguageLightroomShipsIsListedWithItsToggle() {
        let languages = LightroomShortcuts.languages
        #expect(languages.map(\.id) == ["en", "de", "es", "fr", "it", "ja", "ko", "nl", "pt", "sv", "zh_cn", "zh_tw", "ru", "pl", "no", "th"])
        #expect(languages.filter { $0.importToggle == "<" }.map(\.id) == ["it", "sv"])
        #expect(languages.allSatisfy { !$0.name.isEmpty && ["`", "<"].contains($0.importToggle) })
        #expect(LightroomShortcuts.Language.english.overrides.isEmpty)
    }

    @Test func nothingIsPressedDifferentlyToday() {
        // Every press stays English: no override is marked safe to press.
        for language in LightroomShortcuts.languages {
            #expect(language.overrides.allSatisfy { !$0.press }, "\(language.id)")
            for command in LightroomCommandName.allCases {
                #expect(FallbackShortcuts.press(for: command, lightroomLanguage: language.id) == FallbackShortcuts.press(for: command))
            }
            #expect(AppCommand.pasteSettingsFromPrevious.press(lightroomLanguage: language.id)
                    == .character(AppCommand.pasteSettingsFromPrevious.shortcut))
        }
        #expect(LightroomShortcuts.table.compactMap(\.fallbackFor).sorted() == [
            "autoTone", "flagPick", "flagReject", "flagRemove", "pasteSettingsFromPrevious", "redo", "showClipping", "undo",
        ])
    }

    @Test func sharedChordsBecomeKeysToPress() {
        #expect(LightroomKeyPress(logicalChord: "Alt+Primary+V") == .character(CharacterShortcut("v", [.option, .command], usKeyCode: 9)))
        #expect(LightroomKeyPress(logicalChord: "Primary+I") == .character(CharacterShortcut("i", .command, usKeyCode: 34)))
        #expect(LightroomKeyPress(logicalChord: "ArrowRight") == .key(KeyChord(124)))
        #expect(LightroomKeyPress(logicalChord: "Shift+1") == .character(CharacterShortcut("1", .shift, usKeyCode: 18)))
        #expect(LightroomKeyPress(logicalChord: "Hyper+V") == nil)
    }

    @Test func warningsFollowTheLanguage() {
        let shiftH = KeyChord(4, .shift)
        #expect(LightroomShortcuts.match(shiftH, in: .develop, layout: nil, language: nil)?.name == "Toggle HDR view")
        #expect(LightroomShortcuts.match(shiftH, in: .develop, layout: nil, language: "it")?.name == "Display current module shortcuts")
        // French Lightroom lists the same keys as English here.
        #expect(LightroomShortcuts.match(shiftH, in: .develop, layout: nil, language: "fr")?.name == "Toggle HDR view")
        #expect(LightroomShortcuts.match(KeyChord(8), in: .library, layout: nil, language: "es") == nil)          // C
        #expect(LightroomShortcuts.match(KeyChord(4), in: .library, layout: nil, language: "es")?.name == "Enter Library Compare view")
    }
}
