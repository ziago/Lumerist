import Darwin
import Foundation
import Security

// The Mac's own steps of "Update now" (Windows has its own): where the files
// go, the checks on the disk image and the app, and the swap. The order is in
// docs/ARCHITECTURE.md ("Network"); the app's MacUpdateInstaller runs them.

/// Why the Mac updater stopped. Every case leaves the running app untouched
/// (after a swap, the previous copy is put back first).
public enum MacUpdateError: Error, Equatable, LocalizedError, Sendable {
    case privateFolder(String)
    case diskImageSignature(String)
    case attach(String)
    case appMissing
    case appSignature(String)
    case notNotarized(String)
    case wrongBundle(String)
    case needsNewerMacOS(String)
    case wrongArchitecture
    case copy(String)
    case swap(String)
    case tool(String)

    public var errorDescription: String? {
        switch self {
        case .privateFolder(let reason): "Lumerist could not make a private folder for the download: \(reason)"
        case .diskImageSignature(let reason): "The disk image is not signed with Lumerist's Developer ID: \(reason)"
        case .attach(let reason): "The disk image could not be opened: \(reason)"
        case .appMissing: "The disk image has no Lumerist.app."
        case .appSignature(let reason): "The app is not signed with Lumerist's Developer ID: \(reason)"
        case .notNotarized(let reason): "macOS did not accept the app as notarized: \(reason)"
        case .wrongBundle(let reason): reason
        case .needsNewerMacOS(let version): "This version needs macOS \(version) or later."
        case .wrongArchitecture: "This version does not run on this Mac's processor."
        case .copy(let reason): "The new app could not be copied: \(reason)"
        case .swap(let reason): "The new app could not take the old one's place: \(reason)"
        case .tool(let reason): reason
        }
    }
}

public enum MacUpdate {
    public static let bundleIdentifier = "io.github.ziago.lumerist"
    public static let teamIdentifier = "JRNUV73KXY"

    /// Apple's standard Developer ID requirement for this app: signed through
    /// Apple's Developer ID CA, with a Developer ID Application leaf from
    /// this team, and this identifier. The same as the app's designated
    /// requirement, so macOS keeps the Accessibility permission.
    public static let appRequirement =
        "anchor apple generic and identifier \"\(bundleIdentifier)\""
        + " and certificate 1[field.1.2.840.113635.100.6.2.6]"
        + " and certificate leaf[field.1.2.840.113635.100.6.1.13]"
        + " and certificate leaf[subject.OU] = \"\(teamIdentifier)\""

    /// The disk image is signed by the same team (its identifier is its file name).
    public static let diskImageRequirement =
        "anchor apple generic"
        + " and certificate 1[field.1.2.840.113635.100.6.2.6]"
        + " and certificate leaf[field.1.2.840.113635.100.6.1.13]"
        + " and certificate leaf[subject.OU] = \"\(teamIdentifier)\""

    // MARK: Private folder

    /// A new folder only this user can read (mode 700), in the user's own
    /// temporary folder: `mkdtemp`, then checked (a directory, ours, 700, not
    /// a link).
    public static func makePrivateFolder(in parent: URL = URL(fileURLWithPath: NSTemporaryDirectory())) throws -> URL {
        var template = Array(parent.appending(path: "Lumerist-update.XXXXXXXX").path(percentEncoded: false).utf8CString)
        guard let made = mkdtemp(&template) else {
            throw MacUpdateError.privateFolder(String(cString: strerror(errno)))
        }
        let path = String(cString: made)
        var info = stat()
        guard lstat(path, &info) == 0, (info.st_mode & S_IFMT) == S_IFDIR,
              info.st_uid == getuid(), (info.st_mode & 0o777) == 0o700 else {
            try? FileManager.default.removeItem(atPath: path)
            throw MacUpdateError.privateFolder("the folder is not private")
        }
        return URL(fileURLWithPath: path, isDirectory: true)
    }

    /// Creates `name` in `folder` for writing, readable only by this user
    /// (0600); fails if it exists.
    public static func createPrivateFile(_ name: String, in folder: URL) throws -> FileHandle {
        let path = folder.appending(path: name).path(percentEncoded: false)
        let descriptor = open(path, O_WRONLY | O_CREAT | O_EXCL | O_NOFOLLOW, 0o600)
        guard descriptor >= 0 else { throw MacUpdateError.privateFolder(String(cString: strerror(errno))) }
        return FileHandle(fileDescriptor: descriptor, closeOnDealloc: true)
    }

    // MARK: Code signature

    public static let appValidation = SecCSFlags(rawValue: UInt32(kSecCSCheckAllArchitectures | kSecCSCheckNestedCode
                                                           | kSecCSStrictValidate | kSecCSRestrictSymlinks))

