import Foundation

/// What the engine needs to know about Lightroom at the time of an event.
public struct EngineContext: Sendable, Hashable {
    /// The module while Lightroom's Import window is in front. Lightroom
    /// never reports it (its module stays "library"): the app sets it when
    /// it recognises the window.
    public static let importModule = "import"

    public var isLightroomFrontmost: Bool
    /// `LrApplicationView.getCurrentModuleName()`: "develop", "library", …,
    /// or `importModule`.
    public var module: String
    /// `LrDevelopController.getSelectedTool()`: "masking", "crop", "" …
    public var tool: String

    public init(isLightroomFrontmost: Bool = true, module: String = "develop", tool: String = "") {
        self.isLightroomFrontmost = isLightroomFrontmost
        self.module = module
        self.tool = tool
    }

    /// The keymap scope for this module; other modules only get system keys,
    /// and the Import window only the Import keys and the pause toggle.
    public var scope: ParameterScope? {
        switch module {
        case "develop": .develop
        case "library": .library
        default: nil
        }
    }

    public var isMasking: Bool { tool == "masking" }

    /// Lightroom's Import window is in front.
    public var isImportWindow: Bool { module == Self.importModule }
}

public enum MouseButton: Sendable, Hashable {
    case left, right, middle, back, forward
    case other(Int)

    /// CGEvent / NSEvent button numbers: 0 left, 1 right, 2 middle, 3 back, 4 forward.
    public init(buttonNumber: Int) {
        switch buttonNumber {
        case 0: self = .left
        case 1: self = .right
        case 2: self = .middle
        case 3: self = .back
        case 4: self = .forward
        default: self = .other(buttonNumber)
        }
    }
}

/// Decides, event by event, what a key, wheel or mouse event means:
///
/// - **Dial Key** (hold): the key is held back; scrolling, dragging, or ←/→
///   while it is down moves its slider. Released without a gesture, the key
///   is replayed to Lightroom (or runs the tap Command Key on the same chord).
/// - **Nudge Key** (tap): one step down / up; Shift selects the coarse step.
/// - **Command Key** (tap).
/// - **Combos**: with the Combo Key held, the next key is looked up as a
///   combo ("Tab + K"), a Command Key or a Nudge Key of its own. The Combo
///   Key tapped alone, or followed by a key that is no combo, still reaches
///   Lightroom in order.
/// - **System keys**: Palette (hold), Key Reference and pause toggles.
/// - **Mouse**: double / middle click resets the held slider, back / forward
///   buttons change photo.
/// - **Import keys**: in Lightroom's Import window (module "import") only
///   they and the pause toggle act. Each press sends Lightroom's own Import
///   shortcut (P, X, and the key that types ` or <), then → with "Move to
///   the next photo". Every other key, the wheel, the trackpad and the mouse reach
///   Lightroom untouched.
///
/// Typing stays safe: held keys are replayed in order when another key
/// arrives, nothing is captured in text fields, and ⌘/⌃/⌥ chords belong to
/// macOS and Lightroom unless a binding names them. With a mask selected
/// (tool "masking") Develop keys move the mask's own sliders; keys without a
/// mask equivalent do nothing rather than touch the whole photo.
///
/// `Event` is the platform event type; tests use plain values.
public struct KeyGestureEngine<Event> {
    public enum Decision {
        case pass
        case consume
        /// Drop the current event and post these events, in order, instead.
        case replace([Event])
    }

    public struct Outcome {
        public var decision: Decision
        public var intents: [KeyIntent]

        public init(_ decision: Decision, _ intents: [KeyIntent] = []) {
            self.decision = decision
            self.intents = intents
        }
    }

    /// A Palette key released sooner than this, without other input, is a tap:
    /// the Palette stays open until the key is pressed again.
    private static var tapThreshold: TimeInterval { 0.25 }

    // MARK: State

