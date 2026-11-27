import AppKit
import ApplicationServices
import Foundation
import LumeristCore

/// Feeds keyboard, scroll, and mouse events to `KeyGestureEngine` while the
/// user has enabled shortcuts, and applies its decisions. Lightroom context
/// (module and tool) comes from the plug-in's context events; while
/// `ImportWindowDetector` sees Lightroom's Import window, the module is
/// "import" instead.
final class KeyEngineMonitor: @unchecked Sendable {
    /// Marks events Lumerist posts itself so the tap lets them through.
    static let replayMarker: Int64 = 0x4C4B_5250 // "LKRP"
    private static let textInputRoles: Set<String> = [
        kAXTextFieldRole as String,
        kAXTextAreaRole as String,
        kAXComboBoxRole as String,
    ]

    private let lock = NSLock()
    private let onIntents: @Sendable ([KeyIntent]) -> Void
    private var engine: KeyGestureEngine<CGEvent>
    private var module = "develop"
    private var tool = ""
    private var isImportWindowOpen = false
    private var pointerAnchor: CGPoint?
    private var eventTap: CFMachPort?
    private var runLoopSource: CFRunLoopSource?

    init(keymap: Keymap, onIntents: @escaping @Sendable ([KeyIntent]) -> Void) {
        engine = KeyGestureEngine(keymap: keymap)
        self.onIntents = onIntents
    }

    deinit { stop() }

    func start() throws {
        guard AXIsProcessTrusted() else { throw MonitorError.accessibilityPermissionRequired }
        guard eventTap == nil else { return }

        let types: [CGEventType] = [
            .keyDown, .keyUp, .scrollWheel,
            .leftMouseDown, .leftMouseUp, .leftMouseDragged,
            .rightMouseDown, .rightMouseUp, .rightMouseDragged,
            .otherMouseDown, .otherMouseUp, .otherMouseDragged,
            .mouseMoved,
        ]
        let mask = types.reduce(CGEventMask(0)) { $0 | (CGEventMask(1) << $1.rawValue) }
        let pointer = Unmanaged.passUnretained(self).toOpaque()
        guard let tap = CGEvent.tapCreate(
            tap: .cgSessionEventTap,
            place: .headInsertEventTap,
            options: .defaultTap,
            eventsOfInterest: mask,
            callback: Self.callback,
            userInfo: pointer
        ) else {
            throw MonitorError.eventTapUnavailable
        }
        let source = CFMachPortCreateRunLoopSource(kCFAllocatorDefault, tap, 0)
        CFRunLoopAddSource(CFRunLoopGetMain(), source, .commonModes)
        CGEvent.tapEnable(tap: tap, enable: true)
        eventTap = tap
        runLoopSource = source
    }

    func stop() {
        if let source = runLoopSource {
            CFRunLoopRemoveSource(CFRunLoopGetMain(), source, .commonModes)
        }
        if let eventTap {
            CGEvent.tapEnable(tap: eventTap, enable: false)
        }
        runLoopSource = nil
        eventTap = nil
        releaseHeldKeys()
    }

    func update(keymap: Keymap) {
        lock.lock()
        engine.update(keymap: keymap)
        lock.unlock()
    }

    func update(module: String, tool: String) {
        lock.lock()
        self.module = module
        self.tool = tool
        lock.unlock()
    }

    /// Lightroom's Import window came to the front or went away.
    func update(isImportWindowOpen open: Bool) {
        lock.lock()
        isImportWindowOpen = open
        lock.unlock()
    }

    /// The keys the Import keys press, for the keyboard layout in use: set
    /// at start and again whenever the input source changes.
    func update(importShortcuts: ImportShortcuts) {
        lock.lock()
        engine.update(importShortcuts: importShortcuts)
        lock.unlock()
    }

    func paletteDidClose() {
        lock.lock()
        engine.paletteDidClose()
        lock.unlock()
    }

    /// Sends one of Lightroom's own shortcuts (Command Keys of the "shortcut"
    /// kind and fallbacks for commands the SDK cannot perform), only while
    /// Lightroom Classic is frontmost, so a late fallback or a macro step can
    /// never land in another app (⌘Z, X, U…).
    @discardableResult
    static func postToLightroom(_ chord: KeyChord) -> Bool {
        guard NSWorkspace.shared.frontmostApplication?.bundleIdentifier == LightroomLifecycle.bundleIdentifier else {
            return false
        }
        post(chord)
        return true
    }

