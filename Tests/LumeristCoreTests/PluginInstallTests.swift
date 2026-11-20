import Foundation
import Testing
@testable import LumeristCore

struct SemanticVersionTests {
    @Test func parsesTagsAndPlainVersions() {
        #expect(SemanticVersion("0.9.0") == SemanticVersion(major: 0, minor: 9, patch: 0))
        #expect(SemanticVersion("v1.2.3") == SemanticVersion(major: 1, minor: 2, patch: 3))
        #expect(SemanticVersion("1.2") == SemanticVersion(major: 1, minor: 2, patch: 0))
        #expect(SemanticVersion("1.0.0-beta.2")?.prerelease == "beta.2")
        #expect(SemanticVersion("1.0.0+42") == SemanticVersion(major: 1, minor: 0, patch: 0))
        #expect(SemanticVersion("") == nil)
        #expect(SemanticVersion("1.x.0") == nil)
        #expect(SemanticVersion("1.2.3.4") == nil)
    }

    @Test func ordersVersionsIncludingPrereleases() throws {
        let order = ["0.3.0", "0.9.0-beta.1", "0.9.0-beta.2", "0.9.0", "0.10.0", "1.0.0"].map { SemanticVersion($0)! }
        #expect(order == order.sorted())
        #expect(SemanticVersion("0.9.0-beta.10")! > SemanticVersion("0.9.0-beta.9")!)
    }

    @Test func picksTheNewestReleaseTag() {
        let newest = SemanticVersion.newest(in: ["v0.9.0", "v0.9.1-beta.1", "notes", "v0.9.1", "v0.10.0-beta.1"])
        #expect(newest?.tag == "v0.10.0-beta.1")
        #expect(SemanticVersion.newest(in: ["nightly"]) == nil)
    }

    @Test func readsThePluginInfoVersionTable() {
        let info = """
        return {
            LrPluginName = "Lumerist",
            VERSION = { minor = 9, revision = 1, major = 0, display = "0.9.1 (protocol 3)" },
        }
        """
        #expect(SemanticVersion.fromPluginInfo(info) == SemanticVersion(major: 0, minor: 9, patch: 1))
        #expect(SemanticVersion.fromPluginInfo("return { LrPluginName = \"x\" }") == nil)
    }
}

struct PluginInstallStatusTests {
    private let v09 = SemanticVersion("0.9.0")!
    private let v03 = SemanticVersion("0.3.0")!

    @Test func comparesInstalledAndBundledCopies() {
        #expect(PluginInstallStatus.compare(installed: nil, installedExists: false, bundled: v09, sameContents: false) == .notInstalled)
        #expect(PluginInstallStatus.compare(installed: v03, installedExists: true, bundled: v09, sameContents: false)
                == .outdated(installed: v03, bundled: v09))
        #expect(PluginInstallStatus.compare(installed: nil, installedExists: true, bundled: v09, sameContents: false)
                == .outdated(installed: nil, bundled: v09))
        #expect(PluginInstallStatus.compare(installed: v09, installedExists: true, bundled: v09, sameContents: true) == .upToDate(v09))
        #expect(PluginInstallStatus.compare(installed: v09, installedExists: true, bundled: v09, sameContents: false) == .modified(v09))
        #expect(PluginInstallStatus.compare(installed: v09, installedExists: true, bundled: v03, sameContents: false)
                == .newerInstalled(installed: v09, bundled: v03))
        #expect(PluginInstallStatus.notInstalled.needsAction)
        #expect(!PluginInstallStatus.upToDate(v09).needsAction)
        #expect(PluginInstallStatus.outdated(installed: v03, bundled: v09).updatesAutomatically)
        #expect(PluginInstallStatus.outdated(installed: nil, bundled: v09).updatesAutomatically)
        #expect(!PluginInstallStatus.notInstalled.updatesAutomatically)
        #expect(!PluginInstallStatus.modified(v09).updatesAutomatically)
        #expect(!PluginInstallStatus.upToDate(v09).updatesAutomatically)
        #expect(!PluginInstallStatus.newerInstalled(installed: v09, bundled: v03).updatesAutomatically)
    }

    @Test func backupNamesSortByTime() {
        let utc = TimeZone(identifier: "UTC")!
        let name = PluginInstallLayout.backupName(for: Date(timeIntervalSince1970: 0), timeZone: utc)
        #expect(name == "Lumerist.lrplugin.backup-19700101-000000")
    }

    @Test func layoutKeepsBackupsOutsideModules() {
        let home = URL(fileURLWithPath: "/Users/someone")
        let modules = PluginInstallLayout.modulesDirectory(home: home).path
        let backups = PluginInstallLayout.backupDirectory(home: home).path
        #expect(modules == "/Users/someone/Library/Application Support/Adobe/Lightroom/Modules")
        #expect(!backups.hasPrefix(modules))
    }
}

