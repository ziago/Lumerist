import Foundation
import Testing
@testable import LumeristCore

/// Lightroom's own keys pressed by character, key names, and the standard
/// layout per keyboard shape. The layouts below are what macOS 27 reports
/// for these keys (UCKeyTranslate, ISO keyboard type), cut down to them.
struct LayoutCharactersTests {
    private static func layout(_ plain: [Int64: String], command: [Int64: String]? = nil,
                               dead: [Int64: String] = [:]) -> LayoutCharacters {
        LayoutCharacters(plain: plain, command: command ?? plain, display: plain.merging(dead) { a, _ in a })
    }

    static let us = layout([50: "`", 10: "§", 35: "p", 7: "x", 9: "v", 6: "z", 32: "u", 38: "j", 34: "i"])
    static let italian = layout([50: "<", 10: "\\", 35: "p", 7: "x", 9: "v", 6: "z", 32: "u"])
    static let czech = layout([50: "\\", 10: "<", 35: "p", 7: "x", 9: "v", 16: "z", 32: "u"])
    static let britishPC = layout([50: "\\", 10: "`", 35: "p", 7: "x", 9: "v", 6: "z", 32: "u"])
    /// No Latin letters typed alone; the ⌘ table is QWERTY.
    static let russian = layout([50: "]", 10: ">", 35: "з", 7: "ч", 9: "м", 6: "я", 32: "г"],
                                command: [9: "v", 6: "z", 32: "u", 35: "p", 7: "x"])
    /// ` is a dead key on key code 50: alone it types nothing.
    static let usInternationalPC = layout([10: "§", 35: "p", 7: "x", 9: "v", 6: "z", 32: "u"], dead: [50: "`"])
    static let dvorakQwertyCommand = layout([50: "`", 15: "p", 11: "x", 47: "v", 44: "z", 3: "u"],
                                            command: [50: "`", 35: "p", 7: "x", 9: "v", 6: "z", 32: "u"])
    static let dvorak = layout([50: "`", 15: "p", 11: "x", 47: "v", 44: "z", 3: "u"])

    // MARK: Import Toggle

    @Test func toggleIsTheKeyThatTypesAPlainBackQuoteThenALessThan() {
        #expect(ImportShortcuts.forLayout(Self.us).toggle == KeyChord(50))
        #expect(ImportShortcuts.forLayout(Self.italian).toggle == KeyChord(50))        // <, left of Z
        #expect(ImportShortcuts.forLayout(Self.britishPC).toggle == KeyChord(10))      // `, left of 1
        // Czech: 50 types \ (the key that hung Lightroom on Windows); < is 10.
        #expect(ImportShortcuts.forLayout(Self.czech).toggle == KeyChord(10))
    }

    @Test func withoutABackQuoteOrALessThanThereIsNoToggle() {
        #expect(ImportShortcuts.forLayout(Self.russian).toggle == nil)
        // A dead ` types nothing alone, and < needs Shift there.
        #expect(ImportShortcuts.forLayout(Self.usInternationalPC).toggle == nil)
        #expect(ImportShortcuts.forLayout(Self.russian).chord(for: .toggle) == nil)
        // Not even the ASCII-capable layout makes one: Lightroom gets what the active layout types.
        #expect(ImportShortcuts.forLayout(Self.russian, asciiCapable: Self.us).toggle == nil)
    }

    @Test func includeAndExcludeAreTheKeysThatTypePAndX() {
        let dvorak = ImportShortcuts.forLayout(Self.dvorak)
        #expect(dvorak.include == KeyChord(15) && dvorak.exclude == KeyChord(11))
        #expect(dvorak.nextPhoto == KeyChord(KeyChord.rightArrow))
        // No key types p: the ASCII-capable layout's key, else the US one.
        #expect(ImportShortcuts.forLayout(Self.russian, asciiCapable: Self.us).include == KeyChord(35))
        #expect(ImportShortcuts.forLayout(Self.russian).exclude == KeyChord(7))
        // No layout at all: the US keys, ` included.
        #expect(ImportShortcuts.forLayout(nil) == .standard)
        #expect(ImportShortcuts.forLayout(nil, asciiCapable: Self.italian).toggle == KeyChord(50))
    }

    // MARK: Fallback shortcuts and Lumerist's own commands

    private func fallback(_ command: LightroomCommandName, _ layout: LayoutCharacters?,
                          ascii: LayoutCharacters? = nil) -> KeyChord? {
        FallbackShortcuts.press(for: command)?.chord(layout: layout, asciiCapable: ascii)
    }

