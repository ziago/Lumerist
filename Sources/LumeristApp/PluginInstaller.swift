import AppKit
import Foundation
import LumeristCore
import Observation

/// Installs, updates and removes the Lightroom Classic plug-in that ships
/// inside Lumerist.app (Contents/Resources/Lumerist.lrplugin).
///
/// Lightroom keeps plug-in code in memory, so files are only replaced while
/// it is closed. When it is open, "Quit Lightroom & install" asks it to quit
/// normally (exactly like ⌘Q, so its own dialogs still appear), installs, and
/// opens it again. Lightroom is never force-quit.
@MainActor
@Observable
final class PluginInstaller {
    static let shared = PluginInstaller()

    enum Phase: Equatable {
        case idle
        case working(String)
        case finished(String)
        case failed(String)
    }

    private(set) var status: PluginInstallStatus?
    /// Set when this build has no readable bundled plug-in (e.g. `swift run`).
    private(set) var bundleProblem: String?
    private(set) var isLightroomRunning = false
    private(set) var phase: Phase = .idle

    @ObservationIgnored private var observers: [NSObjectProtocol] = []
    @ObservationIgnored private let operation: PluginInstallOperation?
    /// One install/remove/restart at a time (double clicks, menu + button).
    private(set) var isBusy = false

    init(environment rawEnvironment: [String: String] = ProcessInfo.processInfo.environment) {
        let home = FileManager.default.homeDirectoryForCurrentUser
        // LUMERIST_* overrides exist for tests and development only; a
        // distributed (release) build ignores them.
        #if DEBUG
        let environment = rawEnvironment
        #else
        let environment: [String: String] = [:]
        _ = rawEnvironment
        #endif
        let modules = environment["LUMERIST_MODULES_DIR"].map { URL(fileURLWithPath: $0, isDirectory: true) }
            ?? PluginInstallLayout.modulesDirectory(home: home)
        let backups = environment["LUMERIST_BACKUP_DIR"].map { URL(fileURLWithPath: $0, isDirectory: true) }
            ?? (environment["LUMERIST_MODULES_DIR"] != nil
                ? modules.deletingLastPathComponent().appending(path: "Lumerist-plugin-backups", directoryHint: .isDirectory)
                : PluginInstallLayout.backupDirectory(home: home))
        let source = environment["LUMERIST_PLUGIN_SOURCE"].map { URL(fileURLWithPath: $0, isDirectory: true) }
            ?? Bundle.main.resourceURL?.appending(path: PluginInstallLayout.bundleName, directoryHint: .isDirectory)

        if let source, FileManager.default.fileExists(atPath: source.appending(path: "Info.lua").path) {
            operation = PluginInstallOperation(source: source, modulesDirectory: modules, backupDirectory: backups)
        } else {
            operation = nil
            bundleProblem = "This build does not include the Lightroom plug-in. Download Lumerist from GitHub, or build it with Scripts/build-macos-app.sh."
        }

        let center = NSWorkspace.shared.notificationCenter
        for name in [NSWorkspace.didLaunchApplicationNotification, NSWorkspace.didTerminateApplicationNotification] {
            let quits = name == NSWorkspace.didTerminateApplicationNotification
            observers.append(center.addObserver(forName: name, object: nil, queue: .main) { [weak self] note in
                let app = note.userInfo?[NSWorkspace.applicationUserInfoKey] as? NSRunningApplication
                guard app?.bundleIdentifier == LightroomLifecycle.bundleIdentifier else { return }
                MainActor.assumeIsolated {
                    self?.refreshLightroomState()
                    if quits { self?.updateIfOutdated() }
                }
            })
        }
        refresh()
    }

    var installedLocation: URL? { operation?.destination }

    /// True when the installed plug-in matches (or is newer than) the bundled one.
    var isReady: Bool {
        switch status {
        case .upToDate, .newerInstalled: true
        default: false
        }
    }

    var isWorking: Bool {
        if case .working = phase { return true }
        return false
    }

    func refresh() {
        refreshLightroomState()
        guard let operation else {
            status = nil
            return
        }
        do {
            status = try operation.status()
        } catch {
            status = nil
            bundleProblem = error.localizedDescription
        }
    }

    func refreshLightroomState() {
        isLightroomRunning = Self.runningLightroom != nil
    }

    /// A newer plug-in came with this app: it replaces the installed one by
    /// itself whenever Lightroom is closed, at launch or as soon as Lightroom
    /// quits, keeping the old copy in the backup folder. Only an older copy of
    /// Lumerist's own plug-in (`updatesAutomatically`).
    func updateIfOutdated() {
        guard !isBusy, !LightroomLifecycle.isRestartingLightroom else { return }
        refresh()
        guard status?.updatesAutomatically == true, !isLightroomRunning else { return }
        DebugLog.write("Updating the plug-in by itself")
        performInstall()
    }

    /// Installs or updates. Lightroom must be closed.
    func install() {
        guard !isBusy else { return }
        performInstall()
    }

    private func performInstall() {
        guard let operation, lightroomIsClosed() else { return }
        perform("Installing the plug-in…") {
            let backup = try operation.install()
            return backup == nil
                ? "Plug-in installed. Open Lightroom Classic: Lumerist connects by itself."
                : "Plug-in updated; the previous copy was kept in Lumerist's backup folder."
        }
    }