    private struct Held {
        /// The chord as pressed, to look the key up again when the module changes.
        let chord: KeyChord
        let keyDown: Event
        var target: ParameterKey
        var tapAction: CommandKey.Action?

        var keyCode: Int64 { chord.keyCode }

        /// The key is no longer a tap: it now controls its slider.
        func adjusting(began: Bool, didDrag: Bool) -> Active {
            Active(chord: chord, target: target, began: began, didDrag: didDrag)
        }
    }

    private struct Active {
        let chord: KeyChord
        var target: ParameterKey
        /// gestureBegan was emitted (a click-only reset does not start a gesture).
        var began: Bool
        var didDrag: Bool

        var keyCode: Int64 { chord.keyCode }
    }

    private struct PaletteHold {
        let keyCode: Int64
        let keyDown: Event
        let start: TimeInterval
        var interacted: Bool
    }

    private struct ComboKeyHold {
        let keyCode: Int64
        let keyDown: Event
        var used: Bool
    }

    /// A mask is selected and the held key's slider has no mask equivalent:
    /// scrolling is swallowed (never reaching the whole photo), a plain tap
    /// still reaches Lightroom (R still opens Crop).
    private struct Blocked {
        let keyCode: Int64
        let keyDown: Event
        let key: ParameterKey
        var scrolled: Bool
    }

    private enum Phase {
        case idle
        case held(Held)
        case adjusting(Active)
        case blocked(Blocked)
        case palette(PaletteHold)
    }

    /// Combos are indexed with their `.combo` modifier next to plain chords;
    /// key events never carry it, so they are reached only through the
    /// Combo Key.
    private struct ScopeIndex {
        var dial: [KeyChord: String] = [:]
        var nudge: [KeyChord: (id: String, direction: Double)] = [:]
        var actions: [KeyChord: CommandKey] = [:]
    }

    public private(set) var keymap: Keymap
    private(set) var isPaused = false
    private var phase: Phase = .idle
    private var comboKeyHold: ComboKeyHold?
    private var palettePinned = false
    private var consumedKeyUps: Set<Int64> = []
    private var consumedMouseUps: Set<MouseButton> = []
    private var leftButtonDown = false
    private var scroll = ScrollStepAccumulator()
    private var dragResidual = 0.0
    private var suppressesMomentum = false
    private var indexes: [ParameterScope: ScopeIndex] = [:]
    /// The keys the Import keys press in Lightroom's Import window.
    public private(set) var importShortcuts = ImportShortcuts.standard

    public init(keymap: Keymap) {
        self.keymap = keymap
        rebuildIndexes()
    }

    public mutating func update(keymap: Keymap) {
        self.keymap = keymap
        rebuildIndexes()
    }

    /// The app may pass Lightroom's Import shortcuts for the active keyboard layout.
    public mutating func update(importShortcuts: ImportShortcuts) {
        self.importShortcuts = importShortcuts
    }

    // MARK: Queries for the event tap

    /// A key, the Combo Key, or the Palette key is being held.
    public var isEngaged: Bool {
        if comboKeyHold != nil { return true }
        if case .idle = phase { return false }
        return true
    }

    public var needsScrollEvents: Bool { isEngaged || suppressesMomentum }

    /// Mouse moves matter only while a Dial Key is held.
    public var needsMouseMoves: Bool {
        switch phase {
        case .held, .adjusting: true
        default: false
        }
    }

    public var isPaletteOpen: Bool {
        if case .palette = phase { return true }
        return palettePinned
    }

    /// The parameter a held Dial Key currently controls.
    var activeParameter: ParameterKey? {
        switch phase {
        case .held(let held): held.target
        case .adjusting(let active): active.target
        default: nil
        }
    }

    /// Forget every gesture (e.g. after the event tap was disabled).
    public mutating func reset() {
        phase = .idle
        comboKeyHold = nil
        palettePinned = false
        consumedKeyUps.removeAll()
        consumedMouseUps.removeAll()
        scroll.reset()
        dragResidual = 0
        suppressesMomentum = false
    }