    @Test func commandShortcutsUseTheLayoutsCommandTable() {
        // "Dvorak – QWERTY ⌘" moves its keys under ⌘: ⌘Z stays key code 6,
        // not the Dvorak z (⌘/ would show Lightroom's shortcut list).
        #expect(fallback(.undo, Self.dvorakQwertyCommand) == KeyChord(6, .command))
        #expect(fallback(.redo, Self.dvorakQwertyCommand) == KeyChord(6, [.command, .shift]))
        #expect(fallback(.autoTone, Self.dvorakQwertyCommand) == KeyChord(32, .command))     // not ⌘F
        // Plain Dvorak keeps its own keys under ⌘.
        #expect(fallback(.undo, Self.dvorak) == KeyChord(44, .command))
        // QWERTZ: z is key code 16.
        #expect(fallback(.undo, Self.czech) == KeyChord(16, .command))
        // Russian: its ⌘ table is QWERTY.
        #expect(fallback(.autoTone, Self.russian) == KeyChord(32, .command))
    }

    @Test func plainShortcutsUseThePlainTable() {
        #expect(fallback(.flagPick, Self.dvorakQwertyCommand) == KeyChord(15))
        #expect(fallback(.flagRemove, Self.dvorak) == KeyChord(3))
        #expect(fallback(.showClipping, Self.us) == KeyChord(38))
        // Without a key for the letter: the ASCII-capable layout, then the US key.
        #expect(fallback(.flagPick, Self.russian, ascii: Self.dvorak) == KeyChord(15))
        #expect(fallback(.showClipping, Self.russian) == KeyChord(38))
        #expect(fallback(.nextPhoto, Self.russian) == KeyChord(124))
        #expect(fallback(.previousPhoto, nil) == KeyChord(123))
        #expect(fallback(.resetAll, Self.us) == nil)
    }

    @Test func pasteSettingsFromPreviousPressesOptionCommandVByCharacter() {
        let shortcut = AppCommand.pasteSettingsFromPrevious.shortcut
        #expect(shortcut.chord(layout: Self.us, asciiCapable: nil) == KeyChord(9, [.option, .command]))
        #expect(shortcut.chord(layout: Self.dvorak, asciiCapable: nil) == KeyChord(47, [.option, .command]))
        #expect(shortcut.chord(layout: Self.dvorakQwertyCommand, asciiCapable: nil) == KeyChord(9, [.option, .command]))
        #expect(shortcut.chord(layout: nil, asciiCapable: nil) == KeyChord(9, [.option, .command]))
    }

    @Test func lumeristsOwnCommandsNeverClashWithLightroomCommands() {
        for command in AppCommand.allCases {
            #expect(LightroomCommandName(rawValue: command.rawValue) == nil, "\(command)")
        }
        #expect(AppCommand(.command(name: "pasteSettingsFromPrevious", argument: nil)) == .pasteSettingsFromPrevious)
        #expect(AppCommand(.command(name: "pasteSettingsFromPrevious", argument: "x")) == nil)
        #expect(AppCommand(.command(name: "pasteSettings", argument: nil)) == nil)
        #expect(AppCommand(.shortcut(KeyChord(9, [.option, .command]))) == nil)
    }

    // MARK: Names

    @Test func capitalsOnlyWhereTheyKeepTheLetter() {
        #expect(KeyNames.label(forTyped: "q", layoutHasDotlessI: false) == "Q")
        #expect(KeyNames.label(forTyped: "ü", layoutHasDotlessI: false) == "Ü")
        #expect(KeyNames.label(forTyped: "ß", layoutHasDotlessI: false) == "ß")      // not "SS"
        #expect(KeyNames.label(forTyped: "ǰ", layoutHasDotlessI: false) == "ǰ")      // its capital is two characters
        #expect(KeyNames.label(forTyped: "ı", layoutHasDotlessI: true) == "ı")
        #expect(KeyNames.label(forTyped: "i", layoutHasDotlessI: true) == "i")
        #expect(KeyNames.label(forTyped: "i", layoutHasDotlessI: false) == "I")
        #expect(KeyNames.label(forTyped: "\u{1B}", layoutHasDotlessI: false) == nil)
    }

