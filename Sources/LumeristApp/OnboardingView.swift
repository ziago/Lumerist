import AppKit
import SwiftUI
import LumeristCore

/// The first-run setup window: four steps, each checked live, so a new user
/// goes from "downloaded" to "Q + wheel moves Exposure" without the README.
/// When it closes, the optional "Stay in touch" sheet may follow on the main
/// window (`StayInTouchPrompter`).
enum Onboarding {
    static let windowID = "onboarding"
    static let completedKey = "lumerist.onboarding.completed"

    /// The guide's window is on screen: no "Stay in touch" sheet meanwhile.
    @MainActor
    static var isOpen = false

    /// Shown automatically once, until the user finishes or skips it.
    @MainActor
    static var shouldShowAtLaunch: Bool {
        !UserDefaults.standard.bool(forKey: completedKey)
    }

    @MainActor
    static func markCompleted() {
        UserDefaults.standard.set(true, forKey: completedKey)
    }
}

struct OnboardingView: View {
    let store: CommandStore
    @State private var installer = PluginInstaller.shared
    @State private var updates = UpdateChecker.shared
    @State private var accessibilityGranted = AccessibilityPermission.isGranted
    /// Responses received before the window opened don't count as "it works".
    @State private var responsesAtOpen = 0
    @Environment(\.dismissWindow) private var dismissWindow
    @Environment(\.isSnapshotRendering) private var isSnapshotRendering

    var body: some View {
        stepsPage
            .padding(.horizontal, Synth.Space.xxl)
            .padding(.top, 44) // room for the traffic lights over the full-size content
            .padding(.bottom, Synth.Space.xl)
            .frame(width: 640)
            .background(SynthBackground())
            .background(SynthWindowChrome())
            .synthColorScheme()
            .tint(Synth.teal)
            // Rebuilt in the new look when the theme changes (General → Appearance).
            .id(store.preferences.themeKey)
            .onAppear {
                if !isSnapshotRendering { Onboarding.isOpen = true }
                responsesAtOpen = store.responses.count
                installer.refresh()
            }
            .onDisappear {
                guard !isSnapshotRendering else { return }
                Onboarding.isOpen = false
                // The main window is usually in front now: a chance for the
                // optional "Stay in touch" sheet (checked again after a pause).
                StayInTouchPrompter.shared.considerPrompting()
            }
            .task {
                // Live checks: the permission and Lightroom can change at any time.
                while !Task.isCancelled {
                    accessibilityGranted = AccessibilityPermission.isGranted
                    installer.refreshLightroomState()
                    try? await Task.sleep(for: .seconds(1))
                }
            }
    }

    private var stepsPage: some View {
        VStack(alignment: .leading, spacing: Synth.Space.l) {
            VStack(alignment: .leading, spacing: Synth.Space.xs) {
                Text("Welcome to Lumerist").synthTitle(26).accessibilityAddTraits(.isHeader)
                Text("Four steps, then your keyboard and your mouse or trackpad drive Lightroom Classic's sliders.")
                    .synthBody(13, color: Synth.steel)
            }

            step(1, title: "Install the Lightroom plug-in", done: installer.isReady, detail: pluginDetail) {
                pluginActions
            }
            step(2, title: "Open Lightroom Classic", done: store.isConnected, detail: lightroomDetail) {
                lightroomActions
            }
            step(3, title: "Allow keyboard, mouse and trackpad access", done: accessibilityGranted, detail: accessibilityDetail) {
                if !accessibilityGranted {
                    Button("Open System Settings") { AccessibilityPermission.request() }
                        .buttonStyle(.neonSecondary)
                }
            }
            step(4, title: "Turn on shortcuts and try it", done: itWorks, detail: tryDetail) {
                if !store.shortcutsAreEnabled {
                    Button(store.isWaitingForPermission ? "Waiting for permission…" : "Turn on shortcuts") {
                        store.toggleShortcuts()
                    }
                    .buttonStyle(.neonPrimary)
                    .disabled(!accessibilityGranted && store.isWaitingForPermission)
                }
            }

            if case .failed(let message) = installer.phase {
                Text(message).synthBody(12, color: Synth.error).fixedSize(horizontal: false, vertical: true)
            } else if case .working(let message) = installer.phase {
                // Progress, not a warning: grey in Galerist (its amber is the REC red).
                Text(message).synthBody(12, color: Synth.isFlat ? Synth.steel : Synth.amber)
            }

            Toggle(isOn: Binding(get: { updates.checksAutomatically }, set: { updates.checksAutomatically = $0 })) {
                VStack(alignment: .leading, spacing: 2) {
                    Text("Tell me when a new version is out").synthBody(13, weight: .medium, color: Synth.ice)
                    Text("Lumerist asks GitHub for its list of releases when it starts, at most once a day. Nothing about you is sent.")
                        .synthBody(12, color: Synth.steel)
                        .fixedSize(horizontal: false, vertical: true)
                }
            }
            .toggleStyle(.checkbox)

            HStack {
                Button("Skip for now") { finish() }.buttonStyle(.neonQuiet)
                Spacer()
                Text("You can reopen this guide from General or the menu bar.").synthLabel(10)
                finishButton
            }
        }
    }

    private var finishButton: some View {
        Button(allDone ? "Start editing" : "Finish later") { finish() }
            .buttonStyle(allDone ? .neonPrimary : .neonSecondary)
    }

    // MARK: Steps

    private var itWorks: Bool {
        guard store.shortcutsAreEnabled else { return false }
        let newResponses = store.responses.prefix(max(0, store.responses.count - responsesAtOpen))
        return newResponses.contains { $0.accepted && $0.parameter != nil }
    }

