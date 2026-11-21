import Foundation
import Testing
@testable import LumeristCore

private typealias Engine = KeyGestureEngine<String>

private extension KeyGestureEngine.Decision {
    var isPass: Bool { if case .pass = self { true } else { false } }
    var isConsume: Bool { if case .consume = self { true } else { false } }
    var replacement: [Event]? { if case .replace(let events) = self { events } else { nil } }
}

private let develop = EngineContext()
private let masking = EngineContext(tool: "masking")
private let library = EngineContext(module: "library")
private let mapModule = EngineContext(module: "map")
private let away = EngineContext(isLightroomFrontmost: false)

// Key codes.
private let keyQ: Int64 = 12, keyW: Int64 = 13, keyH: Int64 = 4, keyE: Int64 = 14, key3: Int64 = 20, keyP: Int64 = 35, keyA: Int64 = 0
// Default system keys: Tab is the Combo Key, the key left of 1 (US) opens the Palette.
private let keyCombo: Int64 = 48, keyPalette: Int64 = 50, keyEsc: Int64 = 53
private let keyLeft: Int64 = 123, keyRight: Int64 = 124, keyOpenBracket: Int64 = 33, keyCloseBracket: Int64 = 30, keyK: Int64 = 40

private extension KeyGestureEngine where Event == String {
    static func standard(_ change: (inout Keymap) -> Void = { _ in }) -> Self {
        var keymap = Keymap.standardLayout
        change(&keymap)
        return Self(keymap: keymap)
    }

    mutating func down(
        _ keyCode: Int64,
        _ modifiers: KeyModifiers = [],
        _ name: String? = nil,
        repeat isAutoRepeat: Bool = false,
        at time: TimeInterval = 0,
        in context: EngineContext = develop,
        textFocused: () -> Bool = { false }
    ) -> Outcome {
        keyDown(chord: KeyChord(keyCode, modifiers), event: name ?? "\(KeyNames.name(for: keyCode))↓",
                isAutoRepeat: isAutoRepeat, timestamp: time, context: context, isTextInputFocused: textFocused)
    }

    mutating func up(_ keyCode: Int64, _ name: String? = nil, at time: TimeInterval = 0, in context: EngineContext = develop) -> Outcome {
        keyUp(keyCode: keyCode, event: name ?? "\(KeyNames.name(for: keyCode))↑", timestamp: time, context: context)
    }

    mutating func wheel(_ lines: Double, _ modifiers: KeyModifiers = [], in context: EngineContext = develop) -> Outcome {
        scroll(lineDelta: lines, pointDelta: lines * 10, isContinuous: false, isMomentum: false,
               modifiers: modifiers, context: context)
    }

    mutating func swipe(_ points: Double, momentum: Bool = false, in context: EngineContext = develop) -> Outcome {
        scroll(lineDelta: 0, pointDelta: points, isContinuous: true, isMomentum: momentum, modifiers: [], context: context)
    }

    mutating func click(_ button: MouseButton, count: Int = 1, in context: EngineContext = develop) -> Outcome {
        mouseDown(button, clickCount: count, event: "click", context: context)
    }

    mutating func release(_ button: MouseButton, in context: EngineContext = develop) -> Outcome {
        mouseUp(button, event: "release", context: context)
    }

    mutating func move(_ dx: Double, _ modifiers: KeyModifiers = [], in context: EngineContext = develop) -> Outcome {
        mouseMoved(dx: dx, dy: 0, modifiers: modifiers, event: "move", context: context)
    }
}

struct KeyEngineDialKeyTests {
    @Test func aTapIsReplayedInOrder() {
        var engine = Engine.standard()
        #expect(engine.down(keyQ).decision.isConsume)
        #expect(engine.up(keyQ).decision.replacement == ["Q↓", "Q↑"])
        #expect(!engine.isEngaged)
    }