    private static func post(_ chord: KeyChord) {
        let flags = Self.flags(for: chord.modifiers)
        for down in [true, false] {
            guard let event = CGEvent(keyboardEventSource: nil, virtualKey: CGKeyCode(chord.keyCode), keyDown: down) else { continue }
            event.flags = flags
            event.setIntegerValueField(.eventSourceUserData, value: replayMarker)
            event.post(tap: .cghidEventTap)
        }
    }

    // MARK: Event handling

    private static let callback: CGEventTapCallBack = { proxy, type, event, userInfo in
        guard let userInfo else { return Unmanaged.passUnretained(event) }
        let monitor = Unmanaged<KeyEngineMonitor>.fromOpaque(userInfo).takeUnretainedValue()
        return monitor.handle(proxy: proxy, type: type, event: event)
    }

    private func handle(proxy: CGEventTapProxy, type: CGEventType, event: CGEvent) -> Unmanaged<CGEvent>? {
        if type == .tapDisabledByTimeout || type == .tapDisabledByUserInput {
            releaseHeldKeys()
            if let eventTap { CGEvent.tapEnable(tap: eventTap, enable: true) }
            return Unmanaged.passUnretained(event)
        }
        if event.getIntegerValueField(.eventSourceUserData) == Self.replayMarker {
            return Unmanaged.passUnretained(event)
        }

        let frontmost = NSWorkspace.shared.frontmostApplication
        let isLightroomFrontmost = frontmost?.bundleIdentifier == LightroomLifecycle.bundleIdentifier
        let modifiers = Self.modifiers(of: event.flags)
        let now = ProcessInfo.processInfo.systemUptime

        lock.lock()
        let context = EngineContext(isLightroomFrontmost: isLightroomFrontmost,
                                    module: isImportWindowOpen ? EngineContext.importModule : module, tool: tool)
        let wasEngaged = engine.isEngaged
        let outcome: KeyGestureEngine<CGEvent>.Outcome
        switch type {
        case .keyDown:
            let chord = KeyChord(event.getIntegerValueField(.keyboardEventKeycode), modifiers)
            outcome = engine.keyDown(
                chord: chord,
                event: event.copy() ?? event,
                isAutoRepeat: event.getIntegerValueField(.keyboardEventAutorepeat) != 0,
                timestamp: now,
                context: context,
                isTextInputFocused: { Self.isTextInputFocused(in: frontmost) }
            )
        case .keyUp:
            outcome = engine.keyUp(
                keyCode: event.getIntegerValueField(.keyboardEventKeycode),
                event: event.copy() ?? event,
                timestamp: now,
                context: context
            )
        case .scrollWheel:
            guard engine.needsScrollEvents else {
                lock.unlock()
                return Unmanaged.passUnretained(event)
            }
            // Physical direction: wheel (or fingers) up = positive = increase,
            // whatever macOS "natural scrolling" does to the event's sign.
            let physical = Self.physicalDirectionSign(of: event)
            outcome = engine.scroll(
                lineDelta: physical * Self.verticalOrShiftedAxis(
                    Double(event.getIntegerValueField(.scrollWheelEventDeltaAxis1)),
                    Double(event.getIntegerValueField(.scrollWheelEventDeltaAxis2)),
                    modifiers: modifiers
                ),
                pointDelta: physical * Self.verticalOrShiftedAxis(
                    Double(event.getIntegerValueField(.scrollWheelEventPointDeltaAxis1)),
                    Double(event.getIntegerValueField(.scrollWheelEventPointDeltaAxis2)),
                    modifiers: modifiers
                ),
                isContinuous: event.getIntegerValueField(.scrollWheelEventIsContinuous) != 0,
                isMomentum: event.getIntegerValueField(.scrollWheelEventMomentumPhase) != 0,
                modifiers: modifiers,
                context: context
            )
        case .leftMouseDown, .rightMouseDown, .otherMouseDown:
            outcome = engine.mouseDown(
                MouseButton(buttonNumber: Int(event.getIntegerValueField(.mouseEventButtonNumber))),
                clickCount: Int(event.getIntegerValueField(.mouseEventClickState)),
                event: event,
                context: context
            )
        case .leftMouseUp, .rightMouseUp, .otherMouseUp:
            outcome = engine.mouseUp(
                MouseButton(buttonNumber: Int(event.getIntegerValueField(.mouseEventButtonNumber))),
                event: event,
                context: context
            )
        case .mouseMoved, .leftMouseDragged, .rightMouseDragged, .otherMouseDragged:
            guard engine.needsMouseMoves else {
                lock.unlock()
                return Unmanaged.passUnretained(event)
            }
            outcome = engine.mouseMoved(
                dx: event.getDoubleValueField(.mouseEventDeltaX),
                dy: event.getDoubleValueField(.mouseEventDeltaY),
                modifiers: modifiers,
                event: event,
                context: context
            )
        default:
            outcome = KeyGestureEngine<CGEvent>.Outcome(.pass)
        }
        // Remember where the pointer was when a Dial Key gesture started, so
        // "return cursor after release" can put it back.
        if !wasEngaged && engine.isEngaged {
            pointerAnchor = event.location
        }
        var intents = outcome.intents
        var anchor: CGPoint?
        if intents.contains(.restorePointer) {
            anchor = pointerAnchor
            intents.removeAll { $0 == .restorePointer }
        }
        if !engine.isEngaged { pointerAnchor = nil }
        lock.unlock()

        if let anchor {
            CGWarpMouseCursorPosition(anchor)
            CGAssociateMouseAndMouseCursorPosition(1)
        }
        if !intents.isEmpty { onIntents(intents) }

        switch outcome.decision {
        case .pass:
            return Unmanaged.passUnretained(event)
        case .consume:
            return nil
        case .replace(let events):
            // Inserted right after this tap, ahead of events still queued
            // behind the current one, so typing order is preserved.
            for replayed in events {
                replayed.setIntegerValueField(.eventSourceUserData, value: Self.replayMarker)
                replayed.tapPostEvent(proxy)
            }
            return nil
        }
    }