    /// The Palette was closed by the companion (item chosen, click outside).
    public mutating func paletteDidClose() {
        palettePinned = false
        if case .palette(var menu) = phase {
            menu.interacted = true
            phase = .palette(menu)
        }
    }

    /// Key-downs still held back; the companion posts them if it stops early.
    public var heldEvents: [Event] {
        var events: [Event] = []
        if case .held(let held) = phase { events.append(held.keyDown) }
        if case .blocked(let blocked) = phase, !blocked.scrolled { events.append(blocked.keyDown) }
        if case .palette(let menu) = phase { events.append(menu.keyDown) }
        if let hold = comboKeyHold, !hold.used { events.append(hold.keyDown) }
        return events
    }

    // MARK: Keys

    /// - Parameter isTextInputFocused: evaluated at most once, and only when
    ///   the key would otherwise be captured (it may need an Accessibility query).
    public mutating func keyDown(
        chord pressed: KeyChord,
        event: Event,
        isAutoRepeat: Bool,
        timestamp: TimeInterval,
        context: EngineContext,
        isTextInputFocused: () -> Bool
    ) -> Outcome {
        guard context.isLightroomFrontmost else { return releaseEverything(then: event) }
        // A key event is never a combo by itself: only the held Combo Key makes one.
        let chord = pressed.withoutCombo

        var focusAnswer: Bool?
        func textFocused() -> Bool {
            if let focusAnswer { return focusAnswer }
            let answer = isTextInputFocused()
            focusAnswer = answer
            return answer
        }

        // Pause works everywhere, even while paused.
        if let pause = keymap.systemKeys.pauseToggle, chord == pause {
            if isAutoRepeat { return Outcome(.consume) }
            isPaused.toggle()
            guard isPaused else {
                consumedKeyUps.insert(chord.keyCode)
                return Outcome(.consume, [.togglePause(isPaused: false)])
            }
            let released = releaseEverything(then: nil)
            consumedKeyUps.insert(chord.keyCode)
            var intents = released.intents
            intents.append(.togglePause(isPaused: true))
            return Outcome(released.decision.replacingPass(with: .consume), intents)
        }
        if isPaused { return Outcome(.pass) }
        if context.isImportWindow {
            return importKeyDown(chord, event: event, isAutoRepeat: isAutoRepeat, textFocused: textFocused)
        }

        switch phase {
        case .held(let held):
            if chord.keyCode == held.keyCode { return Outcome(.consume) }
            if let direction = arrowDirection(chord) { return arrowStep(direction, chord: chord, context: context) }
            // Another key first: the user is typing. Release the held key ahead
            // of this one so characters stay in order.
            phase = .idle
            return Outcome(.replace([held.keyDown, event]))

        case .adjusting(let active):
            if chord.keyCode == active.keyCode { return Outcome(.consume) }
            if let direction = arrowDirection(chord) { return arrowStep(direction, chord: chord, context: context) }
            return Outcome(.pass)

        case .blocked(let blocked):
            if chord.keyCode == blocked.keyCode { return Outcome(.consume) } // auto-repeat
            // Another key first: the user is typing; deliver both in order.
            phase = .idle
            return Outcome(blocked.scrolled ? .pass : .replace([blocked.keyDown, event]))

        case .palette(var menu):
            if chord.keyCode == menu.keyCode { return Outcome(.consume) }
            menu.interacted = true
            phase = .palette(menu)
            return Outcome(.pass)

        case .idle:
            break
        }

        // Combo Key held: the next chord is looked up as a combo.
        if var hold = comboKeyHold {
            if chord.keyCode == hold.keyCode { return Outcome(.consume) }
            if let guide = keymap.systemKeys.keyReferenceToggle, guide.isCombo, chord.asCombo == guide {
                hold.used = true
                comboKeyHold = hold
                consumedKeyUps.insert(chord.keyCode)
                return Outcome(.consume, isAutoRepeat ? [] : [.toggleKeyReference])
            }
            if let scope = context.scope, let index = indexes[scope] {
                let combo = chord.asCombo
                if let action = index.actions[combo] {
                    hold.used = true
                    comboKeyHold = hold
                    consumedKeyUps.insert(chord.keyCode)
                    // One action per press, like a plain Command Key.
                    return Outcome(.consume, isAutoRepeat ? [] : [.action(action.action)])
                }
                if let match = ChordLookup.match(combo, in: index.nudge) {
                    hold.used = true
                    comboKeyHold = hold
                    consumedKeyUps.insert(chord.keyCode)
                    return Outcome(.consume, nudgeStep(id: match.value.id, scope: scope,
                                                     direction: match.value.direction,
                                                     coarse: isCoarse(shift: match.extraShift),
                                                     context: context))
                }
            }
            // Not a combo: the user is typing.
            comboKeyHold = nil
            return Outcome(.replace(hold.used ? [event] : [hold.keyDown, event]))
        }

        // System keys.
        if let guide = keymap.systemKeys.keyReferenceToggle, chord == guide {
            if isAutoRepeat { return Outcome(.consume) }
            if textFocused() { return Outcome(.pass) }
            consumedKeyUps.insert(chord.keyCode)
            return Outcome(.consume, [.toggleKeyReference])
        }
        if context.scope == .develop,
           let menuKey = keymap.systemKeys.paletteKey, chord == menuKey {
            if palettePinned {
                palettePinned = false
                consumedKeyUps.insert(chord.keyCode)
                return Outcome(.consume, [.closePalette])
            }
            if isAutoRepeat || textFocused() { return Outcome(.pass) }
            phase = .palette(PaletteHold(keyCode: chord.keyCode, keyDown: event, start: timestamp, interacted: false))
            return Outcome(.consume, [.openPalette])
        }
        guard let scope = context.scope, let index = indexes[scope] else { return Outcome(.pass) }
        if let comboKey = keymap.systemKeys.comboKey, chord == comboKey {
            if isAutoRepeat || textFocused() { return Outcome(.pass) }
            comboKeyHold = ComboKeyHold(keyCode: chord.keyCode, keyDown: event, used: false)
            return Outcome(.consume)
        }

        // Nudge keys: one step per press, repeating while held.
        if let match = ChordLookup.match(chord, in: index.nudge) {
            if !isAutoRepeat && textFocused() { return Outcome(.pass) }
            consumedKeyUps.insert(chord.keyCode)
            return Outcome(.consume, nudgeStep(id: match.value.id, scope: scope, direction: match.value.direction,
                                             coarse: isCoarse(shift: match.extraShift), context: context))
        }

        // Dial Keys and tap Command Keys.
        if isAutoRepeat { return Outcome(.pass) }
        let dial = ChordLookup.match(chord, in: index.dial)
        let tapAction = index.actions[chord]
        guard dial != nil || tapAction != nil else { return Outcome(.pass) }
        if textFocused() { return Outcome(.pass) }

        if let dial {
            let key = ParameterKey(scope: scope, id: dial.value)
            guard let target = resolve(key, context: context) else {
                phase = .blocked(Blocked(keyCode: chord.keyCode, keyDown: event, key: key, scrolled: false))
                return Outcome(.consume)
            }
            scroll.reset()
            dragResidual = 0
            phase = .held(Held(chord: chord, keyDown: event, target: target, tapAction: tapAction?.action))
            return Outcome(.consume)
        }
        consumedKeyUps.insert(chord.keyCode)
        return Outcome(.consume, [.action(tapAction!.action)])
    }

