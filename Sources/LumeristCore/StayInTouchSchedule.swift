import Foundation

/// When the app may ask for an email ("Stay in touch"). Pure, so the rule is
/// testable and can be ported as is:
///
/// - never once the user subscribed or chose "Don't ask me again";
/// - prompt 1 at the first opportunity (the app decides what that is: its
///   main window in front and key, never over Lightroom);
/// - prompt 2 at least 3 days after prompt 1, prompt 3 at least 14 days
///   after prompt 2;
/// - never more than 3 prompts.
///
/// A prompt counts as shown when it appears (`recordShown`), so "Not now"
/// and closing it count too. The button in About & Support opens the prompt
/// on request and never counts.
public struct StayInTouchSchedule: Equatable, Sendable {
    public var timesShown: Int
    public var lastShown: Date?
    public var dontAskAgain: Bool
    public var subscribedAt: Date?

    public static let maximumPrompts = 3
    /// The least time between prompt n and prompt n + 1 (index n - 1):
    /// 3 days before the second, 14 days before the third.
    public static let minimumGaps: [TimeInterval] = [3 * 86_400, 14 * 86_400]

    public init(timesShown: Int = 0, lastShown: Date? = nil, dontAskAgain: Bool = false, subscribedAt: Date? = nil) {
        self.timesShown = timesShown
        self.lastShown = lastShown
        self.dontAskAgain = dontAskAgain
        self.subscribedAt = subscribedAt
    }

    public var isSubscribed: Bool { subscribedAt != nil }

    /// Subscribed, "Don't ask me again", or all three prompts used.
    public var isFinished: Bool {
        isSubscribed || dontAskAgain || timesShown >= Self.maximumPrompts
    }

    /// The number of the next scheduled prompt (1...3), or nil when the app
    /// will never ask again.
    public var nextPrompt: Int? {
        isFinished ? nil : max(timesShown, 0) + 1
    }

    /// The earliest moment the next prompt may appear; nil when never.
    /// The first prompt (or a count without a date) is due at once.
    public var nextDate: Date? {
        guard let next = nextPrompt else { return nil }
        guard next > 1, let lastShown else { return .distantPast }
        return lastShown.addingTimeInterval(Self.minimumGaps[next - 2])
    }

    public func isDue(at now: Date) -> Bool {
        guard let nextDate else { return false }
        return now >= nextDate
    }

    /// The prompt appeared (whatever the user does next).
    public mutating func recordShown(at now: Date) {
        guard timesShown < Self.maximumPrompts else { return }
        timesShown = max(timesShown, 0) + 1
        lastShown = now
    }

    /// A sign-up the server confirmed: never ask again.
    public mutating func recordSubscribed(at now: Date) {
        subscribedAt = now
    }

    /// "Don't ask me again".
    public mutating func stopAsking() {
        dontAskAgain = true
    }

    // MARK: Storage

    /// UserDefaults keys in the app's own domain.
    public enum Key {
        public static let timesShown = "org.lumerist.stay-in-touch.times-shown"
        public static let lastShown = "org.lumerist.stay-in-touch.last-shown"
        public static let dontAskAgain = "org.lumerist.stay-in-touch.dont-ask-again"
        /// The key the first sign-up form already wrote: anyone who subscribed
        /// with it is never asked.
        public static let subscribedAt = GaleristOptIn.subscribedAtDefaultsKey

        public static let all = [timesShown, lastShown, dontAskAgain, subscribedAt]
    }

    /// Reads from a key-value store (UserDefaults in the app, a dictionary in
    /// tests). Missing or malformed values read as "never shown".
    public init(reading value: (String) -> Any?) {
        let shown = value(Key.timesShown) as? Int ?? 0
        self.init(
            timesShown: min(max(shown, 0), Self.maximumPrompts),
            lastShown: value(Key.lastShown) as? Date,
            dontAskAgain: value(Key.dontAskAgain) as? Bool ?? false,
            subscribedAt: value(Key.subscribedAt) as? Date
        )
    }

    /// Writes every field; nil removes the key.
    public func write(_ store: (String, Any?) -> Void) {
        store(Key.timesShown, timesShown)
        store(Key.lastShown, lastShown)
        store(Key.dontAskAgain, dontAskAgain)
        store(Key.subscribedAt, subscribedAt)
    }
}
