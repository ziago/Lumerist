import SwiftUI
import LumeristCore

@main
struct LumeristApp: App {
    @NSApplicationDelegateAdaptor(AppDelegate.self) private var appDelegate
    private let store: CommandStore

    init() {
        // The look first (theme and light/dark), before any window or snapshot.
        ThemeController.applyStored()
        // Development aid: render the Value HUD to PNG files and quit.
        if let folder = ProcessInfo.processInfo.environment["LUMERIST_RENDER_HUD"] {
            HUDSnapshots.render(to: URL(fileURLWithPath: folder))
            exit(0)
        }
        // Development aid: render the setup guide and support sections, then quit.
        if let folder = ProcessInfo.processInfo.environment["LUMERIST_RENDER_SETUP"] {
            OnboardingSnapshots.render(to: URL(fileURLWithPath: folder))
            exit(0)
        }
        // Connect at launch, whether or not the window is ever shown.
        store = CommandStore()
        store.start()
    }

    var body: some Scene {
        Window("Lumerist", id: MainWindow.id) {
            ContentView(store: store)
                .background(MainWindowOpenerRegistration(startHidden: store.preferences.startHidden))
        }
        .defaultSize(width: 860, height: 760)

        // First-run setup guide; reopened from General and the menu bar.
        Window("Welcome to Lumerist", id: Onboarding.windowID) {
            OnboardingView(store: store)
        }
        .windowResizability(.contentSize)
        .defaultPosition(.center)

        // Always reachable, even after the window has been closed.
        MenuBarExtra {
            StatusMenu(store: store)
        } label: {
            MenuBarLabel(store: store)
        }
        .menuBarExtraStyle(.window)
    }
}

/// Reopens the single main window from the Dock icon or the menu bar item.
@MainActor
enum MainWindow {
    static let id = "main"
    fileprivate static var open: (() -> Void)?
    fileprivate static var didApplyStartHidden = false
    /// Launched from Lightroom's "Open Lumerist": show the window even
    /// when "Start with the window hidden" is on.
    fileprivate static let launchedToShow = CommandLine.arguments.contains("--show-window")
    /// Started by the plug-in as Lightroom opens: no window over Lightroom.
    fileprivate static let launchedWithLightroom = CommandLine.arguments.contains("--with-lightroom")

    /// The main window once it exists (set by its chrome).
    static weak var window: NSWindow?

    static func show() {
        NSApp.activate()
        open?()
        // Activation is cooperative on macOS 14+: asked from Lightroom's
        // menu, macOS may keep Lightroom active and the window would open
        // behind it. Put the window on top anyway; a click makes it active.
        Task { @MainActor in
            for delay in [50, 250, 600] {
                try? await Task.sleep(for: .milliseconds(delay))
                guard !NSApp.isActive, let window, window.isVisible else { continue }
                window.orderFrontRegardless()
            }
        }
    }
}

private struct MainWindowOpenerRegistration: View {
    let startHidden: Bool
    @Environment(\.openWindow) private var openWindow
    @Environment(\.dismissWindow) private var dismissWindow

    var body: some View {
        Color.clear
            .frame(width: 0, height: 0)
            .onAppear {
                let openWindow = openWindow
                MainWindow.open = { openWindow(id: MainWindow.id) }
                // "Start with the window hidden": close it once, at launch only.
                let hidden = startHidden || MainWindow.launchedWithLightroom
                if hidden && !MainWindow.didApplyStartHidden && !MainWindow.launchedToShow {
                    dismissWindow(id: MainWindow.id)
                }
                // First launch: open the setup guide on top (once, until finished or skipped).
                if !MainWindow.didApplyStartHidden && Onboarding.shouldShowAtLaunch {
                    openWindow(id: Onboarding.windowID)
                }
                MainWindow.didApplyStartHidden = true
            }
    }
}

@MainActor
final class AppDelegate: NSObject, NSApplicationDelegate {
    /// A Dock click, or "Open Lumerist" in Lightroom while Lumerist
    /// already runs, brings the main window forward (reopening it if closed).
    func applicationShouldHandleReopen(_ sender: NSApplication, hasVisibleWindows flag: Bool) -> Bool {
        MainWindow.show()
        return true
    }

    /// Closing the window keeps the companion (and its shortcuts) running.
    func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool { false }
}

@MainActor
@Observable
final class CommandStore {
    private(set) var responses: [LightroomAck] = []
    private(set) var errors: [String] = []
    private(set) var isConnected = false
    /// The active Lightroom module and tool, pushed by the plug-in.
    private(set) var context = LightroomContext(module: "", tool: "")
    private(set) var presets: [PresetItem] = []
    private(set) var unsupportedParameters: [String] = []
    private(set) var isKeyReferenceVisible = false
    private(set) var isPaletteOpen = false
    /// Dial Keys, Nudge Keys, Command Keys, system keys, Palette and input
    /// options (format 3). This is what the gesture engine uses.
    let keymap = KeymapStore()
    let preferences = AppPreferences()
    private var client: LightroomCommandClient?
    @ObservationIgnored private var lifecycle: LightroomLifecycle?
    @ObservationIgnored private var dispatcher: IntentDispatcher?
    @ObservationIgnored let overlay = ValueHUD()
    var overlaySettings = OverlaySettings.load(from: .standard) {
        didSet { overlaySettings.save(to: .standard) }
    }
    private var monitor: KeyEngineMonitor?
    private var permissionWait: Task<Void, Never>?