    @Test func aLoneCombiningMarkSitsOnADottedCircle() {
        #expect(KeyNames.label(forTyped: "\u{094D}", layoutHasDotlessI: false) == "\u{25CC}\u{094D}")   // ◌्
        #expect(KeyNames.label(forTyped: "\u{0F72}", layoutHasDotlessI: false) == "\u{25CC}\u{0F72}")   // ◌ི
        #expect(KeyNames.label(forTyped: "ক", layoutHasDotlessI: false) == "ক")
    }

    @Test func turkishKeepsDotlessAndDottedIApart() {
        let turkishQ = Self.layout([34: "ı", 39: "i", 50: "<"])
        let names = turkishQ.names
        #expect(names[34] == "ı" && names[39] == "i")
        // Lightroom's I is the dotted i, never ı.
        let layout: (Int64) -> String? = { names[$0] }
        #expect(LightroomShortcuts.match(KeyChord(39), in: .library, layout: layout)?.name == "Cycle info overlay")
        #expect(LightroomShortcuts.match(KeyChord(34), in: .library, layout: layout) == nil)
        // Dead keys show their accent.
        #expect(Self.usInternationalPC.names[50] == "`")
    }

    @Test func jisKeysHaveNames() {
        let none: (Int64) -> String? = { _ in nil }
        #expect(KeyNames.name(for: 93, layout: none) == "¥")
        #expect(KeyNames.name(for: 94, layout: none) == "_")
        #expect(KeyNames.name(for: 102, layout: none) == "英数")
        #expect(KeyNames.name(for: 104, layout: none) == "かな")
        // ¥ and _ follow the layout: an Italian layout on a JIS keyboard puts \ and < there.
        #expect(KeyNames.name(for: 93, layout: { [93: "\\"][$0] }) == "\\")
        #expect(LayoutCharacters.searchOrder.contains(94) && !LayoutCharacters.searchOrder.contains(102))
    }

    // MARK: Standard layout per keyboard

    @Test func jisKeyboardsGetNoPaletteKey() throws {
        let jis = Keymap.standardLayout(for: .jis)
        #expect(jis.systemKeys.paletteKey == nil)
        #expect(jis.systemKeys.comboKey == .tab)
        try jis.validate()
        var expected = Keymap.standardLayout
        expected.systemKeys.paletteKey = nil
        #expect(jis == expected)
        #expect(PaletteKeyDefault.reason(shape: .jis, layout: nil) == .noKeyLeftOf1)
    }

    @Test func noPaletteKeyWhereTheKeyLeftOf1TypesALetterOrDigit() {
        // Hungarian (ISO): key 10 types 0; Turkish F – Legacy (ANSI): key 50 types w.
        let hungarian = Self.layout([10: "0", 50: "í"])
        #expect(Keymap.standardLayout(for: .iso, layout: hungarian).systemKeys.paletteKey == nil)
        #expect(PaletteKeyDefault.reason(shape: .iso, layout: hungarian) == .typesLightroomKey("0"))
        #expect(Keymap.standardLayout(for: .ansi, layout: Self.layout([50: "w"])).systemKeys.paletteKey == nil)
        // Anything else keeps the key left of 1: ` § \ º ё ذ.
        #expect(Keymap.standardLayout(for: .ansi, layout: Self.us).systemKeys.paletteKey == KeyChord(50))
        #expect(Keymap.standardLayout(for: .iso, layout: Self.italian).systemKeys.paletteKey == KeyChord(10))
        for typed in ["º", "ё", "ذ"] {
            #expect(Keymap.standardLayout(for: .iso, layout: Self.layout([10: typed])).systemKeys.paletteKey == KeyChord(10))
        }
        #expect(PaletteKeyDefault.explanation(.typesLightroomKey("0")).contains("types “0”"))
    }

    /// ⌥ is a modifier of its own: an Option press never matches a plain key.
    @Test func anOptionPressNeverMatchesAPlainKey() {
        var keymap = Keymap.standardLayout
        keymap.updateSlot(for: ParameterKey(scope: .develop, id: "GrainAmount")) { $0.nudgeUp = KeyChord(4) } // H
        keymap.importKeys.include = KeyChord(6)                                                                // Z
        var engine = KeyGestureEngine<String>(keymap: keymap)
        func passes(_ chord: KeyChord, in context: EngineContext = EngineContext()) -> Bool {
            let outcome = engine.keyDown(chord: chord, event: "down", isAutoRepeat: false, timestamp: 0,
                                         context: context, isTextInputFocused: { false })
            _ = engine.keyUp(keyCode: chord.keyCode, event: "up", timestamp: 0, context: context)
            if case .pass = outcome.decision { return outcome.intents.isEmpty }
            return false
        }
        #expect(passes(KeyChord(12, .option)))                                    // ⌥Q, Q is a Dial Key
        #expect(passes(KeyChord(4, .option)))                                     // ⌥H, H is a Nudge Key
        #expect(passes(KeyChord(50, .option)))                                    // ⌥ + the Palette Key
        #expect(passes(KeyChord(6, .option), in: EngineContext(module: EngineContext.importModule)))
        #expect(passes(KeyChord(12, [.option, .shift])))                          // only ⇧ may be extra
    }
}

