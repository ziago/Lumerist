import CryptoKit
import Foundation

// What a downloaded file must hash to. Shared with Windows:
// Shared/fixtures/updates/checksums.json. Order of the checks (both apps):
//   1. SHA256SUMS.sig must verify under one of the trusted keys;
//   2. SHA256SUMS must be well formed and list the installer;
//   3. the installer's SHA-256 must equal the listed one, and the per-file
//      .sha256 and the API digest too, when present.

/// The public keys whose signature on `SHA256SUMS` is trusted: SPKI DER,
/// base64, ECDSA P-256. A signature valid under any of them is accepted, so
/// a new key can be added before the old one goes. The single source is
/// Shared/update-public-keys.json; Scripts/check-parity.py checks that this
/// list equals it.
public enum UpdateTrust {
    public static let updatePublicKeys: [String] = [
        "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEt37ZfOYd5bo4/ZKP73l2KTyzgZI9Ih0nlBof4WjmWuaoFd3JYfLtZah8HT6R2DP62EYmSxeW2N25UIN1DIhzVA==",
    ]
}

/// Why a download is refused.
public enum UpdateChecksumError: Error, Equatable, Sendable {
    /// A per-file `.sha256` that isn't one `<hex>  <name>` line.
    case malformedChecksumFile
    /// A per-file `.sha256` about another file.
    case nameMismatch
    /// `SHA256SUMS.sig` is not valid for `SHA256SUMS` under any trusted key.
    case badSignature
    /// `SHA256SUMS` has a line that isn't `<64 lowercase hex>  <name>`, a
    /// blank line, a carriage return, a name listed twice, or no line.
    case malformedList
    /// The file is not in `SHA256SUMS`.
    case notListed
    /// The file's SHA-256 differs from the list, the `.sha256` or the API digest.
    case mismatch(UpdateHashSource)
}

/// Where an expected SHA-256 came from.
public enum UpdateHashSource: String, Sendable {
    case list
    case checksumFile
    case digest
}

/// A verified source of the SHA-256 a release file must have. Today the
/// signed `SHA256SUMS` (`SignedChecksumList`); the updater only asks this.
public protocol UpdateChecksumSource: Sendable {
    /// The lowercase hex SHA-256 of `name`, or `UpdateChecksumError.notListed`.
    func expectedSHA256(for name: String) throws -> String
}

public enum UpdateChecksums {
    /// 64 hex digits, any case.
    static func isHex64(_ text: String) -> Bool {
        text.utf8.count == 64 && text.utf8.allSatisfy { isHexDigit($0) }
    }

    private static func isHexDigit(_ byte: UInt8) -> Bool {
        (48...57).contains(byte) || (65...70).contains(byte) || (97...102).contains(byte)
    }

    /// A per-file `.sha256`, as `shasum -a 256` writes it: one line,
    /// `<64 hex>` then two spaces (or a space and `*`) then the file's name,
    /// with or without a final line break (LF or CRLF). Returns lowercase hex.
    public static func checksumFileSHA256(_ data: Data, name: String) throws -> String {
        // Bytes, not Characters: Swift reads "\r\n" as a single Character.
        var bytes = [UInt8](data)
        if bytes.last == 10 {
            bytes.removeLast()
            if bytes.last == 13 { bytes.removeLast() }
        }
        guard bytes.count > 66, !bytes.contains(10), !bytes.contains(13),
              String(bytes: bytes, encoding: .utf8) != nil else { throw UpdateChecksumError.malformedChecksumFile }
        let hex = String(decoding: bytes[0..<64], as: UTF8.self)
        guard isHex64(hex), bytes[64] == 32, bytes[65] == 32 || bytes[65] == 42 else {
            throw UpdateChecksumError.malformedChecksumFile
        }
        guard String(decoding: bytes[66...], as: UTF8.self) == name else { throw UpdateChecksumError.nameMismatch }
        return hex.lowercased()
    }

