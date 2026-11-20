import AppKit
import Foundation
import LumeristCore
import Observation

/// Looks for a newer Lumerist release on GitHub. Nothing is ever downloaded
/// or installed automatically: a newer version is announced in the menu bar
/// item and only opens its release page. Lumerist checks when it starts, at
/// most once a day, unless the user unticks "Tell me when a new version is
/// out" (first-run guide) or the automatic check in General; the request
/// carries nothing about the user.
@MainActor
@Observable
final class UpdateChecker {
    static let shared = UpdateChecker()

    enum State: Equatable {
        case idle
        case checking
        case upToDate(String)
        case available(version: String, page: URL)
        case noReleases
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

    /// The daily check at launch, unless the user turned it off.
    func checkIfDue() {
        guard checksAutomatically, state != .checking else { return }
        if let lastCheck, Date().timeIntervalSince(lastCheck) < Self.automaticInterval { return }
        Task { await check() }
    }

    func check() async {
        guard state != .checking else { return }
        state = .checking
        do {
            var request = URLRequest(url: SupportLinks.releasesAPI, timeoutInterval: 15)
            request.setValue("application/vnd.github+json", forHTTPHeaderField: "Accept")
            request.setValue("Lumerist", forHTTPHeaderField: "User-Agent")
            let (data, response) = try await URLSession.shared.data(for: request)
            let status = (response as? HTTPURLResponse)?.statusCode ?? 0
            if status == 404 {
                // Only a real answer counts as "checked": being offline or
                // rate-limited must not silence the daily check.
                defaults.set(Date(), forKey: Key.lastCheck)
                state = .noReleases
                return
            }
            guard status == 200 else {
                state = .failed("GitHub answered with status \(status). Try again later.")
                return
            }
            let releases = try JSONDecoder().decode([Release].self, from: data)
            defaults.set(Date(), forKey: Key.lastCheck)
            state = Self.evaluate(releases, current: SupportLinks.appVersion)
        } catch {
            state = .failed("Could not reach GitHub: \(error.localizedDescription)")
        }
    }

    func openReleasePage() {
        if case .available(_, let page) = state, Self.isTrustedReleasePage(page) {
            NSWorkspace.shared.open(page)
        } else {
            NSWorkspace.shared.open(SupportLinks.releases)
        }
    }

    struct Release: Decodable {
        let tagName: String
        let htmlURL: URL
        let draft: Bool

        enum CodingKeys: String, CodingKey {
            case tagName = "tag_name"
            case htmlURL = "html_url"
            case draft
        }
    }

    /// Only a https page of this project's releases on github.com is opened,
    /// whatever the API answered.
    /// The path is compared as the browser will request it: an escaped
    /// character (%2F, %2E…) or a `..` step is refused, and so are another
    /// port and user info.
    static func isTrustedReleasePage(_ url: URL) -> Bool {
        let releases = SupportLinks.releases
        guard let parts = URLComponents(url: url, resolvingAgainstBaseURL: false) else { return false }
        let path = parts.percentEncodedPath
        return parts.scheme == "https"
            && parts.host == "github.com"
            && parts.port == nil
            && parts.user == nil && parts.password == nil
            && !path.contains("%")
            && !path.split(separator: "/").contains("..")
            && path.hasPrefix(releases.path + "/")
    }

    /// The newest non-draft release (pre-releases included) against the running version.
    static func evaluate(_ releases: [Release], current: String) -> State {
        let published = releases.filter { !$0.draft }
        guard let newest = SemanticVersion.newest(in: published.map(\.tagName)),
              let page = published.first(where: { $0.tagName == newest.tag })?.htmlURL else { return .noReleases }
        guard let running = SemanticVersion(current) else {
            // A development build: just point at the newest release.
            return .available(version: newest.version.description, page: page)
        }
        if running < newest.version {
            return .available(version: newest.version.description, page: page)
        }
        return .upToDate(running.description)
    }
}