    /// A key held back when the monitor stops or the tap was disabled still
    /// reaches Lightroom; the gesture state starts clean.
    private func releaseHeldKeys() {
        lock.lock()
        let held = engine.heldEvents
        engine.reset()
        pointerAnchor = nil
        lock.unlock()
        for event in held {
            event.setIntegerValueField(.eventSourceUserData, value: Self.replayMarker)
            event.post(tap: .cghidEventTap)
        }
    }

    private static func modifiers(of flags: CGEventFlags) -> KeyModifiers {
        var result: KeyModifiers = []
        if flags.contains(.maskShift) { result.insert(.shift) }
        if flags.contains(.maskControl) { result.insert(.control) }
        if flags.contains(.maskAlternate) { result.insert(.option) }
        if flags.contains(.maskCommand) { result.insert(.command) }
        return result
    }

    private static func flags(for modifiers: KeyModifiers) -> CGEventFlags {
        var flags: CGEventFlags = []
        if modifiers.contains(.shift) { flags.insert(.maskShift) }
        if modifiers.contains(.control) { flags.insert(.maskControl) }
        if modifiers.contains(.option) { flags.insert(.maskAlternate) }
        if modifiers.contains(.command) { flags.insert(.maskCommand) }
        return flags
    }

    /// −1 when macOS inverted the event relative to the device (natural
    /// scrolling), so Dial Keys follow the physical movement of the wheel.
    private static func physicalDirectionSign(of event: CGEvent) -> Double {
        NSEvent(cgEvent: event)?.isDirectionInvertedFromDevice == true ? -1 : 1
    }

    /// macOS turns Shift + vertical wheel into horizontal scrolling for notched
    /// mice, so with Shift the horizontal axis is read too.
    private static func verticalOrShiftedAxis(_ vertical: Double, _ horizontal: Double, modifiers: KeyModifiers) -> Double {
        vertical != 0 || !modifiers.contains(.shift) ? vertical : horizontal
    }

    /// Typing a keyword or a search in Lightroom never starts a gesture.
    private static func isTextInputFocused(in application: NSRunningApplication?) -> Bool {
        guard let application else { return false }
        let appElement = AXUIElementCreateApplication(application.processIdentifier)
        AXUIElementSetMessagingTimeout(appElement, 0.03)
        var focusedValue: CFTypeRef?
        guard AXUIElementCopyAttributeValue(appElement, kAXFocusedUIElementAttribute as CFString, &focusedValue) == .success,
              let focusedValue,
              CFGetTypeID(focusedValue) == AXUIElementGetTypeID() else {
            return false
        }
        let focused = unsafeDowncast(focusedValue, to: AXUIElement.self)
        var roleValue: CFTypeRef?
        guard AXUIElementCopyAttributeValue(focused, kAXRoleAttribute as CFString, &roleValue) == .success,
              let role = roleValue as? String else {
            return false
        }
        return textInputRoles.contains(role)
    }

    enum MonitorError: LocalizedError {
        case accessibilityPermissionRequired
        case eventTapUnavailable

        var errorDescription: String? {
            switch self {
            case .accessibilityPermissionRequired:
                "Enable Lumerist in System Settings → Privacy & Security → Accessibility (on macOS 27: “Device control and data access”), then try again."
            case .eventTapUnavailable:
                "macOS could not start watching the keyboard, mouse and trackpad."
            }
        }
    }
}
