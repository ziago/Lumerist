/// What the user asked for with a key, wheel or mouse gesture. The companion
/// maps intents to bridge requests (docs/protocol-v3.md) or to its own UI.
public enum KeyIntent: Sendable, Hashable {
    /// Change a parameter by `delta` (slider units; see the catalog unit).
    case adjust(ParameterKey, delta: Double)
    case reset(ParameterKey)
    /// A Dial Key gesture started / ended (history grouping, overlay).
    case gestureBegan(ParameterKey)
    case gestureEnded(ParameterKey)
    case action(CommandKey.Action)
    case openPalette
    case closePalette
    case toggleKeyReference
    case togglePause(isPaused: Bool)
    /// Put the pointer back where the drag started.
    case restorePointer
    case revealPanel(ParameterKey)
    /// A key was pressed while a mask is selected, but its slider has no mask
    /// equivalent; nothing was changed (the global photo is left alone).
    case unavailableInMask(ParameterKey)

    /// Intents that should reach Lightroom without waiting for the next
    /// update-rate tick (everything except slider changes).
    var isImmediate: Bool {
        if case .adjust = self { return false }
        return true
    }
}

/// Sums slider changes between sends so Lightroom receives at most one change
/// per parameter per tick. Consecutive changes to the same parameter are
/// added; order is kept; totals above the parameter's limit are split; zero
/// totals are dropped. Other intents pass through in order.
///
/// Usage: `add` every intent; when it returns true (a non-adjust intent),
/// `flush()` now so the order is kept, otherwise flush at the update rate.
public struct IntentCoalescer: Sendable {
    /// Update rates offered to the user (how often adjustments are sent).
    public static let rateRange = 10.0...60.0
    public static let defaultRate = 30.0

    private var queue: [KeyIntent] = []

    public init() {}

    public var isEmpty: Bool { queue.isEmpty }

    /// Returns true when the queue should be flushed immediately.
    @discardableResult
    public mutating func add(_ intent: KeyIntent) -> Bool {
        if case .adjust(let key, let delta) = intent,
           let last = queue.indices.last,
           case .adjust(let lastKey, let lastDelta) = queue[last],
           lastKey == key {
            queue[last] = .adjust(key, delta: lastDelta + delta)
            return false
        }
        queue.append(intent)
        return intent.isImmediate
    }

    public mutating func flush() -> [KeyIntent] {
        defer { queue.removeAll(keepingCapacity: true) }
        var result: [KeyIntent] = []
        for intent in queue {
            guard case .adjust(let key, let delta) = intent else {
                result.append(intent)
                continue
            }
            let limit = ParameterCatalog.descriptor(for: key)?.maximumDelta ?? .infinity
            var remaining = Self.round(delta)
            while remaining != 0 {
                let part = max(-limit, min(limit, remaining))
                result.append(.adjust(key, delta: part))
                remaining = Self.round(remaining - part)
            }
        }
        return result
    }

    static func round(_ value: Double) -> Double {
        guard value.isFinite else { return 0 }
        return (value * 10_000).rounded() / 10_000
    }
}
