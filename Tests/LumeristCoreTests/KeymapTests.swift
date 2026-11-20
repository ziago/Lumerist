import Foundation
import Testing
@testable import LumeristCore

private func dev(_ id: String) -> ParameterKey { ParameterKey(scope: .develop, id: id) }
private func lib(_ id: String) -> ParameterKey { ParameterKey(scope: .library, id: id) }

struct KeymapDefaultsTests {
    @Test func standardLayoutFollowsCaptureOnesSpeedEditKeys() throws {
        let keymap = Keymap.standardLayout
        let expected: [String: String] = [
            "Temperature": "1", "Tint": "2", "Vibrance": "3", "Texture": "4", "Clarity": "5", "Dehaze": "6",
            "Exposure": "Q", "Contrast": "W", "Saturation": "E", "straightenAngle": "R", "lumerist.cropZoom": "T",
            "Highlights": "A", "Shadows": "S", "Whites": "D", "Blacks": "F",
            "ParametricShadows": "X", "ParametricDarks": "C", "ParametricLights": "V", "ParametricHighlights": "B",
            "PresetAmount": "P", "local_Amount": "M",
        ]
        #expect(keymap.develop.count == expected.count)
        for (id, key) in expected {
            #expect(keymap.develop[id]?.dial?.displayText == key, "\(id)")
            #expect(keymap.develop[id]?.nudgeDown == nil)
            #expect(keymap.develop[id]?.nudgeUp == nil)
        }
        try keymap.validate()
    }

    @Test func libraryUsesTheSameKeysAndNoNudgeKeysArePreset() throws {
        let keymap = Keymap.standardLayout
        let expected: [String: String] = [
            "Temperature": "1", "Tint": "2", "Vibrance": "3", "Texture": "4", "Clarity": "5",
            "Exposure": "Q", "Contrast": "W", "Saturation": "E",
            "Highlights": "A", "Shadows": "S", "Whites": "D", "Blacks": "F",
        ]
        #expect(keymap.library.count == expected.count)
        for (id, key) in expected {
            #expect(keymap.library[id]?.dial?.displayText == key, "\(id)")
            #expect(keymap.library[id]?.dial == keymap.develop[id]?.dial, "\(id)")
        }
        #expect(keymap.commandKeys(in: .library).isEmpty)
        #expect(keymap.systemKeys == SystemKeys())
        #expect(keymap.systemKeys.comboKey?.displayText == "Tab")
        #expect(keymap.systemKeys.paletteKey == KeyChord(50))
        #expect(keymap.systemKeys.keyReferenceToggle == KeyChord(4, .combo))
        #expect(keymap.systemKeys.pauseToggle?.displayText == "⌃Esc")
        #expect(keymap.summary(for: .library).nudgeKeyCount == 0)
    }

    @Test func fourCombosArePresetInDevelop() throws {
        let keymap = Keymap.standardLayout
        let combos = keymap.commandKeys
        #expect(combos.count == 4)
        #expect(combos.allSatisfy { $0.scope == .develop && $0.chord.isCombo && $0.group == "Lightroom" })
        let byKey = Dictionary(uniqueKeysWithValues: combos.map { (keymap.displayText(for: $0.chord), $0) })
        #expect(byKey["Tab + A"]?.action == .command(name: "autoTone", argument: nil))
        #expect(byKey["Tab + W"]?.action == .command(name: "autoWhiteBalance", argument: nil))
        #expect(byKey["Tab + R"]?.action == .command(name: "resetAll", argument: nil))
        #expect(byKey["Tab + Q"]?.action == .shortcut(KeyChord(9, [.option, .command]))) // ⌥⌘V
        #expect(byKey["Tab + Q"]?.title == "Paste settings from previous")
        // Fixed ids: the same keys on every launch.
        #expect(Keymap.standardLayout.commandKeys.map(\.id) == combos.map(\.id))
        #expect(byKey["Tab + A"]?.id == UUID(uuidString: "8022E100-5E15-408A-A02C-9796E4B60CBA"))
        // The plain keys stay Dial Keys.
        #expect(keymap.develop["Highlights"]?.dial == KeyChord(0))
        #expect(keymap.develop["Exposure"]?.dial == KeyChord(12))
        #expect(keymap.conflicts().isEmpty)
    }

