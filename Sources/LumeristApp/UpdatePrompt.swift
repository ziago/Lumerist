import AppKit
import Observation
import SwiftUI
import LumeristCore

/// Decides when the update window appears (`UpdatePromptSchedule`, kept in
/// the user defaults) and owns it. It appears after the check at launch when
/// a newer version is out, unless it was skipped or put off with "Later", and
/// always after "Check for updates". It comes to the front even when Lumerist
/// started hidden with Lightroom, and floats above Lightroom until answered.
@MainActor
@Observable
final class UpdatePrompter {
    static let shared = UpdatePrompter()

    private(set) var model: UpdatePromptModel?
    @ObservationIgnored private var schedule: UpdatePromptSchedule
    @ObservationIgnored private let defaults: UserDefaults
    @ObservationIgnored private var window: UpdateWindow?
    /// Once per launch.
    @ObservationIgnored private var didShowInstalled = false

    init(defaults: UserDefaults = .standard) {
        self.defaults = defaults
        schedule = UpdatePromptSchedule(reading: { defaults.object(forKey: $0) })
    }

    /// A check found `update`. Shown if the schedule allows it; an open
    /// window that is already installing is never replaced.
    func offer(_ update: AvailableUpdate, current: String, trigger: UpdatePromptSchedule.Trigger) {
        guard schedule.shouldShow(available: update.version.description, current: current, trigger: trigger, now: .now) else { return }
        if let model, model.isBusy || model.update == update {
            window?.bringToFront()
            return
        }
        present(UpdatePromptModel(update: update, current: current, prompter: self))
    }

    /// The new version started by "Update now" (`--updated`): says it is
    /// installed, and offers "Quit Lightroom & update" while Lightroom keeps
    /// the old plug-in.
    func showInstalledIfRelaunched() {
        guard CommandLine.arguments.contains("--updated"), !didShowInstalled, model == nil else { return }
        didShowInstalled = true
        present(UpdatePromptModel(installed: SupportLinks.appVersion, prompter: self))
    }

    // MARK: Answers

    func later() {
        schedule.later(at: .now)
        save()
        close(answered: true)
    }

    func skip(_ version: String) {
        schedule.skip(version)
        save()
        close(answered: true)
    }

    func openReleasePage(_ page: URL) {
        NSWorkspace.shared.open(page)
        later()
    }

    /// Closes the window; `answered` when a button already recorded the choice.
    func close(answered: Bool = false) {
        if answered { model?.isAnswered = true }
        window?.close()
    }

    /// The window closed, however (a button, its close button, Esc): unless
    /// a button answered, that is "Later" while it offered the update.
    private func windowClosed(_ closed: UpdatePromptModel) {
        guard closed === model else { return } // a window this one replaced
        if !closed.isAnswered, closed.closingCountsAsLater {
            schedule.later(at: .now)
            save()
        }
        closed.cancel()
        model = nil
        window = nil
    }

    private func present(_ model: UpdatePromptModel) {
        let previous = window
        self.model = model
        let window = UpdateWindow(model: model) { [weak self, weak model] in
            if let model { self?.windowClosed(model) }
        }
        self.window = window
        previous?.close()
        window.bringToFront()
    }

    private func save() {
        schedule.write { key, value in
            if let value { defaults.set(value, forKey: key) } else { defaults.removeObject(forKey: key) }
        }
    }
}

/// One showing of the update window.
@MainActor
@Observable
final class UpdatePromptModel {
    enum Phase: Equatable {
        case offer
        case working(MacUpdateInstaller.Step)
        case failed(UpdateFailure)
        /// In the new version, after "Update now".
        case installed
    }

