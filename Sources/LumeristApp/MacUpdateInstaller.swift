import AppKit
import CoreServices
import Foundation
import LumeristCore

/// What stopped "Update now", as the update window says it. Every failure
/// leaves the running Lumerist as it was.
struct UpdateFailure: Error, Equatable {
    enum Kind: Equatable {
        /// The release failed a check: never installed automatically.
        case cantInstall
        /// The network, the disk or a tool: trying again may work.
        case stopped
        case cancelled
        /// The previous copy could not be put back (should never happen).
        case notRestored
    }

    let kind: Kind
    let detail: String

    var canRetry: Bool { kind == .stopped }

    var headline: String {
        switch kind {
        case .cantInstall: "This release can't be installed automatically."
        case .stopped: "The update stopped before it finished."
        case .cancelled: "The update was cancelled."
        case .notRestored: "Lumerist could not put the previous version back."
        }
    }
}

/// "Update now" on the Mac. The order is fixed (docs/ARCHITECTURE.md, Network):
///
/// 1. `SHA256SUMS` and `SHA256SUMS.sig` from the release's assets; the
///    signature must verify under a key in `UpdateTrust`; the list must name
///    the DMG.
/// 2. The DMG (and its `.sha256` when the release has one), into a private
///    folder; its SHA-256 must equal the listed one, the `.sha256` and the
///    API digest.
/// 3. The DMG's own Developer ID signature, then it is mounted read-only and
///    hidden; Lumerist.app inside must meet the Developer ID requirement and
///    be exactly the release's version. Nothing on the image is run.
/// 4. A copy next to the running app (`ditto`), checked again: requirement,
///    Gatekeeper's notarization verdict, version.
/// 5. The copy and the running app swap places (atomically where the disk
///    allows); the new app is opened; only once it has finished launching is
///    the old copy deleted and this one quits. If it can't start, the old
///    copy is swapped back.
@MainActor
final class MacUpdateInstaller {
    enum Step: Equatable {
        case downloading(received: Int, total: Int)
        case checking
        case installing
        case restarting
    }

    private let update: AvailableUpdate
    private let assets: UpdateAssets
    private let appURL: URL
    private let allowFileURLs: Bool

    init(update: AvailableUpdate, assets: UpdateAssets, appURL: URL = Bundle.main.bundleURL,
         allowFileURLs: Bool = UpdateEnvironment.allowsFileURLs) {
        self.update = update
        self.assets = assets
        self.appURL = appURL
        self.allowFileURLs = allowFileURLs
    }

    /// Runs every step; on success the new version is running and this app
    /// quits. Throws an `UpdateFailure` otherwise.
    func run(step: @escaping @MainActor (Step) -> Void) async throws {
        if let blocker = MacUpdate.installBlocker(appURL: appURL) { throw UpdateFailure(kind: .cantInstall, detail: blocker) }
        let folder: URL
        do { folder = try MacUpdate.makePrivateFolder() } catch { throw failure(error) }
        defer { try? FileManager.default.removeItem(at: folder) }
        let installer = assets.installer

        // 1. The signed list.
        step(.downloading(received: 0, total: installer.size))
        let sums = try await fetch(assets.sums, into: folder)
        let signature = try await fetch(assets.signature, into: folder)
        let listed: String
        do {
            let list = try SignedChecksumList(list: Data(contentsOf: sums.file), signature: Data(contentsOf: signature.file))
            listed = try list.expectedSHA256(for: installer.name)
        } catch UpdateChecksumError.notListed {
            throw UpdateFailure(kind: .cantInstall, detail: "The release's signed checksums don't list \(installer.name).")
        } catch {
            throw UpdateFailure(kind: .cantInstall, detail: "The release's checksums are not signed with Lumerist's key.")
        }

        // 2. The disk image, and its .sha256 when there is one.
        var checksumFile: String?
        if let asset = assets.checksum {
            let file = try await fetch(asset, into: folder)
            do {
                checksumFile = try UpdateChecksums.checksumFileSHA256(Data(contentsOf: file.file), name: installer.name)
            } catch {
                throw UpdateFailure(kind: .cantInstall, detail: "\(asset.name) does not describe \(installer.name).")
            }
        }
        let image = try await fetch(installer, into: folder) { received in
            Task { @MainActor in step(.downloading(received: received, total: installer.size)) }
        }
        step(.checking)
        do {
            try UpdateChecksums.verify(actual: image.sha256, listed: listed, checksumFile: checksumFile, digest: installer.sha256)
        } catch {
            throw UpdateFailure(kind: .cantInstall, detail: "The download doesn't match the checksums signed for this release.")
        }

        // 3. The image and the app inside it; 4. the copy beside this app.
        let version = update.version.description
        let staged = MacUpdate.stagingURL(beside: appURL, version: version)
        let mountPoint = folder.appending(path: "mount", directoryHint: .isDirectory)
        try await offMain {
            try MacUpdate.checkDiskImageSignature(image.file)
            let mounted = try MacUpdate.attach(image.file, at: mountPoint)
            defer { MacUpdate.detach(mounted) }
            let inImage = try MacUpdate.appInImage(mounted)
            try MacUpdate.checkSignature(of: inImage, requirement: MacUpdate.appRequirement)
            try MacUpdate.checkBundle(inImage, version: version)
            try MacUpdate.copyApp(inImage, to: staged)
        }
        step(.installing)
        do {
            try await offMain {
                try MacUpdate.checkSignature(of: staged, requirement: MacUpdate.appRequirement)
                try MacUpdate.assessNotarization(of: staged)
                try MacUpdate.checkBundle(staged, version: version)
            }
        } catch {
            try? FileManager.default.removeItem(at: staged)
            throw error
        }

        // 5. Swap, open the new app, and only then let go of the old one.
        do {
            try MacUpdate.swap(staged, appURL)
        } catch {
            try? FileManager.default.removeItem(at: staged)
            throw failure(error)
        }
        // From here `staged` holds the previous version.
        step(.restarting)
        do {
            try await launchNewVersion()
        } catch {
            do {
                try MacUpdate.swap(staged, appURL)
                try? FileManager.default.removeItem(at: staged)
            } catch {
                throw UpdateFailure(kind: .notRestored, detail: "The previous version is kept at \(staged.path(percentEncoded: false)).")
            }
            throw UpdateFailure(kind: .stopped, detail: "The new version did not start: \(error.localizedDescription)")
        }
        DebugLog.write("Updated to Lumerist \(version); quitting the previous version")
        try? FileManager.default.removeItem(at: staged)
        try? FileManager.default.removeItem(at: folder)
        NSApp.terminate(nil)
    }