    @Test func defaultOptions() {
        let input = InputOptions()
        #expect(!input.invertWheel && !input.swapNormalAndCoarse && input.requireMouseDown)
        #expect(input.dragSensitivity == 25 && input.dragPointsPerStep == 6)
        #expect(input.returnCursorAfterRelease && input.doubleClickReset && input.middleClickReset)
        #expect(input.mouseBackForward && input.followSelectedMask)
        #expect(!input.revealPanels && input.arrowKeysAdjust && input.trackpadPointsPerStep == 12)
        let menu = PaletteOptions()
        #expect(!menu.keepVisible && menu.favourites.isEmpty)
    }
}

struct KeymapPersistenceTests {
    @Test func roundTripsEverything() throws {
        var keymap = Keymap.standardLayout
        keymap.updateSlot(for: dev("Exposure")) {
            $0.nudgeDown = KeyChord(33)
            $0.nudgeUp = KeyChord(30)
            $0.normalStep = 0.1
            $0.coarseStep = 0.5
        }
        keymap.updateSlot(for: dev("Contrast")) { $0.nudgeDown = KeyChord(33, .combo) } // Tab + [
        keymap.systemKeys.comboKey = nil
        keymap.palette.keepVisible = true
        keymap.palette.favourites = [PaletteEntry(kind: .tool, id: "createMask", argument: "aiSelection/sky", title: "Sky")]
        keymap.input.swapNormalAndCoarse = true
        keymap.commandKeys.append(CommandKey(scope: .develop, chord: KeyChord(4, [.option, .combo]),
                                           title: "Flag and next", action: .macro([
                                               MacroStep(action: .command(name: "flagPick", argument: nil), delay: 0.2),
                                               MacroStep(action: .shortcut(KeyChord(124)), delay: 0),
                                           ])))
        let data = try keymap.encoded()
        let decoded = try Keymap.decodeStored(data)
        #expect(decoded == keymap)
        #expect(decoded.systemKeys.comboKey == nil) // a cleared key stays cleared
        // Only the new form is written.
        let json = String(decoding: data, as: UTF8.self)
        #expect(json.contains("\"comboKey\":null"))
        #expect(!json.contains("layerKey") && !json.contains("LayerKey"))
    }