    @Test func holdAndScrollAdjustsAndEndsTheGesture() {
        var engine = Engine.standard()
        #expect(engine.down(keyQ).decision.isConsume)
        let first = engine.wheel(1)
        #expect(first.decision.isConsume)
        #expect(first.intents == [.gestureBegan(dev("Exposure")), .adjust(dev("Exposure"), delta: 0.05)])
        #expect(engine.wheel(-2).intents == [.adjust(dev("Exposure"), delta: -0.1)])
        let end = engine.up(keyQ)
        #expect(end.decision.isConsume)
        #expect(end.intents == [.gestureEnded(dev("Exposure"))])
    }

    @Test func shiftSelectsTheCoarseStepAndFlipSwapsThem() {
        var engine = Engine.standard()
        _ = engine.down(keyQ)
        #expect(engine.wheel(1, .shift).intents.last == .adjust(dev("Exposure"), delta: 0.33))

        var flipped = Engine.standard { $0.input.swapNormalAndCoarse = true }
        _ = flipped.down(keyQ)
        #expect(flipped.wheel(1).intents.last == .adjust(dev("Exposure"), delta: 0.33))
        #expect(flipped.wheel(1, .shift).intents.last == .adjust(dev("Exposure"), delta: 0.05))
    }

    @Test func invertedWheel() {
        var engine = Engine.standard { $0.input.invertWheel = true }
        _ = engine.down(keyQ)
        #expect(engine.wheel(1).intents.last == .adjust(dev("Exposure"), delta: -0.05))
    }

    @Test func customStepsAreUsed() {
        var engine = Engine.standard { $0.updateSlot(for: dev("Contrast")) { $0.normalStep = 3 } }
        _ = engine.down(keyW)
        #expect(engine.wheel(2).intents.last == .adjust(dev("Contrast"), delta: 6))
    }

    @Test func typingAnotherKeyReleasesTheHeldKeyFirst() {
        var engine = Engine.standard()
        _ = engine.down(keyQ)
        #expect(engine.down(keyH).decision.replacement == ["Q↓", "H↓"])
        #expect(engine.up(keyQ).decision.isPass)
    }

    @Test func autoRepeatWhileHeldIsConsumed() {
        var engine = Engine.standard()
        _ = engine.down(keyQ)
        #expect(engine.down(keyQ, repeat: true).decision.isConsume)
        #expect(engine.down(keyQ, repeat: true).intents.isEmpty)
    }

    @Test func reservedModifiersAndUnboundKeysPassWithoutAskingForFocus() {
        var engine = Engine.standard()
        var asked = 0
        #expect(engine.down(keyQ, [.command], textFocused: { asked += 1; return false }).decision.isPass)
        #expect(engine.down(keyH, textFocused: { asked += 1; return false }).decision.isPass)
        #expect(asked == 0)
    }

    @Test func textFieldsKeepTheirKeys() {
        var engine = Engine.standard()
        var asked = 0
        #expect(engine.down(keyQ, textFocused: { asked += 1; return true }).decision.isPass)
        #expect(asked == 1)
    }

    @Test func losingLightroomDeliversTheHeldKey() {
        var engine = Engine.standard()
        _ = engine.down(keyQ)
        #expect(engine.down(keyH, in: away).decision.replacement == ["Q↓", "H↓"])
        #expect(!engine.isEngaged)
    }

    @Test func otherModulesOnlyGetSystemKeys() {
        var engine = Engine.standard()
        #expect(engine.down(keyQ, in: mapModule).decision.isPass)
        // Combos live in Develop and Library only, so Tab stays Lightroom's here.
        #expect(engine.down(keyCombo, in: mapModule).decision.isPass)
        var plain = Engine.standard { $0.systemKeys.keyReferenceToggle = KeyChord(keyPalette, .shift) }
        #expect(plain.down(keyPalette, [.shift], in: mapModule).intents == [.toggleKeyReference])
    }

