import CryptoKit
import Foundation

/// Where the Lightroom Classic plug-in lives and where replaced copies go.
public enum PluginInstallLayout {
    public static let bundleName = "Lumerist.lrplugin"
    private static let backupPrefix = "Lumerist.lrplugin.backup-"

    /// Lightroom Classic's per-user plug-in folder (same for every version and language).
    public static func modulesDirectory(home: URL) -> URL {
        home.appending(path: "Library/Application Support/Adobe/Lightroom/Modules", directoryHint: .isDirectory)
    }

    /// Replaced plug-ins are kept here, OUTSIDE Modules: Lightroom scans that
    /// folder, and two plug-ins with the same identifier disable each other.
    public static func backupDirectory(home: URL) -> URL {
        home.appending(path: "Library/Application Support/Lumerist/plugin-backups", directoryHint: .isDirectory)
    }

    static func backupName(for date: Date, timeZone: TimeZone = .current) -> String {
        let formatter = DateFormatter()
        formatter.locale = Locale(identifier: "en_US_POSIX")
        formatter.timeZone = timeZone
        formatter.dateFormat = "yyyyMMdd-HHmmss"
        return backupPrefix + formatter.string(from: date)
    }
}

/// How the installed plug-in compares with the one shipped inside the app.
public enum PluginInstallStatus: Equatable, Sendable {
    /// No Lumerist plug-in in Lightroom's Modules folder.
    case notInstalled
    /// The installed copy is older (or unreadable): offer "Update".
    case outdated(installed: SemanticVersion?, bundled: SemanticVersion)
    /// Same version but different files (a development build): offer "Reinstall".
    case modified(SemanticVersion)
    case upToDate(SemanticVersion)
    /// The installed copy is newer than this app's (an older app was opened).
    case newerInstalled(installed: SemanticVersion, bundled: SemanticVersion)

    public var needsAction: Bool {
        switch self {
        case .notInstalled, .outdated, .modified: true
        case .upToDate, .newerInstalled: false
        }
    }

    /// The app replaces the installed plug-in by itself whenever Lightroom is
    /// closed: only an older copy of Lumerist's own plug-in. A missing one is
    /// installed by the user (first-run guide), and a copy changed by hand or
    /// newer is never touched.
    public var updatesAutomatically: Bool {
        if case .outdated = self { true } else { false }
    }

    static func compare(
        installed: SemanticVersion?,
        installedExists: Bool,
        bundled: SemanticVersion,
        sameContents: Bool
    ) -> PluginInstallStatus {
        guard installedExists else { return .notInstalled }
        guard let installed else { return .outdated(installed: nil, bundled: bundled) }
        if installed < bundled { return .outdated(installed: installed, bundled: bundled) }
        if bundled < installed { return .newerInstalled(installed: installed, bundled: bundled) }
        return sameContents ? .upToDate(bundled) : .modified(bundled)
    }
}

/// Installs, updates and removes the plug-in with plain file operations.
/// Nothing is ever deleted: a replaced or removed copy is moved to the backup
/// folder. Callers must make sure Lightroom Classic is not running.
public struct PluginInstallOperation: Sendable {
    public let source: URL
    public let modulesDirectory: URL
    public let backupDirectory: URL

    public init(source: URL, modulesDirectory: URL, backupDirectory: URL) {
        self.source = source
        self.modulesDirectory = modulesDirectory
        self.backupDirectory = backupDirectory
    }

    public var destination: URL {
        modulesDirectory.appending(path: PluginInstallLayout.bundleName, directoryHint: .isDirectory)
    }

    /// True for a real folder and for a symbolic link (even a broken one, as
    /// developers sometimes link a working copy): both occupy the slot.
    public var isInstalled: Bool { Self.exists(destination) }

    /// `fileExists` follows links, so a dangling link would look absent and
    /// then block `moveItem`. Checking the link itself avoids that.
    static func exists(_ url: URL) -> Bool {
        (try? FileManager.default.attributesOfItem(atPath: url.path)) != nil
    }

    var bundledVersion: SemanticVersion? { Self.version(ofPlugin: source) }
    var installedVersion: SemanticVersion? { Self.version(ofPlugin: destination) }

    public func status() throws -> PluginInstallStatus {
        guard let bundled = bundledVersion else { throw PluginInstallError.bundledPluginUnreadable(source) }
        let installed = isInstalled
        return PluginInstallStatus.compare(
            installed: installed ? installedVersion : nil,
            installedExists: installed,
            bundled: bundled,
            sameContents: installed && Self.fingerprint(ofPlugin: destination) == Self.fingerprint(ofPlugin: source)
        )
    }

