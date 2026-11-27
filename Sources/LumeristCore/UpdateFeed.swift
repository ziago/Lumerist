import Foundation

// The updater's choice of release and files. Shared with Windows: the cases
// in Shared/fixtures/updates/releases.json pin it down (format:
// Shared/fixtures/README.md, "Update cases").

/// One release of GitHub's releases API (`/repos/ziago/Lumerist/releases`):
/// only the fields the update check and the updater read.
public struct UpdateFeedRelease: Decodable, Equatable, Sendable {
    public let tagName: String
    /// Kept as written: it is opened only if `UpdateURLRule.isTrustedReleasePage` accepts it.
    public let htmlURL: String
    public let draft: Bool
    public let prerelease: Bool
    /// The release notes (Markdown), made from the version's CHANGELOG section.
    public let body: String
    /// Assets with a name, a download URL and a size; others are left out.
    public let assets: [UpdateFeedAsset]

    enum CodingKeys: String, CodingKey {
        case tagName = "tag_name"
        case htmlURL = "html_url"
        case draft, prerelease, body, assets
    }

    public init(tagName: String, htmlURL: String, draft: Bool = false, prerelease: Bool = false,
                body: String = "", assets: [UpdateFeedAsset] = []) {
        self.tagName = tagName
        self.htmlURL = htmlURL
        self.draft = draft
        self.prerelease = prerelease
        self.body = body
        self.assets = assets
    }

    /// `tag_name`, `html_url` (strings) and `draft` (a boolean) are required, as
    /// before the updater: without them the whole answer is unreadable.
    /// `prerelease`, `body` and `assets` may be missing or null.
    public init(from decoder: Decoder) throws {
        let container = try decoder.container(keyedBy: CodingKeys.self)
        tagName = try container.decode(String.self, forKey: .tagName)
        htmlURL = try container.decode(String.self, forKey: .htmlURL)
        draft = try container.decode(Bool.self, forKey: .draft)
        prerelease = (try? container.decodeIfPresent(Bool.self, forKey: .prerelease)) ?? false
        body = (try? container.decodeIfPresent(String.self, forKey: .body)) ?? ""
        let lenient = (try? container.decodeIfPresent([LenientAsset].self, forKey: .assets)) ?? []
        assets = lenient.compactMap(\.value)
    }

    /// The API's answer: a JSON array of releases.
    public static func decodeList(_ data: Data) throws -> [UpdateFeedRelease] {
        try JSONDecoder().decode([UpdateFeedRelease].self, from: data)
    }
}

/// One file of a release, as the API lists it.
public struct UpdateFeedAsset: Decodable, Equatable, Sendable {
    public let name: String
    /// `browser_download_url`, kept as written: compared as text (UpdateURLRule).
    public let downloadURL: String
    public let size: Int
    /// `"sha256:<hex>"` on assets uploaded since 2025; null or missing before.
    public let digest: String?
    /// `"uploaded"`, or `"open"` while an upload is unfinished.
    public let state: String?

    enum CodingKeys: String, CodingKey {
        case name
        case downloadURL = "browser_download_url"
        case size, digest, state
    }

    public init(name: String, downloadURL: String, size: Int, digest: String? = nil, state: String? = "uploaded") {
        self.name = name
        self.downloadURL = downloadURL
        self.size = size
        self.digest = digest
        self.state = state
    }

    public init(from decoder: Decoder) throws {
        let container = try decoder.container(keyedBy: CodingKeys.self)
        name = try container.decode(String.self, forKey: .name)
        downloadURL = try container.decode(String.self, forKey: .downloadURL)
        size = try container.decode(Int.self, forKey: .size)
        digest = try? container.decodeIfPresent(String.self, forKey: .digest)
        state = try? container.decodeIfPresent(String.self, forKey: .state)
    }
}

/// An asset that can't be read is left out instead of failing the whole answer.
private struct LenientAsset: Decodable {
    let value: UpdateFeedAsset?
    init(from decoder: Decoder) throws { value = try? UpdateFeedAsset(from: decoder) }
}