    @Test func inertiaDoesNotStartAGestureAndIsSwallowedAfterOne() {
        var engine = Engine.standard()
        _ = engine.down(keyQ)
        #expect(engine.swipe(40, momentum: true).decision.isPass)
        #expect(engine.up(keyQ).decision.replacement == ["Q↓", "Q↑"])

        _ = engine.down(keyQ)
        _ = engine.swipe(24)
        _ = engine.up(keyQ)
        #expect(engine.needsScrollEvents)
        #expect(engine.swipe(200, momentum: true).decision.isConsume)
        #expect(engine.swipe(5).decision.isPass)
        #expect(!engine.needsScrollEvents)
    }

    @Test func trackpadSensitivityComesFromTheKeymap() {
        var engine = Engine.standard { $0.input.trackpadPointsPerStep = 6 }
        _ = engine.down(keyQ)
        #expect(engine.swipe(12).intents.last == .adjust(dev("Exposure"), delta: 0.1))
    }

    @Test func keymapUpdatesApply() {
        var engine = Engine.standard()
        var keymap = Keymap.standardLayout
        keymap.updateSlot(for: dev("Exposure")) { $0.dial = KeyChord(keyH) }
        engine.update(keymap: keymap)
        _ = engine.down(keyH)
        #expect(engine.wheel(1).intents.last == .adjust(dev("Exposure"), delta: 0.05))
        #expect(engine.heldEvents.isEmpty)
    }
}

struct KeyEngineArrowAndMouseTests {
    @Test func arrowsStepTheHeldSlider() {
        var engine = Engine.standard()
        _ = engine.down(keyQ)
        let right = engine.down(keyRight)
        #expect(right.decision.isConsume)
        #expect(right.intents == [.gestureBegan(dev("Exposure")), .adjust(dev("Exposure"), delta: 0.05)])
        #expect(engine.down(keyLeft, [.shift]).intents == [.adjust(dev("Exposure"), delta: -0.33)])
        #expect(engine.up(keyRight).decision.isConsume)
        #expect(engine.up(keyLeft).decision.isConsume)
        #expect(engine.up(keyQ).intents == [.gestureEnded(dev("Exposure"))])
    }

    @Test func arrowsAreTypingWhenTheOptionIsOff() {
        var engine = Engine.standard { $0.input.arrowKeysAdjust = false }
        _ = engine.down(keyQ)
        #expect(engine.down(keyRight).decision.replacement == ["Q↓", "→↓"])
    }

    @Test func dragNeedsTheMouseButtonByDefault() {
        var engine = Engine.standard()
        _ = engine.down(keyQ)
        let hover = engine.move(100)
        #expect(hover.decision.isPass && hover.intents.isEmpty)
        #expect(engine.click(.left).decision.isConsume)
        let drag = engine.move(6) // 31 − 25 = 6 points per step
        #expect(drag.decision.isConsume)
        #expect(drag.intents == [.gestureBegan(dev("Exposure")), .adjust(dev("Exposure"), delta: 0.05)])
        #expect(engine.move(13).intents == [.adjust(dev("Exposure"), delta: 0.1)])
        #expect(engine.move(-7).intents == [.adjust(dev("Exposure"), delta: -0.05)])
        #expect(engine.release(.left).decision.isConsume)
        #expect(engine.up(keyQ).intents == [.gestureEnded(dev("Exposure")), .restorePointer])
    }

    @Test func dragWithoutTheButtonWhenAllowed() {
        var engine = Engine.standard { $0.input.requireMouseDown = false; $0.input.returnCursorAfterRelease = false }
        _ = engine.down(keyQ)
        let hover = engine.move(6)
        #expect(hover.decision.isPass)
        #expect(hover.intents.last == .adjust(dev("Exposure"), delta: 0.05))
        #expect(engine.up(keyQ).intents == [.gestureEnded(dev("Exposure"))])
    }