    /// The code at `url` is intact and meets `requirement` (every architecture
    /// and nested code, strictly, no symbolic links out of the bundle).
    public static func checkSignature(of url: URL, requirement: String, flags: SecCSFlags = appValidation) throws {
        var code: SecStaticCode?
        var status = SecStaticCodeCreateWithPath(url as CFURL, [], &code)
        guard status == errSecSuccess, let code else { throw MacUpdateError.appSignature(describe(status)) }
        var compiled: SecRequirement?
        status = SecRequirementCreateWithString(requirement as CFString, [], &compiled)
        guard status == errSecSuccess, let compiled else { throw MacUpdateError.appSignature(describe(status)) }
        var error: Unmanaged<CFError>?
        status = SecStaticCodeCheckValidityWithErrors(code, flags, compiled, &error)
        guard status == errSecSuccess else {
            let reason = error?.takeRetainedValue().localizedDescription ?? describe(status)
            throw MacUpdateError.appSignature(reason)
        }
    }

    /// The disk image's own signature, before anything reads its contents.
    public static func checkDiskImageSignature(_ url: URL) throws {
        do {
            try checkSignature(of: url, requirement: diskImageRequirement, flags: [])
        } catch MacUpdateError.appSignature(let reason) {
            throw MacUpdateError.diskImageSignature(reason)
        }
    }

    private static func describe(_ status: OSStatus) -> String {
        (SecCopyErrorMessageString(status, nil) as String?) ?? "error \(status)"
    }

    // MARK: Gatekeeper

    /// macOS's own verdict for opening the app (as on first launch): accepted,
    /// as notarized Developer ID software. `spctl --assess --type execute`.
    public static func assessNotarization(of app: URL) throws {
        let result = try run("/usr/sbin/spctl", ["--assess", "--type", "execute", "--verbose", app.path(percentEncoded: false)],
                             timeout: 120)
        let text = String(decoding: result.output + result.errors, as: UTF8.self)
        let lines = text.split(whereSeparator: \.isNewline).map { $0.trimmingCharacters(in: .whitespaces) }
        guard result.status == 0, lines.contains("source=Notarized Developer ID") else {
            throw MacUpdateError.notNotarized(lines.last ?? "status \(result.status)")
        }
    }

    // MARK: The app bundle

    /// The app's Info.plist: this app's identifier, exactly `version`, a
    /// macOS this Mac has, and code for this Mac's processor.
    public static func checkBundle(_ app: URL, version: String,
                                   system: OperatingSystemVersion = ProcessInfo.processInfo.operatingSystemVersion) throws {
        let plist = app.appending(path: "Contents/Info.plist")
        guard let data = try? Data(contentsOf: plist),
              let info = try? PropertyListSerialization.propertyList(from: data, format: nil) as? [String: Any] else {
            throw MacUpdateError.wrongBundle("The new app has no readable Info.plist.")
        }
        guard info["CFBundleIdentifier"] as? String == bundleIdentifier else {
            throw MacUpdateError.wrongBundle("The new app is not Lumerist (bundle identifier).")
        }
        guard let found = info["CFBundleShortVersionString"] as? String, found == version else {
            throw MacUpdateError.wrongBundle("The new app is version \(info["CFBundleShortVersionString"] as? String ?? "unknown"), not \(version).")
        }
        if let minimum = info["LSMinimumSystemVersion"] as? String, !isAtLeast(system, minimum) {
            throw MacUpdateError.needsNewerMacOS(minimum)
        }
        guard let executable = info["CFBundleExecutable"] as? String,
              let architectures = machOArchitectures(app.appending(path: "Contents/MacOS/\(executable)")),
              architectures.contains(currentArchitecture) else { throw MacUpdateError.wrongArchitecture }
    }

    static func isAtLeast(_ system: OperatingSystemVersion, _ minimum: String) -> Bool {
        let parts = minimum.split(separator: ".").map { Int($0) ?? 0 }
        let wanted = (parts.count > 0 ? parts[0] : 0, parts.count > 1 ? parts[1] : 0, parts.count > 2 ? parts[2] : 0)
        return (system.majorVersion, system.minorVersion, system.patchVersion) >= wanted
    }

    /// CPU types of a Mach-O file, thin or fat (no Bundle: its cache could
    /// describe what used to be at a path).
    static func machOArchitectures(_ url: URL) -> Set<Int32>? {
        guard let handle = try? FileHandle(forReadingFrom: url) else { return nil }
        defer { try? handle.close() }
        guard let header = try? handle.read(upToCount: 4096), header.count >= 8 else { return nil }
        func be32(_ offset: Int) -> UInt32 {
            header[offset..<offset + 4].reduce(0) { $0 << 8 | UInt32($1) }
        }
        func le32(_ offset: Int) -> UInt32 { be32(offset).byteSwapped }
        switch be32(0) {
        case 0xCAFE_BABE, 0xCAFE_BABF: // fat (32- or 64-bit offsets), big-endian
            let count = Int(be32(4)), stride = be32(0) == 0xCAFE_BABE ? 20 : 32
            guard count > 0, count < 16, header.count >= 8 + count * stride else { return nil }
            return Set((0..<count).map { Int32(bitPattern: be32(8 + $0 * stride)) })
        case 0xCFFA_EDFE: // thin 64-bit, little-endian
            return [Int32(bitPattern: le32(4))]
        default:
            return nil
        }
    }

