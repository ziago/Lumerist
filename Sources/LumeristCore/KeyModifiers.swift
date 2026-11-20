import Foundation

/// Modifier keys that matter to Dial Key, Nudge Key and Command Key decisions.
public struct KeyModifiers: OptionSet, Sendable, Hashable {
    public let rawValue: Int
    public init(rawValue: Int) { self.rawValue = rawValue }

    public static let shift = KeyModifiers(rawValue: 1 << 0)
    public static let control = KeyModifiers(rawValue: 1 << 1)
    public static let option = KeyModifiers(rawValue: 1 << 2)
    public static let command = KeyModifiers(rawValue: 1 << 3)
    /// Not a macOS modifier: a chord with it is a *combo*, which works only
    /// while the Combo Key is held ("Tab + K"). Keyboard events never carry
    /// it; only bindings do.
    public static let combo = KeyModifiers(rawValue: 1 << 8)

    /// Command, Control, and Option combinations belong to Lightroom and macOS
    /// (Cmd+R, Ctrl+Z, ...). Shift alone is allowed: it selects the coarse step.
    public var reservesKey: Bool {
        !isDisjoint(with: [.command, .control, .option])
    }
}

/// Converts raw scroll-wheel values into whole slider steps.
///
/// Notched wheels report line deltas that map one-to-one onto steps.
/// Continuous devices (trackpads, Magic Mouse) report many small point deltas,
/// which are accumulated so a short swipe does not jump the slider. Momentum
/// (inertia after the fingers lift) is ignored so the slider stops with them.
public struct ScrollStepAccumulator: Sendable, Equatable {
    /// Default trackpad travel, in points, for one slider step.
    public static let pointsPerStep = 12.0
    public static let pointsPerStepRange = 4.0...40.0

    /// Trackpad travel per step for this accumulator (user sensitivity).
    public var pointsPerStep: Double = Self.pointsPerStep {
        didSet {
            pointsPerStep = min(Self.pointsPerStepRange.upperBound, max(Self.pointsPerStepRange.lowerBound, pointsPerStep))
        }
    }
    public static let maximumStepsPerEvent = 10

    private var residual = 0.0

    public init() {}

    public mutating func reset() { residual = 0 }

    public mutating func steps(
        lineDelta: Double,
        pointDelta: Double,
        isContinuous: Bool,
        isMomentum: Bool
    ) -> Int {
        guard !isMomentum else { return 0 }
        let limit = Self.maximumStepsPerEvent

        if !isContinuous {
            guard lineDelta.isFinite else { return 0 }
            var whole = lineDelta.rounded(.towardZero)
            // Some smooth-scrolling mice report fractional lines: never lose a notch.
            if whole == 0 && lineDelta != 0 { whole = lineDelta > 0 ? 1 : -1 }
            return max(-limit, min(limit, Int(whole)))
        }

        guard pointDelta.isFinite, pointDelta != 0 else { return 0 }
        if residual != 0 && (residual > 0) != (pointDelta > 0) {
            residual = 0 // Direction reversed: start from the new direction.
        }
        residual += pointDelta
        let whole = (residual / pointsPerStep).rounded(.towardZero)
        residual -= whole * pointsPerStep
        return max(-limit, min(limit, Int(whole)))
    }
}
