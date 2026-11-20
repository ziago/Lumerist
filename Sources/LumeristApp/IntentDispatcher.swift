import Foundation
import LumeristCore

/// Turns the gesture engine's intents into protocol-3 requests (adjustments
/// summed at the chosen update rate), runs macros, sends Lightroom's own
/// shortcuts, and falls back to a shortcut when the SDK refuses a command.
@MainActor
final class IntentDispatcher {
    struct Hooks {
        /// Sends a request; returns its id (nil when not connected).
        var send: (LightroomRequest) -> Int?
        var updateRateHz: () -> Double
        var notice: (String) -> Void
        var openPalette: () -> Void
        var closePalette: () -> Void
        var toggleKeyReference: () -> Void
        var log: (String) -> Void
    }

    /// Lightroom Classic's own shortcuts, by character, so they are sent with
    /// the physical key that types that character in the active layout
    /// (⌘Z is a different key on AZERTY and QWERTZ keyboards).
    private static let fallbackCharacters: [LightroomCommandName: (character: String, modifiers: KeyModifiers, ansiKeyCode: Int64)] = [
        .autoTone: ("u", .command, 32),          // ⌘U
        .undo: ("z", .command, 6),               // ⌘Z
        .redo: ("z", [.command, .shift], 6),     // ⇧⌘Z
        .flagPick: ("p", [], 35),                // P
        .flagReject: ("x", [], 7),               // X
        .flagRemove: ("u", [], 32),              // U
        .showClipping: ("j", [], 38),            // J
    ]

    /// Arrow keys are the same on every layout.
    private static let fallbackKeys: [LightroomCommandName: KeyChord] = [
        .nextPhoto: KeyChord(124),               // →
        .previousPhoto: KeyChord(123),           // ←
    ]

    /// Used when a command is not available through the SDK in the current
    /// module (e.g. Auto Tone in Library).
    static func fallbackShortcut(for command: LightroomCommandName) -> KeyChord? {
        if let chord = fallbackKeys[command] { return chord }
        guard let entry = fallbackCharacters[command] else { return nil }
        let keyCode = KeyboardLayout.keyCode(for: entry.character) ?? entry.ansiKeyCode
        return KeyChord(keyCode, entry.modifiers)
    }

    private let hooks: Hooks
    private var coalescer = IntentCoalescer()
    private var flushTask: Task<Void, Never>?
    /// Shortcut to send if Lightroom refuses a command, keyed by request id.
    /// Entries expire: a late answer must not trigger a keystroke.
    private var fallbacks: [Int: (chord: KeyChord, sent: Date)] = [:]
    private static let fallbackLifetime: TimeInterval = 3

    init(hooks: Hooks) {
        self.hooks = hooks
    }

    func handle(_ intents: [KeyIntent]) {
        for intent in intents {
            if coalescer.add(intent) {
                flushTask?.cancel()
                flushTask = nil
                perform(coalescer.flush())
            } else {
                scheduleFlush()
            }
        }
    }

    /// Called for every ack; returns true when a rejected command was retried
    /// with Lightroom's own shortcut.
    @discardableResult
    func didReceive(_ ack: LightroomAck) -> Bool {
        guard let id = ack.id, let fallback = fallbacks.removeValue(forKey: id) else { return false }
        guard !ack.accepted,
              Date().timeIntervalSince(fallback.sent) < Self.fallbackLifetime else { return false }
        guard KeyEngineMonitor.postToLightroom(fallback.chord) else {
            hooks.log("Fallback \(fallback.chord.displayText) skipped: Lightroom is not in front")
            return false
        }
        hooks.log("Fallback shortcut \(fallback.chord.displayText) after: \(ack.message ?? "rejected")")
        return true
    }

    /// The bridge dropped: pending answers will never arrive, and ids may be
    /// reused after reconnecting.
    func connectionLost() {
        fallbacks.removeAll()
    }

    private func scheduleFlush() {
        guard flushTask == nil else { return }
        let interval = 1 / max(1, hooks.updateRateHz())
        flushTask = Task { [weak self] in
            try? await Task.sleep(for: .seconds(interval))
            guard let self, !Task.isCancelled else { return }
            self.flushTask = nil
            self.perform(self.coalescer.flush())
        }
    }

    private func perform(_ intents: [KeyIntent]) {
        for intent in intents {
            switch intent {
            case .adjust(let key, let delta):
                send({ try .adjust(key.scope, key.id, delta: delta) })
            case .reset(let key):
                send({ try .reset(key.scope, key.id) })
            case .gestureBegan(let key):
                if key.scope == .develop { send({ try .gesture(.begin, key.id) }) }
            case .gestureEnded(let key):
                if key.scope == .develop { send({ try .gesture(.end, key.id) }) }
            case .action(let action):
                run(action)
            case .revealPanel(let key):
                send({ try .command(.revealPanel, key.id) })
            case .openPalette:
                DebugLog.write("Palette open")
                hooks.openPalette()
            case .closePalette:
                DebugLog.write("Palette closed")
                hooks.closePalette()
            case .toggleKeyReference:
                hooks.toggleKeyReference()
            case .togglePause(let isPaused):
                hooks.notice(isPaused ? "Lumerist paused" : "Lumerist resumed")
            case .unavailableInMask(let key):
                let label = ParameterCatalog.descriptor(for: key)?.label ?? key.id
                hooks.notice("\(label) is not available in a mask")
            case .restorePointer:
                break // Handled by the monitor, which knows where the gesture began.
            }
        }
    }

    private func run(_ action: CommandKey.Action) {
        switch action {
        case .command(let name, let argument):
            guard let command = LightroomCommandName(rawValue: name) else {
                hooks.log("Unknown command \(name)")
                return
            }
            let id = send({ try .command(command, argument) })
            if let id, let chord = Self.fallbackShortcut(for: command) {
                fallbacks[id] = (chord, Date())
            }
        case .preset(let uuid, _):
            send({ try .command(.applyPreset, uuid) })
        case .shortcut(let chord):
            KeyEngineMonitor.postToLightroom(chord)
        case .macro(let steps):
            Task { [weak self] in
                for step in steps {
                    guard let self else { return }
                    self.run(step.action)
                    if step.delay > 0 {
                        try? await Task.sleep(for: .seconds(step.delay))
                    }
                }
            }
        }
    }

    @discardableResult
    private func send(_ make: () throws -> LightroomRequest) -> Int? {
        do {
            return hooks.send(try make())
        } catch {
            hooks.log("Request not sent: \(error.localizedDescription)")
            return nil
        }
    }
}