    @Test func layerKeyFilesBecomeCombos() throws {
        // A format 3 file written before combos: the Layer flags and the
        // "layerKey" name are read once and turned into combos.
        let json = #"""
        {"format":3,
         "develop":{
           "Exposure":{"dial":{"keyCode":12,"modifiers":0},
                       "nudgeDown":{"keyCode":33,"modifiers":0},"nudgeUp":{"keyCode":30,"modifiers":1},
                       "nudgeRequiresLayerKey":true},
           "Contrast":{"dial":{"keyCode":13,"modifiers":0},
                       "nudgeUp":{"keyCode":39,"modifiers":0},"nudgeRequiresLayerKey":false}},
         "commandKeys":[
           {"id":"0B5C3F4E-2D1A-4C8B-9E7F-6A5B4C3D2E1F","scope":"develop","chord":{"keyCode":40,"modifiers":0},
            "requiresLayerKey":true,"group":"Lightroom","title":"Undo","action":{"command":{"name":"undo"}}},
           {"id":"1B5C3F4E-2D1A-4C8B-9E7F-6A5B4C3D2E1F","scope":"any","chord":{"keyCode":4,"modifiers":0},
            "requiresLayerKey":false,"group":"Lightroom","title":"Next","action":{"command":{"name":"nextPhoto"}}}],
         "systemKeys":{"layerKey":{"keyCode":50,"modifiers":2},"paletteKey":{"keyCode":50,"modifiers":0}}}
        """#
        let keymap = try Keymap.decodeStored(Data(json.utf8))
        #expect(keymap.develop["Exposure"]?.dial == KeyChord(12))
        #expect(keymap.develop["Exposure"]?.nudgeDown == KeyChord(33, .combo))
        #expect(keymap.develop["Exposure"]?.nudgeUp == KeyChord(30, [.shift, .combo]))
        #expect(keymap.develop["Contrast"]?.nudgeUp == KeyChord(39))
        #expect(keymap.commandKeys.map(\.chord) == [KeyChord(40, .combo), KeyChord(4)])
        #expect(keymap.systemKeys.comboKey == KeyChord(50, .control))
        #expect(keymap.displayText(for: KeyChord(40, .combo)) == "⌃` + K")

        // Written back in the new form only, and read again unchanged.
        let rewritten = String(decoding: try keymap.encoded(), as: UTF8.self)
        #expect(!rewritten.contains("requiresLayerKey") && !rewritten.contains("nudgeRequiresLayerKey"))
        #expect(!rewritten.contains("layerKey"))
        #expect(try Keymap.decodeStored(try keymap.encoded()) == keymap)

        // A cleared Layer Key stays cleared; "comboKey" wins over "layerKey".
        let cleared = try Keymap.decodeStored(Data(#"{"format":3,"systemKeys":{"layerKey":null}}"#.utf8))
        #expect(cleared.systemKeys.comboKey == nil)
        let both = #"{"format":3,"systemKeys":{"layerKey":null,"comboKey":{"keyCode":48,"modifiers":0}}}"#
        #expect(try Keymap.decodeStored(Data(both.utf8)).systemKeys.comboKey == .tab)
    }

    @Test func missingSectionsAndOptionsTakeDefaults() throws {
        let json = #"{"format":3,"develop":{"Exposure":{"dial":{"keyCode":12,"modifiers":0}}},"systemKeys":{"pauseToggle":null},"input":{"invertWheel":true}}"#
        let keymap = try Keymap.decodeStored(Data(json.utf8))
        #expect(keymap.develop["Exposure"]?.dial == KeyChord(12))
        #expect(keymap.systemKeys.pauseToggle == nil)
        #expect(keymap.systemKeys.paletteKey == .grave)
        #expect(keymap.input.invertWheel)
        #expect(keymap.input.dragSensitivity == 25)
        #expect(keymap.library.isEmpty && keymap.commandKeys.isEmpty)
    }

    @Test func storedKeymapsWithUnknownFieldsAreRejectedNotLoadedEmpty() throws {
        // A slot written with a field this build does not know must not decode
        // as an empty slot (that once silently disabled every key).
        let oldSlot = #"{"format":3,"develop":{"Exposure":{"speed":{"keyCode":12,"modifiers":0}}}}"#
        #expect(throws: KeymapError.unknownFields(["speed"])) {
            _ = try Keymap.decodeStored(Data(oldSlot.utf8))
        }
        let oldSection = #"{"format":3,"actionKeys":[]}"#
        #expect(throws: KeymapError.unknownFields(["actionKeys"])) {
            _ = try Keymap.decodeStored(Data(oldSection.utf8))
        }
        let commandKey = #"""
        {"format":3,"commandKeys":[{"id":"0B5C3F4E-2D1A-4C8B-9E7F-6A5B4C3D2E1F","scope":"develop",
         "chord":{"keyCode":40,"modifiers":0},"requiresShiftKey":true,"group":"Lightroom","title":"Undo",
         "action":{"command":{"name":"undo"}}}]}
        """#
        #expect(throws: KeymapError.unknownFields(["requiresShiftKey"])) {
            _ = try Keymap.decodeStored(Data(commandKey.utf8))
        }
        let systemKey = #"{"format":3,"systemKeys":{"menuKey":null}}"#
        #expect(throws: KeymapError.unknownFields(["menuKey"])) {
            _ = try Keymap.decodeStored(Data(systemKey.utf8))
        }
    }

    @Test func dataWithoutAFormatIsRejected() {
        let profile = #"{"bindings":[{"action":"contrast","keyCode":13}]}"#
        #expect(throws: KeymapError.unsupportedFormat(0)) {
            _ = try Keymap.decodeStored(Data(profile.utf8))
        }
    }

    @Test func rejectsFutureFormatsAndUnknownParameters() throws {
        #expect(throws: KeymapError.unsupportedFormat(4)) {
            _ = try Keymap.decodeStored(Data(#"{"format":4}"#.utf8))
        }
        let unknown = #"{"format":3,"develop":{"NoSuchSlider":{"dial":{"keyCode":12,"modifiers":0}}}}"#
        #expect(throws: KeymapError.unknownParameter(dev("NoSuchSlider"))) {
            _ = try Keymap.decodeStored(Data(unknown.utf8))
        }
    }
}

struct KeymapValidationTests {
    @Test func stepsMustStayInTheCatalogRange() {
        var keymap = Keymap.standardLayout
        keymap.updateSlot(for: dev("Exposure")) { $0.normalStep = 3 }
        #expect(throws: KeymapError.invalidStep(dev("Exposure"))) { try keymap.validate() }
        keymap.updateSlot(for: dev("Exposure")) { $0.normalStep = 0.05; $0.coarseStep = 0 }
        #expect(throws: KeymapError.invalidStep(dev("Exposure"))) { try keymap.validate() }
    }

    @Test func optionsMustBeInRange() {
        var keymap = Keymap.standardLayout
        keymap.input.dragSensitivity = 40
        #expect(throws: KeymapError.invalidOption("drag sensitivity")) { try keymap.validate() }
        keymap.input.dragSensitivity = 25
        keymap.input.trackpadPointsPerStep = 2
        #expect(throws: KeymapError.invalidOption("trackpad sensitivity")) { try keymap.validate() }
    }

    @Test func commandKeysNeedATitleAndFlatShortMacros() {
        var keymap = Keymap.standardLayout
        keymap.commandKeys = [CommandKey(scope: .develop, chord: KeyChord(4), title: "  ", action: .command(name: "undo", argument: nil))]
        #expect(throws: (any Error).self) { try keymap.validate() }

        let nested = CommandKey.Action.macro([MacroStep(action: .macro([MacroStep(action: .command(name: "undo", argument: nil))]))])
        keymap.commandKeys = [CommandKey(scope: .develop, chord: KeyChord(4), title: "Nested", action: nested)]
        #expect(throws: KeymapError.invalidCommandKey("macros cannot contain macros")) { try keymap.validate() }

        keymap.commandKeys = [CommandKey(scope: .develop, chord: KeyChord(4), title: "Slow",
                                       action: .macro([MacroStep(action: .command(name: "undo", argument: nil), delay: 11)]))]
        #expect(throws: KeymapError.invalidCommandKey("macro delays must be 0–10 s")) { try keymap.validate() }
    }

    @Test func dialKeysCannotBeCombos() {
        var keymap = Keymap.standardLayout
        keymap.updateSlot(for: dev("GrainAmount")) { $0.dial = KeyChord(40, .combo) }
        #expect(throws: KeymapError.invalidCombo(.dialKey(dev("GrainAmount")))) { try keymap.validate() }
        #expect(KeymapError.invalidCombo(.dialKey(dev("GrainAmount"))).localizedDescription.contains("Dial Key can't be a combo"))
        // Nudge Keys can.
        keymap.updateSlot(for: dev("GrainAmount")) { $0.dial = nil; $0.nudgeUp = KeyChord(40, .combo) }
        #expect(throws: Never.self) { try keymap.validate() }
    }

    @Test func lightroomShortcutsAndSystemKeysCannotBeCombos() {
        var keymap = Keymap.standardLayout
        keymap.commandKeys.append(CommandKey(scope: .develop, chord: KeyChord(40, .combo), title: "Send",
                                           action: .shortcut(KeyChord(9, [.command, .combo]))))
        #expect(throws: KeymapError.invalidCombo(.lightroomShortcut)) { try keymap.validate() }

        keymap.commandKeys[keymap.commandKeys.count - 1].action = .macro([
            MacroStep(action: .command(name: "undo", argument: nil)),
            MacroStep(action: .shortcut(KeyChord(9, .combo))),
        ])
        #expect(throws: KeymapError.invalidCombo(.lightroomShortcut)) { try keymap.validate() }

        var system = Keymap.standardLayout
        system.systemKeys.paletteKey = KeyChord(50, .combo)
        #expect(throws: KeymapError.invalidCombo(.systemKey(.paletteKey))) { try system.validate() }

        // The Palette Key is the key left of 1: key code 10 on ISO keyboards.
        #expect(Keymap.standardLayout(isoKeyboard: true).systemKeys.paletteKey == KeyChord(10))
        #expect(Keymap.standardLayout(isoKeyboard: false) == Keymap.standardLayout)
        #expect(throws: Never.self) { try Keymap.standardLayout(isoKeyboard: true).validate() }

        // The Key Reference toggle is the one system key that can be a combo.
        let standard = Keymap.standardLayout
        #expect(standard.systemKeys.keyReferenceToggle == KeyChord(4, .combo))
        #expect(throws: Never.self) { try standard.validate() }
        #expect(standard.displayText(for: KeyChord(4, .combo)) == "Tab + H")
        var taken = Keymap.standardLayout
        taken.commandKeys.append(CommandKey(scope: .develop, chord: KeyChord(4, .combo),
                                            title: "Undo", action: .command(name: "undo", argument: nil)))
        #expect(taken.conflicts().contains { $0.severity == .error && $0.usages.contains(.system(.keyReferenceToggle)) })
        var selfCombo = Keymap.standardLayout
        selfCombo.systemKeys.keyReferenceToggle = KeyChord(48, .combo)
        #expect(selfCombo.conflicts().contains { $0.severity == .error && $0.scope == nil })
    }

    @Test func emptySlotsAreRemoved() {
        var keymap = Keymap.standardLayout
        keymap.updateSlot(for: dev("Exposure")) { $0.dial = nil }
        #expect(keymap.develop["Exposure"] == nil)
        keymap.clearDialKeys(in: .library)
        #expect(keymap.library.isEmpty)
    }

    @Test func clearNudgeKeysKeepsDialKeys() {
        var keymap = Keymap.standardLayout
        keymap.updateSlot(for: dev("Exposure")) { $0.nudgeUp = KeyChord(30) }
        keymap.clearNudgeKeys(in: .develop)
        #expect(keymap.develop["Exposure"]?.nudgeUp == nil)
        #expect(keymap.develop["Exposure"]?.dial == KeyChord(12))
    }
}

struct KeymapConflictTests {
    private func errors(_ keymap: Keymap) -> [KeyConflict] { keymap.conflicts().filter { $0.severity == .error } }

    @Test func theDefaultsHaveNoErrors() {
        #expect(errors(.standardLayout).isEmpty)
    }

    @Test func aDialKeyAndATapActionMayShareAChord() {
        // Library W: hold = Quick Contrast, tap = Auto Tone.
        #expect(errors(.libraryAutoToneOnW).isEmpty)
        #expect(Keymap.libraryAutoToneOnW.library["Contrast"]?.dial == KeyChord(13))
    }

    @Test func duplicateDialKeysAreErrors() throws {
        var keymap = Keymap.standardLayout
        keymap.updateSlot(for: dev("GrainAmount")) { $0.dial = KeyChord(12) }
        let conflict = try #require(errors(keymap).first)
        #expect(conflict.chord == KeyChord(12))
        #expect(conflict.scope == .develop)
        #expect(throws: (any Error).self) { try keymap.validate() }
    }

    @Test func nudgeKeysCannotShareAChordWithAnythingInTheirScope() {
        var keymap = Keymap.standardLayout
        keymap.updateSlot(for: dev("GrainAmount")) { $0.nudgeUp = KeyChord(12) } // Q is Exposure's Dial Key
        #expect(errors(keymap).count == 1)

        var twoNudges = Keymap.standardLayout
        twoNudges.updateSlot(for: dev("GrainAmount")) { $0.nudgeUp = KeyChord(30) }
        twoNudges.updateSlot(for: dev("GrainSize")) { $0.nudgeDown = KeyChord(30) }
        #expect(errors(twoNudges).count == 1)

        var withAction = Keymap.libraryAutoToneOnW
        withAction.updateSlot(for: lib("Exposure")) { $0.nudgeUp = KeyChord(13) } // W is a library action
        #expect(!errors(withAction).isEmpty)
    }

    @Test func systemKeysCannotBeReused() {
        var keymap = Keymap.standardLayout
        keymap.updateSlot(for: dev("GrainAmount")) { $0.dial = .grave }
        #expect(errors(keymap).contains { $0.usages.contains(.system(.paletteKey)) })

        var duplicateSystems = Keymap.standardLayout
        duplicateSystems.systemKeys.pauseToggle = .grave
        #expect(errors(duplicateSystems).contains { $0.scope == nil })
    }

    @Test func combosAreChordsOfTheirOwn() throws {
        // Two combos on the same key conflict, written with the Combo Key's name.
        var keymap = Keymap.standardLayout
        keymap.commandKeys.append(CommandKey(scope: .develop, chord: KeyChord(4, .combo),
                                           title: "A", action: .command(name: "undo", argument: nil)))
        keymap.commandKeys.append(CommandKey(scope: .any, chord: KeyChord(4, .combo),
                                           title: "B", action: .command(name: "redo", argument: nil)))
        let twice = try #require(errors(keymap).first { $0.chord == KeyChord(4, .combo) })
        #expect(twice.message == "Tab + H runs more than one action.")

        // A combo Nudge Key and a combo Command Key on the same key.
        var nudgeAndAction = Keymap.standardLayout
        nudgeAndAction.updateSlot(for: dev("GrainAmount")) { $0.nudgeUp = KeyChord(0, .combo) } // Tab + A is Auto Tone
        #expect(errors(nudgeAndAction).map(\.message) == ["Tab + A is both a Nudge Key and another key."])

        // The plain key and the combo are different chords: Q is Exposure's Dial Key,
        // Tab + Q a combo, Tab + [ and plain ] the two Nudge Keys of one slider.
        var separate = Keymap.standardLayout
        separate.updateSlot(for: dev("GrainAmount")) {
            $0.nudgeDown = KeyChord(33, .combo)
            $0.nudgeUp = KeyChord(30)
        }
        #expect(errors(separate).isEmpty)
        #expect(separate.conflicts().isEmpty)
    }

    @Test func aComboCannotUseTheComboKeyOrThePauseKey() {
        var itself = Keymap.standardLayout
        itself.updateSlot(for: dev("GrainAmount")) { $0.nudgeUp = KeyChord(48, [.shift, .combo]) } // Tab + ⇧Tab
        let conflict = errors(itself).first
        #expect(conflict?.chord == KeyChord(48, [.shift, .combo]))
        #expect(conflict?.usages.contains(.system(.comboKey)) == true)
        #expect(conflict?.message == "Tab + ⇧Tab can't work: a combo can't use the Combo Key itself.")
        #expect(throws: (any Error).self) { try itself.validate() }

        // Moving the Combo Key onto a combo's key breaks that combo.
        var moved = Keymap.standardLayout
        moved.systemKeys.comboKey = KeyChord(0) // A, while Tab + A is Auto Tone
        #expect(errors(moved).contains { $0.chord == KeyChord(0, .combo) })

        var pause = Keymap.standardLayout
        pause.commandKeys.append(CommandKey(scope: .develop, chord: KeyChord(53, [.control, .combo]), title: "Undo",
                                          action: .command(name: "undo", argument: nil)))
        #expect(errors(pause).contains { $0.usages.contains(.system(.pauseToggle)) })
    }

    @Test func combosWithoutAComboKeyAreAWarning() {
        var keymap = Keymap.standardLayout
        keymap.systemKeys.comboKey = nil
        let warnings = keymap.conflicts().filter { $0.severity == .warning }
        #expect(warnings.count == 5) // the 4 default combos and Tab + H
        #expect(warnings.contains { $0.message == "Combo + A does nothing until you set a Combo Key." })
        #expect(errors(keymap).isEmpty)
    }

    @Test func typingKeysAndReservedModifiersAreWarnings() {
        var keymap = Keymap.standardLayout
        keymap.updateSlot(for: dev("GrainAmount")) { $0.dial = KeyChord(49) } // Space
        keymap.updateSlot(for: dev("GrainSize")) { $0.dial = KeyChord(4, [.command]) }
        let warnings = keymap.conflicts().filter { $0.severity == .warning }
        #expect(warnings.count == 2)
        #expect(errors(keymap).isEmpty)
    }
}

private extension Keymap {
    /// The standard layout plus a tap Command Key on Library's W.
    static var libraryAutoToneOnW: Keymap {
        var keymap = Keymap.standardLayout
        keymap.commandKeys.append(CommandKey(scope: .library, chord: KeyChord(13), title: "Auto Tone",
                                             action: .command(name: "autoTone", argument: nil)))
        return keymap
    }
}

struct KeymapSummaryTests {
    @Test func countsAndSorting() throws {
        var keymap = Keymap.standardLayout
        keymap.updateSlot(for: dev("Exposure")) { $0.nudgeDown = KeyChord(33); $0.nudgeUp = KeyChord(30) }
        keymap.updateSlot(for: dev("GrainAmount")) { $0.nudgeUp = KeyChord(39) }
        let summary = keymap.summary(for: .develop, sortedBy: .parameter)
        #expect(summary.dialKeyCount == 21)
        #expect(summary.nudgeKeyCount == 3)
        #expect(summary.nudgeKeys.count == 2)
        let labels = summary.dialKeys.map(\.descriptor.label)
        #expect(labels == labels.sorted { $0.localizedCaseInsensitiveCompare($1) == .orderedAscending })

        let byKey = keymap.summary(for: .develop, sortedBy: .key).dialKeys.compactMap { $0.slot.dial?.displayText }
        #expect(byKey == byKey.sorted())

        let bySection = keymap.summary(for: .develop, sortedBy: .section).dialKeys.map(\.descriptor.id)
        #expect(bySection.first == "Temperature")
        #expect(keymap.summary(for: .library).dialKeyCount == 12)
    }

    @Test func effectiveStepsFallBackToTheCatalog() {
        var keymap = Keymap.standardLayout
        #expect(keymap.steps(for: dev("Exposure"))! == (0.05, 0.33))
        #expect(keymap.steps(for: lib("Temperature"))! == (5, 20))
        keymap.updateSlot(for: dev("Exposure")) { $0.coarseStep = 0.5 }
        #expect(keymap.steps(for: dev("Exposure"))! == (0.05, 0.5))
        #expect(keymap.steps(for: dev("Nope")) == nil)
    }
}

struct KeymapMaskTests {
    @Test func localEquivalents() {
        #expect(Keymap.localKey(for: dev("Exposure")) == dev("local_Exposure"))
        #expect(Keymap.localKey(for: dev("LuminanceSmoothing")) == dev("local_LuminanceNoise"))
        #expect(Keymap.localKey(for: dev("GrainAmount")) == dev("local_Grain"))
        #expect(Keymap.localKey(for: dev("PresetAmount")) == dev("local_Amount"))
        #expect(Keymap.localKey(for: dev("local_Amount")) == dev("local_Amount"))
        #expect(Keymap.localKey(for: dev("Vibrance")) == nil)
        #expect(Keymap.localKey(for: dev("ParametricShadows")) == nil)
        #expect(Keymap.localKey(for: lib("Exposure")) == nil)
    }

    @Test func everyLocalTargetIsInTheCatalog() {
        for local in Set(Keymap.localParameterMap.values) {
            #expect(ParameterCatalog.descriptor(scope: .develop, id: local) != nil, "\(local)")
        }
        let hue = ParameterCatalog.descriptor(scope: .develop, id: "local_Hue")
        #expect(hue?.wraps == true)
        #expect(hue?.section == "Masking (local)")
    }
}

struct KeyChordTests {
    @Test func displayText() {
        #expect(KeyChord(48, .shift).displayText == "⇧Tab")
        #expect(KeyChord.controlEscape.displayText == "⌃Esc")
        #expect(KeyChord(67).displayText == "Num *")
        #expect(KeyChord(12, [.command, .shift, .option, .control]).displayText == "⌃⌥⇧⌘Q")
        #expect(KeyChord(92).displayText == "Num 9")
        #expect(KeyChord(82).displayText == "Num 0")
        #expect(KeyChord(42).displayText == "\\")
        #expect(KeyChord(12, [.shift]).plain == KeyChord(12))
    }

    @Test func combosNameTheComboKey() {
        let combo = KeyChord(40, .combo)
        #expect(combo.isCombo && !KeyChord(40).isCombo)
        #expect(combo.displayText == "Combo + K")
        #expect(combo.displayText(comboKey: .tab) == "Tab + K")
        #expect(combo.displayText(comboKey: nil) == "Combo + K")
        #expect(KeyChord(30, [.shift, .combo]).displayText(comboKey: .tab) == "Tab + ⇧]")
        #expect(combo.displayText(comboKey: KeyChord(48, .shift)) == "⇧Tab + K")
        #expect(KeyChord(40).displayText(comboKey: .tab) == "K") // plain keys are unchanged
        #expect(combo.withoutCombo == KeyChord(40) && KeyChord(40).asCombo == combo)
        #expect(Keymap.standardLayout.displayText(for: combo) == "Tab + K")
        #expect(KeyModifiers.combo.rawValue == 1 << 8 && !KeyModifiers.combo.reservesKey)
    }

    @Test func codable() throws {
        #expect(try JSONDecoder().decode(KeyChord.self, from: JSONEncoder().encode(KeyChord(40, .combo))) == KeyChord(40, .combo))
        let chord = KeyChord(48, [.shift, .command])
        let data = try JSONEncoder().encode(chord)
        #expect(try JSONDecoder().decode(KeyChord.self, from: data) == chord)
    }
}

struct KeyNameTests {
    @Test func layoutNamesReplaceLettersButNotNamedKeys() {
        // An AZERTY-like layout: the physical Q key types A, the Z key types W.
        let azerty: (Int64) -> String? = { [12: "A", 6: "W", 13: "Z", 42: "*"][$0] }
        #expect(KeyNames.name(for: 12, layout: azerty) == "A")
        #expect(KeyNames.name(for: 6, layout: azerty) == "W")
        // Tab, Space, arrows and the keypad keep their names on every layout.
        #expect(KeyNames.name(for: 48, layout: { _ in "x" }) == "Tab")
        #expect(KeyNames.name(for: 123, layout: { _ in "x" }) == "←")
        #expect(KeyNames.name(for: 67, layout: { _ in "x" }) == "Num *")
    }

    @Test func missingLayoutNamesFallBackToANSI() {
        #expect(KeyNames.name(for: 12, layout: nil) == "Q")
        #expect(KeyNames.name(for: 12, layout: { _ in nil }) == "Q")
        #expect(KeyNames.name(for: 12, layout: { _ in "" }) == "Q")
        #expect(KeyNames.ansiName(for: 10) == "§")
        #expect(KeyNames.ansiName(for: 200) == "Key 200")
    }
}

struct IntentCoalescerTests {
    @Test func sumsConsecutiveAdjustmentsAndKeepsOrder() {
        var coalescer = IntentCoalescer()
        #expect(coalescer.add(.adjust(dev("Exposure"), delta: 0.05)) == false)
        coalescer.add(.adjust(dev("Exposure"), delta: 0.05))
        coalescer.add(.adjust(dev("Contrast"), delta: 1))
        coalescer.add(.adjust(dev("Exposure"), delta: 0.05))
        #expect(coalescer.flush() == [
            .adjust(dev("Exposure"), delta: 0.1),
            .adjust(dev("Contrast"), delta: 1),
            .adjust(dev("Exposure"), delta: 0.05),
        ])
        #expect(coalescer.isEmpty)
    }

    @Test func splitsAboveTheLimitAndDropsZero() {
        var coalescer = IntentCoalescer()
        for _ in 0..<5 { coalescer.add(.adjust(dev("Exposure"), delta: 0.5)) }
        #expect(coalescer.flush() == [
            .adjust(dev("Exposure"), delta: 1), .adjust(dev("Exposure"), delta: 1), .adjust(dev("Exposure"), delta: 0.5),
        ])
        coalescer.add(.adjust(dev("Tint"), delta: 2))
        coalescer.add(.adjust(dev("Tint"), delta: -2))
        #expect(coalescer.flush().isEmpty)
    }

    @Test func otherIntentsAskForAnImmediateFlushInOrder() {
        var coalescer = IntentCoalescer()
        coalescer.add(.gestureBegan(dev("Exposure")))
        coalescer.add(.adjust(dev("Exposure"), delta: 0.05))
        #expect(coalescer.add(.gestureEnded(dev("Exposure"))) == true)
        #expect(coalescer.flush() == [
            .gestureBegan(dev("Exposure")), .adjust(dev("Exposure"), delta: 0.05), .gestureEnded(dev("Exposure")),
        ])
    }
}