    let version: String
    let current: String?
    let highlights: [String]
    let page: URL
    let update: AvailableUpdate?
    /// The Mac's files of this release, when they can be installed automatically.
    let assets: UpdateAssets?
    /// Why "Update now" isn't offered (not an app bundle, a folder this user
    /// can't change, no signed checksums…), or nil.
    let blocker: String?
    private(set) var phase: Phase
    /// A button recorded the choice (Later, Skip, Release page).
    @ObservationIgnored var isAnswered = false
    @ObservationIgnored private weak var prompter: UpdatePrompter?
    @ObservationIgnored private var task: Task<Void, Never>?
    @ObservationIgnored private let isPreview: Bool
    /// Snapshots only: whether Lightroom still has the old plug-in.
    @ObservationIgnored let previewPluginWaits: Bool?

    init(update: AvailableUpdate, current: String, prompter: UpdatePrompter) {
        version = update.version.description
        self.current = current
        highlights = update.highlights
        page = update.page
        self.update = update
        let assets = update.assets(for: .mac, allowFileURLs: UpdateEnvironment.allowsFileURLs)
        self.assets = assets
        blocker = assets == nil ? UpdateCopy.notAutomatic : MacUpdate.installBlocker(appURL: Bundle.main.bundleURL)
        phase = .offer
        self.prompter = prompter
        isPreview = false
        previewPluginWaits = nil
    }

    init(installed version: String, prompter: UpdatePrompter) {
        self.version = version
        current = nil
        highlights = []
        page = UpdateURLRule.releasesPage
        update = nil
        assets = nil
        blocker = nil
        phase = .installed
        self.prompter = prompter
        isPreview = false
        previewPluginWaits = nil
    }

    /// Snapshots only: nothing is downloaded, saved or opened.
    init(preview phase: Phase, version: String = "0.9.3", current: String? = "0.9.2", highlights: [String],
         blocker: String? = nil, pluginWaits: Bool = false) {
        self.version = version
        self.current = current
        self.highlights = highlights
        page = UpdateURLRule.releasesPage
        update = nil
        assets = nil
        self.blocker = blocker
        self.phase = phase
        isPreview = true
        previewPluginWaits = pluginWaits
    }

    var canUpdateNow: Bool { blocker == nil && (assets != nil || isPreview) }

    var isBusy: Bool {
        if case .working = phase { return true }
        return false
    }

    /// Cancel works while downloading, before anything is changed.
    var canCancel: Bool {
        if case .working(.downloading) = phase { return true }
        return false
    }

    var closingCountsAsLater: Bool {
        switch phase {
        case .offer, .failed: true
        case .working, .installed: false
        }
    }

    func updateNow() {
        guard !isPreview, phase == .offer || failureCanRetry, let update, let assets, blocker == nil else { return }
        phase = .working(.downloading(received: 0, total: assets.installer.size))
        let installer = MacUpdateInstaller(update: update, assets: assets)
        task = Task { [weak self] in
            do {
                try await installer.run { step in self?.phase = .working(step) }
            } catch let failure as UpdateFailure {
                DebugLog.write("Update to \(update.version) stopped: \(failure.headline) \(failure.detail)")
                self?.phase = .failed(failure)
                AccessibilityNotification.Announcement(failure.headline).post()
            } catch {
                self?.phase = .failed(UpdateFailure(kind: .stopped, detail: error.localizedDescription))
            }
        }
    }

    private var failureCanRetry: Bool {
        if case .failed(let failure) = phase { return failure.canRetry || failure.kind == .cancelled }
        return false
    }

    func cancel() {
        guard canCancel else { return }
        task?.cancel()
    }

    func later() { if !isPreview { prompter?.later() } }
    func skip() { if !isPreview { prompter?.skip(version) } }
    func releasePage() { if !isPreview { prompter?.openReleasePage(page) } }
    func close() { if !isPreview { prompter?.close() } }
}