struct PluginInstallOperationTests {
    private func makeSandbox() throws -> (root: URL, operation: PluginInstallOperation) {
        let root = FileManager.default.temporaryDirectory.appending(path: "lumerist-install-\(UUID().uuidString)")
        let source = root.appending(path: "bundle/Lumerist.lrplugin")
        try FileManager.default.createDirectory(at: source, withIntermediateDirectories: true)
        try writePlugin(at: source, version: (0, 9, 0), body: "-- new")
        let operation = PluginInstallOperation(
            source: source,
            modulesDirectory: root.appending(path: "Modules"),
            backupDirectory: root.appending(path: "backups")
        )
        return (root, operation)
    }

    private func writePlugin(at url: URL, version: (Int, Int, Int), body: String) throws {
        try FileManager.default.createDirectory(at: url, withIntermediateDirectories: true)
        let info = "return { VERSION = { major = \(version.0), minor = \(version.1), revision = \(version.2) } }\n"
        try info.write(to: url.appending(path: "Info.lua"), atomically: true, encoding: .utf8)
        try body.write(to: url.appending(path: "Init.lua"), atomically: true, encoding: .utf8)
    }

    @Test func installsIntoAnEmptyModulesFolder() throws {
        let (root, operation) = try makeSandbox()
        defer { try? FileManager.default.removeItem(at: root) }
        #expect(try operation.status() == .notInstalled)
        #expect(try operation.install() == nil)
        #expect(try operation.status() == .upToDate(SemanticVersion("0.9.0")!))
        // No staging leftovers next to the plug-in.
        let modules = try FileManager.default.contentsOfDirectory(atPath: operation.modulesDirectory.path)
        #expect(modules == ["Lumerist.lrplugin"])
    }

    @Test func updatingKeepsTheOldCopyAsABackup() throws {
        let (root, operation) = try makeSandbox()
        defer { try? FileManager.default.removeItem(at: root) }
        try writePlugin(at: operation.destination, version: (0, 3, 0), body: "-- old")
        #expect(try operation.status() == .outdated(installed: SemanticVersion("0.3.0"), bundled: SemanticVersion("0.9.0")!))

        let backup = try #require(try operation.install(now: Date(timeIntervalSince1970: 1_000)))
        #expect(PluginInstallOperation.version(ofPlugin: backup) == SemanticVersion("0.3.0"))
        #expect(backup.path.hasPrefix(operation.backupDirectory.path))
        #expect(operation.installedVersion == SemanticVersion("0.9.0"))
    }

    @Test func aDanglingSymlinkIsMovedAsideNotTreatedAsMissing() throws {
        let (root, operation) = try makeSandbox()
        defer { try? FileManager.default.removeItem(at: root) }
        try FileManager.default.createDirectory(at: operation.modulesDirectory, withIntermediateDirectories: true)
        try FileManager.default.createSymbolicLink(
            at: operation.destination,
            withDestinationURL: root.appending(path: "a-working-copy-that-was-deleted")
        )
        #expect(operation.isInstalled)

        let backup = try #require(try operation.install(now: Date(timeIntervalSince1970: 2_000)))
        #expect(backup.path.hasPrefix(operation.backupDirectory.path))
        #expect(operation.installedVersion == SemanticVersion("0.9.0"))
    }

    @Test func sameVersionWithOtherFilesIsReportedAsModified() throws {
        let (root, operation) = try makeSandbox()
        defer { try? FileManager.default.removeItem(at: root) }
        try writePlugin(at: operation.destination, version: (0, 9, 0), body: "-- a development build")
        #expect(try operation.status() == .modified(SemanticVersion("0.9.0")!))
        // Finder metadata does not count as a difference.
        try operation.install()
        try Data([0, 1, 2]).write(to: operation.destination.appending(path: ".DS_Store"))
        #expect(try operation.status() == .upToDate(SemanticVersion("0.9.0")!))
    }

    @Test func removingMovesThePluginToTheBackupFolder() throws {
        let (root, operation) = try makeSandbox()
        defer { try? FileManager.default.removeItem(at: root) }
        try operation.install()
        let first = try #require(try operation.remove(now: Date(timeIntervalSince1970: 2_000)))
        #expect(!operation.isInstalled)
        try operation.install()
        // Two removals in the same second never overwrite each other.
        let second = try #require(try operation.remove(now: Date(timeIntervalSince1970: 2_000)))
        #expect(first != second)
        #expect(FileManager.default.fileExists(atPath: first.path))
        #expect(FileManager.default.fileExists(atPath: second.path))
        #expect(try operation.remove() == nil)
    }

    @Test func refusesAnUnreadableBundledPlugin() throws {
        let (root, operation) = try makeSandbox()
        defer { try? FileManager.default.removeItem(at: root) }
        try FileManager.default.removeItem(at: operation.source.appending(path: "Info.lua"))
        #expect(throws: PluginInstallError.bundledPluginUnreadable(operation.source)) { try operation.install() }
        #expect(!operation.isInstalled)
    }
}