    @Test func doubleClickAndMiddleClickReset() {
        var engine = Engine.standard()
        _ = engine.down(keyQ)
        _ = engine.click(.left)
        _ = engine.release(.left)
        let double = engine.click(.left, count: 2)
        #expect(double.decision.isConsume)
        #expect(double.intents == [.reset(dev("Exposure"))])
        _ = engine.release(.left)
        // No gesture started and the key is not replayed.
        let end = engine.up(keyQ)
        #expect(end.decision.isConsume && end.intents.isEmpty)

        _ = engine.down(keyW)
        #expect(engine.click(.middle).intents == [.reset(dev("Contrast"))])
        #expect(engine.release(.middle).decision.isConsume)
        #expect(engine.up(keyW).decision.isConsume)

        var off = Engine.standard { $0.input.middleClickReset = false; $0.input.doubleClickReset = false }
        _ = off.down(keyQ)
        #expect(off.click(.middle).decision.isPass)
        #expect(off.click(.left, count: 2).intents.isEmpty)
    }

    @Test func rightButtonLeavesTheStepAlone() {
        var engine = Engine.standard()
        _ = engine.down(keyQ)
        #expect(engine.click(.right).decision.isPass)
        let step = engine.wheel(1).intents.last
        #expect(engine.release(.right).decision.isPass)
        #expect(engine.wheel(1).intents.last == step)
    }

    @Test func clicksPassWhenNoDialKeyIsHeld() {
        var engine = Engine.standard()
        #expect(engine.click(.left).decision.isPass)
        #expect(engine.release(.left).decision.isPass)
        #expect(engine.click(.right).decision.isPass)
    }

    @Test func backAndForwardButtonsChangePhoto() {
        var engine = Engine.standard()
        let back = engine.click(.back)
        #expect(back.decision.isConsume)
        #expect(back.intents == [.action(.command(name: "previousPhoto", argument: nil))])
        #expect(engine.release(.back).decision.isConsume)
        #expect(engine.click(.forward, in: library).intents == [.action(.command(name: "nextPhoto", argument: nil))])
        #expect(engine.click(.forward, in: mapModule).decision.isPass)
        #expect(engine.click(.forward, in: away).decision.isPass)

        var off = Engine.standard { $0.input.mouseBackForward = false }
        #expect(off.click(.back).decision.isPass)
        #expect(MouseButton(buttonNumber: 3) == .back && MouseButton(buttonNumber: 4) == .forward)
    }
}

struct KeyEngineNudgeAndCommandKeyTests {
    private static func withNudgeKeys(combo: Bool = false) -> Engine {
        let modifiers: KeyModifiers = combo ? .combo : []
        return Engine.standard {
            $0.updateSlot(for: dev("Exposure")) {
                $0.nudgeDown = KeyChord(keyOpenBracket, modifiers)
                $0.nudgeUp = KeyChord(keyCloseBracket, modifiers)
            }
        }
    }

    @Test func nudgeKeysStepOnEachPressAndRepeat() {
        var engine = Self.withNudgeKeys()
        let down = engine.down(keyOpenBracket)
        #expect(down.decision.isConsume)
        #expect(down.intents == [.adjust(dev("Exposure"), delta: -0.05)])
        #expect(engine.down(keyOpenBracket, repeat: true).intents == [.adjust(dev("Exposure"), delta: -0.05)])
        #expect(engine.up(keyOpenBracket).decision.isConsume)
        #expect(engine.down(keyCloseBracket, [.shift]).intents == [.adjust(dev("Exposure"), delta: 0.33)])
    }

    @Test func comboNudgeKeysNeedTheComboKey() {
        var engine = Self.withNudgeKeys(combo: true)
        #expect(engine.down(keyCloseBracket).decision.isPass)
        #expect(engine.down(keyCombo).decision.isConsume)
        #expect(engine.down(keyCloseBracket).intents == [.adjust(dev("Exposure"), delta: 0.05)])
        #expect(engine.down(keyCloseBracket, repeat: true).intents == [.adjust(dev("Exposure"), delta: 0.05)])
        #expect(engine.up(keyCloseBracket).decision.isConsume)
        // ⇧ selects the coarse step for combos too.
        #expect(engine.down(keyOpenBracket, [.shift]).intents == [.adjust(dev("Exposure"), delta: -0.33)])
        #expect(engine.up(keyOpenBracket).decision.isConsume)
        #expect(engine.up(keyCombo).decision.isConsume) // used, so not replayed
        #expect(!engine.isEngaged)
    }

