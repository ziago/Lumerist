import Foundation
import Testing
@testable import LumeristCore

struct InputSettingsTests {
    @Test func trackpadSensitivityChangesTheStepSize() {
        var accumulator = ScrollStepAccumulator()
        accumulator.pointsPerStep = 6
        #expect(accumulator.steps(lineDelta: 0, pointDelta: 12, isContinuous: true, isMomentum: false) == 2)
        accumulator.pointsPerStep = 1 // clamped to the minimum of 4
        #expect(accumulator.pointsPerStep == 4)
    }

    @Test func keymapStoresTrackpadSensitivityAndRejectsOutOfRange() throws {
        var keymap = Keymap.standardLayout
        keymap.input.trackpadPointsPerStep = 20
        #expect(try Keymap.decodeStored(try keymap.encoded()).input.trackpadPointsPerStep == 20)
        keymap.input.trackpadPointsPerStep = 100
        #expect(throws: KeymapError.invalidOption("trackpad sensitivity")) { try keymap.validate() }
    }

    @Test func engineUsesTheConfiguredTrackpadSensitivity() {
        var keymap = Keymap.standardLayout
        keymap.input.trackpadPointsPerStep = 6
        var engine = KeyGestureEngine<String>(keymap: keymap)
        let develop = EngineContext(isLightroomFrontmost: true, module: "develop", tool: "")
        _ = engine.keyDown(chord: KeyChord(12), event: "Q↓", isAutoRepeat: false, timestamp: 0,
                           context: develop, isTextInputFocused: { false })
        let outcome = engine.scroll(lineDelta: 0, pointDelta: 12, isContinuous: true, isMomentum: false,
                                    modifiers: [], context: develop)
        #expect(outcome.intents.contains(.adjust(ParameterKey(scope: .develop, id: "Exposure"), delta: 0.1)))
    }

    @Test func updateRateRangeIsSensible() {
        #expect(IntentCoalescer.rateRange.contains(IntentCoalescer.defaultRate))
        #expect(IntentCoalescer.rateRange.lowerBound >= 1)
    }
}
