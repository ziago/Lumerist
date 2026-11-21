import Foundation

/// The optional "Stay in touch" prompt: texts, rules and links shared by the
/// app and its tests. `Copy.message` and `Copy.notice` are shown to the user
/// AND stored by Galerist as proof of consent under `consentVersion`, so they
/// must never change without a new consent version agreed with the server.
///
/// Nothing here runs by itself: the only network call is
/// `GaleristSubscriptionClient.subscribe`, made when the user has typed an
/// email and presses "Keep me posted" (or Return in the email field).
public enum GaleristOptIn {
    /// Consent version id; the server maps it to `Copy.message` and `Copy.notice`.
    public static let consentVersion = "lk-optin-v2"

    /// User-facing texts, character for character (straight ASCII apostrophes).
    public enum Copy {
        public static let title = "Stay in touch"
        /// Consent text: the purpose (Lumerist news and the developer's other tools).
        public static let message = "Leave your email to hear about new versions of Lumerist and about other photography tools from its developer."
        public static let emailPlaceholder = "you@example.com"
        /// Consent text, followed by the `privacyLink` (which names who holds the data).
        public static let notice = "Optional and free. No spam: every email has a one-click unsubscribe."
        public static let privacyLink = "Privacy"
        public static let button = "Keep me posted"
        public static let notNow = "Not now"
        public static let dontAskAgain = "Don't ask me again"
        public static let success = "Thanks, you're on the list."
        public static let done = "Done"
        /// General → About & Support.
        public static let aboutDetail = "News about Lumerist and other photography tools from its developer, by email."
        public static let aboutButton = "Leave your email…"
        public static let aboutSubscribed = "You're on the list"
        /// App About section and README.
        public static let credit = "Lumerist is made by the creator of Galerist, online galleries where photographers and videographers deliver their work to clients."
    }

    /// The consent texts of `lk-optin-v1`, the first form (never released;
    /// the app was then called LumenKeys), kept verbatim. The app never shows
    /// or sends them: they stay so the v1 drift guard keeps matching the map
    /// the server still holds as proof of consent for that version.
    public enum ConsentV1 {
        public static let consentVersion = "lk-optin-v1"
        public static let lumenkeysUpdates = "Email me when a new version of LumenKeys is out."
        public static let galeristNews = "Also send me news and offers from Galerist, online galleries to deliver photos to your clients, from the maker of LumenKeys."
        public static let clientsQuestion = "Do you photograph for clients?"
        public static let notice = "Optional and free. Your email goes to Galerist, the maker of LumenKeys, and is used only for what you tick. Every email has a one-click unsubscribe."
    }

    /// Production server; tests may point elsewhere with `baseURLEnvironmentKey`.
    public static let productionBaseURL = URL(string: "https://galerist.photo")!
    public static let baseURLEnvironmentKey = "LUMERIST_GALERIST_BASE_URL"
    public static let privacyURL = URL(string: "https://galerist.photo/en/legal/privacy#lumenkeys")!

    /// The date of a confirmed sign-up, the only thing remembered about it
    /// (the email never is). Written since the first form, so anyone who
    /// already subscribed is never asked again.
    public static let subscribedAtDefaultsKey = "lumerist.galerist.subscribed-at"

    /// Tracked short links `https://galerist.photo/c/<code>` (production only;
    /// they carry no personal data).
    public enum TrackedLink: String, CaseIterable, Sendable {
        case settings = "lk-settings"
        case menu = "lk-menu"
        /// Used by README.md and CHANGELOG.md, not by the app.
        case readme = "lk-readme"
        case release = "lk-release"

        public var url: URL {
            GaleristOptIn.productionBaseURL.appending(path: "c/\(rawValue)")
        }
    }

    // MARK: Rules