    /// The user's own opt-in, remembered so shortcuts come back after a relaunch
    /// (only when macOS already grants the permission; never prompted at launch).
    private static let shortcutsWantedKey = "org.lumerist.shortcuts-wanted"
    private var shortcutsWanted: Bool {
        get { UserDefaults.standard.bool(forKey: Self.shortcutsWantedKey) }
        set { UserDefaults.standard.set(newValue, forKey: Self.shortcutsWantedKey) }
    }

    var shortcutsAreEnabled: Bool { monitor != nil }
    var isWaitingForPermission: Bool { permissionWait != nil }

    func start() {
        guard client == nil else { return }

        dispatcher = IntentDispatcher(hooks: IntentDispatcher.Hooks(
            send: { [weak self] request in self?.send(request) },
            updateRateHz: { [weak self] in self?.preferences.updateRateHz ?? 30 },
            notice: { [weak self] text in self?.showNotice(text) },
            openPalette: { [weak self] in self?.openPalette() },
            closePalette: { [weak self] in self?.closePalette() },
            toggleKeyReference: { [weak self] in self?.toggleKeyReference() },
            log: { DebugLog.write($0) }
        ))

        let client = LightroomCommandClient(
            port: 50_550,
            onState: { [weak self] state in
                Task { @MainActor in
                    guard let self else { return }
                    switch state {
                    case .ready:
                        let wasConnected = self.isConnected
                        self.isConnected = true
                        DebugLog.write("Bridge connected")
                        if !wasConnected {
                            self.send(.query(.context))
                        }
                    case .waiting(let error):
                        if self.isConnected { DebugLog.write("Bridge lost: \(error)") }
                        self.isConnected = false
                        self.dispatcher?.connectionLost()
                    }
                }
            },
            onMessage: { [weak self] message in
                Task { @MainActor in
                    self?.receive(message)
                }
            }
        )
        client.start()
        self.client = client
        lifecycle = LightroomLifecycle(preferences: preferences)
        DebugLog.write("Lumerist started")
        // A newer plug-in came with this version: in place now if Lightroom is
        // closed, otherwise as soon as it quits.
        PluginInstaller.shared.updateIfOutdated()

        if shortcutsWanted && AccessibilityPermission.isGranted {
            startShortcutMonitor()
        }
    }

    func sendTestExposure() {
        send({ try .adjust(.develop, "Exposure", delta: 0.1) })
    }

    func sendConnectionCheck() {
        send(.ping)
    }

    /// Asks the plug-in which catalog parameters this Lightroom does not accept.
    func checkParameterCatalog() {
        send(.query(.catalog))
    }

    func refreshPresets() {
        send(.query(.presets))
    }

    func reconnect() {
        client?.reconnect()
    }

    func toggleShortcuts() {
        if let monitor {
            monitor.stop()
            self.monitor = nil
            shortcutsWanted = false
            return
        }
        if let permissionWait {
            permissionWait.cancel()
            self.permissionWait = nil
            shortcutsWanted = false
            return
        }
        shortcutsWanted = true
        guard AccessibilityPermission.isGranted else {
            // macOS lists the app in Privacy & Security → Accessibility and
            // shows its prompt; the shortcuts start by themselves once allowed.
            AccessibilityPermission.request()
            permissionWait = Task { [weak self] in
                for _ in 0..<300 {
                    try? await Task.sleep(for: .seconds(1))
                    if Task.isCancelled { return }
                    if AccessibilityPermission.isGranted {
                        self?.permissionWait = nil
                        self?.startShortcutMonitor()
                        return
                    }
                }
                self?.permissionWait = nil
            }
            return
        }
        startShortcutMonitor()
    }

    private func startShortcutMonitor() {
        let monitor = KeyEngineMonitor(keymap: keymap.keymap) { [weak self] intents in
            // The event tap runs on the main run loop.
            MainActor.assumeIsolated {
                self?.dispatcher?.handle(intents)
            }
        }
        monitor.update(module: context.module.isEmpty ? "develop" : context.module, tool: context.tool)
        do {
            try monitor.start()
            self.monitor = monitor
        } catch {
            recordError(error.localizedDescription)
        }
    }

    /// Applies a keymap change and hands it to the running monitor.
    func editKeymap(_ change: (inout Keymap) throws -> Void) {
        do {
            try keymap.mutate(change)
            monitor?.update(keymap: keymap.keymap)
        } catch {
            recordError(error.localizedDescription)
        }
    }

    /// Runs a keymap-store operation (restore, import…) and refreshes the monitor.
    func withKeymapStore(_ operation: (KeymapStore) throws -> Void) {
        do {
            try operation(keymap)
            monitor?.update(keymap: keymap.keymap)
        } catch {
            recordError(error.localizedDescription)
        }
    }

    // MARK: Palette and Key Reference