    @Test func eachNudgeKeyIsAComboOrNotOnItsOwn() {
        // Nudge − is Tab + [, Nudge + is a plain ].
        var engine = Engine.standard {
            $0.updateSlot(for: dev("Exposure")) {
                $0.nudgeDown = KeyChord(keyOpenBracket, .combo)
                $0.nudgeUp = KeyChord(keyCloseBracket)
            }
        }
        #expect(engine.down(keyCloseBracket).intents == [.adjust(dev("Exposure"), delta: 0.05)])
        _ = engine.up(keyCloseBracket)
        #expect(engine.down(keyOpenBracket).decision.isPass)
        _ = engine.down(keyCombo)
        #expect(engine.down(keyOpenBracket).intents == [.adjust(dev("Exposure"), delta: -0.05)])
        _ = engine.up(keyOpenBracket)
        _ = engine.up(keyCombo)
        // With Tab held, the plain ] is no combo: both keys are typed.
        _ = engine.down(keyCombo)
        #expect(engine.down(keyCloseBracket).decision.replacement == ["Tab↓", "]↓"])
    }

    @Test func theComboKeyAloneIsReplayed() {
        var engine = Engine.standard()
        _ = engine.down(keyCombo)
        #expect(engine.isEngaged)
        #expect(engine.down(keyCombo, repeat: true).decision.isConsume)
        #expect(engine.up(keyCombo).decision.replacement == ["Tab↓", "Tab↑"])

        _ = engine.down(keyCombo)
        #expect(engine.down(keyP).decision.replacement == ["Tab↓", "P↓"])
        #expect(!engine.isEngaged)
    }

    @Test func comboCommandKeys() {
        var engine = Engine.standard {
            $0.commandKeys.append(CommandKey(scope: .develop, chord: KeyChord(keyK, .combo),
                                           title: "Undo", action: .command(name: "undo", argument: nil)))
        }
        #expect(engine.down(keyK).decision.isPass) // K alone is no combo
        _ = engine.up(keyK)
        #expect(engine.down(keyCombo).decision.isConsume)
        let combo = engine.down(keyK)
        #expect(combo.decision.isConsume)
        #expect(combo.intents == [.action(.command(name: "undo", argument: nil))])
        let repeated = engine.down(keyK, repeat: true) // one action per press
        #expect(repeated.decision.isConsume && repeated.intents.isEmpty)
        #expect(engine.up(keyK).decision.isConsume)
        #expect(engine.up(keyCombo).decision.isConsume)
        // A key event never makes a combo on its own.
        #expect(engine.down(keyK, [.combo]).decision.isPass)
    }

    @Test func aChangedComboKeyCarriesTheCombos() {
        var engine = Engine.standard {
            $0.systemKeys.comboKey = KeyChord(keyH)
            $0.commandKeys.append(CommandKey(scope: .develop, chord: KeyChord(keyK, .combo),
                                           title: "Undo", action: .command(name: "undo", argument: nil)))
        }
        #expect(engine.down(keyCombo).decision.isPass) // Tab is a plain key again
        _ = engine.up(keyCombo)
        _ = engine.down(keyH)
        #expect(engine.down(keyK).intents == [.action(.command(name: "undo", argument: nil))])

        var cleared = Engine.standard { $0.systemKeys.comboKey = nil }
        #expect(cleared.down(keyCombo).decision.isPass)
        #expect(cleared.down(keyA).decision.isConsume) // still the Highlights Dial Key
    }