    /// A local sanity check (the server checks syntax and the domain's DNS):
    /// ASCII only, like the server (an internationalised domain is written
    /// "xn--…"); one `@`; an unquoted local part of at most 64 characters; a
    /// dotted domain of letters, digits and hyphens whose top-level label is
    /// letters only or "xn--"; at most 254 characters.
    public static func isPlausibleEmail(_ raw: String) -> Bool {
        let email = raw.trimmingCharacters(in: .whitespacesAndNewlines)
        guard (3...254).contains(email.utf16.count) else { return false }
        let parts = email.split(separator: "@", omittingEmptySubsequences: false)
        guard parts.count == 2 else { return false }
        let local = parts[0], domain = parts[1]
        guard !local.isEmpty, local.utf16.count <= 64,
              local.unicodeScalars.allSatisfy(isLocalPartCharacter),
              !local.hasPrefix("."), !local.hasSuffix("."), !local.contains("..") else { return false }
        let labels = domain.split(separator: ".", omittingEmptySubsequences: false)
        guard labels.count >= 2 else { return false }
        for label in labels {
            guard (1...63).contains(label.utf16.count),
                  !label.hasPrefix("-"), !label.hasSuffix("-"),
                  label.unicodeScalars.allSatisfy({ isASCIILetterOrDigit($0) || $0 == "-" })
            else { return false }
        }
        // Top-level label: letters only (so never an IP address), or "xn--".
        guard let topLevel = labels.last, topLevel.count >= 2,
              topLevel.lowercased().hasPrefix("xn--")
                || topLevel.unicodeScalars.allSatisfy({ ("a"..."z").contains($0) || ("A"..."Z").contains($0) })
        else { return false }
        return true
    }

    private static func isASCIILetterOrDigit(_ scalar: Unicode.Scalar) -> Bool {
        ("a"..."z").contains(scalar) || ("A"..."Z").contains(scalar) || ("0"..."9").contains(scalar)
    }

    /// RFC 5322 "atext" plus the dot: no quotes, spaces, brackets or non-ASCII.
    private static func isLocalPartCharacter(_ scalar: Unicode.Scalar) -> Bool {
        isASCIILetterOrDigit(scalar) || "!#$%&'*+-/=?^_`{|}~.".unicodeScalars.contains(scalar)
    }

    /// Two-letter lowercase primary language of the first preferred language
    /// ("it-IT" → "it", "zh-Hans-CN" → "zh"); nil when there is none or it is
    /// not a two-letter code (e.g. "fil").
    public static func primaryLanguage(from preferredLanguages: [String]) -> String? {
        guard let first = preferredLanguages.first else { return nil }
        let code = first.split(whereSeparator: { $0 == "-" || $0 == "_" }).first.map { $0.lowercased() } ?? ""
        return isLanguageCode(code) ? code : nil
    }

    static func isLanguageCode(_ code: String) -> Bool {
        code.count == 2 && code.unicodeScalars.allSatisfy { ("a"..."z").contains($0) }
    }

    /// Hosts a test override may point at over https: the Privacy link says
    /// who receives the email, so an environment variable must never be able
    /// to send it anywhere else.
    /// To test against another Galerist server over https, add its host here
    /// in a local build only.
    static let overrideHosts: Set<String> = ["galerist.photo"]

    /// Production unless `LUMERIST_GALERIST_BASE_URL` is set. An override must
    /// be https to a Galerist host (`overrideHosts`), or http to 127.0.0.1 for
    /// a local server; anything else returns nil so a mistyped test setting
    /// never falls back to the production server.
    public static func baseURL(environment: [String: String]) -> URL? {
        guard let raw = environment[baseURLEnvironmentKey]?.trimmingCharacters(in: .whitespacesAndNewlines),
              !raw.isEmpty else { return productionBaseURL }
        return validatedOverride(raw)
    }

    static func validatedOverride(_ raw: String) -> URL? {
        guard let components = URLComponents(string: raw),
              let scheme = components.scheme?.lowercased(),
              let host = components.host?.lowercased(), !host.isEmpty,
              components.user == nil, components.password == nil,
              components.query == nil, components.fragment == nil,
              let url = components.url else { return nil }
        switch scheme {
        case "https" where overrideHosts.contains(host): return url
        case "http" where host == "127.0.0.1": return url
        default: return nil
        }
    }
}

/// What the user typed in the prompt. Pure, so the privacy rules are
/// testable: nothing is kept after a confirmed sign-up, and no request is
/// ever prepared for an implausible email or when the test override is
/// unusable.
public struct GaleristSignUpForm: Equatable, Sendable {
    public var email: String

    public init(email: String = "") {
        self.email = email
    }

    public var emailLooksValid: Bool { GaleristOptIn.isPlausibleEmail(email) }

    public enum Submission: Equatable, Sendable {
        case send(baseURL: URL, subscription: GaleristSubscription)
        case refuse(GaleristSubscriptionOutcome)
    }

    /// The request to send, or the message to show instead of sending.
    public func submission(environment: [String: String], preferredLanguages: [String],
                           appVersion: String, prompt: GaleristSubscription.Prompt?) -> Submission {
        let subscription = GaleristSubscription(
            email: email,
            language: GaleristOptIn.primaryLanguage(from: preferredLanguages),
            appVersion: appVersion,
            prompt: prompt
        )
        if let problem = subscription.localProblem { return .refuse(problem) }
        // A test override that is not usable: never fall back to production.
        guard let base = GaleristOptIn.baseURL(environment: environment) else { return .refuse(.invalidRequest) }
        return .send(baseURL: base, subscription: subscription)
    }

