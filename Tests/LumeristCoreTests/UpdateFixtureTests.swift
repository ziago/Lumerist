import CryptoKit
import Foundation
import Testing
@testable import LumeristCore

// Runs Shared/fixtures/updates (format: Shared/fixtures/README.md, "Update
// cases"): the choice of release and files, the URL rules, the checksums and
// their signature, the Highlights and the update window's timing. The
// Windows runner reads the same files.

private enum UpdateRunner {
    static func run(_ testCase: [String: Any], id: String) throws -> [String] {
        switch id.split(separator: "/").dropFirst().first {
        case "releases": try releases(testCase)
        case "urls": try urls(testCase)
        case "checksums": try checksums(testCase, id: id)
        case "highlights": try highlights(testCase)
        case "prompt": try prompt(testCase)
        default: throw FixtureError("unknown kind of update case")
        }
    }

    // MARK: Releases

    static func releases(_ testCase: [String: Any]) throws -> [String] {
        guard let current = testCase["current"] as? String, let list = testCase["releases"] else {
            throw FixtureError("a releases case needs current and releases")
        }
        let data = try JSONSerialization.data(withJSONObject: list)
        let releases = try UpdateFeedRelease.decodeList(data)
        let allowFiles = testCase["allowFileURLs"] as? Bool ?? false
        let update = UpdateChoice.update(in: releases, current: current)
        guard let expected = testCase["expect"] as? [String: Any] else {
            return update.map { ["offered \($0.version) instead of nothing"] } ?? []
        }
        guard let update else { return ["offered nothing"] }
        var failures: [String] = []
        if update.version.description != expected["version"] as? String { failures.append("version \(update.version)") }
        if update.tag != expected["tag"] as? String { failures.append("tag \(update.tag)") }
        if update.page.absoluteString != expected["page"] as? String { failures.append("page \(update.page)") }
        let platforms = expected["platforms"] as? [String: Any] ?? [:]
        for platform in UpdatePlatform.allCases {
            let assets = update.assets(for: platform, allowFileURLs: allowFiles)
            let wanted = platforms[platform.rawValue] as? [String: Any]
            guard let wanted else {
                if let assets { failures.append("\(platform.rawValue): \(assets.installer.name) instead of none") }
                continue
            }
            guard let assets else {
                failures.append("\(platform.rawValue): none")
                continue
            }
            let roles: [(String, UpdateAsset?)] = [("installer", assets.installer), ("checksum", assets.checksum),
                                                   ("sums", assets.sums), ("signature", assets.signature)]
            for (role, asset) in roles {
                let want = wanted[role] as? [String: Any]
                if describe(asset) != describe(want) {
                    failures.append("\(platform.rawValue) \(role): \(describe(asset)) ≠ \(describe(want))")
                }
            }
        }
        return failures
    }

    private static func describe(_ asset: UpdateAsset?) -> String {
        guard let asset else { return "none" }
        return "\(asset.name) \(asset.url.absoluteString) \(asset.size) \(asset.sha256 ?? "-")"
    }

    private static func describe(_ asset: [String: Any]?) -> String {
        guard let asset else { return "none" }
        let size = (asset["size"] as? NSNumber)?.intValue ?? -1
        return "\(asset["name"] as? String ?? "?") \(asset["url"] as? String ?? "?") \(size) \(asset["sha256"] as? String ?? "-")"
    }

    // MARK: URLs

    static func urls(_ testCase: [String: Any]) throws -> [String] {
        guard let text = testCase["url"] as? String, let expected = testCase["expect"] as? [String: Bool] else {
            throw FixtureError("a urls case needs url and expect")
        }
        let allowFiles = testCase["allowFileURLs"] as? Bool ?? false
        let url = URL(string: text)
        var failures: [String] = []
        if let hop = expected["hop"] {
            let got = url.map { UpdateURLRule.isTrustedHop($0, allowFileURLs: allowFiles) } ?? false
            if got != hop { failures.append("hop \(got)") }
        }
        if let asset = expected["asset"] {
            guard let tag = testCase["tag"] as? String, let name = testCase["name"] as? String else {
                throw FixtureError("an asset URL case needs tag and name")
            }
            let got = UpdateURLRule.isTrustedAssetURL(text, tag: tag, name: name, allowFileURLs: allowFiles)
            if got != asset { failures.append("asset \(got)") }
        }
        if let page = expected["releasePage"] {
            let got = url.map(UpdateURLRule.isTrustedReleasePage) ?? false
            if got != page { failures.append("releasePage \(got)") }
        }
        return failures
    }

    // MARK: Checksums