    @Test func defaultCombosInDevelop() {
        var engine = Engine.standard()
        // Tab + A runs Auto Tone.
        #expect(engine.down(keyCombo).decision.isConsume)
        let autoTone = engine.down(keyA)
        #expect(autoTone.decision.isConsume)
        #expect(autoTone.intents == [.action(.command(name: "autoTone", argument: nil))])
        #expect(engine.up(keyA).decision.isConsume)
        // Tab + Q, still holding Tab: Lightroom's ⌥⌘V.
        #expect(engine.down(keyQ).intents == [.action(.shortcut(KeyChord(9, [.option, .command])))])
        _ = engine.up(keyQ)
        #expect(engine.up(keyCombo).decision.isConsume)

        // A alone is still the Highlights Dial Key.
        #expect(engine.down(keyA).decision.isConsume)
        #expect(engine.wheel(1).intents == [.gestureBegan(dev("Highlights")), .adjust(dev("Highlights"), delta: 1)])
        #expect(engine.up(keyA).intents == [.gestureEnded(dev("Highlights"))])

        // The combos are Develop only: in Library, Tab + A is typed.
        _ = engine.down(keyCombo, in: library)
        #expect(engine.down(keyA, in: library).decision.replacement == ["Tab↓", "A↓"])
    }

    @Test func aDialKeyTapRunsItsActionAndAHoldAdjusts() {
        var engine = Engine.standard {
            $0.commandKeys.append(CommandKey(scope: .library, chord: KeyChord(keyW), title: "Auto Tone",
                                           action: .command(name: "autoTone", argument: nil)))
        }
        #expect(engine.down(keyW, in: library).decision.isConsume)
        let tap = engine.up(keyW, in: library)
        #expect(tap.decision.isConsume)
        #expect(tap.intents == [.action(.command(name: "autoTone", argument: nil))])

        _ = engine.down(keyW, in: library)
        #expect(engine.wheel(1, in: library).intents.last == .adjust(lib("Contrast"), delta: 5))
        #expect(engine.up(keyW, in: library).intents == [.gestureEnded(lib("Contrast"))])
    }

    @Test func actionOnlyKeysFireOnKeyDown() {
        var engine = Engine.standard {
            $0.commandKeys.append(CommandKey(scope: .any, chord: KeyChord(keyH), title: "Next",
                                           action: .command(name: "nextPhoto", argument: nil)))
        }
        let down = engine.down(keyH)
        #expect(down.decision.isConsume)
        #expect(down.intents == [.action(.command(name: "nextPhoto", argument: nil))])
        #expect(engine.up(keyH).decision.isConsume)
        #expect(engine.down(keyH, in: library).intents.count == 1)
    }

    @Test func libraryUsesLibraryParametersAndSteps() {
        var engine = Engine.standard()
        _ = engine.down(18, in: library) // 1: Quick Develop Temperature, steps 5 / 20 (Lightroom's single and double arrows)
        #expect(engine.wheel(1, in: library).intents.last == .adjust(lib("Temperature"), delta: 5))
        #expect(engine.wheel(1, .shift, in: library).intents.last == .adjust(lib("Temperature"), delta: 20))
    }
}

struct KeyEngineSystemTests {
    @Test func aTapLeavesThePaletteOpenAndAHoldClosesItOnRelease() {
        var engine = Engine.standard()
        let open = engine.down(keyPalette, at: 0)
        #expect(open.decision.isConsume)
        #expect(open.intents == [.openPalette])
        #expect(engine.down(keyPalette, repeat: true, at: 0.1).decision.isConsume)
        // A tap never reaches Lightroom: the Palette stays up.
        let tap = engine.up(keyPalette, at: 0.1)
        #expect(tap.decision.isConsume && tap.intents.isEmpty)
        #expect(engine.isPaletteOpen)
        // The next press closes it, and its release is swallowed too.
        #expect(engine.down(keyPalette, at: 0.5).intents == [.closePalette])
        #expect(engine.up(keyPalette, at: 0.6).decision.isConsume)
        #expect(!engine.isPaletteOpen)

        _ = engine.down(keyPalette, at: 1)
        let hold = engine.up(keyPalette, at: 1.6)
        #expect(hold.decision.isConsume)
        #expect(hold.intents == [.closePalette])
    }