    /// Moves the installed plug-in to the backup folder. Lightroom must be closed.
    func remove() {
        guard let operation, !isBusy, lightroomIsClosed() else { return }
        perform("Removing the plug-in…") {
            _ = try operation.remove()
            return "Plug-in removed from Lightroom. A copy is kept in Lumerist's backup folder."
        }
    }

    /// Quits Lightroom normally (if it is open), installs, and opens it again
    /// only if it was open. If installing fails after quitting, Lightroom is
    /// reopened anyway so the user is never left without it.
    func quitLightroomAndInstall() async {
        guard operation != nil, !isBusy else { return }
        isBusy = true
        LightroomLifecycle.beginRestart()
        defer {
            isBusy = false
            LightroomLifecycle.endRestart()
        }
        guard let quit = await quitLightroom() else { return }
        performInstall()
        let reopen = quit.wasRunning ? quit.appURL : nil
        if case .failed(let text) = phase {
            if let reopen {
                openLightroom(at: reopen)
                phase = .failed(text + " Lightroom Classic was opened again.")
            }
            return
        }
        guard let reopen else { return }
        let message = phaseMessage
        openLightroom(at: reopen)
        phase = .finished((message ?? "Plug-in installed.") + " Lightroom Classic is starting again.")
    }

    /// Quits and reopens Lightroom so it loads the plug-in (or opens it).
    func restartLightroom() async {
        guard !isBusy else { return }
        isBusy = true
        LightroomLifecycle.beginRestart()
        defer {
            isBusy = false
            LightroomLifecycle.endRestart()
        }
        guard let quit = await quitLightroom() else { return }
        guard let url = quit.appURL else {
            phase = .failed("Lightroom Classic was not found on this Mac.")
            return
        }
        openLightroom(at: url)
        phase = .finished("Lightroom Classic is starting again; Lumerist will connect by itself.")
    }

    func openLightroom() {
        if let url = NSWorkspace.shared.urlForApplication(withBundleIdentifier: LightroomLifecycle.bundleIdentifier) {
            openLightroom(at: url)
        } else {
            phase = .failed("Lightroom Classic was not found on this Mac.")
        }
    }

    func revealInFinder() {
        guard let operation else { return }
        if operation.isInstalled {
            NSWorkspace.shared.activateFileViewerSelecting([operation.destination])
        } else {
            NSWorkspace.shared.activateFileViewerSelecting([operation.modulesDirectory])
        }
    }

    // MARK: Private

    /// Files are only replaced while Lightroom is closed: false, with the
    /// reason in `phase`, while it is open.
    private func lightroomIsClosed() -> Bool {
        refreshLightroomState()
        guard isLightroomRunning else { return true }
        phase = .failed(PluginInstallError.lightroomIsRunning.errorDescription ?? "Quit Lightroom Classic first.")
        return false
    }

    private var phaseMessage: String? {
        if case .finished(let text) = phase { return text }
        return nil
    }

    private static var runningLightroom: NSRunningApplication? {
        NSRunningApplication.runningApplications(withBundleIdentifier: LightroomLifecycle.bundleIdentifier).first
    }

    private struct QuitResult {
        /// Where Lightroom lives (nil when it is not installed).
        let appURL: URL?
        /// Whether it was open, i.e. whether to reopen it afterwards.
        let wasRunning: Bool
    }

    /// Asks Lightroom to quit like ⌘Q and waits (up to 3 minutes, for its own
    /// catalog-backup dialog). Nil when it refused or is still open.
    private func quitLightroom() async -> QuitResult? {
        guard let lightroom = Self.runningLightroom else {
            refreshLightroomState()
            return QuitResult(
                appURL: NSWorkspace.shared.urlForApplication(withBundleIdentifier: LightroomLifecycle.bundleIdentifier),
                wasRunning: false
            )
        }
        let appURL = lightroom.bundleURL
        phase = .working("Waiting for Lightroom Classic to quit… (answer any dialog it shows)")
        guard lightroom.terminate() else {
            phase = .failed("Lightroom Classic did not accept the request to quit. Quit it yourself, then try again.")
            return nil
        }
        for _ in 0..<360 {
            if lightroom.isTerminated { break }
            try? await Task.sleep(for: .milliseconds(500))
        }
        refreshLightroomState()
        guard lightroom.isTerminated else {
            phase = .failed("Lightroom Classic is still open. Finish any dialog it shows (e.g. catalog backup), then try again.")
            return nil
        }
        return QuitResult(appURL: appURL, wasRunning: true)
    }

    private func openLightroom(at url: URL) {
        let configuration = NSWorkspace.OpenConfiguration()
        configuration.activates = true
        NSWorkspace.shared.openApplication(at: url, configuration: configuration) { _, error in
            if let error {
                Task { @MainActor in
                    PluginInstaller.shared.phase = .failed("Could not open Lightroom Classic: \(error.localizedDescription)")
                }
            }
        }
    }

    private func perform(_ working: String, _ action: () throws -> String) {
        phase = .working(working)
        do {
            let message = try action()
            phase = .finished(message)
            DebugLog.write("Plug-in: \(message)")
        } catch {
            phase = .failed(error.localizedDescription)
            DebugLog.write("Plug-in operation failed: \(error.localizedDescription)")
        }
        refresh()
    }
}