/// Reading what a Mac stored: older defaults, and keymaps this build can't read.
struct KeymapLoadTests {
    @Test func theOldTabQBecomesTheCommand() throws {
        // The stored shape of the old default, as earlier builds wrote it.
        let old = try JSONEncoder().encode(CommandKey(
            id: UUID(uuidString: "787E1A28-ED89-40D3-9855-6049A5F3F639")!, scope: .develop,
            chord: KeyChord(12, .combo), title: "Paste settings from previous",
            action: .shortcut(KeyChord(9, [.option, .command]))))
        let read = try JSONDecoder().decode(CommandKey.self, from: old)
        #expect(read.action == .command(name: "pasteSettingsFromPrevious", argument: nil))
        #expect(read.title == "Paste settings from previous" && read.chord == KeyChord(12, .combo))

        // A keymap with it reads as today's standard layout.
        var stored = Keymap.standardLayout
        stored.commandKeys[3].action = .shortcut(KeyChord(9, [.option, .command]))
        #expect(try Keymap.decodeStored(stored.encoded()) == Keymap.standardLayout)
    }

    @Test func onlyTheExactOldDefaultIsMigrated() {
        let optionCommandV = CommandKey.Action.shortcut(KeyChord(9, [.option, .command]))
        #expect(CommandKey.migrated(optionCommandV, title: "Paste from previous") == optionCommandV)
        let other = CommandKey.Action.shortcut(KeyChord(9, [.command]))
        #expect(CommandKey.migrated(other, title: "Paste settings from previous") == other)
        #expect(CommandKey.migrated(.command(name: "autoTone", argument: nil), title: "Paste settings from previous")
                == .command(name: "autoTone", argument: nil))
        // Inside a macro a step has no title: it stays a shortcut.
        let macro = CommandKey.Action.macro([MacroStep(action: optionCommandV)])
        #expect(CommandKey.migrated(macro, title: "Paste settings from previous") == macro)
    }

    @Test func aFirstRunSavesTheStandardLayout() {
        let load = KeymapLoad.decide(stored: nil, standard: Keymap.standardLayout)
        #expect(load.keymap == Keymap.standardLayout && load.saveNow && load.unreadable == nil)
    }

    @Test func aReadableKeymapIsUsedAsItIs() throws {
        var keymap = Keymap.standardLayout
        keymap.importKeys.include = KeyChord(6)
        let load = KeymapLoad.decide(stored: try keymap.encoded(), standard: Keymap.standardLayout)
        #expect(load.keymap == keymap && !load.saveNow && load.unreadable == nil)
    }

    @Test func anUnreadableKeymapIsKeptAndNeverWrittenOver() throws {
        // A newer build's field, which this build's strict decoding refuses.
        var object = try #require(try JSONSerialization.jsonObject(with: Keymap.standardLayout.encoded()) as? [String: Any])
        object["fieldFromTheFuture"] = true
        let newer = try JSONSerialization.data(withJSONObject: object)
        let load = KeymapLoad.decide(stored: newer, standard: Keymap.standardLayout(for: .iso))
        #expect(load.keymap == Keymap.standardLayout(for: .iso))
        #expect(!load.saveNow)
        #expect(load.unreadable?.data == newer)
        #expect(load.unreadable?.reason.contains("fieldFromTheFuture") == true)
        #expect(KeymapLoad.decide(stored: Data("not json".utf8), standard: .standardLayout).unreadable != nil)
    }

    @Test func backupNamesFollowTheContent() {
        let a = KeymapBackup.fileName(for: Data("a".utf8))
        #expect(a == KeymapBackup.fileName(for: Data("a".utf8)))
        #expect(a != KeymapBackup.fileName(for: Data("b".utf8)))
        #expect(a.hasPrefix("keymap-") && a.hasSuffix(".json") && a.count == "keymap-".count + 12 + ".json".count)
    }
}