    private func fetch(_ asset: UpdateAsset, into folder: URL,
                       progress: @escaping @Sendable (Int) -> Void = { _ in }) async throws -> UpdateDownload.Result {
        let result: UpdateDownload.Result
        do {
            result = try await UpdateDownload.fetch(asset, into: folder, allowFileURLs: allowFileURLs, progress: progress)
        } catch {
            throw failure(error)
        }
        // The small files carry an API digest too.
        if let digest = asset.sha256, digest != result.sha256 {
            throw UpdateFailure(kind: .cantInstall, detail: "\(asset.name) doesn't match the digest GitHub lists for it.")
        }
        return result
    }

    /// Opens the swapped-in app as a new instance and waits (up to 20 s)
    /// until it has finished launching.
    private func launchNewVersion() async throws {
        // Launch Services must read the new Info.plist, not a cached one.
        LSRegisterURL(appURL as CFURL, true)
        let configuration = NSWorkspace.OpenConfiguration()
        configuration.createsNewApplicationInstance = true
        configuration.activates = true
        configuration.arguments = ["--updated"]
        let app = try await NSWorkspace.shared.openApplication(at: appURL, configuration: configuration)
        for _ in 0..<200 where !app.isFinishedLaunching && !app.isTerminated {
            try? await Task.sleep(for: .milliseconds(100))
        }
        guard app.isFinishedLaunching, !app.isTerminated else {
            app.forceTerminate()
            throw UpdateFailure(kind: .stopped, detail: "it did not finish launching")
        }
    }

    private func offMain(_ body: @escaping @Sendable () throws -> Void) async throws {
        do {
            try await Task.detached(priority: .userInitiated) { try body() }.value
        } catch {
            throw failure(error)
        }
    }

    private func failure(_ error: Error) -> UpdateFailure {
        if let failure = error as? UpdateFailure { return failure }
        if let download = error as? UpdateDownload.Failure {
            switch download {
            case .cancelled: return UpdateFailure(kind: .cancelled, detail: "Nothing was changed.")
            case .untrustedAddress, .tooManyRedirects, .wrongSize:
                return UpdateFailure(kind: .cantInstall, detail: download.localizedDescription)
            case .status, .transport:
                return UpdateFailure(kind: .stopped, detail: download.localizedDescription)
            }
        }
        if let mac = error as? MacUpdateError {
            switch mac {
            case .privateFolder, .copy, .swap, .tool:
                return UpdateFailure(kind: .stopped, detail: mac.localizedDescription)
            default:
                return UpdateFailure(kind: .cantInstall, detail: mac.localizedDescription)
            }
        }
        if error is CancellationError { return UpdateFailure(kind: .cancelled, detail: "Nothing was changed.") }
        return UpdateFailure(kind: .stopped, detail: error.localizedDescription)
    }
}
