import AppKit
import Foundation
import LumeristCore
import Observation
import ServiceManagement

/// App-wide preferences (everything that is not part of the Dial Key profile).
@MainActor
@Observable
final class AppPreferences {
    private enum Key {
        static let startHidden = "org.lumerist.start-hidden"
        static let quitWithLightroom = "org.lumerist.quit-with-lightroom"
        /// Also read by the Lightroom plug-in (Init.lua → OpenApp.lua) with `defaults read`.
        static let openWithLightroom = "org.lumerist.open-with-lightroom"
        static let quitDelay = "org.lumerist.quit-delay-seconds"
        static let updateRate = "org.lumerist.update-rate-hz"
        static let debugLog = "org.lumerist.debug-log"
        static let theme = "org.lumerist.theme"
        static let themeAppearance = "org.lumerist.theme-appearance"
    }

    static let quitDelayRange = 0.0...60.0

    @ObservationIgnored private let defaults: UserDefaults

    /// Launch with only the menu bar item; the window opens from there or the Dock.
    var startHidden: Bool { didSet { defaults.set(startHidden, forKey: Key.startHidden) } }
    /// Quit Lumerist after Lightroom Classic quits (cancelled if it comes back).
    var quitWithLightroom: Bool { didSet { defaults.set(quitWithLightroom, forKey: Key.quitWithLightroom) } }
    /// The plug-in opens Lumerist (in the background) when Lightroom Classic starts.
    var openWithLightroom: Bool { didSet { defaults.set(openWithLightroom, forKey: Key.openWithLightroom) } }
    var quitDelaySeconds: Double {
        didSet {
            let clamped = min(Self.quitDelayRange.upperBound, max(Self.quitDelayRange.lowerBound, quitDelaySeconds))
            if clamped != quitDelaySeconds { quitDelaySeconds = clamped }
            defaults.set(quitDelaySeconds, forKey: Key.quitDelay)
        }
    }
    /// How often Dial Key changes are sent to Lightroom (they are summed in between).
    var updateRateHz: Double {
        didSet {
            let range = IntentCoalescer.rateRange
            let clamped = min(range.upperBound, max(range.lowerBound, updateRateHz))
            if clamped != updateRateHz { updateRateHz = clamped }
            defaults.set(updateRateHz, forKey: Key.updateRate)
        }
    }
    var debugLogEnabled: Bool {
        didSet {
            defaults.set(debugLogEnabled, forKey: Key.debugLog)
            DebugLog.isEnabled = debugLogEnabled
        }
    }
    /// The look of the app: Galerist (default) or Miami. Applied at once.
    var theme: AppTheme {
        didSet {
            defaults.set(theme.rawValue, forKey: Key.theme)
            ThemeController.apply(theme, appearance: themeAppearance)
        }
    }
    /// Galerist only: follow the Mac, or always light / dark. Applied at once.
    var themeAppearance: ThemeAppearance {
        didSet {
            defaults.set(themeAppearance.rawValue, forKey: Key.themeAppearance)
            ThemeController.apply(theme, appearance: themeAppearance)
        }
    }

    /// Changes with the theme or its light/dark choice. SwiftUI roots use it
    /// as `.id`, so they are rebuilt in the new look.
    var themeKey: String { "\(theme.rawValue).\(themeAppearance.rawValue)" }

    /// The stored theme, readable before any `AppPreferences` exists (launch).
    nonisolated static func storedTheme(_ defaults: UserDefaults) -> (theme: AppTheme, appearance: ThemeAppearance) {
        (defaults.string(forKey: Key.theme).flatMap(AppTheme.init(rawValue:)) ?? .galerist,
         defaults.string(forKey: Key.themeAppearance).flatMap(ThemeAppearance.init(rawValue:)) ?? .system)
    }

    init(defaults: UserDefaults = .standard) {
        self.defaults = defaults
        startHidden = defaults.bool(forKey: Key.startHidden)
        // Lumerist only works inside Lightroom Classic, so by default it
        // follows Lightroom: opened by the plug-in, closed 3 s after it.
        quitWithLightroom = defaults.object(forKey: Key.quitWithLightroom) as? Bool ?? true
        openWithLightroom = defaults.object(forKey: Key.openWithLightroom) as? Bool ?? true
        quitDelaySeconds = defaults.object(forKey: Key.quitDelay) as? Double ?? 3
        updateRateHz = defaults.object(forKey: Key.updateRate) as? Double ?? IntentCoalescer.defaultRate
        debugLogEnabled = defaults.bool(forKey: Key.debugLog)
        // Read only: the app applies the stored theme at launch (ThemeController.applyStored).
        let stored = Self.storedTheme(defaults)
        theme = stored.theme
        themeAppearance = stored.appearance
        DebugLog.isEnabled = debugLogEnabled
    }