    public mutating func keyUp(keyCode: Int64, event: Event, timestamp: TimeInterval, context: EngineContext) -> Outcome {
        if context.isImportWindow && context.isLightroomFrontmost && isInterrupted {
            return releaseEverything(then: event)
        }
        if consumedKeyUps.remove(keyCode) != nil { return Outcome(.consume) }

        switch phase {
        case .held(let held) where held.keyCode == keyCode:
            phase = .idle
            if let tap = held.tapAction { return Outcome(.consume, [.action(tap)]) }
            // A tap: Lightroom gets the complete key press it would have had.
            return Outcome(.replace([held.keyDown, event]))

        case .adjusting(let active) where active.keyCode == keyCode:
            phase = .idle
            scroll.reset()
            dragResidual = 0
            suppressesMomentum = true
            var intents: [KeyIntent] = []
            if active.began { intents.append(.gestureEnded(active.target)) }
            if active.didDrag && keymap.input.returnCursorAfterRelease { intents.append(.restorePointer) }
            return Outcome(.consume, intents)

        case .blocked(let blocked) where blocked.keyCode == keyCode:
            phase = .idle
            return Outcome(blocked.scrolled ? .consume : .replace([blocked.keyDown, event]))

        case .palette(let menu) where menu.keyCode == keyCode:
            phase = .idle
            // The Palette Key has one job and never reaches Lightroom: a quick
            // tap leaves the Palette open until the next press, a hold closes
            // it on release unless it is pinned.
            let tapped = timestamp - menu.start < Self.tapThreshold && !menu.interacted
            if tapped || keymap.palette.keepVisible {
                palettePinned = true
                return Outcome(.consume)
            }
            return Outcome(.consume, [.closePalette])

        default:
            break
        }

        // A Combo Key that made a combo is consumed; tapped alone, it is replayed.
        if let hold = comboKeyHold, hold.keyCode == keyCode {
            comboKeyHold = nil
            return Outcome(hold.used ? .consume : .replace([hold.keyDown, event]))
        }
        return Outcome(.pass)
    }

