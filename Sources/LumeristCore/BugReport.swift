import Foundation

/// "Report a problem": GitHub's bug report form (.github/ISSUE_TEMPLATE/bug_report.yml)
/// opened in the browser with what Lumerist already knows filled in. Lumerist
/// itself sends nothing: the person reads the form, says what happened and
/// creates the issue from their GitHub account. Windows: BugReport.cs; the
/// links are pinned down in Shared/fixtures/bug-report.
public enum BugReport {
    /// The fields Lumerist fills in, named by their id in bug_report.yml.
    public struct Details: Equatable, Sendable {
        public var lumerist: String
        public var system: String
        public var computer: String
        public var keyboard: String

        public init(lumerist: String, system: String, computer: String, keyboard: String) {
            self.lumerist = lumerist
            self.system = system
            self.computer = computer
            self.keyboard = keyboard
        }
    }

    static let form = "https://github.com/ziago/Lumerist/issues/new"
    static let template = "bug_report.yml"

    /// The form's address with the details filled in. A blank detail is left
    /// out, so the form shows its placeholder there.
    public static func url(for details: Details) -> URL {
        let fields = [("template", template), ("lumerist", details.lumerist), ("system", details.system),
                      ("computer", details.computer), ("keyboard", details.keyboard)]
        let query = fields.compactMap { name, value -> String? in
            let trimmed = value.trimmingCharacters(in: .whitespacesAndNewlines)
            return trimmed.isEmpty ? nil : "\(name)=\(escape(trimmed))"
        }
        // Only ASCII letters, digits and %XX remain, so the address always parses.
        return URL(string: form + "?" + query.joined(separator: "&"))!
    }

    /// The "Computer" field, worded as in the form's placeholder.
    public static func computer(isMac: Bool, isArm: Bool) -> String {
        switch (isMac, isArm) {
        case (true, true): "Mac with Apple Silicon"
        case (true, false): "Mac with Intel"
        case (false, true): "Windows PC (Arm)"
        case (false, false): "Windows PC (x64)"
        }
    }

    /// "macOS 27.0 (26A123)"; the patch number only when there is one.
    public static func macOSDescription(major: Int, minor: Int, patch: Int, build: String?) -> String {
        let version = patch > 0 ? "\(major).\(minor).\(patch)" : "\(major).\(minor)"
        guard let build, !build.isEmpty else { return "macOS \(version)" }
        return "macOS \(version) (\(build))"
    }

    /// The build from ProcessInfo's "Version 27.0 (Build 26A123)", or nil.
    public static func macOSBuild(fromVersionString text: String) -> String? {
        guard let range = text.range(of: "Build ") else { return nil }
        let build = text[range.upperBound...].prefix { $0.isLetter || $0.isNumber }
        return build.isEmpty ? nil : String(build)
    }

    private static let unreserved = CharacterSet(
        charactersIn: "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-._~")

    /// RFC 3986: only unreserved characters stay, everything else becomes
    /// UTF-8 %XX, exactly as Uri.EscapeDataString does on Windows.
    static func escape(_ value: String) -> String {
        value.addingPercentEncoding(withAllowedCharacters: unreserved) ?? value
    }
}