    /// Copies the bundled plug-in into Modules. An existing copy is moved to
    /// the backup folder first; its new location is returned.
    @discardableResult
    public func install(now: Date = Date()) throws -> URL? {
        guard bundledVersion != nil else { throw PluginInstallError.bundledPluginUnreadable(source) }
        let manager = FileManager.default
        try manager.createDirectory(at: modulesDirectory, withIntermediateDirectories: true)
        let backup = try moveExistingToBackup(now: now)
        // Copy to a temporary sibling first, then move into place, so Lightroom
        // never sees a half-copied plug-in.
        let staging = modulesDirectory.appending(path: ".Lumerist.lrplugin.installing-\(UUID().uuidString)", directoryHint: .isDirectory)
        do {
            try manager.copyItem(at: source, to: staging)
            Self.removeFinderMetadata(in: staging)
            try manager.moveItem(at: staging, to: destination)
        } catch {
            try? manager.removeItem(at: staging)
            if let backup, !Self.exists(destination) {
                do {
                    try manager.moveItem(at: backup, to: destination) // put the old copy back
                } catch {
                    // Never leave the user guessing where their plug-in went.
                    throw PluginInstallError.restoreFailed(backup: backup)
                }
            }
            throw error
        }
        return backup
    }

    /// Moves the installed plug-in to the backup folder; returns where it went.
    @discardableResult
    public func remove(now: Date = Date()) throws -> URL? {
        try moveExistingToBackup(now: now)
    }

    private func moveExistingToBackup(now: Date) throws -> URL? {
        let manager = FileManager.default
        guard Self.exists(destination) else { return nil }
        try manager.createDirectory(at: backupDirectory, withIntermediateDirectories: true)
        var target = backupDirectory.appending(path: PluginInstallLayout.backupName(for: now), directoryHint: .isDirectory)
        var suffix = 2
        while Self.exists(target) {
            target = backupDirectory.appending(path: PluginInstallLayout.backupName(for: now) + "-\(suffix)", directoryHint: .isDirectory)
            suffix += 1
        }
        try manager.moveItem(at: destination, to: target)
        return target
    }

    static func version(ofPlugin plugin: URL) -> SemanticVersion? {
        let info = plugin.appending(path: "Info.lua")
        guard let text = try? String(contentsOf: info, encoding: .utf8) else { return nil }
        return SemanticVersion.fromPluginInfo(text)
    }

    /// A digest of the plug-in's files (names and contents, Finder metadata
    /// ignored), to tell two builds with the same version apart.
    private static func fingerprint(ofPlugin plugin: URL) -> String? {
        let manager = FileManager.default
        guard let enumerator = manager.enumerator(at: plugin, includingPropertiesForKeys: [.isRegularFileKey]) else { return nil }
        // Resolve symlinks on both sides (/var vs /private/var) so the relative
        // names match whatever folder the copy lives in.
        let base = plugin.resolvingSymlinksInPath().path
        var files: [(String, URL)] = []
        for case let url as URL in enumerator {
            guard (try? url.resourceValues(forKeys: [.isRegularFileKey]).isRegularFile) == true,
                  url.lastPathComponent != ".DS_Store" else { continue }
            let full = url.resolvingSymlinksInPath().path
            let relative = full.hasPrefix(base) ? String(full.dropFirst(base.count)) : url.lastPathComponent
            files.append((relative, url))
        }
        guard !files.isEmpty else { return nil }
        var hasher = SHA256()
        for (relative, url) in files.sorted(by: { $0.0 < $1.0 }) {
            hasher.update(data: Data(relative.utf8))
            guard let data = try? Data(contentsOf: url) else { return nil }
            hasher.update(data: data)
        }
        return hasher.finalize().map { String(format: "%02x", $0) }.joined()
    }

    private static func removeFinderMetadata(in folder: URL) {
        let manager = FileManager.default
        guard let enumerator = manager.enumerator(at: folder, includingPropertiesForKeys: nil) else { return }
        for case let url as URL in enumerator where url.lastPathComponent == ".DS_Store" {
            try? manager.removeItem(at: url)
        }
    }
}

public enum PluginInstallError: Error, Equatable, LocalizedError, Sendable {
    case bundledPluginUnreadable(URL)
    case lightroomIsRunning
    /// Installing failed and the previous copy could not be put back.
    case restoreFailed(backup: URL)

    public var errorDescription: String? {
        switch self {
        case .bundledPluginUnreadable:
            "This copy of Lumerist does not contain a readable Lightroom plug-in. Download Lumerist again from GitHub."
        case .lightroomIsRunning:
            "Quit Lightroom Classic first: it keeps plug-in code in memory."
        case .restoreFailed(let backup):
            "Installing failed and the previous plug-in could not be put back. Your copy is safe in \(backup.path); move it back to Lightroom's Modules folder or install again."
        }
    }
}