    /// Login item state, read live from macOS.
    var opensAtLogin: Bool { SMAppService.mainApp.status == .enabled }

    func setOpensAtLogin(_ enabled: Bool) throws {
        if enabled {
            try SMAppService.mainApp.register()
        } else {
            try SMAppService.mainApp.unregister()
        }
    }
}

/// Watches Lightroom Classic launching and quitting.
@MainActor
final class LightroomLifecycle {
    /// Lightroom Classic's bundle identifier, for every check of the app itself.
    nonisolated static let bundleIdentifier = "com.adobe.LightroomClassicCC7"

    private var observers: [NSObjectProtocol] = []
    private var pendingQuit: Task<Void, Never>?
    private let preferences: AppPreferences

    init(preferences: AppPreferences) {
        self.preferences = preferences
        let center = NSWorkspace.shared.notificationCenter
        observers.append(center.addObserver(
            forName: NSWorkspace.didTerminateApplicationNotification, object: nil, queue: .main
        ) { [weak self] note in
            guard Self.isLightroom(note) else { return }
            MainActor.assumeIsolated { self?.lightroomQuit() }
        })
        observers.append(center.addObserver(
            forName: NSWorkspace.didLaunchApplicationNotification, object: nil, queue: .main
        ) { [weak self] note in
            guard Self.isLightroom(note) else { return }
            MainActor.assumeIsolated { self?.lightroomLaunched() }
        })
    }

    static var isLightroomRunning: Bool {
        !NSRunningApplication.runningApplications(withBundleIdentifier: bundleIdentifier).isEmpty
    }

    nonisolated private static func isLightroom(_ note: Notification) -> Bool {
        let app = note.userInfo?[NSWorkspace.applicationUserInfoKey] as? NSRunningApplication
        return app?.bundleIdentifier == bundleIdentifier
    }

    /// While Lumerist itself quits and reopens Lightroom (plug-in install or
    /// restart), "Quit Lumerist too" must not close Lumerist mid-way. A
    /// counter, so overlapping operations cannot clear each other's guard.
    private static var restartDepth = 0
    static var isRestartingLightroom: Bool { restartDepth > 0 }
    static func beginRestart() { restartDepth += 1 }
    static func endRestart() { restartDepth = max(0, restartDepth - 1) }

    private func lightroomQuit() {
        DebugLog.write("Lightroom Classic quit")
        guard preferences.quitWithLightroom, !Self.isRestartingLightroom else { return }
        let delay = preferences.quitDelaySeconds
        pendingQuit?.cancel()
        pendingQuit = Task { @MainActor in
            try? await Task.sleep(for: .seconds(delay))
            guard !Task.isCancelled, !Self.isLightroomRunning else { return }
            DebugLog.write("Quitting with Lightroom Classic")
            NSApp.terminate(nil)
        }
    }

    private func lightroomLaunched() {
        DebugLog.write("Lightroom Classic launched")
        pendingQuit?.cancel()
        pendingQuit = nil
    }
}

/// Optional troubleshooting log in ~/Library/Logs/Lumerist/Lumerist.log.
/// It records connection states, commands, and Lightroom outcomes only.
enum DebugLog {
    nonisolated(unsafe) static var isEnabled = false
    private static let queue = DispatchQueue(label: "org.lumerist.debug-log")
    private static let maximumBytes = 5_000_000

    static var fileURL: URL {
        FileManager.default.homeDirectoryForCurrentUser
            .appending(path: "Library/Logs/Lumerist/Lumerist.log")
    }

    static func write(_ message: String) {
        guard isEnabled else { return }
        let line = "\(Date().formatted(.iso8601)) \(message)\n"
        queue.async {
            let url = fileURL
            let manager = FileManager.default
            try? manager.createDirectory(at: url.deletingLastPathComponent(), withIntermediateDirectories: true)
            if let size = (try? manager.attributesOfItem(atPath: url.path))?[.size] as? Int, size > maximumBytes {
                try? manager.removeItem(at: url) // Start over rather than grow without bound.
            }
            if let handle = try? FileHandle(forWritingTo: url) {
                handle.seekToEndOfFile()
                handle.write(Data(line.utf8))
                try? handle.close()
            } else {
                try? Data(line.utf8).write(to: url)
            }
        }
    }
}