    /// `CPU_TYPE_ARM64` and `CPU_TYPE_X86_64` (mach/machine.h).
    static let arm64: Int32 = 0x0100_000C
    static let x86_64: Int32 = 0x0100_0007

    static var currentArchitecture: Int32 {
        #if arch(arm64)
        arm64
        #else
        x86_64
        #endif
    }

    // MARK: Where the app runs

    /// Why the app at `appURL` can't replace itself, or nil when it can: an
    /// app bundle of Lumerist, not run from a disk image or a translocated
    /// copy, in a folder this user may change.
    public static func installBlocker(appURL: URL) -> String? {
        let path = appURL.path(percentEncoded: false)
        let info = (try? Data(contentsOf: appURL.appending(path: "Contents/Info.plist")))
            .flatMap { try? PropertyListSerialization.propertyList(from: $0, format: nil) as? [String: Any] }
        guard appURL.pathExtension == "app", info?["CFBundleIdentifier"] as? String == bundleIdentifier else {
            return "This copy of Lumerist is not an app bundle, so it can't replace itself."
        }
        if path.contains("/AppTranslocation/") {
            return "macOS runs this copy of Lumerist from a temporary place. Move Lumerist to Applications, or download the new version."
        }
        if (try? appURL.resourceValues(forKeys: [.volumeIsReadOnlyKey]))?.volumeIsReadOnly == true {
            return "Lumerist runs from a disk image or a read-only disk. Copy it to Applications, or download the new version."
        }
        let folder = appURL.deletingLastPathComponent()
        guard access(folder.path(percentEncoded: false), W_OK) == 0, access(path, W_OK) == 0,
              access(appURL.appending(path: "Contents").path(percentEncoded: false), W_OK) == 0 else {
            return "Lumerist can't replace itself in “\(folder.path(percentEncoded: false))”: this account may not change it. Download the new version instead."
        }
        return nil
    }

    // MARK: Disk image

    public struct DiskImage: Sendable {
        public let mountPoint: URL
        /// The whole image's device, detached at the end.
        public let device: String
    }

    /// Mounts read-only, hidden from the Finder, without opening anything,
    /// at `mountPoint` (an empty folder in the private folder).
    public static func attach(_ image: URL, at mountPoint: URL) throws -> DiskImage {
        try FileManager.default.createDirectory(at: mountPoint, withIntermediateDirectories: false,
                                                attributes: [.posixPermissions: 0o700])
        let result = try run("/usr/bin/hdiutil", ["attach", "-plist", "-nobrowse", "-readonly", "-noautoopen", "-noautofsck",
                                                  "-mountpoint", mountPoint.path(percentEncoded: false),
                                                  image.path(percentEncoded: false)], timeout: 180)
        let wanted = canonicalPath(mountPoint.path(percentEncoded: false))
        let plist = (try? PropertyListSerialization.propertyList(from: result.output, format: nil)) as? [String: Any]
        let entities = plist?["system-entities"] as? [[String: Any]] ?? []
        let devices = entities.compactMap { $0["dev-entry"] as? String }
        let mounted = entities.contains { ($0["mount-point"] as? String).map(canonicalPath) == wanted }
        guard result.status == 0, mounted, let device = devices.min(by: { $0.count < $1.count }) else {
            // Whatever got mounted goes away again.
            _ = try? run("/usr/bin/hdiutil", ["detach", "-force", mountPoint.path(percentEncoded: false)], timeout: 60)
            let reason = String(decoding: result.errors, as: UTF8.self)
                .split(whereSeparator: \.isNewline).last { !$0.contains("deprecated") }
            throw MacUpdateError.attach(reason.map(String.init)
                ?? "hdiutil status \(result.status), \(entities.count) entities, mounted at the wrong place: \(!mounted)")
        }
        return DiskImage(mountPoint: mountPoint, device: device)
    }

    /// The path with every link resolved (`realpath`): /var/… and /private/var/… compare equal.
    static func canonicalPath(_ path: String) -> String {
        guard let resolved = realpath(path, nil) else { return path }
        defer { free(resolved) }
        return String(cString: resolved)
    }

    public static func detach(_ image: DiskImage) {
        let normal = try? run("/usr/bin/hdiutil", ["detach", image.device], timeout: 60)
        if normal?.status != 0 {
            _ = try? run("/usr/bin/hdiutil", ["detach", "-force", image.device], timeout: 60)
        }
    }