    /// After a confirmed sign-up nothing typed is kept.
    public mutating func reset() {
        self = GaleristSignUpForm()
    }
}

/// The JSON body of `POST /api/lumenkeys/subscribe` (contract `lk-optin-v2`).
/// The v2 text covers both Lumerist news and the developer's other tools,
/// so `lumenkeysUpdates` and `galeristNews` are always sent as `true`.
/// Optional fields are omitted when nil.
public struct GaleristSubscription: Encodable, Equatable, Sendable {
    /// Which prompt the sign-up came from: the scheduled prompt's number, or
    /// the button in About & Support. Sent as `1`, `2`, `3` or `"about"`.
    public enum Prompt: Hashable, Sendable, Encodable {
        case scheduled(Int)
        case about

        public func encode(to encoder: Encoder) throws {
            var container = encoder.singleValueContainer()
            switch self {
            case .scheduled(let number): try container.encode(number)
            case .about: try container.encode("about")
            }
        }

        var isValid: Bool {
            switch self {
            case .scheduled(let number): (1...StayInTouchSchedule.maximumPrompts).contains(number)
            case .about: true
            }
        }
    }

    public var email: String
    public var language: String?
    public var appVersion: String?
    public var prompt: Prompt?
    public var consentVersion: String

    /// Always true in v2: the text the user agreed to names Lumerist news.
    public var lumenkeysUpdates: Bool { true }
    /// Always true in v2: the text names the developer's other photography tools.
    public var galeristNews: Bool { true }

    enum CodingKeys: String, CodingKey {
        case email
        case lumenkeysUpdates
        case galeristNews
        case language
        case consentVersion
        case appVersion
        case prompt
    }

    /// Trims the email; drops a language that is not a two-letter lowercase
    /// code, an app version that is empty or longer than 32 characters, and
    /// a prompt number outside 1...3.
    public init(
        email: String,
        language: String? = nil,
        appVersion: String? = nil,
        prompt: Prompt? = nil,
        consentVersion: String = GaleristOptIn.consentVersion
    ) {
        self.email = email.trimmingCharacters(in: .whitespacesAndNewlines)
        self.language = language.flatMap { GaleristOptIn.isLanguageCode($0) ? $0 : nil }
        self.appVersion = appVersion.flatMap { (1...32).contains($0.count) ? $0 : nil }
        self.prompt = prompt.flatMap { $0.isValid ? $0 : nil }
        self.consentVersion = consentVersion
    }

    public func encode(to encoder: Encoder) throws {
        var container = encoder.container(keyedBy: CodingKeys.self)
        try container.encode(email, forKey: .email)
        try container.encode(lumenkeysUpdates, forKey: .lumenkeysUpdates)
        try container.encode(galeristNews, forKey: .galeristNews)
        try container.encodeIfPresent(language, forKey: .language)
        try container.encode(consentVersion, forKey: .consentVersion)
        try container.encodeIfPresent(appVersion, forKey: .appVersion)
        try container.encodeIfPresent(prompt, forKey: .prompt)
    }

    /// The local reason the server would refuse it, if any.
    public var localProblem: GaleristSubscriptionOutcome? {
        GaleristOptIn.isPlausibleEmail(email) ? nil : .invalidEmail
    }

    public func encoded() throws -> Data {
        let encoder = JSONEncoder()
        encoder.outputFormatting = [.sortedKeys, .withoutEscapingSlashes]
        return try encoder.encode(self)
    }
}

/// What the server (or the network) answered, with the message the app shows.
public enum GaleristSubscriptionOutcome: Equatable, Sendable {
    case subscribed
    case invalidEmail
    /// The server's `no_consent`. The v2 body always carries both consents,
    /// so this only means the app and the server disagree.
    case noConsent
    case unknownConsentVersion
    case invalidRequest
    case rateLimited
    case unreachable

    public var message: String {
        switch self {
        case .subscribed: GaleristOptIn.Copy.success
        case .invalidEmail: "That email address doesn't look right."
        case .noConsent, .invalidRequest: "Something went wrong. Please try again later."
        case .unknownConsentVersion: "Please update Lumerist and try again."
        case .rateLimited: "Too many attempts. Please try again later."
        case .unreachable: "Couldn't connect. Check your connection and try again."
        }
    }

