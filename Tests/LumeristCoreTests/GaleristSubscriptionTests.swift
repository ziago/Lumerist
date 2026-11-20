import CryptoKit
import Foundation
import Testing
@testable import LumeristCore

/// The optional "Stay in touch" sign-up (contract `lk-optin-v2`). No test here
/// touches the network, except the live one that runs only when
/// `LUMERIST_E2E_BASE_URL` is set.
struct GaleristSubscriptionTests {
    // MARK: Texts: stored by the server as proof of consent, so exact.

    @Test func consentTextsMatchTheContractCharacterForCharacter() {
        #expect(GaleristOptIn.consentVersion == "lk-optin-v2")
        typealias Copy = GaleristOptIn.Copy
        #expect(Copy.title == "Stay in touch")
        #expect(Copy.message == "Leave your email to hear about new versions of Lumerist and about other photography tools from its developer.")
        #expect(Copy.emailPlaceholder == "you@example.com")
        #expect(Copy.notice == "Optional and free. No spam: every email has a one-click unsubscribe.")
        #expect(Copy.privacyLink == "Privacy")
        #expect(Copy.button == "Keep me posted")
        #expect(Copy.notNow == "Not now")
        #expect(Copy.dontAskAgain == "Don't ask me again")
        #expect(Copy.success == "Thanks, you're on the list.")
        #expect(Copy.aboutDetail == "News about Lumerist and other photography tools from its developer, by email.")
        #expect(Copy.aboutButton == "Leave your email…")
        #expect(Copy.aboutSubscribed == "You're on the list")
        #expect(Copy.credit == "Lumerist is made by the creator of Galerist, online galleries where photographers and videographers deliver their work to clients.")
    }

    private func sha256(_ texts: [String]) -> String {
        SHA256.hash(data: Data(texts.joined(separator: "\n").utf8)).map { String(format: "%02x", $0) }.joined()
    }