    // MARK: Wheel

    public mutating func scroll(
        lineDelta: Double,
        pointDelta: Double,
        isContinuous: Bool,
        isMomentum: Bool,
        modifiers: KeyModifiers,
        context: EngineContext
    ) -> Outcome {
        // Scrolling another app never changes a gesture; key events settle it.
        guard context.isLightroomFrontmost, !isPaused else { return Outcome(.pass) }
        if context.isImportWindow {
            // Lightroom scrolls the Import window as usual. This event can't
            // be posted again, so a key still held back is delivered alone.
            suppressesMomentum = false
            return isInterrupted ? releaseEverything(then: nil) : Outcome(.pass)
        }

        var intents = followContext(context)
        switch phase {
        case .idle:
            if suppressesMomentum {
                if isMomentum { return Outcome(.consume) }
                suppressesMomentum = false
            }
            return Outcome(.pass)
        case .palette:
            return Outcome(.pass)
        case .blocked(var blocked):
            if isMomentum { return Outcome(.pass) }
            guard lineDelta != 0 || pointDelta != 0 else { return Outcome(.pass) }
            let first = !blocked.scrolled
            blocked.scrolled = true
            phase = .blocked(blocked)
            return Outcome(.consume, first ? [.unavailableInMask(blocked.key)] : [])
        case .held:
            // Inertia from an earlier swipe, or an empty phase event, is not the
            // user scrolling: the key may still turn out to be a tap.
            if isMomentum { return Outcome(suppressesMomentum ? .consume : .pass) }
            guard lineDelta != 0 || pointDelta != 0 else { return Outcome(.pass) }
            intents += beginGesture()
        case .adjusting(let active):
            if !active.began && !isMomentum && (lineDelta != 0 || pointDelta != 0) {
                intents += beginGesture()
            }
        }

        let steps = scroll.steps(lineDelta: lineDelta, pointDelta: pointDelta,
                                 isContinuous: isContinuous, isMomentum: isMomentum)
        let directed = keymap.input.invertWheel ? -steps : steps
        if directed != 0 {
            intents += adjust(direction: Double(directed),
                              coarse: isCoarse(shift: modifiers.contains(.shift)))
        }
        return Outcome(.consume, intents)
    }

    // MARK: Mouse

