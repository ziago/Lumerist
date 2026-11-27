import AppKit
import Foundation
import LumeristCore
import Observation

/// Looks for a newer Lumerist release on GitHub. Lumerist checks when it
/// starts, at most once a day, unless the user unticks "Tell me when a new
/// version is out" (first-run guide) or the automatic check in General; the
/// request carries nothing about the user. A newer version is announced in
/// the menu bar item and in the update window (`UpdatePrompter`); nothing is
/// downloaded until the user presses "Update now" there.
@MainActor
@Observable
final class UpdateChecker {
    static let shared = UpdateChecker()

    enum State: Equatable {
        case idle
        case checking
        case upToDate(String)
        case available(AvailableUpdate)
        case noReleases
        /// `swift run` and other builds without a version: never offered an update.
        case developmentBuild
        case failed(String)
    }

    private enum Key {
        static let automatic = "lumerist.updates.automatic"
        static let lastCheck = "lumerist.updates.last-check"
    }

    static let automaticInterval: TimeInterval = 24 * 60 * 60

    private(set) var state: State = .idle
    @ObservationIgnored private let defaults: UserDefaults

    var checksAutomatically: Bool {
        didSet { defaults.set(checksAutomatically, forKey: Key.automatic) }
    }

    var lastCheck: Date? {
        defaults.object(forKey: Key.lastCheck) as? Date
    }

    init(defaults: UserDefaults = .standard) {
        self.defaults = defaults
        // On unless the user turned it off.
        checksAutomatically = defaults.object(forKey: Key.automatic) as? Bool ?? true
    }

    /// The daily check at launch, unless the user turned it off. With a
    /// debug test hook set it is always due, and leaves no time behind.
    func checkIfDue() {
        guard checksAutomatically, state != .checking else { return }
        if !UpdateEnvironment.isTesting, let lastCheck, Date().timeIntervalSince(lastCheck) < Self.automaticInterval { return }
        Task { await check(trigger: .launch) }
    }

    func check(trigger: UpdatePromptSchedule.Trigger) async {
        guard state != .checking else { return }
        state = .checking
        let current = UpdateEnvironment.currentVersion
        do {
            let data: Data
            switch UpdateEnvironment.feed {
            case .github:
                var request = URLRequest(url: SupportLinks.releasesAPI, timeoutInterval: 15)
                request.setValue("application/vnd.github+json", forHTTPHeaderField: "Accept")
                request.setValue("Lumerist", forHTTPHeaderField: "User-Agent")
                let (body, response) = try await URLSession.shared.data(for: request)
                let status = (response as? HTTPURLResponse)?.statusCode ?? 0
                if status == 404 {
                    // Only a real answer counts as "checked": being offline or
                    // rate-limited must not silence the daily check.
                    recordCheck()
                    state = .noReleases
                    return
                }
                guard status == 200 else {
                    state = .failed("GitHub answered with status \(status). Try again later.")
                    return
                }
                data = body
            case .file(let url):
                data = try Data(contentsOf: url)
            case .invalid(let reason):
                state = .failed(reason)
                return
            }
            let releases = try UpdateFeedRelease.decodeList(data)
            recordCheck()
            state = Self.evaluate(releases, current: current)
            if case .available(let update) = state {
                UpdatePrompter.shared.offer(update, current: current, trigger: trigger)
            }
        } catch {
            state = .failed("Could not reach GitHub: \(error.localizedDescription)")
        }
    }

    private func recordCheck() {
        guard !UpdateEnvironment.isTesting else { return }
        defaults.set(Date(), forKey: Key.lastCheck)
    }

    func openReleasePage() {
        if case .available(let update) = state {
            NSWorkspace.shared.open(update.page)
        } else {
            NSWorkspace.shared.open(SupportLinks.releases)
        }
    }

    /// The newest non-draft release with a `v<semver>` tag (pre-releases
    /// included), offered only when strictly newer than the running version.
    static func evaluate(_ releases: [UpdateFeedRelease], current: String) -> State {
        guard let running = UpdateChoice.version(fromCurrent: current) else { return .developmentBuild }
        guard UpdateChoice.newest(in: releases) != nil else { return .noReleases }
        if let update = UpdateChoice.update(in: releases, current: current) { return .available(update) }
        return .upToDate(running.description)
    }
}

/// The updater's debug-only test hooks. A release build (the signed app from
/// Scripts/build-macos-app.sh without --debug) ignores them, like the other
/// LUMERIST_* overrides.
///
/// - `LUMERIST_UPDATE_PRETEND_VERSION`: the version Lumerist believes it is
///   ("0.9.0" is offered the real v0.9.1).
/// - `LUMERIST_UPDATE_FEED_URL`: a `file://` URL (or an absolute path) of a
///   JSON in the releases API's format, read instead of api.github.com; its
///   assets may then also be `file://` URLs. https downloads keep the host
///   allowlist.
///
/// With either set, the launch check runs at every launch and its time is not saved.
enum UpdateEnvironment {
    enum Feed: Equatable {
        case github
        case file(URL)
        case invalid(String)
    }

    private static let values: [String: String] = {
        #if DEBUG
        ProcessInfo.processInfo.environment
        #else
        [:]
        #endif
    }()

    static var pretendVersion: String? {
        values["LUMERIST_UPDATE_PRETEND_VERSION"].flatMap { $0.isEmpty ? nil : $0 }
    }

    static var feed: Feed {
        guard let text = values["LUMERIST_UPDATE_FEED_URL"], !text.isEmpty else { return .github }
        if text.hasPrefix("/") { return .file(URL(fileURLWithPath: text)) }
        guard let url = URL(string: text), url.isFileURL else {
            // Refuse rather than fall back to GitHub.
            return .invalid("LUMERIST_UPDATE_FEED_URL must be a file:// URL or an absolute path.")
        }
        return .file(url)
    }

    static var isTesting: Bool { pretendVersion != nil || feed != .github }

    /// Asset URLs may be `file://` only with the test feed.
    static var allowsFileURLs: Bool {
        if case .file = feed { return true }
        return false
    }

    static var currentVersion: String { pretendVersion ?? SupportLinks.appVersion }
}
