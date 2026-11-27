import Darwin
import Foundation
import Testing
@testable import LumeristCore

/// The Mac's own updater steps (the shared rules are in UpdateFixtureTests).
/// Everything runs in temporary folders; nothing touches the network.
struct MacUpdateTests {
    private func scratch() throws -> URL {
        let folder = FileManager.default.temporaryDirectory.appending(path: "lumerist-update-tests-\(UUID().uuidString)")
        try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
        return folder
    }

    private func write(_ text: String, to url: URL) throws {
        try FileManager.default.createDirectory(at: url.deletingLastPathComponent(), withIntermediateDirectories: true)
        try Data(text.utf8).write(to: url)
    }

    /// A fake Lumerist.app: Info.plist and an executable whose Mach-O header names `architectures`.
    private func fakeApp(at url: URL, version: String = "0.9.2", identifier: String = MacUpdate.bundleIdentifier,
                         minimumSystem: String = "14.0", architectures: [Int32] = [MacUpdate.arm64, MacUpdate.x86_64]) throws {
        let info: [String: Any] = [
            "CFBundleIdentifier": identifier, "CFBundleShortVersionString": version,
            "CFBundleExecutable": "LumeristApp", "LSMinimumSystemVersion": minimumSystem,
        ]
        let plist = try PropertyListSerialization.data(fromPropertyList: info, format: .xml, options: 0)
        try FileManager.default.createDirectory(at: url.appending(path: "Contents/MacOS"), withIntermediateDirectories: true)
        try plist.write(to: url.appending(path: "Contents/Info.plist"))
        var header = Data([0xCA, 0xFE, 0xBA, 0xBE])
        func be32(_ value: UInt32) { header.append(contentsOf: withUnsafeBytes(of: value.bigEndian, Array.init)) }
        be32(UInt32(architectures.count))
        for cpu in architectures {
            be32(UInt32(bitPattern: cpu)); be32(0); be32(4096); be32(16); be32(12)
        }
        try header.write(to: url.appending(path: "Contents/MacOS/LumeristApp"))
    }

    @Test func swapExchangesTwoAppsAndSwappingAgainUndoesIt() throws {
        let folder = try scratch()
        defer { try? FileManager.default.removeItem(at: folder) }
        let current = folder.appending(path: "Lumerist.app"), staged = folder.appending(path: ".Lumerist-0.9.3-x.app")
        try write("old", to: current.appending(path: "Contents/marker"))
        try write("new", to: staged.appending(path: "Contents/marker"))
        try MacUpdate.swap(staged, current)
        #expect(try String(contentsOf: current.appending(path: "Contents/marker"), encoding: .utf8) == "new")
        #expect(try String(contentsOf: staged.appending(path: "Contents/marker"), encoding: .utf8) == "old")
        try MacUpdate.swap(staged, current)
        #expect(try String(contentsOf: current.appending(path: "Contents/marker"), encoding: .utf8) == "old")
    }

    @Test func aFailedSwapLeavesBothInPlace() throws {
        let folder = try scratch()
        defer { try? FileManager.default.removeItem(at: folder) }
        let current = folder.appending(path: "Lumerist.app")
        try write("old", to: current.appending(path: "Contents/marker"))
        #expect(throws: MacUpdateError.self) { try MacUpdate.swap(folder.appending(path: "missing.app"), current) }
        #expect(try String(contentsOf: current.appending(path: "Contents/marker"), encoding: .utf8) == "old")
    }

    @Test func thePrivateFolderIsOnlyForThisUser() throws {
        let parent = try scratch()
        defer { try? FileManager.default.removeItem(at: parent) }
        let folder = try MacUpdate.makePrivateFolder(in: parent)
        let attributes = try FileManager.default.attributesOfItem(atPath: folder.path(percentEncoded: false))
        #expect((attributes[.posixPermissions] as? NSNumber)?.intValue == 0o700)
        #expect((attributes[.ownerAccountID] as? NSNumber)?.uint32Value == getuid())
        let handle = try MacUpdate.createPrivateFile("SHA256SUMS", in: folder)
        try handle.close()
        let file = try FileManager.default.attributesOfItem(atPath: folder.appending(path: "SHA256SUMS").path(percentEncoded: false))
        #expect((file[.posixPermissions] as? NSNumber)?.intValue == 0o600)
        // Never written over.
        #expect(throws: MacUpdateError.self) { _ = try MacUpdate.createPrivateFile("SHA256SUMS", in: folder) }
    }

    @Test func theRequirementsCompile() {
        for requirement in [MacUpdate.appRequirement, MacUpdate.diskImageRequirement] {
            var compiled: SecRequirement?
            #expect(SecRequirementCreateWithString(requirement as CFString, [], &compiled) == errSecSuccess)
        }
        #expect(MacUpdate.appRequirement.contains("identifier \"io.github.ziago.lumerist\""))
        #expect(MacUpdate.appRequirement.contains("certificate leaf[subject.OU] = \"JRNUV73KXY\""))
    }