    public mutating func mouseDown(
        _ button: MouseButton,
        clickCount: Int,
        event: Event,
        context: EngineContext
    ) -> Outcome {
        if button == .left { leftButtonDown = true }
        guard context.isLightroomFrontmost, !isPaused else { return Outcome(.pass) }
        if context.isImportWindow { return isInterrupted ? releaseEverything(then: event) : Outcome(.pass) }

        if (button == .back || button == .forward), keymap.input.mouseBackForward, context.scope != nil {
            consumedMouseUps.insert(button)
            let name = button == .back ? "previousPhoto" : "nextPhoto"
            return Outcome(.consume, [.action(.command(name: name, argument: nil))])
        }

        guard activeParameter != nil else { return Outcome(.pass) }
        let followed = followContext(context)
        guard let target = activeParameter else { return Outcome(.pass, followed) }
        switch button {
        case .left:
            consumedMouseUps.insert(.left)
            enterAdjustingSilently()
            let resets = clickCount >= 2 && keymap.input.doubleClickReset
            return Outcome(.consume, resets ? followed + [.reset(target)] : followed)
        case .middle where keymap.input.middleClickReset:
            consumedMouseUps.insert(.middle)
            enterAdjustingSilently()
            return Outcome(.consume, followed + [.reset(target)])
        default:
            return Outcome(.pass, followed)
        }
    }

    public mutating func mouseUp(_ button: MouseButton, event: Event, context: EngineContext) -> Outcome {
        if button == .left { leftButtonDown = false }
        return Outcome(consumedMouseUps.remove(button) != nil ? .consume : .pass)
    }

    /// Pointer movement (`dx` in points, positive to the right). Drags adjust
    /// the held Dial Key's slider horizontally.
    public mutating func mouseMoved(
        dx: Double,
        dy: Double,
        modifiers: KeyModifiers,
        event: Event,
        context: EngineContext
    ) -> Outcome {
        guard context.isLightroomFrontmost, !isPaused, needsMouseMoves else { return Outcome(.pass) }
        if context.isImportWindow { return releaseEverything(then: event) }
        let dragging = leftButtonDown
        guard dragging || !keymap.input.requireMouseDown else { return Outcome(.pass) }
        guard dx.isFinite else { return Outcome(dragging ? .consume : .pass) }

        var intents = followContext(context)
        let pointsPerStep = keymap.input.dragPointsPerStep
        if dragResidual != 0 && (dragResidual > 0) != (dx > 0) && dx != 0 { dragResidual = 0 }
        dragResidual += dx
        let steps = (dragResidual / pointsPerStep).rounded(.towardZero)
        dragResidual -= steps * pointsPerStep
        if dx != 0 { markDragged() }
        if steps != 0 {
            intents += beginGesture()
            intents += adjust(direction: steps, coarse: isCoarse(shift: modifiers.contains(.shift)))
        }
        return Outcome(dragging ? .consume : .pass, intents)
    }

    // MARK: Import window

    /// A gesture, a held key or an open Palette is left over from before the
    /// Import window came to the front.
    private var isInterrupted: Bool { isEngaged || isPaletteOpen }

    /// In Lightroom's Import window only the Import keys act. Anything left
    /// over from Develop or Library ends first, as when Lightroom loses
    /// focus: held keys reach Lightroom, a gesture ends, the Palette closes.
    /// The Develop and Library keys mean nothing there, so a held Dial Key
    /// does not keep its slider (Quick Develop would change the catalog's
    /// photos behind the window).
    private mutating func importKeyDown(_ chord: KeyChord, event: Event, isAutoRepeat: Bool,
                                        textFocused: () -> Bool) -> Outcome {
        let released = isInterrupted ? releaseEverything(then: nil) : Outcome(.pass)
        let own = importKey(chord, isAutoRepeat: isAutoRepeat, textFocused: textFocused)
        guard case .replace(let held) = released.decision else { return Outcome(own.decision, released.intents + own.intents) }
        let consumed = if case .consume = own.decision { true } else { false }
        return Outcome(.replace(consumed ? held : held + [event]), released.intents + own.intents)
    }