    /// Drift guard: the Galerist server runs the same check, so the texts the
    /// app shows and the proof of consent it stores can never drift apart.
    /// SHA-256 of `message` and `notice` joined with "\n" (UTF-8, no trailing newline).
    @Test func consentTextsMatchTheServerDriftGuard() {
        typealias Copy = GaleristOptIn.Copy
        #expect(GaleristOptIn.consentVersion == "lk-optin-v2")
        #expect(sha256([Copy.message, Copy.notice])
                == "4bd6382380b390149de25bed81c1639306528d76b97e6c4161cb99b6062f50b7")
    }

    /// v1 is no longer shown or sent, but the server still holds its map.
    @Test func previousConsentTextsStillMatchTheServerDriftGuard() {
        typealias V1 = GaleristOptIn.ConsentV1
        #expect(V1.consentVersion == "lk-optin-v1")
        #expect(sha256([V1.lumenkeysUpdates, V1.galeristNews, V1.clientsQuestion, V1.notice])
                == "f110ca666f07ef24dc6cd39bf402c1df4207cfc2df1cb758822c9fc2d2d936bc")
    }

    @Test func consentTextsUseStraightASCIIPunctuation() {
        typealias Copy = GaleristOptIn.Copy
        for text in [Copy.title, Copy.message, Copy.notice, Copy.button, Copy.notNow, Copy.dontAskAgain,
                     Copy.success, Copy.aboutDetail, Copy.aboutSubscribed, Copy.credit] {
            let ascii = text.unicodeScalars.allSatisfy { $0.isASCII }
            #expect(ascii, "\(text)")
        }
    }

    @Test func linksPointAtGalerist() {
        #expect(GaleristOptIn.productionBaseURL.absoluteString == "https://galerist.photo")
        #expect(GaleristOptIn.privacyURL.absoluteString == "https://galerist.photo/en/legal/privacy#lumenkeys")
        #expect(GaleristOptIn.subscribedAtDefaultsKey == "lumerist.galerist.subscribed-at")
        #expect(GaleristOptIn.TrackedLink.onboarding.url.absoluteString == "https://galerist.photo/c/lk-onboarding")
        #expect(GaleristOptIn.TrackedLink.settings.url.absoluteString == "https://galerist.photo/c/lk-settings")
        #expect(GaleristOptIn.TrackedLink.menu.url.absoluteString == "https://galerist.photo/c/lk-menu")
        #expect(GaleristOptIn.TrackedLink.readme.url.absoluteString == "https://galerist.photo/c/lk-readme")
        #expect(GaleristOptIn.TrackedLink.release.url.absoluteString == "https://galerist.photo/c/lk-release")
    }

    // MARK: Encoding

    private func json(_ subscription: GaleristSubscription) throws -> [String: Any] {
        let object = try JSONSerialization.jsonObject(with: subscription.encoded())
        return try #require(object as? [String: Any])
    }

    @Test func encodesEveryFieldWithTheContractKeys() throws {
        let subscription = GaleristSubscription(email: "name@example.com", language: "it", appVersion: "0.9.0",
                                                prompt: .scheduled(2))
        let body = try json(subscription)
        #expect(Set(body.keys) == ["email", "lumenkeysUpdates", "galeristNews", "language", "consentVersion",
                                   "appVersion", "prompt"])
        #expect(body["email"] as? String == "name@example.com")
        #expect(body["lumenkeysUpdates"] as? Bool == true)
        #expect(body["galeristNews"] as? Bool == true)
        #expect(body["language"] as? String == "it")
        #expect(body["consentVersion"] as? String == "lk-optin-v2")
        #expect(body["appVersion"] as? String == "0.9.0")
        #expect(body["prompt"] as? Int == 2)
        #expect(body["shootsForClients"] == nil)
        // Booleans are JSON booleans, not 0/1; the prompt number is a JSON number.
        let text = try #require(String(data: subscription.encoded(), encoding: .utf8))
        #expect(text.contains("\"lumenkeysUpdates\":true"))
        #expect(text.contains("\"galeristNews\":true"))
        #expect(text.contains("\"prompt\":2"))
        #expect(try subscription.encoded().count < 2_048)
    }

    @Test func v2AlwaysSendsBothConsents() throws {
        // The v2 text covers Lumerist news and the developer's other tools:
        // there is no way to send one without the other.
        let subscription = GaleristSubscription(email: "a@b.co")
        #expect(subscription.lumenkeysUpdates && subscription.galeristNews)
        let body = try json(subscription)
        #expect(body["lumenkeysUpdates"] as? Bool == true)
        #expect(body["galeristNews"] as? Bool == true)
    }

    @Test func encodesThePromptAsANumberOrAbout() throws {
        for number in 1...3 {
            let body = try json(GaleristSubscription(email: "a@b.co", prompt: .scheduled(number)))
            #expect(body["prompt"] as? Int == number)
        }
        let text = try #require(String(data: GaleristSubscription(email: "a@b.co", prompt: .about).encoded(),
                                       encoding: .utf8))
        #expect(text.contains("\"prompt\":\"about\""))
        // Outside the schedule: left out rather than sent wrong.
        for number in [0, 4, -1] {
            #expect(GaleristSubscription(email: "a@b.co", prompt: .scheduled(number)).prompt == nil)
        }
    }

    @Test func omitsOptionalFieldsWhenNil() throws {
        let body = try json(GaleristSubscription(email: "a@b.co"))
        #expect(Set(body.keys) == ["email", "lumenkeysUpdates", "galeristNews", "consentVersion"])
        let text = try #require(String(data: GaleristSubscription(email: "a@b.co").encoded(), encoding: .utf8))
        #expect(!text.contains("null"))
    }

    @Test func cleansTheFieldsItSends() {
        let subscription = GaleristSubscription(
            email: "  Name@Example.com\n", language: "IT", appVersion: String(repeating: "9", count: 33))
        #expect(subscription.email == "Name@Example.com") // the server lowercases
        #expect(subscription.language == nil)
        #expect(subscription.appVersion == nil)
        #expect(GaleristSubscription(email: "a@b.co", language: "ita", appVersion: "").language == nil)
        #expect(GaleristSubscription(email: "a@b.co", appVersion: "").appVersion == nil)
        #expect(GaleristSubscription(email: "a@b.co",
                                     appVersion: String(repeating: "1", count: 32)).appVersion?.count == 32)
    }

    // MARK: Validation

    @Test func acceptsPlausibleEmails() {
        for email in ["name@example.com", "first.last+tag@sub.example.co.uk", "a@b.co", "  padded@example.org ",
                      "x_y-z@my-domain.photo", "user@xn--80ak6aa92e.xn--p1ai", "nome@dominio.it",
                      "o'brien@example.ie"] {
            #expect(GaleristOptIn.isPlausibleEmail(email), "\(email)")
        }
    }

    @Test func rejectsImplausibleEmails() {
        let tooLongLocal = String(repeating: "a", count: 65) + "@example.com"
        let longest = "a@" + String(repeating: "b", count: 60) + "." + String(repeating: "c", count: 60) + "."
            + String(repeating: "d", count: 60) + "." + String(repeating: "e", count: 63) + ".photo"
        #expect(longest.utf16.count == 254)
        #expect(GaleristOptIn.isPlausibleEmail(longest))
        let tooLong = longest + "s"
        for email in ["", "plain", "@example.com", "name@", "name@example", "name@@example.com",
                      "na me@example.com", "name@exa mple.com", "name@example.c", "name@example.123",
                      "name@-example.com", "name@example-.com", "name@example..com", ".name@example.com",
                      "name.@example.com", "na..me@example.com", "name@.example.com", "name@example.com.",
                      "a@b@c.com", "name@exam_ple.com", tooLongLocal, tooLong,
                      // ASCII only, like the server: IDNs go as "xn--…".
                      "a@münchen.de", "ñame@example.com", "İsmail@example.com", "name@exämple.com",
                      // No quoted local parts, IP literals or control characters.
                      "\"name\"@example.com", "a@[127.0.0.1]", "a@127.0.0.1", "na\u{0}me@example.com",
                      "na\u{7}me@example.com", "na(me)@example.com"] {
            #expect(!GaleristOptIn.isPlausibleEmail(email), "\(email)")
        }
        #expect(tooLong.utf16.count > 254)
    }

    @Test func reportsTheLocalProblemBeforeSending() {
        #expect(GaleristSubscription(email: "nope").localProblem == .invalidEmail)
        #expect(GaleristSubscription(email: "").localProblem == .invalidEmail)
        #expect(GaleristSubscription(email: "a@b.co").localProblem == nil)
    }

    // MARK: Language

    @Test func extractsTheTwoLetterPrimaryLanguage() {
        #expect(GaleristOptIn.primaryLanguage(from: ["it-IT", "en-US"]) == "it")
        #expect(GaleristOptIn.primaryLanguage(from: ["en"]) == "en")
        #expect(GaleristOptIn.primaryLanguage(from: ["zh-Hans-CN"]) == "zh")
        #expect(GaleristOptIn.primaryLanguage(from: ["pt_BR"]) == "pt")
        #expect(GaleristOptIn.primaryLanguage(from: ["DE-de"]) == "de")
        #expect(GaleristOptIn.primaryLanguage(from: ["fil-PH", "en"]) == nil) // only the first counts
        #expect(GaleristOptIn.primaryLanguage(from: [""]) == nil)
        #expect(GaleristOptIn.primaryLanguage(from: []) == nil)
    }

    // MARK: Base URL

    @Test func usesProductionUnlessOverridden() {
        #expect(GaleristOptIn.baseURL(environment: [:]) == GaleristOptIn.productionBaseURL)
        #expect(GaleristOptIn.baseURL(environment: ["LUMERIST_GALERIST_BASE_URL": ""]) == GaleristOptIn.productionBaseURL)
        #expect(GaleristOptIn.baseURL(environment: ["LUMERIST_GALERIST_BASE_URL": "  "]) == GaleristOptIn.productionBaseURL)
    }

    @Test func acceptsOnlyGaleristHTTPSAndLocalHTTPOverrides() {
        let key = GaleristOptIn.baseURLEnvironmentKey
        #expect(key == "LUMERIST_GALERIST_BASE_URL")
        for raw in ["https://galerist.photo", "https://galerist.photo/", "HTTPS://GALERIST.PHOTO",
                    "http://127.0.0.1:3000"] {
            #expect(GaleristOptIn.baseURL(environment: [key: raw])?.absoluteString == raw, "\(raw)")
        }
    }

    @Test func refusesOtherOverridesInsteadOfFallingBackToProduction() {
        let key = GaleristOptIn.baseURLEnvironmentKey
        for raw in ["https://other.vercel.app", "http://192.168.1.2:3000", "ftp://galerist.photo",
                    "galerist.photo", "https://", "https://user:pass@galerist.photo",
                    "https://galerist.photo?x=1", "https://galerist.photo#x", "not a url",
                    // The form says the email goes to Galerist: no other host, ever.
                    "https://example.com", "https://galerist.photo.example.com", "https://evil-galerist.photo",
                    "http://localhost:3000", "http://galerist.photo"] {
            #expect(GaleristOptIn.baseURL(environment: [key: raw]) == nil, "\(raw)")
        }
    }

    // MARK: Form (the privacy rules of the app's sign-up)

    @Test func formStartsEmpty() {
        let form = GaleristSignUpForm()
        #expect(form.email.isEmpty)
        #expect(!form.emailLooksValid)
    }

    @Test func formPreparesTheRequestOnlyForAPlausibleEmail() {
        var form = GaleristSignUpForm(email: " name@example.com ")
        let expected = GaleristSubscription(email: "name@example.com", language: "it", appVersion: "0.9.0",
                                            prompt: .scheduled(1))
        #expect(form.submission(environment: [:], preferredLanguages: ["it-IT"], appVersion: "0.9.0",
                                prompt: .scheduled(1))
                == .send(baseURL: GaleristOptIn.productionBaseURL, subscription: expected))
        form.email = "name@example"
        #expect(form.submission(environment: [:], preferredLanguages: [], appVersion: "0.9.0", prompt: .about)
                == .refuse(.invalidEmail))
        form.email = ""
        #expect(form.submission(environment: [:], preferredLanguages: [], appVersion: "0.9.0", prompt: nil)
                == .refuse(.invalidEmail))
    }

    @Test func formNeverPreparesARequestForAnUnusableOverride() {
        let form = GaleristSignUpForm(email: "name@example.com")
        let environment = [GaleristOptIn.baseURLEnvironmentKey: "https://example.com"]
        #expect(form.submission(environment: environment, preferredLanguages: ["en"], appVersion: "0.9.0",
                                prompt: .scheduled(1))
                == .refuse(.invalidRequest))
    }

    @Test func formForgetsEverythingAfterASignUp() {
        var form = GaleristSignUpForm(email: "name@example.com")
        form.reset()
        #expect(form == GaleristSignUpForm())
    }

    // MARK: Redirects

    @Test func redirectsAreRefusedSoTheEmailNeverChangesHost() async throws {
        let from = try #require(URL(string: "https://galerist.photo/api/lumenkeys/subscribe"))
        let to = try #require(URL(string: "https://example.com/collect"))
        let response = try #require(HTTPURLResponse(url: from, statusCode: 307, httpVersion: "HTTP/1.1",
                                                    headerFields: ["Location": to.absoluteString]))
        let task = URLSession.shared.dataTask(with: from) // never resumed
        let next = await RefuseRedirects().urlSession(.shared, task: task, willPerformHTTPRedirection: response,
                                                      newRequest: URLRequest(url: to))
        #expect(next == nil)
        #expect(GaleristSubscriptionOutcome(statusCode: 307, body: Data()) == .unreachable)
    }

    @Test func buildsTheEndpointFromTheBase() {
        for base in ["https://galerist.photo", "https://galerist.photo/", "http://127.0.0.1:3000"] {
            let client = GaleristSubscriptionClient(baseURL: URL(string: base)!, appVersion: "0.9.0")
            #expect(client.endpoint.absoluteString.hasSuffix("/api/lumenkeys/subscribe"))
            #expect(!client.endpoint.absoluteString.contains("//api"))
        }
        let client = GaleristSubscriptionClient(baseURL: GaleristOptIn.productionBaseURL, appVersion: "0.9.0")
        #expect(client.endpoint.absoluteString == "https://galerist.photo/api/lumenkeys/subscribe")
    }

    // MARK: Response → message

    private func outcome(_ status: Int, _ body: String) -> GaleristSubscriptionOutcome {
        GaleristSubscriptionOutcome(statusCode: status, body: Data(body.utf8))
    }

    @Test func mapsEveryContractResponse() {
        #expect(outcome(200, #"{"ok":true}"#) == .subscribed)
        #expect(outcome(400, #"{"ok":false,"error":"invalid_email"}"#) == .invalidEmail)
        #expect(outcome(400, #"{"ok":false,"error":"no_consent"}"#) == .noConsent)
        #expect(outcome(400, #"{"ok":false,"error":"unknown_consent_version"}"#) == .unknownConsentVersion)
        #expect(outcome(400, #"{"ok":false,"error":"invalid_request"}"#) == .invalidRequest)
        #expect(outcome(429, #"{"ok":false,"error":"rate_limited"}"#) == .rateLimited)
        #expect(outcome(500, #"{"ok":false,"error":"server_error"}"#) == .unreachable)
        #expect(outcome(502, "<html>Bad gateway</html>") == .unreachable)
        #expect(outcome(503, "") == .unreachable)
    }

    @Test func neverReportsAnUnconfirmedSignUpAsSuccess() {
        #expect(outcome(200, "") == .unreachable)
        #expect(outcome(200, "<html>captive portal</html>") == .unreachable)
        #expect(outcome(200, #"{"ok":false}"#) == .unreachable)
        #expect(outcome(200, #"{"ok":false,"error":"invalid_email"}"#) == .invalidEmail)
        #expect(outcome(201, #"{"ok":true}"#) == .subscribed)
        #expect(outcome(301, "") == .unreachable)
    }

    @Test func mapsUnexpectedClientErrors() {
        #expect(outcome(400, "") == .invalidRequest)
        #expect(outcome(400, #"{"ok":false,"error":"something_new"}"#) == .invalidRequest)
        #expect(outcome(404, "Not found") == .invalidRequest)
        #expect(outcome(413, "") == .invalidRequest)
        #expect(outcome(415, #"{"ok":false,"error":"invalid_request"}"#) == .invalidRequest)
        #expect(outcome(429, "") == .rateLimited)
    }

    @Test func messagesMatchTheContract() {
        #expect(GaleristSubscriptionOutcome.subscribed.message == "Thanks, you're on the list.")
        #expect(GaleristSubscriptionOutcome.invalidEmail.message == "That email address doesn't look right.")
        // v2 always sends both consents: "no_consent" only means a mismatch with the server.
        #expect(GaleristSubscriptionOutcome.noConsent.message == "Something went wrong. Please try again later.")
        #expect(GaleristSubscriptionOutcome.unknownConsentVersion.message == "Please update Lumerist and try again.")
        #expect(GaleristSubscriptionOutcome.invalidRequest.message == "Something went wrong. Please try again later.")
        #expect(GaleristSubscriptionOutcome.rateLimited.message == "Too many attempts. Please try again later.")
        // The prompt doesn't introduce Galerist, so no message names it.
        #expect(GaleristSubscriptionOutcome.unreachable.message == "Couldn't connect. Check your connection and try again.")
    }

    // MARK: Client (fake transport)

    private final class Recorder: @unchecked Sendable {
        private let lock = NSLock()
        private var stored: [URLRequest] = []
        var requests: [URLRequest] { lock.withLock { stored } }
        func record(_ request: URLRequest) { lock.withLock { stored.append(request) } }
    }

    private func reply(_ status: Int, _ body: String, recorder: Recorder) -> GaleristSubscriptionClient.Transport {
        { request in
            recorder.record(request)
            let response = HTTPURLResponse(url: request.url!, statusCode: status, httpVersion: "HTTP/1.1",
                                           headerFields: ["Content-Type": "application/json"])!
            return (Data(body.utf8), response)
        }
    }

    private let valid = GaleristSubscription(email: "name@example.com", language: "en", appVersion: "0.9.0",
                                             prompt: .scheduled(1))

    @Test func postsJSONWithTheContractHeaders() async throws {
        let recorder = Recorder()
        let client = GaleristSubscriptionClient(baseURL: URL(string: "https://galerist.photo")!,
                                                appVersion: "0.9.0", transport: reply(200, #"{"ok":true}"#, recorder: recorder))
        #expect(await client.subscribe(valid) == .subscribed)
        let request = try #require(recorder.requests.first)
        #expect(recorder.requests.count == 1)
        #expect(request.httpMethod == "POST")
        #expect(request.url?.absoluteString == "https://galerist.photo/api/lumenkeys/subscribe")
        #expect(request.value(forHTTPHeaderField: "Content-Type") == "application/json")
        #expect(request.value(forHTTPHeaderField: "User-Agent") == "Lumerist/0.9.0")
        #expect(request.timeoutInterval == 15)
        #expect(request.httpShouldHandleCookies == false)
        #expect(request.httpBody == (try valid.encoded()))
    }

    @Test func userAgentStaysOneToken() {
        let client = GaleristSubscriptionClient(baseURL: GaleristOptIn.productionBaseURL, appVersion: "development build")
        #expect(client.userAgent == "Lumerist/development-build")
        #expect(GaleristSubscriptionClient(baseURL: GaleristOptIn.productionBaseURL, appVersion: "").userAgent == "Lumerist/unknown")
    }

    @Test func mapsServerAnswersThroughTheClient() async {
        let cases: [(Int, String, GaleristSubscriptionOutcome)] = [
            (400, #"{"ok":false,"error":"invalid_email"}"#, .invalidEmail),
            (400, #"{"ok":false,"error":"no_consent"}"#, .noConsent),
            (400, #"{"ok":false,"error":"unknown_consent_version"}"#, .unknownConsentVersion),
            (400, #"{"ok":false,"error":"invalid_request"}"#, .invalidRequest),
            (429, #"{"ok":false,"error":"rate_limited"}"#, .rateLimited),
            (500, #"{"ok":false,"error":"server_error"}"#, .unreachable),
        ]
        for (status, body, expected) in cases {
            let client = GaleristSubscriptionClient(baseURL: GaleristOptIn.productionBaseURL, appVersion: "0.9.0",
                                                    transport: reply(status, body, recorder: Recorder()))
            #expect(await client.subscribe(valid) == expected, "\(status) \(body)")
        }
    }

    @Test func networkFailuresMeanUnreachable() async {
        let offline = GaleristSubscriptionClient(baseURL: GaleristOptIn.productionBaseURL, appVersion: "0.9.0") { _ in
            throw URLError(.notConnectedToInternet)
        }
        #expect(await offline.subscribe(valid) == .unreachable)
        let timedOut = GaleristSubscriptionClient(baseURL: GaleristOptIn.productionBaseURL, appVersion: "0.9.0") { _ in
            throw URLError(.timedOut)
        }
        #expect(await timedOut.subscribe(valid) == .unreachable)
        let notHTTP = GaleristSubscriptionClient(baseURL: GaleristOptIn.productionBaseURL, appVersion: "0.9.0") { request in
            (Data(), URLResponse(url: request.url!, mimeType: nil, expectedContentLength: 0, textEncodingName: nil))
        }
        #expect(await notHTTP.subscribe(valid) == .unreachable)
    }

    @Test func sendsNothingWhenTheFormIsNotValid() async {
        let recorder = Recorder()
        let client = GaleristSubscriptionClient(baseURL: GaleristOptIn.productionBaseURL, appVersion: "0.9.0",
                                                transport: reply(200, #"{"ok":true}"#, recorder: recorder))
        #expect(await client.subscribe(GaleristSubscription(email: "nope")) == .invalidEmail)
        #expect(await client.subscribe(GaleristSubscription(email: "  ")) == .invalidEmail)
        #expect(recorder.requests.isEmpty)
    }

    // MARK: Live (opt-in)

    /// Runs only with `LUMERIST_E2E_BASE_URL` set, e.g. a local Galerist server:
    /// `LUMERIST_E2E_BASE_URL=http://127.0.0.1:3000 swift test --filter liveServer`
    /// (another https server must first be added to `overrideHosts`, locally).
    /// Sends two requests (counts towards the 5 per 10 minutes per IP).
    /// `LUMERIST_E2E_EMAIL` overrides the sign-up address.
    @Test(.enabled(if: ProcessInfo.processInfo.environment["LUMERIST_E2E_BASE_URL"] != nil,
                   "set LUMERIST_E2E_BASE_URL to run against a live Galerist server"))
    func liveServerAcceptsASignUpAndRejectsABadDomain() async throws {
        let environment = ProcessInfo.processInfo.environment
        let raw = try #require(environment["LUMERIST_E2E_BASE_URL"])
        let base = try #require(GaleristOptIn.baseURL(environment: [GaleristOptIn.baseURLEnvironmentKey: raw]),
                                "LUMERIST_E2E_BASE_URL must be https to a Galerist host, or http://127.0.0.1")
        let client = GaleristSubscriptionClient(baseURL: base, appVersion: "0.0.0-e2e")
        let stamp = Int(Date().timeIntervalSince1970)
        let email = environment["LUMERIST_E2E_EMAIL"] ?? "lumerist-e2e+\(stamp)@galerist.photo"
        let accepted = await client.subscribe(GaleristSubscription(
            email: email, language: "en", appVersion: "0.0.0-e2e", prompt: .scheduled(1)))
        #expect(accepted == .subscribed)
        let rejected = await client.subscribe(GaleristSubscription(
            email: "lumerist-e2e@no-such-domain-\(stamp).invalid", prompt: .about))
        #expect(rejected == .invalidEmail)
    }
}