    /// The four steps.
    private var allDone: Bool {
        installer.isReady && store.isConnected && accessibilityGranted && store.shortcutsAreEnabled
    }

    private var pluginDetail: String {
        if let problem = installer.bundleProblem { return problem }
        switch installer.status {
        case .notInstalled?:
            return installer.isLightroomRunning
                ? "Lightroom is open: Lumerist will ask it to quit, install the plug-in, and open it again."
                : "Copies the plug-in into Lightroom's Modules folder. Nothing else on your Mac changes."
        case .outdated(let installed, let bundled)?:
            return "Installed: \(installed?.description ?? "unknown") — this app brings \(bundled)."
        case .modified(let version)?:
            return "Version \(version) is installed but its files differ from this app's copy."
        case .upToDate(let version)?:
            return "Plug-in \(version) is installed."
        case .newerInstalled(let installed, _)?:
            return "A newer plug-in (\(installed)) is installed; update the app when you can."
        case nil:
            return "Checking…"
        }
    }

    @ViewBuilder
    private var pluginActions: some View {
        if installer.bundleProblem == nil, installer.status?.needsAction == true {
            if installer.isLightroomRunning {
                Button("Quit Lightroom & install") { Task { await installer.quitLightroomAndInstall() } }
                    .buttonStyle(.neonPrimary)
                    .disabled(installer.isWorking)
            } else {
                Button(installer.status == .notInstalled ? "Install plug-in" : "Update plug-in") { installer.install() }
                    .buttonStyle(.neonPrimary)
                    .disabled(installer.isWorking)
            }
        }
    }

    private var lightroomDetail: String {
        if store.isConnected { return "Connected: Lumerist and Lightroom are talking (only on this Mac)." }
        if !installer.isReady { return "Install the plug-in first." }
        if installer.isLightroomRunning {
            return "Lightroom loads plug-ins when it starts. If it was already open, restart it once."
        }
        return "Start Lightroom Classic: the plug-in starts with it and connects by itself."
    }

    @ViewBuilder
    private var lightroomActions: some View {
        if !store.isConnected, installer.isReady {
            if installer.isLightroomRunning {
                Button("Restart Lightroom") { Task { await installer.restartLightroom() } }
                    .buttonStyle(.neonSecondary)
                    .disabled(installer.isWorking)
            } else {
                Button("Open Lightroom Classic") { installer.openLightroom() }
                    .buttonStyle(.neonSecondary)
            }
        }
    }

    private var accessibilityDetail: String {
        accessibilityGranted
            ? "Granted. Lumerist only reacts while Lightroom Classic is in front."
            : "System Settings → Privacy & Security → Accessibility (on macOS 27: “Device control and data access”). Turn on Lumerist. If it stays off, quit and reopen Lumerist."
    }

    private var tryDetail: String {
        if itWorks { return "It works! Every key can be changed in the Develop and Library pages." }
        if store.shortcutsAreEnabled {
            return "In Lightroom's Develop module, hold Q and turn the mouse wheel or swipe on the trackpad: Exposure moves. Hold ⇧ for bigger steps."
        }
        return "Shortcuts only act while Lightroom Classic is in front; a quick tap of a key still reaches Lightroom."
    }

    private func step<Actions: View>(
        _ number: Int,
        title: String,
        done: Bool,
        detail: String,
        @ViewBuilder actions: () -> Actions
    ) -> some View {
        NeonPanel {
            HStack(alignment: .center, spacing: Synth.Space.l) {
                stepNumber(number, done: done)
                    .accessibilityHidden(true)
                VStack(alignment: .leading, spacing: 3) {
                    Text(title).synthBody(14, weight: .semibold, color: Synth.ice)
                    Text(detail)
                        .synthBody(12, color: Synth.steel)
                        .fixedSize(horizontal: false, vertical: true)
                }
                // The text reads as one element; the buttons stay separate.
                .accessibilityElement(children: .ignore)
                .accessibilityLabel("Step \(number): \(title). \(done ? "Done." : "To do.") \(detail)")
                Spacer(minLength: Synth.Space.m)
                if done {
                    StatusPill(text: "Done", systemImage: "checkmark", tone: .good)
                } else {
                    actions()
                }
            }
            .padding(.horizontal, Synth.rowInset)
            .padding(.vertical, 12)
        }
    }

    /// Miami: a teal ring, filled when done. Galerist: a square, ink when done.
    @ViewBuilder
    private func stepNumber(_ number: Int, done: Bool) -> some View {
        if Synth.isFlat {
            Text("\(number)")
                .font(SynthFont.font(.mono, size: 15, weight: .medium))
                .foregroundStyle(done ? Synth.onMagenta : Synth.ice)
                .frame(width: 34, height: 34)
                .background(Rectangle().fill(done ? Synth.magenta : .clear))
                .overlay(Rectangle().strokeBorder(done ? Synth.magenta : Synth.ice, lineWidth: 1))
        } else {
            Text("\(number)")
                .font(SynthFont.font(.display, size: 18, weight: .bold))
                .foregroundStyle(done ? Synth.onMagenta : Synth.teal)
                .frame(width: 34, height: 34)
                .background(Circle().fill(done ? Synth.teal : .clear))
                .overlay(Circle().strokeBorder(Synth.teal, lineWidth: 1))
        }
    }

    private func finish() {
        Onboarding.markCompleted()
        dismissWindow(id: Onboarding.windowID)
    }
}