    /// An Import key presses Lightroom's own shortcut, plus → after Include
    /// or Exclude with "Move to the next photo". Auto-repeats are swallowed
    /// when the first press was taken, and pass when it went to a text field.
    /// A Toggle the layout has no key for is taken and sends nothing.
    private mutating func importKey(_ chord: KeyChord, isAutoRepeat: Bool, textFocused: () -> Bool) -> Outcome {
        guard let role = keymap.importKeys.role(for: chord) else { return Outcome(.pass) }
        if isAutoRepeat { return Outcome(consumedKeyUps.contains(chord.keyCode) ? .consume : .pass) }
        if textFocused() { return Outcome(.pass) }
        consumedKeyUps.insert(chord.keyCode)
        guard let lightroomKey = importShortcuts.chord(for: role) else { return Outcome(.consume) }
        var intents: [KeyIntent] = [.action(.shortcut(lightroomKey))]
        if keymap.importKeys.advance && role != .toggle {
            intents.append(.action(.shortcut(importShortcuts.nextPhoto)))
        }
        return Outcome(.consume, intents)
    }

    // MARK: Helpers

    private mutating func rebuildIndexes() {
        indexes = [:]
        for scope in ParameterScope.allCases {
            var index = ScopeIndex()
            for (id, slot) in keymap.slots(for: scope).sorted(by: { $0.key < $1.key }) {
                if let chord = slot.dial, !chord.isCombo, index.dial[chord] == nil { index.dial[chord] = id }
                for (chord, direction) in [(slot.nudgeDown, -1.0), (slot.nudgeUp, 1.0)] {
                    guard let chord, index.nudge[chord] == nil else { continue }
                    index.nudge[chord] = (id, direction)
                }
            }
            for action in keymap.commandKeys(in: scope) where index.actions[action.chord] == nil {
                index.actions[action.chord] = action
            }
            indexes[scope] = index
        }
        scroll.pointsPerStep = keymap.input.trackpadPointsPerStep
    }

    /// The parameter a key controls now, or nil when a mask is selected and
    /// the slider has no mask equivalent.
    private func resolve(_ key: ParameterKey, context: EngineContext) -> ParameterKey? {
        guard key.scope == .develop, context.isMasking, keymap.input.followSelectedMask else { return key }
        guard let local = Keymap.localKey(for: key), ParameterCatalog.descriptor(for: local) != nil else { return nil }
        return local
    }

    private func isCoarse(shift: Bool) -> Bool {
        shift != keymap.input.swapNormalAndCoarse
    }

    private func arrowDirection(_ chord: KeyChord) -> Double? {
        guard keymap.input.arrowKeysAdjust, !chord.usesReservedModifiers else { return nil }
        switch chord.keyCode {
        case KeyChord.leftArrow: return -1
        case KeyChord.rightArrow: return 1
        default: return nil
        }
    }

    /// ← or → while a Dial Key is held: one step on its slider.
    private mutating func arrowStep(_ direction: Double, chord: KeyChord, context: EngineContext) -> Outcome {
        var intents = followContext(context)
        intents += beginGesture()
        intents += adjust(direction: direction, coarse: isCoarse(shift: chord.modifiers.contains(.shift)))
        consumedKeyUps.insert(chord.keyCode)
        return Outcome(.consume, intents)
    }

    /// `direction` whole steps on the active gesture's slider.
    private func adjust(direction: Double, coarse: Bool) -> [KeyIntent] {
        guard let target = activeParameter else { return [] }
        return adjustment(of: target, direction: direction, coarse: coarse)
    }

    private func nudgeStep(id: String, scope: ParameterScope, direction: Double, coarse: Bool,
                           context: EngineContext) -> [KeyIntent] {
        let key = ParameterKey(scope: scope, id: id)
        guard let target = resolve(key, context: context) else { return [.unavailableInMask(key)] }
        return adjustment(of: target, direction: direction, coarse: coarse)
    }

