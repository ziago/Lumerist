import Foundation

/// When the update window appears. Shared with Windows:
/// Shared/fixtures/updates/prompt.json.
///
/// - Only for a version strictly newer than the running one (both
///   `<semver>`; a development build never sees it).
/// - "Check for updates" pressed by hand (`manual`) always shows it.
/// - After a check at launch (`launch`), not for the version the user
///   skipped ("Skip this version"; a newer one shows again), and not before
///   `laterUntil` ("Later": 24 hours). A `laterUntil` more than 24 hours
///   ahead (the clock was moved back) no longer counts.
/// - Whether the launch check runs at all is the update check's rule
///   (at most once a day, unless turned off).
public struct UpdatePromptSchedule: Equatable, Sendable {
    public enum Trigger: String, Sendable {
        case launch
        case manual
    }

    public var skippedVersion: String?
    public var laterUntil: Date?

    public static let laterInterval: TimeInterval = 24 * 60 * 60

    public init(skippedVersion: String? = nil, laterUntil: Date? = nil) {
        self.skippedVersion = skippedVersion
        self.laterUntil = laterUntil
    }

    public func shouldShow(available: String, current: String, trigger: Trigger, now: Date) -> Bool {
        guard let offered = UpdateChoice.version(fromCurrent: available),
              let running = UpdateChoice.version(fromCurrent: current),
              running < offered else { return false }
        if trigger == .manual { return true }
        if let skippedVersion, UpdateChoice.version(fromCurrent: skippedVersion) == offered { return false }
        if let laterUntil, now < laterUntil, laterUntil.timeIntervalSince(now) <= Self.laterInterval { return false }
        return true
    }

    /// "Later" (and "Release page"): not again before a day has passed.
    public mutating func later(at now: Date) {
        laterUntil = now.addingTimeInterval(Self.laterInterval)
    }

    /// "Skip this version".
    public mutating func skip(_ version: String) {
        skippedVersion = version
    }

    // MARK: Storage

    /// UserDefaults keys in the Mac app's own domain.
    public enum Key {
        public static let skippedVersion = "lumerist.updates.skipped-version"
        public static let laterUntil = "lumerist.updates.later-until"
    }

    /// Missing or malformed values read as "never answered".
    public init(reading value: (String) -> Any?) {
        self.init(skippedVersion: value(Key.skippedVersion) as? String, laterUntil: value(Key.laterUntil) as? Date)
    }

    /// Writes both fields; nil removes the key.
    public func write(_ store: (String, Any?) -> Void) {
        store(Key.skippedVersion, skippedVersion)
        store(Key.laterUntil, laterUntil)
    }
}
