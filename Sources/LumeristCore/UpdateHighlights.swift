import Foundation

/// The "What's new" lines of the update window, from a release's notes.
/// Shared with Windows: Shared/fixtures/updates/highlights.json.
///
/// Each CHANGELOG version section starts with a `### Highlights` list, and
/// the release notes are made from that section:
///
/// - the section starts after the first line that reads `### Highlights`
///   (any case, spaces around ignored) and ends at the next line starting
///   with `#` (after leading spaces), i.e. the next heading;
/// - a line starting with `- ` or `* ` (after leading spaces) starts a
///   bullet; a following line indented by spaces or tabs continues it (joined
///   with one space); a blank line or a line of plain, unindented text ends it;
/// - lines may end with LF or CRLF.
///
/// The result is plain text, never rendered: links become their text, code
/// spans their content, bold and italics lose their markers; control
/// characters become spaces and bidirectional controls are removed; runs of
/// whitespace become one space. A bullet longer than `maximumLength` Unicode
/// scalars is cut, at a space when there is one, and ends with "…". At most
/// `maximumCount` bullets, and no more than `maximumTotal` scalars in all
/// (the bullet that would pass it and those after it are left out).
public enum UpdateHighlights {
    public static let maximumCount = 5
    public static let maximumLength = 140
    public static let maximumTotal = 600
    /// Shown when the notes have no Highlights.
    public static let fallback = "See what's new on the release page."

    public static func parse(_ body: String) -> [String] {
        var bullets: [String] = []
        var current: String?
        var inSection = false
        func close() {
            if let text = current { bullets.append(text) }
            current = nil
        }
        // Split on LF bytes, then drop a CR: Swift reads "\r\n" as one Character.
        for row in [UInt8](body.utf8).split(separator: 10, omittingEmptySubsequences: false) {
            var line = String(decoding: row, as: UTF8.self)
            if line.utf8.last == 13 { line = String(decoding: row.dropLast(), as: UTF8.self) }
            if !inSection {
                if line.trimmingCharacters(in: .whitespaces).lowercased() == "### highlights" { inSection = true }
                continue
            }
            let stripped = String(line.drop { $0 == " " || $0 == "\t" })
            if stripped.hasPrefix("#") { break }
            if stripped.hasPrefix("- ") || stripped.hasPrefix("* ") {
                close()
                current = String(stripped.dropFirst(2))
            } else if stripped.trimmingCharacters(in: .whitespaces).isEmpty {
                close()
            } else if let text = current, line.first == " " || line.first == "\t" {
                current = text + " " + stripped
            } else {
                close()
            }
        }
        close()

        var result: [String] = []
        var total = 0
        for bullet in bullets {
            let text = cut(plainText(bullet))
            guard !text.isEmpty else { continue }
            let length = text.unicodeScalars.count
            guard result.count < maximumCount, total + length <= maximumTotal else { break }
            result.append(text)
            total += length
        }
        return result
    }

    // MARK: Plain text

    private static func regex(_ pattern: String) -> NSRegularExpression { try! NSRegularExpression(pattern: pattern) }
    private static let link = regex(#"!?\[([^\]]*)\]\([^)]*\)"#)
    private static let code = regex("`([^`]*)`")
    private static let strongStars = regex(#"\*\*(?=\S)(.+?)(?<=\S)\*\*"#)
    private static let strongUnderscores = regex(#"(?<![A-Za-z0-9])__(?=\S)(.+?)(?<=\S)__(?![A-Za-z0-9])"#)
    private static let emphasisStar = regex(#"\*(?=\S)(.+?)(?<=\S)\*"#)
    private static let emphasisUnderscore = regex(#"(?<![A-Za-z0-9])_(?=\S)(.+?)(?<=\S)_(?![A-Za-z0-9])"#)

    /// One bullet's Markdown as plain text (see the type's notes).
    static func plainText(_ markdown: String) -> String {
        var scalars = String.UnicodeScalarView()
        for scalar in markdown.unicodeScalars {
            switch scalar.value {
            case 0x00...0x1F, 0x7F...0x9F, 0x2028, 0x2029: scalars.append(" ")
            case 0x061C, 0x200E, 0x200F, 0x202A...0x202E, 0x2066...0x2069, 0xFEFF: continue
            default: scalars.append(scalar)
            }
        }
        let text = replace(link, in: String(scalars), with: "$1")
        // Code spans keep their content as is; Markdown is stripped around them.
        var plain = ""
        var rest = text[...]
        let whole = NSRange(text.startIndex..., in: text)
        for match in code.matches(in: text, range: whole) {
            guard let span = Range(match.range, in: text), let inner = Range(match.range(at: 1), in: text) else { continue }
            plain += stripEmphasis(String(text[rest.startIndex..<span.lowerBound]))
            plain += text[inner]
            rest = text[span.upperBound...]
        }
        plain += stripEmphasis(String(rest))
        return plain.split(whereSeparator: \.isWhitespace).joined(separator: " ")
    }

    private static func stripEmphasis(_ text: String) -> String {
        var text = text
        for pattern in [strongStars, strongUnderscores, emphasisStar, emphasisUnderscore] {
            text = replace(pattern, in: text, with: "$1")
        }
        return text
    }

    private static func replace(_ pattern: NSRegularExpression, in text: String, with template: String) -> String {
        pattern.stringByReplacingMatches(in: text, range: NSRange(text.startIndex..., in: text), withTemplate: template)
    }

    /// At most `maximumLength` scalars: cut after `maximumLength - 1`, back to
    /// the last space when the cut falls inside a word, then "…".
    static func cut(_ text: String) -> String {
        let scalars = Array(text.unicodeScalars)
        guard scalars.count > maximumLength else { return text }
        var kept = Array(scalars[0..<(maximumLength - 1)])
        if scalars[maximumLength - 1] != " ", let space = kept.lastIndex(of: " ") {
            kept = Array(kept[..<space])
        }
        while kept.last == " " { kept.removeLast() }
        var result = String.UnicodeScalarView()
        result.append(contentsOf: kept)
        return String(result) + "…"
    }
}