    /// `direction` whole steps on `target`; nothing when that rounds to zero.
    private func adjustment(of target: ParameterKey, direction: Double, coarse: Bool) -> [KeyIntent] {
        guard let steps = keymap.steps(for: target) else { return [] }
        let delta = IntentCoalescer.round(direction * (coarse ? steps.coarse : steps.normal))
        return delta == 0 ? [] : [.adjust(target, delta: delta)]
    }

    /// Held → adjusting with gestureBegan (and revealPanel when enabled).
    private mutating func beginGesture() -> [KeyIntent] {
        switch phase {
        case .held(let held):
            phase = .adjusting(held.adjusting(began: true, didDrag: false))
            return began(held.target)
        case .adjusting(var active) where !active.began:
            active.began = true
            phase = .adjusting(active)
            return began(active.target)
        default:
            return []
        }
    }

    private func began(_ target: ParameterKey) -> [KeyIntent] {
        keymap.input.revealPanels ? [.gestureBegan(target), .revealPanel(target)] : [.gestureBegan(target)]
    }

    /// The module or the Masking tool changed while a Dial Key is held. The
    /// new context reaches the app a moment after Lightroom switches, often
    /// after the key went down, so the key moves the slider it has now. A
    /// gesture already begun ends on the old slider and begins again on the
    /// new one at the next step. When the key means nothing in the new
    /// context, it keeps its slider and Lightroom says why it cannot move it.
    private mutating func followContext(_ context: EngineContext) -> [KeyIntent] {
        let chord: KeyChord
        let current: ParameterKey
        switch phase {
        case .held(let held): (chord, current) = (held.chord, held.target)
        case .adjusting(let active): (chord, current) = (active.chord, active.target)
        default: return []
        }
        guard let scope = context.scope, let index = indexes[scope],
              let dial = ChordLookup.match(chord, in: index.dial),
              let target = resolve(ParameterKey(scope: scope, id: dial.value), context: context),
              target != current else { return [] }
        switch phase {
        case .held(var held):
            held.target = target
            held.tapAction = index.actions[chord]?.action
            phase = .held(held)
            return []
        case .adjusting(var active):
            let ended: [KeyIntent] = active.began ? [.gestureEnded(active.target)] : []
            active.target = target
            active.began = false
            phase = .adjusting(active)
            return ended
        default:
            return []
        }
    }

    /// A click while a Dial Key is held: the key is no longer a tap, but no
    /// slider gesture has started yet.
    private mutating func enterAdjustingSilently() {
        if case .held(let held) = phase {
            phase = .adjusting(held.adjusting(began: false, didDrag: false))
        }
    }

    private mutating func markDragged() {
        if case .held(let held) = phase {
            phase = .adjusting(held.adjusting(began: false, didDrag: true))
        } else if case .adjusting(var active) = phase {
            active.didDrag = true
            phase = .adjusting(active)
        }
    }

    /// Lightroom lost focus (or pause): deliver every held key rather than
    /// lose it, end gestures, close the menu.
    private mutating func releaseEverything(then event: Event?) -> Outcome {
        let held = heldEvents
        var intents: [KeyIntent] = []
        if case .adjusting(let active) = phase, active.began { intents.append(.gestureEnded(active.target)) }
        if isPaletteOpen { intents.append(.closePalette) }
        let pausedState = isPaused
        reset()
        isPaused = pausedState
        // Nothing held back: the current event (if any) goes through unchanged.
        guard !held.isEmpty else { return Outcome(.pass, intents) }
        return Outcome(.replace(held + (event.map { [$0] } ?? [])), intents)
    }
}

private extension KeyGestureEngine.Decision {
    func replacingPass(with other: KeyGestureEngine.Decision) -> KeyGestureEngine.Decision {
        if case .pass = self { return other }
        return self
    }
}