    @Test func anUnsignedAppIsRefused() throws {
        let folder = try scratch()
        defer { try? FileManager.default.removeItem(at: folder) }
        let app = folder.appending(path: "Lumerist.app")
        try fakeApp(at: app)
        #expect(throws: MacUpdateError.self) { try MacUpdate.checkSignature(of: app, requirement: MacUpdate.appRequirement) }
    }

    @Test func appleSignedCodeIsNotLumerist() {
        // Valid code, but not this team's Developer ID.
        #expect(throws: MacUpdateError.self) {
            try MacUpdate.checkSignature(of: URL(fileURLWithPath: "/System/Applications/Calculator.app"),
                                         requirement: MacUpdate.appRequirement)
        }
    }

    @Test func theBundleMustBeThisAppAtThisVersion() throws {
        let folder = try scratch()
        defer { try? FileManager.default.removeItem(at: folder) }
        let system = OperatingSystemVersion(majorVersion: 15, minorVersion: 1, patchVersion: 0)
        let good = folder.appending(path: "Good.app")
        try fakeApp(at: good)
        try MacUpdate.checkBundle(good, version: "0.9.2", system: system)
        #expect(throws: MacUpdateError.wrongBundle("The new app is version 0.9.2, not 0.9.3.")) {
            try MacUpdate.checkBundle(good, version: "0.9.3", system: system)
        }
        let other = folder.appending(path: "Other.app")
        try fakeApp(at: other, identifier: "com.example.other")
        #expect(throws: MacUpdateError.self) { try MacUpdate.checkBundle(other, version: "0.9.2", system: system) }
        let newer = folder.appending(path: "Newer.app")
        try fakeApp(at: newer, minimumSystem: "15.2")
        #expect(throws: MacUpdateError.needsNewerMacOS("15.2")) { try MacUpdate.checkBundle(newer, version: "0.9.2", system: system) }
        let foreign = folder.appending(path: "Foreign.app")
        try fakeApp(at: foreign, architectures: [MacUpdate.currentArchitecture == MacUpdate.arm64 ? MacUpdate.x86_64 : MacUpdate.arm64])
        #expect(throws: MacUpdateError.wrongArchitecture) { try MacUpdate.checkBundle(foreign, version: "0.9.2", system: system) }
    }

    @Test func machOArchitecturesOfASystemTool() {
        let architectures = MacUpdate.machOArchitectures(URL(fileURLWithPath: "/bin/ls")) ?? []
        #expect(architectures.contains(MacUpdate.currentArchitecture))
    }

    @Test func installBlockers() throws {
        let folder = try scratch()
        defer {
            chmod(folder.path(percentEncoded: false), 0o755)
            try? FileManager.default.removeItem(at: folder)
        }
        #expect(MacUpdate.installBlocker(appURL: folder.appending(path: "debug")) != nil)
        let app = folder.appending(path: "Lumerist.app")
        try fakeApp(at: app)
        #expect(MacUpdate.installBlocker(appURL: app) == nil)
        chmod(folder.path(percentEncoded: false), 0o555)
        #expect(MacUpdate.installBlocker(appURL: app)?.contains("can't replace itself") == true)
    }

    @Test func toolsRunWithATimeout() throws {
        let result = try MacUpdate.run("/bin/echo", ["hello"], timeout: 10)
        #expect(result.status == 0 && String(decoding: result.output, as: UTF8.self) == "hello\n")
        #expect(throws: MacUpdateError.self) { _ = try MacUpdate.run("/bin/sleep", ["5"], timeout: 0.2) }
    }

    /// Opt-in, with a real release disk image and no download:
    /// `LUMERIST_UPDATE_E2E_DMG=/path/Lumerist-0.9.1.dmg LUMERIST_UPDATE_E2E_VERSION=0.9.1 swift test --filter liveDiskImage`.
    /// Runs every Mac check of "Update now" up to the swap, on a copy in a private folder.
    @Test(.enabled(if: ProcessInfo.processInfo.environment["LUMERIST_UPDATE_E2E_DMG"] != nil))
    func liveDiskImageChecks() throws {
        let environment = ProcessInfo.processInfo.environment
        let image = URL(fileURLWithPath: try #require(environment["LUMERIST_UPDATE_E2E_DMG"]))
        let version = try #require(environment["LUMERIST_UPDATE_E2E_VERSION"])
        let folder = try MacUpdate.makePrivateFolder()
        defer { try? FileManager.default.removeItem(at: folder) }
        try MacUpdate.checkDiskImageSignature(image)
        let mounted = try MacUpdate.attach(image, at: folder.appending(path: "mount"))
        defer { MacUpdate.detach(mounted) }
        let inImage = try MacUpdate.appInImage(mounted)
        try MacUpdate.checkSignature(of: inImage, requirement: MacUpdate.appRequirement)
        try MacUpdate.checkBundle(inImage, version: version)
        let copy = folder.appending(path: "Lumerist.app")
        try MacUpdate.copyApp(inImage, to: copy)
        try MacUpdate.checkSignature(of: copy, requirement: MacUpdate.appRequirement)
        try MacUpdate.assessNotarization(of: copy)
        try MacUpdate.checkBundle(copy, version: version)
    }
}