    /// `Lumerist.app` at the top of the mounted image: a real folder, not a link.
    public static func appInImage(_ image: DiskImage) throws -> URL {
        let app = image.mountPoint.appending(path: "Lumerist.app", directoryHint: .isDirectory)
        var info = stat()
        guard lstat(app.path(percentEncoded: false), &info) == 0, (info.st_mode & S_IFMT) == S_IFDIR else {
            throw MacUpdateError.appMissing
        }
        return app
    }

    /// `ditto` to `destination`, which must not exist yet.
    public static func copyApp(_ source: URL, to destination: URL) throws {
        guard !FileManager.default.fileExists(atPath: destination.path(percentEncoded: false)) else {
            throw MacUpdateError.copy("\(destination.lastPathComponent) already exists")
        }
        let result = try run("/usr/bin/ditto", [source.path(percentEncoded: false), destination.path(percentEncoded: false)],
                             timeout: 300)
        guard result.status == 0 else {
            try? FileManager.default.removeItem(at: destination)
            throw MacUpdateError.copy(String(decoding: result.errors, as: UTF8.self).trimmingCharacters(in: .whitespacesAndNewlines))
        }
    }

    // MARK: Swap

    /// Exchanges what is at `first` and at `second`: one atomic
    /// `renamex_np(RENAME_SWAP)` where the volume allows it, otherwise three
    /// renames that put everything back if one fails. Swapping again undoes it.
    public static func swap(_ first: URL, _ second: URL) throws {
        let a = first.path(percentEncoded: false), b = second.path(percentEncoded: false)
        if renamex_np(a, b, UInt32(RENAME_SWAP)) == 0 { return }
        let swapError = errno
        guard swapError == ENOTSUP || swapError == EINVAL else {
            throw MacUpdateError.swap(String(cString: strerror(swapError)))
        }
        let parking = second.deletingLastPathComponent()
            .appending(path: ".\(second.lastPathComponent).swap-\(UUID().uuidString.prefix(8))").path(percentEncoded: false)
        guard rename(a, parking) == 0 else { throw MacUpdateError.swap(String(cString: strerror(errno))) }
        guard rename(b, a) == 0 else {
            let reason = String(cString: strerror(errno))
            _ = rename(parking, a)
            throw MacUpdateError.swap(reason)
        }
        guard rename(parking, b) == 0 else {
            let reason = String(cString: strerror(errno))
            _ = rename(a, b)
            _ = rename(parking, a)
            throw MacUpdateError.swap(reason)
        }
    }

    /// A unique hidden name next to the app, for the new copy.
    public static func stagingURL(beside app: URL, version: String) -> URL {
        app.deletingLastPathComponent()
            .appending(path: ".Lumerist-\(version)-\(UUID().uuidString.prefix(8)).app", directoryHint: .isDirectory)
    }

    // MARK: Tools

    public struct ToolResult: Sendable {
        public let status: Int32
        public let output: Data
        public let errors: Data
    }

    /// Runs a system tool by its full path, with a small fixed environment and
    /// no input, and stops it after `timeout` seconds.
    public static func run(_ tool: String, _ arguments: [String], timeout: TimeInterval) throws -> ToolResult {
        let process = Process()
        process.executableURL = URL(fileURLWithPath: tool)
        process.arguments = arguments
        process.environment = ["PATH": "/usr/bin:/bin:/usr/sbin:/sbin", "HOME": NSHomeDirectory(), "TMPDIR": NSTemporaryDirectory()]
        process.standardInput = FileHandle.nullDevice
        let output = Pipe(), errors = Pipe()
        process.standardOutput = output
        process.standardError = errors
        let finished = DispatchSemaphore(value: 0)
        process.terminationHandler = { _ in finished.signal() }
        try process.run()
        // Both pipes are read while the tool runs, so neither can fill up.
        let reads = DispatchGroup()
        let out = ToolOutput(), err = ToolOutput()
        DispatchQueue.global().async(group: reads) { out.data = output.fileHandleForReading.readDataToEndOfFile() }
        DispatchQueue.global().async(group: reads) { err.data = errors.fileHandleForReading.readDataToEndOfFile() }
        if finished.wait(timeout: .now() + timeout) == .timedOut {
            process.terminate()
            _ = finished.wait(timeout: .now() + 5)
            throw MacUpdateError.tool("\(URL(fileURLWithPath: tool).lastPathComponent) did not finish in time.")
        }
        reads.wait()
        return ToolResult(status: process.terminationStatus, output: out.data, errors: err.data)
    }
}

/// One pipe's bytes, written by its reader before `DispatchGroup.wait` returns.
private final class ToolOutput: @unchecked Sendable {
    var data = Data()
}