    @Test func otherInputDuringTheMenuMakesItAHold() {
        var engine = Engine.standard()
        _ = engine.down(keyPalette, at: 0)
        #expect(engine.down(keyH, at: 0.05).decision.isPass)
        #expect(engine.wheel(1).decision.isPass)
        let release = engine.up(keyPalette, at: 0.1)
        #expect(release.decision.isConsume)
        #expect(release.intents == [.closePalette])
    }

    @Test func keepVisiblePinsTheMenuUntilTheKeyIsPressedAgain() {
        var engine = Engine.standard { $0.palette.keepVisible = true }
        _ = engine.down(keyPalette, at: 0)
        #expect(engine.up(keyPalette, at: 1).intents.isEmpty)
        #expect(engine.isPaletteOpen)
        #expect(engine.down(keyPalette, at: 2).intents == [.closePalette])
        #expect(engine.up(keyPalette, at: 2.1).decision.isConsume)
        #expect(!engine.isPaletteOpen)

        _ = engine.down(keyPalette, at: 3)
        _ = engine.up(keyPalette, at: 4)
        engine.paletteDidClose()
        #expect(!engine.isPaletteOpen)
    }

    @Test func thePaletteIsDevelopOnlyAndOffWithoutAKey() {
        var engine = Engine.standard()
        #expect(engine.down(keyPalette, in: library).decision.isPass)
        var cleared = Engine.standard { $0.systemKeys.paletteKey = nil }
        #expect(cleared.down(keyPalette).decision.isPass)
        #expect(engine.down(keyPalette, textFocused: { true }).decision.isPass)
    }

    @Test func keyReferenceToggleIsTabPlusH() {
        var engine = Engine.standard()
        #expect(engine.down(keyCombo).decision.isConsume)
        let toggle = engine.down(keyH)
        #expect(toggle.decision.isConsume)
        #expect(toggle.intents == [.toggleKeyReference])
        #expect(engine.down(keyH, repeat: true).intents.isEmpty)
        #expect(engine.up(keyH).decision.isConsume)
        #expect(engine.up(keyCombo).decision.isConsume) // used: Tab is not replayed
        #expect(engine.down(keyH).decision.isPass) // H alone is Lightroom's
        _ = engine.up(keyH)
        #expect(engine.down(keyCombo, textFocused: { true }).decision.isPass)
    }

    @Test func keyReferenceToggleCanBeAPlainKey() {
        var engine = Engine.standard { $0.systemKeys.keyReferenceToggle = KeyChord(keyPalette, .shift) }
        let toggle = engine.down(keyPalette, [.shift])
        #expect(toggle.intents == [.toggleKeyReference])
        #expect(engine.up(keyPalette).decision.isConsume)
        #expect(engine.down(keyPalette, [.shift], textFocused: { true }).decision.isPass)
    }

    @Test func pauseStopsEverythingUntilToggledAgain() {
        var engine = Engine.standard()
        #expect(engine.down(keyEsc, [.control]).intents == [.togglePause(isPaused: true)])
        #expect(engine.isPaused)
        #expect(engine.up(keyEsc).decision.isConsume)
        #expect(engine.down(keyQ).decision.isPass)
        #expect(engine.down(keyPalette, [.shift]).decision.isPass)
        #expect(engine.click(.back).decision.isPass)
        #expect(engine.down(keyEsc, [.control]).intents == [.togglePause(isPaused: false)])
        #expect(engine.down(keyQ).decision.isConsume)
    }

    @Test func pausingWhileAKeyIsHeldDeliversIt() {
        var engine = Engine.standard()
        _ = engine.down(keyQ)
        let pause = engine.down(keyEsc, [.control])
        #expect(pause.decision.replacement == ["Q↓"])
        #expect(pause.intents == [.togglePause(isPaused: true)])
    }
}

struct KeyEngineMaskTests {
    @Test func dialKeysMoveTheMasksSlider() {
        var engine = Engine.standard()
        _ = engine.down(keyQ, in: masking)
        let adjust = engine.wheel(1, in: masking)
        #expect(adjust.intents == [.gestureBegan(dev("local_Exposure")), .adjust(dev("local_Exposure"), delta: 0.05)])
        #expect(engine.up(keyQ, in: masking).intents == [.gestureEnded(dev("local_Exposure"))])
    }

