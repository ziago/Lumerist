import Foundation
import LumeristCore

/// Every external link the app shows, in one place.
///
/// Donation accounts: set each username below once the account exists.
/// Until then the app leaves that service's button out.
enum SupportLinks {
    static let repository = URL(string: "https://github.com/ziago/Lumerist")!
    static let releases = URL(string: "https://github.com/ziago/Lumerist/releases")!
    static let discussions = URL(string: "https://github.com/ziago/Lumerist/discussions")!
    static let license = URL(string: "https://github.com/ziago/Lumerist/blob/main/LICENSE")!
    /// Includes pre-releases (the `/releases/latest` endpoint skips them).
    static let releasesAPI = URL(string: "https://api.github.com/repos/ziago/Lumerist/releases?per_page=20")!

    // MARK: Galerist (the maker's photo-delivery site). Tracked short links
    // that only count the visit: they carry no personal data. Opened in the
    // browser on click; Lumerist itself sends nothing.

    /// General → About & Support.
    static let galeristFromSettings = GaleristOptIn.TrackedLink.settings.url
    /// The quiet item in the menu bar window.
    static let galeristFromMenu = GaleristOptIn.TrackedLink.menu.url
    /// The "Privacy" link in the "Stay in touch" sheet's notice.
    static let galeristPrivacy = GaleristOptIn.privacyURL

    // MARK: Donations. nil: no account yet, so no button.

    /// Buy Me a Coffee username (buymeacoffee.com/<username>).
    static let buyMeACoffeeUsername: String? = "lumerist"
    /// Ko-fi username (ko-fi.com/<username>).
    static let koFiUsername: String? = nil
    /// PayPal.me name (paypal.me/<name>).
    static let payPalMeName: String? = nil
    /// GitHub Sponsors account (github.com/sponsors/<account>),
    /// usable once GitHub has approved the Sponsors profile.
    static let gitHubSponsorsAccount: String? = nil

    struct Donation: Identifiable {
        let id: String
        let title: String
        let detail: String
        let systemImage: String
        let url: URL?
    }

    static var donations: [Donation] {
        [
            Donation(id: "coffee", title: "Buy me a coffee", detail: "Buy Me a Coffee",
                     systemImage: "cup.and.saucer",
                     url: buyMeACoffeeUsername.flatMap { URL(string: "https://buymeacoffee.com/\($0)") }),
            Donation(id: "kofi", title: "Ko-fi", detail: "A one-off tip",
                     systemImage: "heart",
                     url: koFiUsername.flatMap { URL(string: "https://ko-fi.com/\($0)") }),
            Donation(id: "paypal", title: "PayPal", detail: "Send a pizza",
                     systemImage: "gift",
                     url: payPalMeName.flatMap { URL(string: "https://paypal.me/\($0)") }),
            Donation(id: "sponsors", title: "GitHub Sponsors", detail: "Support on GitHub",
                     systemImage: "star",
                     url: gitHubSponsorsAccount.flatMap { URL(string: "https://github.com/sponsors/\($0)") }),
        ]
    }

    /// The buttons the app shows: only the services with an account.
    static var availableDonations: [Donation] { donations.filter { $0.url != nil } }

    /// The running app's version (from Info.plist, stamped from VERSION at build time).
    static var appVersion: String {
        Bundle.main.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String ?? "development build"
    }

    // MARK: Report a problem

    /// GitHub's bug form with the version, macOS, the kind of Mac and the
    /// keyboard layout filled in. Built on click, so it reads the layout in use;
    /// opened in the browser, where the person sees everything before sending.
    @MainActor static var bugReport: URL {
        let os = ProcessInfo.processInfo.operatingSystemVersion
        let system = BugReport.macOSDescription(
            major: os.majorVersion, minor: os.minorVersion, patch: os.patchVersion,
            build: BugReport.macOSBuild(fromVersionString: ProcessInfo.processInfo.operatingSystemVersionString))
        return BugReport.url(for: .init(lumerist: appVersion, system: system,
                                        computer: BugReport.computer(isMac: true, isArm: isAppleSilicon),
                                        keyboard: KeyboardLayout.currentName ?? ""))
    }

    /// The hardware, not the process: also true when running under Rosetta.
    private static var isAppleSilicon: Bool {
        var value: Int32 = 0
        var size = MemoryLayout<Int32>.size
        return sysctlbyname("hw.optional.arm64", &value, &size, nil, 0) == 0 && value == 1
    }
}
