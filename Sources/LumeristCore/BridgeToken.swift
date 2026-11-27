import Foundation
import Security

/// The secret that proves a request comes from this Lumerist app (protocol 4).
///
/// Created once: 32 random bytes written as 64 lowercase hexadecimal
/// characters to `~/Library/Application Support/Lumerist/bridge-token`, which
/// only this user can read (0600). Every request line carries it, and the
/// plug-in (BridgeToken.lua) reads the same file and refuses any request
/// without it. Another account on the Mac, or a web page posting to
/// 127.0.0.1, cannot read the file, so it cannot drive Lightroom.
/// Windows: BridgeToken.cs.
public enum BridgeToken {
    public static let fileName = "bridge-token"
    public static let length = 64

    /// ~/Library/Application Support/Lumerist (the plug-in backups live there too).
    public static func defaultFolder(home: URL = FileManager.default.homeDirectoryForCurrentUser) -> URL {
        home.appending(path: "Library/Application Support/Lumerist", directoryHint: .isDirectory)
    }

    /// 64 characters, 0-9 and a-f only.
    public static func isValid(_ token: String) -> Bool {
        token.utf8.count == length && token.utf8.allSatisfy { (0x30...0x39).contains($0) || (0x61...0x66).contains($0) }
    }

    /// 32 bytes from the system's secure random generator, as lowercase hex.
    public static func generate() -> String {
        var bytes = [UInt8](repeating: 0, count: length / 2)
        let status = SecRandomCopyBytes(kSecRandomDefault, bytes.count, &bytes)
        precondition(status == errSecSuccess, "SecRandomCopyBytes failed (\(status))")
        return bytes.map { String(format: "%02x", $0) }.joined()
    }

    /// The token in `folder`, created first when the file is missing or does
    /// not hold a valid token. A file readable by others (restored from a
    /// backup, for example) is made private again; if that fails the token
    /// is still used, as the plug-in reads the same file.
    public static func loadOrCreate(in folder: URL) throws -> String {
        let file = folder.appending(path: fileName, directoryHint: .notDirectory)
        if let data = try? Data(contentsOf: file),
           let text = String(data: data, encoding: .utf8)?.trimmingCharacters(in: .whitespacesAndNewlines).lowercased(),
           isValid(text) {
            try? makePrivate(file)
            return text
        }
        let token = generate()
        try write(token, to: file)
        return token
    }

    public struct WriteError: Error, LocalizedError, Equatable {
        public let path: String
        public let reason: String

        public var errorDescription: String? {
            "Could not save the bridge token at \(path): \(reason)"
        }
    }

    private static func makePrivate(_ file: URL) throws {
        let attributes = try FileManager.default.attributesOfItem(atPath: file.path)
        if (attributes[.posixPermissions] as? NSNumber)?.intValue != 0o600 {
            try FileManager.default.setAttributes([.posixPermissions: 0o600], ofItemAtPath: file.path)
        }
    }

    /// Created as 0600 from the start (never readable by others, not even
    /// for a moment), then renamed over the old file so the plug-in never
    /// reads half a token.
    private static func write(_ token: String, to file: URL) throws {
        let folder = file.deletingLastPathComponent()
        try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true,
                                                attributes: [.posixPermissions: 0o700])
        let temporary = folder.appending(path: ".\(fileName).\(UUID().uuidString).tmp", directoryHint: .notDirectory)
        func failure(_ step: String) -> WriteError {
            WriteError(path: file.path, reason: "\(step): \(String(cString: strerror(errno)))")
        }
        let descriptor = open(temporary.path, O_WRONLY | O_CREAT | O_EXCL | O_NOFOLLOW, 0o600)
        guard descriptor >= 0 else { throw failure("create") }
        let bytes = Array(token.utf8)
        let written = bytes.withUnsafeBytes { Darwin.write(descriptor, $0.baseAddress, $0.count) }
        let synced = fsync(descriptor)
        close(descriptor)
        guard written == bytes.count, synced == 0 else {
            let error = failure("write")
            unlink(temporary.path)
            throw error
        }
        guard rename(temporary.path, file.path) == 0 else {
            let error = failure("rename")
            unlink(temporary.path)
            throw error
        }
    }
}