/// Which installer a system downloads.
public enum UpdatePlatform: String, CaseIterable, Sendable {
    case mac
    case windowsX64 = "windows-x64"
    case windowsArm64 = "windows-arm64"

    /// `Lumerist-<v>.dmg` on the Mac, `Lumerist-<v>-windows-<arch>-setup.exe` on Windows.
    public func installerName(version: String) -> String {
        switch self {
        case .mac: "Lumerist-\(version).dmg"
        case .windowsX64: "Lumerist-\(version)-windows-x64-setup.exe"
        case .windowsArm64: "Lumerist-\(version)-windows-arm64-setup.exe"
        }
    }
}

/// A file the updater may download: its name and URL come from the API's
/// `assets` only (never from release text), checked by `UpdateURLRule`.
public struct UpdateAsset: Equatable, Sendable {
    public let name: String
    public let url: URL
    /// The size the API declares; the download must have exactly this many bytes.
    public let size: Int
    /// The API's `digest` as lowercase hex, when it is a SHA-256.
    public let sha256: String?

    public init(name: String, url: URL, size: Int, sha256: String?) {
        self.name = name
        self.url = url
        self.size = size
        self.sha256 = sha256
    }
}

/// What one system needs to install a release.
public struct UpdateAssets: Equatable, Sendable {
    /// The DMG or the setup program.
    public let installer: UpdateAsset
    /// `<installer>.sha256`, when the release has it: it must agree too.
    public let checksum: UpdateAsset?
    /// `SHA256SUMS`, the signed list of every file's SHA-256.
    public let sums: UpdateAsset
    /// `SHA256SUMS.sig`.
    public let signature: UpdateAsset
}

/// A release newer than the running app.
public struct AvailableUpdate: Equatable, Sendable {
    public let version: SemanticVersion
    public let tag: String
    /// Its release page, or the releases page when the API's isn't trusted.
    public let page: URL
    public let release: UpdateFeedRelease

    /// The "What's new" lines, as plain text (`UpdateHighlights`).
    public var highlights: [String] { UpdateHighlights.parse(release.body) }

    /// The files for a system, or nil when this release can't be installed
    /// automatically there.
    public func assets(for platform: UpdatePlatform, allowFileURLs: Bool = false) -> UpdateAssets? {
        UpdateChoice.assets(of: release, version: version, platform: platform, allowFileURLs: allowFileURLs)
    }
}

/// The newest release, its files, and the version rules.
public enum UpdateChoice {
    public static let sumsName = "SHA256SUMS"
    public static let signatureName = "SHA256SUMS.sig"

    /// The largest file accepted for each role, in bytes.
    public enum Limit {
        public static let installer = 300 * 1024 * 1024
        public static let checksum = 1024
        public static let sums = 64 * 1024
        public static let signature = 1024
    }

    /// The longest tag considered.
    static let maximumTagLength = 64