/// The update window's words, in one place.
enum UpdateCopy {
    static func title(_ version: String) -> String { "Lumerist \(version) is out" }
    static func installedTitle(_ version: String) -> String { "Lumerist \(version) is installed" }
    static func current(_ version: String) -> String { "You have \(version)." }
    static let whatsNew = "What's new"
    static let updateNow = "Update now"
    static let releasePage = "Release page"
    static let later = "Later"
    static let skip = "Skip this version"
    static let cancel = "Cancel"
    static let close = "Close"
    static let tryAgain = "Try again"
    static let done = "Done"
    static let notAutomatic = "This release can't be installed automatically. Download it from the release page."
    static let unchanged = "Your Lumerist is unchanged."
    static let checking = "Checking the download…"
    static let installing = "Checking the new app and installing it…"
    static let restarting = "Opening the new version…"
    static let keysKept = "Your keys and settings are kept."
    static let pluginWaits = "Lightroom Classic still runs the previous plug-in. Lumerist updates it when Lightroom quits."
    static let quitLightroom = "Quit Lightroom & update"

    static func downloading(_ received: Int, of total: Int) -> String {
        let format = ByteCountFormatStyle(style: .file)
        return "Downloading… \(Int64(received).formatted(format)) of \(Int64(total).formatted(format))"
    }
}

/// The window: titled but with no visible title bar, in the current theme,
/// floating so a Lightroom window opening afterwards doesn't cover it.
@MainActor
final class UpdateWindow: NSObject, NSWindowDelegate {
    private let window: NSWindow
    private let hosting: NSHostingView<AnyView>
    private let model: UpdatePromptModel
    private let onClose: () -> Void
    private var themeObserver: NSObjectProtocol?

    init(model: UpdatePromptModel, onClose: @escaping () -> Void) {
        self.model = model
        self.onClose = onClose
        hosting = NSHostingView(rootView: Self.root(model))
        window = NSWindow(contentRect: NSRect(x: 0, y: 0, width: 540, height: 360),
                          styleMask: [.titled, .closable, .fullSizeContentView], backing: .buffered, defer: false)
        super.init()
        window.title = "Lumerist update"
        window.titlebarAppearsTransparent = true
        window.titleVisibility = .hidden
        window.isMovableByWindowBackground = true
        window.isReleasedWhenClosed = false
        window.level = .floating
        window.hidesOnDeactivate = false
        window.collectionBehavior = [.moveToActiveSpace, .fullScreenAuxiliary]
        window.contentView = hosting
        window.delegate = self
        applyTheme()
        themeObserver = NotificationCenter.default.addObserver(forName: .synthThemeDidChange, object: nil, queue: .main) { [weak self] _ in
            MainActor.assumeIsolated {
                guard let self else { return }
                self.hosting.rootView = Self.root(self.model)
                self.applyTheme()
            }
        }
    }

    private static func root(_ model: UpdatePromptModel) -> AnyView {
        AnyView(UpdatePromptView(model: model).synthColorScheme())
    }

    private func applyTheme() {
        window.backgroundColor = Synth.isFlat ? Synth.nsTone(\.panel) : Synth.nsTone(\.abyss)
        window.appearance = ThemeController.windowAppearance
    }

    /// Lumerist comes forward with the window (activation is cooperative on
    /// macOS 14+, so the window is also ordered front by itself).
    func bringToFront() {
        window.setContentSize(hosting.fittingSize)
        if !window.isVisible { window.center() }
        NSApp.activate()
        window.makeKeyAndOrderFront(nil)
        window.orderFrontRegardless()
        Task { @MainActor [weak window] in
            for delay in [100, 400] {
                try? await Task.sleep(for: .milliseconds(delay))
                guard let window, window.isVisible, !window.isKeyWindow else { continue }
                window.orderFrontRegardless()
            }
        }
    }

    func close() { window.close() }

    func windowShouldClose(_ sender: NSWindow) -> Bool {
        // Downloading: closing cancels. Past that, the swap must finish.
        if model.canCancel { model.cancel(); return true }
        return !model.isBusy
    }

    func windowWillClose(_ notification: Notification) {
        if let themeObserver { NotificationCenter.default.removeObserver(themeObserver) }
        themeObserver = nil
        onClose()
    }
}