    /// Maps an HTTP answer. Success needs a 2xx with `{"ok":true}`; anything
    /// that does not confirm the sign-up is never reported as one.
    public init(statusCode: Int, body: Data) {
        struct Reply: Decodable {
            let ok: Bool?
            let error: String?
        }
        let reply = try? JSONDecoder().decode(Reply.self, from: body)
        switch statusCode {
        case 200..<300:
            if reply?.ok == true {
                self = .subscribed
            } else {
                self = reply?.error.flatMap { Self(errorCode: $0) } ?? .unreachable
            }
        case 429:
            self = .rateLimited
        case 400..<500:
            self = reply?.error.flatMap { Self(errorCode: $0) } ?? .invalidRequest
        default:
            // 5xx, and anything unexpected (1xx, 3xx).
            self = .unreachable
        }
    }

    /// The `error` codes of the contract.
    init?(errorCode: String) {
        switch errorCode {
        case "invalid_email": self = .invalidEmail
        case "no_consent": self = .noConsent
        case "unknown_consent_version": self = .unknownConsentVersion
        case "invalid_request": self = .invalidRequest
        case "rate_limited": self = .rateLimited
        case "server_error": self = .unreachable
        default: return nil
        }
    }
}

/// Sends one sign-up to Galerist. The transport is injectable so tests never
/// touch the network.
public struct GaleristSubscriptionClient: Sendable {
    public typealias Transport = @Sendable (URLRequest) async throws -> (Data, URLResponse)

    public static let endpointPath = "api/lumenkeys/subscribe"
    public static let timeout: TimeInterval = 15

    public let baseURL: URL
    public let appVersion: String
    private let transport: Transport

    public init(baseURL: URL, appVersion: String, transport: Transport? = nil) {
        self.baseURL = baseURL
        self.appVersion = appVersion
        self.transport = transport ?? { request in try await Self.session.data(for: request) }
    }

    public var endpoint: URL { baseURL.appending(path: Self.endpointPath) }

    /// `Lumerist/<version>`, spaces replaced so the header stays one token.
    public var userAgent: String {
        let version = appVersion.split(whereSeparator: \.isWhitespace).joined(separator: "-")
        return "Lumerist/\(version.isEmpty ? "unknown" : version)"
    }

    public func urlRequest(for subscription: GaleristSubscription) throws -> URLRequest {
        var request = URLRequest(url: endpoint, cachePolicy: .reloadIgnoringLocalAndRemoteCacheData,
                                 timeoutInterval: Self.timeout)
        request.httpMethod = "POST"
        request.setValue("application/json", forHTTPHeaderField: "Content-Type")
        request.setValue("application/json", forHTTPHeaderField: "Accept")
        request.setValue(userAgent, forHTTPHeaderField: "User-Agent")
        request.httpShouldHandleCookies = false
        request.httpBody = try subscription.encoded()
        return request
    }

    /// Checks locally first (no request for an implausible email), then posts
    /// once. Every failure maps to one of the contract's messages.
    public func subscribe(_ subscription: GaleristSubscription) async -> GaleristSubscriptionOutcome {
        if let problem = subscription.localProblem { return problem }
        guard let request = try? urlRequest(for: subscription) else { return .invalidRequest }
        do {
            let (data, response) = try await transport(request)
            guard let http = response as? HTTPURLResponse else { return .unreachable }
            return GaleristSubscriptionOutcome(statusCode: http.statusCode, body: data)
        } catch {
            return .unreachable
        }
    }

    /// Ephemeral: no cookies, cache or credentials kept, and no redirects
    /// followed. Created on first use.
    private static let session: URLSession = {
        let configuration = URLSessionConfiguration.ephemeral
        configuration.httpCookieStorage = nil
        configuration.httpShouldSetCookies = false
        configuration.urlCache = nil
        configuration.urlCredentialStorage = nil
        configuration.timeoutIntervalForRequest = timeout
        configuration.timeoutIntervalForResource = timeout
        return URLSession(configuration: configuration, delegate: RefuseRedirects(), delegateQueue: nil)
    }()
}

/// A redirect would resend the POST, email included, to whatever host the
/// `Location` names. Refusing it hands the 3xx back, which maps to
/// `.unreachable`: the email only ever goes to the host the app chose.
final class RefuseRedirects: NSObject, URLSessionTaskDelegate, Sendable {
    func urlSession(_ session: URLSession, task: URLSessionTask,
                    willPerformHTTPRedirection response: HTTPURLResponse,
                    newRequest request: URLRequest) async -> URLRequest? {
        nil
    }
}