    private func openPalette() {
        isPaletteOpen = true
    }

    func closePalette() {
        guard isPaletteOpen else { return }
        isPaletteOpen = false
        monitor?.paletteDidClose()
    }

    func toggleKeyReference() {
        setKeyReferenceVisible(!isKeyReferenceVisible)
    }

    func setKeyReferenceVisible(_ visible: Bool) {
        isKeyReferenceVisible = visible
    }

    /// Runs a Command Key action (command, preset, shortcut or macro), e.g.
    /// from the Palette or a Run button in the Commands tab.
    func perform(_ action: CommandKey.Action) {
        dispatcher?.handle([.action(action)])
    }

    private func showNotice(_ text: String) {
        overlay.showNotice(text, settings: overlaySettings)
    }

    // MARK: Bridge

    private func send(_ make: () throws -> LightroomRequest) {
        do {
            send(try make())
        } catch {
            recordError(error.localizedDescription)
        }
    }

    @discardableResult
    private func send(_ request: LightroomRequest) -> Int? {
        DebugLog.write("Send \(RequestRecord(request).summary)")
        guard let client else {
            recordError("The local command client is not available.")
            return nil
        }
        let id = client.send(request) { [weak self] result in
            if case .failure(let error) = result {
                Task { @MainActor in
                    self?.recordError(error.localizedDescription)
                }
            }
        }
        return id
    }

    private func recordError(_ message: String) {
        Self.prepend(message, to: &errors)
    }

    private func receive(_ message: LightroomMessage) {
        switch message {
        case .ack(let ack):
            record(ack)
        case .context(let newContext):
            apply(newContext)
        case .result(let result):
            switch result {
            case .context(_, let newContext):
                apply(newContext)
            case .presets(_, let items, _):
                presets = items
            case .catalog(_, let unsupported, let skipped):
                unsupportedParameters = unsupported
                DebugLog.write("Catalog check: unsupported \(unsupported), skipped \(skipped)")
            }
        }
    }

    private func apply(_ newContext: LightroomContext) {
        guard newContext != context else { return }
        context = newContext
        monitor?.update(module: newContext.module, tool: newContext.tool)
        DebugLog.write("Lightroom context: \(newContext.module) \(newContext.tool)")
        if !newContext.isMasking && isPaletteOpen { closePalette() }
    }

    /// Keeps the response and error lists short: a scroll gesture sends many requests per second.
    private static let historyLimit = 100

    private static func prepend<Element>(_ element: Element, to list: inout [Element]) {
        list.insert(element, at: 0)
        if list.count > historyLimit {
            list.removeLast(list.count - historyLimit)
        }
    }

    private func record(_ ack: LightroomAck) {
        DebugLog.write("Lightroom \(ack.accepted ? "accepted" : "rejected") #\(ack.id.map(String.init) ?? "-") \(ack.parameter ?? ""): \(ack.message ?? "")")
        Self.prepend(ack, to: &responses)
        if dispatcher?.didReceive(ack) == true { return }
        if ack.parameter != nil {
            if ack.accepted {
                overlay.show(ack: ack, settings: overlaySettings)
            } else {
                overlay.showNotice(ack.message ?? "Lightroom rejected the change", settings: overlaySettings)
            }
        }
        if !ack.accepted {
            recordError(ack.message ?? "The plug-in rejected the request.")
        }
    }
}

/// One sent request in a few words, for the debug log.
private struct RequestRecord {
    let title: String
    let detail: String?

    init(_ request: LightroomRequest) {
        switch request.kind {
        case .ping:
            title = "Connection check"; detail = nil
        case .adjust(let scope, let parameter, let delta):
            title = ParameterCatalog.descriptor(scope: scope, id: parameter)?.label ?? parameter
            detail = delta.formatted(.number.sign(strategy: .always()).precision(.fractionLength(0...3)))
        case .reset(let scope, let parameter):
            title = "Reset " + (ParameterCatalog.descriptor(scope: scope, id: parameter)?.label ?? parameter); detail = nil
        case .gesture(let phase, let parameter):
            title = "Gesture \(phase.rawValue)"; detail = parameter
        case .command(let name, let argument):
            title = name.rawValue; detail = argument
        case .query(let query):
            title = "Query \(query.rawValue)"; detail = nil
        }
    }

    var summary: String { detail.map { "\(title) \($0)" } ?? title }
}

/// The Accessibility permission the keyboard + scroll monitor needs.
@MainActor
enum AccessibilityPermission {
    static var isGranted: Bool { AXIsProcessTrusted() }

    /// Shows macOS's own prompt (which also adds Lumerist to the list) and
    /// opens the Accessibility pane, where the user switches it on.
    static func request() {
        let options = ["AXTrustedCheckOptionPrompt": true] as CFDictionary
        _ = AXIsProcessTrustedWithOptions(options)
        openSettings()
    }

    /// Opens System Settings → Privacy & Security → Accessibility.
    static func openSettings() {
        if let pane = URL(string: "x-apple.systempreferences:com.apple.preference.security?Privacy_Accessibility") {
            NSWorkspace.shared.open(pane)
        }
    }
}