    static func checksums(_ testCase: [String: Any], id: String) throws -> [String] {
        let kind = id.split(separator: "/").last.map(String.init) ?? ""
        if kind.hasPrefix("sha256-file-") {
            guard let text = testCase["checksumFile"] as? String, let name = testCase["name"] as? String else {
                throw FixtureError("a .sha256 case needs checksumFile and name")
            }
            return compare({ try UpdateChecksums.checksumFileSHA256(Data(text.utf8), name: name) }, testCase["expect"])
        }
        if kind.hasPrefix("list-") {
            guard let sums = testCase["sums"] as? String, let signature = testCase["signature"] as? String,
                  let keys = testCase["keys"] as? [String], let name = testCase["name"] as? String else {
                throw FixtureError("a list case needs sums, signature, keys and name")
            }
            return compare({
                try SignedChecksumList(list: Data(sums.utf8), signature: Data(signature.utf8), trustedKeys: keys)
                    .expectedSHA256(for: name)
            }, testCase["expect"])
        }
        if kind.hasPrefix("agree-") {
            guard let actual = testCase["actual"] as? String, let listed = testCase["listed"] as? String else {
                throw FixtureError("an agreement case needs actual and listed")
            }
            let outcome: String
            do {
                try UpdateChecksums.verify(actual: actual, listed: listed,
                                           checksumFile: testCase["checksumFile"] as? String, digest: testCase["digest"] as? String)
                outcome = "ok"
            } catch UpdateChecksumError.mismatch(let source) {
                outcome = "mismatch \(source.rawValue)"
            }
            let expected: String
            if let error = testCase["expect"] as? [String: String] {
                expected = "\(error["error"] ?? "?") \(error["source"] ?? "?")"
            } else {
                expected = testCase["expect"] as? String ?? "?"
            }
            return outcome == expected ? [] : [outcome]
        }
        throw FixtureError("unknown kind of checksums case")
    }

    private static func compare(_ body: () throws -> String, _ expect: Any?) -> [String] {
        let expected = expect as? [String: String] ?? [:]
        do {
            let hex = try body()
            return hex == expected["sha256"] ? [] : ["sha256 \(hex)"]
        } catch let error as UpdateChecksumError {
            return name(error) == expected["error"] ? [] : ["error \(name(error))"]
        } catch {
            return ["error \(error)"]
        }
    }

    private static func name(_ error: UpdateChecksumError) -> String {
        switch error {
        case .malformedChecksumFile: "malformedChecksumFile"
        case .nameMismatch: "nameMismatch"
        case .badSignature: "badSignature"
        case .malformedList: "malformedList"
        case .notListed: "notListed"
        case .mismatch(let source): "mismatch \(source.rawValue)"
        }
    }

    // MARK: Highlights

    static func highlights(_ testCase: [String: Any]) throws -> [String] {
        guard let body = testCase["body"] as? String, let expected = testCase["expect"] as? [String] else {
            throw FixtureError("a highlights case needs body and expect")
        }
        let got = UpdateHighlights.parse(body)
        return got == expected ? [] : ["got \(got)"]
    }

    // MARK: Prompt

    static func prompt(_ testCase: [String: Any]) throws -> [String] {
        let dates = ISO8601DateFormatter()
        func date(_ key: String, in object: [String: Any] = testCase) -> Date? {
            (object[key] as? String).flatMap(dates.date(from:))
        }
        switch testCase["action"] as? String {
        case "later":
            guard let now = date("now"), let expected = (testCase["expect"] as? [String: Any]).flatMap({ date("laterUntil", in: $0) }) else {
                throw FixtureError("a later case needs now and expect.laterUntil")
            }
            var schedule = UpdatePromptSchedule()
            schedule.later(at: now)
            return schedule.laterUntil == expected ? [] : ["laterUntil \(String(describing: schedule.laterUntil))"]
        case "skip":
            guard let version = testCase["version"] as? String,
                  let expected = (testCase["expect"] as? [String: Any])?["skippedVersion"] as? String else {
                throw FixtureError("a skip case needs version and expect.skippedVersion")
            }
            var schedule = UpdatePromptSchedule()
            schedule.skip(version)
            return schedule.skippedVersion == expected ? [] : ["skippedVersion \(schedule.skippedVersion ?? "nil")"]
        default:
            guard let available = testCase["available"] as? String, let current = testCase["current"] as? String,
                  let now = date("now"), let triggerName = testCase["trigger"] as? String,
                  let trigger = UpdatePromptSchedule.Trigger(rawValue: triggerName),
                  let expected = testCase["expect"] as? Bool else {
                throw FixtureError("a prompt case needs available, current, now, trigger and expect")
            }
            let schedule = UpdatePromptSchedule(skippedVersion: testCase["skippedVersion"] as? String, laterUntil: date("laterUntil"))
            let got = schedule.shouldShow(available: available, current: current, trigger: trigger, now: now)
            return got == expected ? [] : ["show \(got)"]
        }
    }
}

struct UpdateFixtureTests {
    @Test func sharedUpdateCases() throws {
        try Fixtures.run("updates", try Fixtures.cases(in: "updates")) { testCase, id in
            try UpdateRunner.run(testCase, id: id)
        }
    }

    /// The embedded keys are exactly those of Shared/update-public-keys.json
    /// (check-parity checks the C# list against the same file).
    @Test func trustedKeysMatchTheSharedFile() throws {
        let file = try Fixtures.object(at: Fixtures.root.appendingPathComponent("Shared/update-public-keys.json"))
        #expect(file["updatePublicKeys"] as? [String] == UpdateTrust.updatePublicKeys)
        for key in UpdateTrust.updatePublicKeys {
            let spki = try #require(Data(base64Encoded: key))
            #expect(throws: Never.self) { _ = try P256.Signing.PublicKey(derRepresentation: spki) }
        }
    }
}