    /// The downloaded file's SHA-256 against every source: the signed list
    /// first, then the `.sha256` and the API digest when the release has them.
    public static func verify(actual: String, listed: String, checksumFile: String?, digest: String?) throws {
        let actual = actual.lowercased()
        guard listed.lowercased() == actual else { throw UpdateChecksumError.mismatch(.list) }
        if let checksumFile, checksumFile.lowercased() != actual { throw UpdateChecksumError.mismatch(.checksumFile) }
        if let digest, digest.lowercased() != actual { throw UpdateChecksumError.mismatch(.digest) }
    }

    /// Lowercase hex of a SHA-256 digest.
    public static func hex(_ digest: SHA256.Digest) -> String {
        digest.map { String(format: "%02x", $0) }.joined()
    }
}

/// `SHA256SUMS` once its signature has been checked.
///
/// - `SHA256SUMS.sig` is base64 (standard alphabet, padded, nothing inside
///   but base64) of a DER ECDSA P-256 / SHA-256 signature over the exact
///   bytes of `SHA256SUMS`; whitespace around it is ignored.
/// - `SHA256SUMS` is UTF-8 with LF line breaks, one `<64 lowercase hex>  <name>`
///   line (two spaces) per file; the last line break may be missing. A name
///   is ASCII letters, digits, `.`, `_`, `+` and `-`, and not `.` or `..`.
///   The order is the signer's (it is not checked again).
public struct SignedChecksumList: UpdateChecksumSource, Equatable {
    public let entries: [String: String]

    /// Checks the signature first; then parses. Keys that can't be read are skipped.
    public init(list: Data, signature: Data, trustedKeys: [String] = UpdateTrust.updatePublicKeys) throws {
        guard Self.verify(list: list, signature: signature, trustedKeys: trustedKeys) else {
            throw UpdateChecksumError.badSignature
        }
        entries = try Self.parse(list)
    }

    public func expectedSHA256(for name: String) throws -> String {
        guard let hex = entries[name] else { throw UpdateChecksumError.notListed }
        return hex
    }

    static func verify(list: Data, signature: Data, trustedKeys: [String]) -> Bool {
        guard let text = String(data: signature, encoding: .utf8) else { return false }
        let base64 = text.trimmingCharacters(in: CharacterSet(charactersIn: " \t\r\n"))
        guard !base64.isEmpty, base64.utf8.count % 4 == 0,
              UpdateChoice.matches(base64Pattern, base64),
              let der = Data(base64Encoded: base64),
              let ecdsa = try? P256.Signing.ECDSASignature(derRepresentation: der) else { return false }
        for key in trustedKeys {
            guard let spki = Data(base64Encoded: key),
                  let publicKey = try? P256.Signing.PublicKey(derRepresentation: spki) else { continue }
            if publicKey.isValidSignature(ecdsa, for: list) { return true }
        }
        return false
    }

    private static let base64Pattern = try! NSRegularExpression(pattern: #"\A[A-Za-z0-9+/]+={0,2}\z"#)
    private static let linePattern = try! NSRegularExpression(pattern: #"\A([0-9a-f]{64})  ([A-Za-z0-9._+-]+)\z"#)

    static func parse(_ data: Data) throws -> [String: String] {
        guard String(data: data, encoding: .utf8) != nil, !data.contains(13) else {
            throw UpdateChecksumError.malformedList
        }
        // Split on the LF byte (never on Characters, see checksumFileSHA256).
        var rows = [UInt8](data).split(separator: 10, omittingEmptySubsequences: false)
        if rows.last?.isEmpty == true { rows.removeLast() }
        guard !rows.isEmpty else { throw UpdateChecksumError.malformedList }
        var entries: [String: String] = [:]
        for row in rows {
            let line = String(decoding: row, as: UTF8.self)
            guard let match = linePattern.firstMatch(in: line, range: NSRange(line.startIndex..., in: line)),
                  let hexRange = Range(match.range(at: 1), in: line),
                  let nameRange = Range(match.range(at: 2), in: line) else { throw UpdateChecksumError.malformedList }
            let name = String(line[nameRange])
            guard name != ".", name != "..", entries[name] == nil else { throw UpdateChecksumError.malformedList }
            entries[name] = String(line[hexRange])
        }
        return entries
    }
}