    /// `MAJOR.MINOR.PATCH` with an optional `-prerelease` (ASCII letters,
    /// digits, `-`, dot-separated); no leading zeros, no build metadata.
    private static let semver = #"(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(-[0-9A-Za-z-]+(\.[0-9A-Za-z-]+)*)?"#
    private static let tagPattern = try! NSRegularExpression(pattern: #"\Av"# + semver + #"\z"#)
    private static let versionPattern = try! NSRegularExpression(pattern: #"\A"# + semver + #"\z"#)

    /// The version of a release tag of the form `v<semver>`, e.g. "v0.9.1" or
    /// "v1.0.0-beta.2". Anything else (no `v`, "V", two parts, build metadata,
    /// leading zeros, spaces) is not a release the updater considers.
    public static func version(fromTag tag: String) -> SemanticVersion? {
        guard tag.utf16.count <= maximumTagLength, matches(tagPattern, tag) else { return nil }
        return SemanticVersion(tag)
    }

    /// The running app's version (`CFBundleShortVersionString`): `<semver>`
    /// without `v`. A development build has none, and is never offered an update.
    public static func version(fromCurrent text: String) -> SemanticVersion? {
        guard text.utf16.count <= maximumTagLength, matches(versionPattern, text) else { return nil }
        return SemanticVersion(text)
    }

    /// The newest non-draft release with a valid tag (pre-releases included);
    /// the first one in the API's order wins a tie.
    public static func newest(in releases: [UpdateFeedRelease]) -> (release: UpdateFeedRelease, version: SemanticVersion)? {
        var best: (release: UpdateFeedRelease, version: SemanticVersion)?
        for release in releases where !release.draft {
            guard let version = version(fromTag: release.tagName) else { continue }
            if best == nil || best!.version < version { best = (release, version) }
        }
        return best
    }

    /// The newest release when it is strictly newer than `current`; never a
    /// downgrade, and nothing for a development build.
    public static func update(in releases: [UpdateFeedRelease], current: String) -> AvailableUpdate? {
        guard let running = version(fromCurrent: current), let newest = newest(in: releases),
              running < newest.version else { return nil }
        return AvailableUpdate(version: newest.version, tag: newest.release.tagName,
                               page: releasePage(newest.release.htmlURL), release: newest.release)
    }

    /// The API's page when it is one of this project's release pages, else the releases page.
    public static func releasePage(_ text: String) -> URL {
        if let url = URL(string: text), UpdateURLRule.isTrustedReleasePage(url) { return url }
        return UpdateURLRule.releasesPage
    }

    /// The installer, its `.sha256`, `SHA256SUMS` and `SHA256SUMS.sig`, each
    /// found by its exact name among the assets. nil when the installer, the
    /// list or its signature is missing, or when any of the four (the
    /// `.sha256` only when present) has an untrusted URL, a size of 0 or over
    /// its limit, a malformed SHA-256 digest, or a name listed twice.
    public static func assets(of release: UpdateFeedRelease, version: SemanticVersion, platform: UpdatePlatform,
                              allowFileURLs: Bool = false) -> UpdateAssets? {
        let installerName = platform.installerName(version: version.description)
        func pick(_ name: String, limit: Int) -> Pick {
            let found = release.assets.filter { $0.name == name && ($0.state == nil || $0.state == "uploaded") }
            guard !found.isEmpty else { return .missing }
            guard found.count == 1, let asset = found.first,
                  asset.size > 0, asset.size <= limit,
                  UpdateURLRule.isTrustedAssetURL(asset.downloadURL, tag: release.tagName, name: name, allowFileURLs: allowFileURLs),
                  let url = URL(string: asset.downloadURL),
                  case .valid(let sha256) = digest(asset.digest) else { return .invalid }
            return .found(UpdateAsset(name: name, url: url, size: asset.size, sha256: sha256))
        }
        guard case .found(let installer) = pick(installerName, limit: Limit.installer),
              case .found(let sums) = pick(sumsName, limit: Limit.sums),
              case .found(let signature) = pick(signatureName, limit: Limit.signature) else { return nil }
        let checksum: UpdateAsset?
        switch pick(installerName + ".sha256", limit: Limit.checksum) {
        case .found(let asset): checksum = asset
        case .missing: checksum = nil
        case .invalid: return nil
        }
        return UpdateAssets(installer: installer, checksum: checksum, sums: sums, signature: signature)
    }

    private enum Pick {
        case found(UpdateAsset)
        case missing
        case invalid
    }

    private enum Digest {
        case valid(String?)
        case invalid
    }

    /// `sha256:<64 hex>` gives the lowercase hex; none, or another algorithm,
    /// gives nil (nothing to compare); `sha256:` with anything else is invalid.
    private static func digest(_ text: String?) -> Digest {
        guard let text else { return .valid(nil) }
        let prefix = "sha256:"
        guard text.hasPrefix(prefix) else { return .valid(nil) }
        let hex = String(text.dropFirst(prefix.count))
        return UpdateChecksums.isHex64(hex) ? .valid(hex.lowercased()) : .invalid
    }

    static func matches(_ pattern: NSRegularExpression, _ text: String) -> Bool {
        pattern.firstMatch(in: text, range: NSRange(text.startIndex..., in: text)) != nil
    }
}
