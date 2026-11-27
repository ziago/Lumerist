import Foundation

/// Which addresses the update check opens and the updater downloads from.
/// Shared with Windows: Shared/fixtures/updates/urls.json.
public enum UpdateURLRule {
    /// The only hosts a download may touch, on its first URL and on every
    /// redirect: GitHub's API and site, and the two hosts it serves release
    /// files from.
    public static let downloadHosts: Set<String> = [
        "api.github.com", "github.com", "objects.githubusercontent.com", "release-assets.githubusercontent.com",
    ]

    /// This project's releases; also the fallback when a release's own page isn't trusted.
    public static let releasesPage = URL(string: "https://github.com/ziago/Lumerist/releases")!

    /// Every asset of a release is served from here, under its tag.
    public static let downloadPrefix = "https://github.com/ziago/Lumerist/releases/download/"

    /// A step of a download, the first URL or a redirect: `https` to one of
    /// `downloadHosts` (any case), on the default port, without user info or
    /// a `%` in the host. A `file:` URL with no host counts only when
    /// `allowFileURLs` (debug builds with `LUMERIST_UPDATE_FEED_URL`); a
    /// redirect from https is never allowed to reach one.
    public static func isTrustedHop(_ url: URL, allowFileURLs: Bool = false) -> Bool {
        guard let parts = URLComponents(url: url, resolvingAgainstBaseURL: false),
              let scheme = parts.scheme?.lowercased() else { return false }
        if scheme == "file" { return allowFileURLs && isLocalFile(parts) }
        guard scheme == "https", let host = parts.percentEncodedHost, !host.contains("%"),
              downloadHosts.contains(host.lowercased()) else { return false }
        return (parts.port == nil || parts.port == 443) && parts.user == nil && parts.password == nil
    }

    /// The `browser_download_url` of an asset: exactly
    /// `https://github.com/ziago/Lumerist/releases/download/<tag>/<name>`, as
    /// text, for the asset's own name and the release's own tag. With
    /// `allowFileURLs`, a local `file:` URL as well.
    public static func isTrustedAssetURL(_ text: String, tag: String, name: String, allowFileURLs: Bool = false) -> Bool {
        if text == downloadPrefix + tag + "/" + name { return true }
        guard allowFileURLs, let url = URL(string: text),
              let parts = URLComponents(url: url, resolvingAgainstBaseURL: false),
              parts.scheme?.lowercased() == "file" else { return false }
        return isLocalFile(parts)
    }

    /// Only a https page of this project's releases on github.com is opened,
    /// whatever the API answered. The path is compared as the browser will
    /// request it: an escaped character (%2F, %2E…) or a `..` step is
    /// refused, and so are another port than 443 and user info.
    public static func isTrustedReleasePage(_ url: URL) -> Bool {
        guard let parts = URLComponents(url: url, resolvingAgainstBaseURL: false) else { return false }
        let path = parts.percentEncodedPath
        return parts.scheme == "https"
            && parts.host == "github.com"
            && (parts.port == nil || parts.port == 443)
            && parts.user == nil && parts.password == nil
            && !path.contains("%")
            && !path.split(separator: "/").contains("..")
            && path.hasPrefix(releasesPage.path + "/")
    }

    /// `file:///absolute/path`: no host, a path.
    private static func isLocalFile(_ parts: URLComponents) -> Bool {
        (parts.percentEncodedHost ?? "").isEmpty && parts.path.hasPrefix("/") && parts.user == nil
    }
}