    @Test func slidersWithoutAMaskEquivalentNeverTouchThePhoto() {
        var engine = Engine.standard()
        let down = engine.down(key3, in: masking) // Vibrance
        #expect(down.decision.isConsume)
        #expect(down.intents.isEmpty)
        let wheel = engine.wheel(3, in: masking)
        #expect(wheel.decision.isConsume)
        #expect(wheel.intents == [.unavailableInMask(dev("Vibrance"))])
        // One notice per hold, and still no adjustment.
        #expect(engine.wheel(1, in: masking).intents.isEmpty)
        #expect(engine.down(key3, repeat: true, in: masking).decision.isConsume)
        #expect(engine.up(key3, in: masking).decision.isConsume)
        #expect(!engine.isEngaged)
    }

    @Test func tappingAKeyWithoutAMaskEquivalentStillReachesLightroom() {
        var engine = Engine.standard()
        #expect(engine.down(key3, in: masking).decision.isConsume)
        #expect(engine.up(key3, in: masking).decision.replacement == ["3↓", "3↑"])
        #expect(!engine.isEngaged)
    }

    @Test func typingWhileAMaskIsOpenKeepsTheOrder() {
        var engine = Engine.standard()
        #expect(engine.down(key3, in: masking).decision.isConsume)
        #expect(engine.down(4, in: masking).decision.replacement == ["3↓", "H↓"])
    }

    @Test func leavingTheMaskRestoresGlobalSliders() {
        var engine = Engine.standard()
        _ = engine.down(keyQ, in: masking)
        _ = engine.up(keyQ, in: masking)
        _ = engine.down(keyQ, in: develop)
        #expect(engine.wheel(1).intents.last == .adjust(dev("Exposure"), delta: 0.05))
    }

    @Test func remapCanBeTurnedOff() {
        var engine = Engine.standard { $0.input.followSelectedMask = false }
        _ = engine.down(keyQ, in: masking)
        #expect(engine.wheel(1, in: masking).intents.last == .adjust(dev("Exposure"), delta: 0.05))
        _ = engine.up(keyQ, in: masking)
        _ = engine.down(key3, in: masking)
        #expect(engine.wheel(1, in: masking).intents.last == .adjust(dev("Vibrance"), delta: 1))
    }

    @Test func nudgeKeysRemapTheSameWay() {
        var engine = Engine.standard {
            $0.updateSlot(for: dev("Exposure")) { $0.nudgeUp = KeyChord(keyCloseBracket) }
            $0.updateSlot(for: dev("Vibrance")) { $0.nudgeUp = KeyChord(keyOpenBracket) }
        }
        #expect(engine.down(keyCloseBracket, in: masking).intents == [.adjust(dev("local_Exposure"), delta: 0.05)])
        let vibrance = engine.down(keyOpenBracket, in: masking)
        #expect(vibrance.decision.isConsume)
        #expect(vibrance.intents == [.unavailableInMask(dev("Vibrance"))])
        #expect(engine.down(keyCloseBracket).intents == [.adjust(dev("Exposure"), delta: 0.05)])
    }

    @Test func resetInAMaskResetsTheMasksSlider() {
        var engine = Engine.standard()
        _ = engine.down(keyQ, in: masking)
        #expect(engine.click(.left, count: 2, in: masking).intents == [.reset(dev("local_Exposure"))])
    }

    @Test func presetAmountBecomesTheMaskAmount() {
        var engine = Engine.standard()
        _ = engine.down(keyP, in: masking)
        #expect(engine.wheel(1, in: masking).intents.last == .adjust(dev("local_Amount"), delta: 1))
        _ = engine.up(keyP, in: masking)
        _ = engine.down(keyP)
        #expect(engine.wheel(1).intents.last == .adjust(dev("PresetAmount"), delta: 1))
    }
}
