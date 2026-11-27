import Foundation
import Testing
@testable import LumeristCore

/// The bridge token file (protocol 4): created once, private to the user, and
/// the same token on every start. Windows: BridgeTokenTests.cs.
struct BridgeTokenTests {
    private func temporaryFolder() throws -> URL {
        let folder = FileManager.default.temporaryDirectory
            .appending(path: "lumerist-token-\(UUID().uuidString)", directoryHint: .isDirectory)
        try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
        return folder
    }

    private func permissions(_ url: URL) throws -> Int {
        try (FileManager.default.attributesOfItem(atPath: url.path)[.posixPermissions] as? NSNumber)?.intValue ?? -1
    }

    @Test func generatedTokensAreLongRandomAndLowercaseHex() {
        let first = BridgeToken.generate()
        let second = BridgeToken.generate()
        #expect(BridgeToken.isValid(first))
        #expect(BridgeToken.isValid(second))
        #expect(first != second)
    }

    @Test func onlySixtyFourLowercaseHexCharactersAreValid() {
        #expect(BridgeToken.isValid(testToken))
        #expect(!BridgeToken.isValid(String(testToken.dropLast())))
        #expect(!BridgeToken.isValid(testToken + "0"))
        #expect(!BridgeToken.isValid(testToken.uppercased()))
        #expect(!BridgeToken.isValid(String(testToken.dropLast()) + "g"))
        #expect(!BridgeToken.isValid(""))
    }

    @Test func theFileIsCreatedOncePrivateAndReused() throws {
        let parent = try temporaryFolder()
        defer { try? FileManager.default.removeItem(at: parent) }
        let folder = parent.appending(path: "Lumerist", directoryHint: .isDirectory)
        let file = folder.appending(path: BridgeToken.fileName)

        let token = try BridgeToken.loadOrCreate(in: folder)
        #expect(BridgeToken.isValid(token))
        #expect(try String(contentsOf: file, encoding: .utf8) == token)
        #expect(try permissions(file) == 0o600)
        #expect(try permissions(folder) == 0o700)
        #expect(try BridgeToken.loadOrCreate(in: folder) == token)
        let leftovers = try FileManager.default.contentsOfDirectory(atPath: folder.path).filter { $0.hasSuffix(".tmp") }
        #expect(leftovers.isEmpty)
    }

    @Test func aBrokenFileIsReplacedAndAnOpenOneMadePrivate() throws {
        let folder = try temporaryFolder()
        defer { try? FileManager.default.removeItem(at: folder) }
        let file = folder.appending(path: BridgeToken.fileName)

        try Data("not a token".utf8).write(to: file)
        let replaced = try BridgeToken.loadOrCreate(in: folder)
        #expect(BridgeToken.isValid(replaced))
        #expect(try permissions(file) == 0o600)

        // A copy restored from a backup: readable by everyone, upper case, a newline.
        try Data((testToken.uppercased() + "\n").utf8).write(to: file)
        try FileManager.default.setAttributes([.posixPermissions: 0o644], ofItemAtPath: file.path)
        #expect(try BridgeToken.loadOrCreate(in: folder) == testToken)
        #expect(try permissions(file) == 0o600)
    }

    @Test func theDefaultFolderIsLumeristsApplicationSupportFolder() {
        let home = URL(filePath: "/Users/test", directoryHint: .isDirectory)
        #expect(BridgeToken.defaultFolder(home: home).path == "/Users/test/Library/Application Support/Lumerist")
    }
}
