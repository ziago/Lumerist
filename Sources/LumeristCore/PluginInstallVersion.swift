import Foundation

/// A `major.minor.patch` version, as in the top-level `VERSION` file, the
/// app's `CFBundleShortVersionString`, the plug-in's `Info.lua` VERSION table,
/// and GitHub release tags (`v0.9.0`, `v1.0.0-beta.2`).
public struct SemanticVersion: Comparable, Hashable, Sendable, CustomStringConvertible {
    public let major: Int
    public let minor: Int
    public let patch: Int
    /// Text after `-` (e.g. "beta.2"); a version with one sorts before the same
    /// version without.
    public let prerelease: String?

    public init(major: Int, minor: Int, patch: Int, prerelease: String? = nil) {
        self.major = major
        self.minor = minor
        self.patch = patch
        self.prerelease = prerelease?.isEmpty == true ? nil : prerelease
    }

    /// Accepts "0.9.0", "v0.9.0", "1.2", "1.0.0-beta.1" and ignores build
    /// metadata ("+42"). Returns nil for anything else.
    public init?(_ text: String) {
        var core = text.trimmingCharacters(in: .whitespacesAndNewlines)
        if core.hasPrefix("v") || core.hasPrefix("V") { core.removeFirst() }
        if let plus = core.firstIndex(of: "+") { core = String(core[..<plus]) }
        var prerelease: String?
        if let dash = core.firstIndex(of: "-") {
            prerelease = String(core[core.index(after: dash)...])
            core = String(core[..<dash])
        }
        let parts = core.split(separator: ".", omittingEmptySubsequences: false)
        guard (1...3).contains(parts.count) else { return nil }
        var numbers: [Int] = []
        for part in parts {
            guard !part.isEmpty, part.allSatisfy(\.isASCII), let value = Int(part), value >= 0 else { return nil }
            numbers.append(value)
        }
        while numbers.count < 3 { numbers.append(0) }
        self.init(major: numbers[0], minor: numbers[1], patch: numbers[2], prerelease: prerelease)
    }

    public var description: String {
        let base = "\(major).\(minor).\(patch)"
        return prerelease.map { "\(base)-\($0)" } ?? base
    }

    public static func < (lhs: SemanticVersion, rhs: SemanticVersion) -> Bool {
        if lhs.major != rhs.major { return lhs.major < rhs.major }
        if lhs.minor != rhs.minor { return lhs.minor < rhs.minor }
        if lhs.patch != rhs.patch { return lhs.patch < rhs.patch }
        switch (lhs.prerelease, rhs.prerelease) {
        case (nil, nil), (nil, _?): return false
        case (_?, nil): return true
        case (let left?, let right?): return left.compare(right, options: .numeric) == .orderedAscending
        }
    }

    /// The highest version among release tags (unparsable tags are skipped).
    public static func newest(in tags: [String]) -> (version: SemanticVersion, tag: String)? {
        tags.compactMap { tag in SemanticVersion(tag).map { (version: $0, tag: tag) } }
            .max { $0.version < $1.version }
    }

    /// The version in a Lightroom plug-in's `Info.lua`, read from its
    /// `VERSION = { major = …, minor = …, revision = … }` table (key order free).
    public static func fromPluginInfo(_ infoLua: String) -> SemanticVersion? {
        guard let start = infoLua.range(of: "VERSION"),
              let open = infoLua[start.upperBound...].firstIndex(of: "{"),
              let close = infoLua[open...].firstIndex(of: "}") else { return nil }
        let table = String(infoLua[infoLua.index(after: open)..<close])
        func number(_ key: String) -> Int? {
            guard let keyRange = table.range(of: "\\b\(key)\\s*=\\s*[0-9]+", options: .regularExpression) else { return nil }
            let digits = table[keyRange].drop { !$0.isNumber }
            return Int(digits)
        }
        guard let major = number("major"), let minor = number("minor") else { return nil }
        return SemanticVersion(major: major, minor: minor, patch: number("revision") ?? 0)
    }
}
