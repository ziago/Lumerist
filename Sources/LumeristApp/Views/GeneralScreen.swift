import AppKit
import SwiftUI
import LumeristCore

/// Appearance, the macOS permission, startup, System Keys, the Lightroom
/// link and options, the profile file, diagnostics, updates and support.
struct GeneralScreen: View {
    let store: CommandStore
    @State private var loginError: String?
    @State private var confirmsRestore = false
    @State private var opensAtLogin = false

    var body: some View {
        VStack(alignment: .leading, spacing: Synth.Space.xl) {
            ScreenHeader(title: "General", readout: "appearance · permission · startup · keys · lightroom · profile")

            AppearanceSection(preferences: store.preferences)

            PermissionSection(store: store)

            startupSection

            SystemKeysSection(store: store)

            LightroomSection(store: store)

            PluginSection()

            profileSection

            diagnosticsSection

            UpdatesSection()

            AboutSupportSection()
        }
        .onAppear { opensAtLogin = store.preferences.opensAtLogin }
        .confirmationDialog("Restore the standard layout?", isPresented: $confirmsRestore) {
            Button("Restore", role: .destructive) {
                store.withKeymapStore { try $0.restoreStandardLayout() }
            }
        } message: {
            Text("Dial Keys, Nudge Keys, Command Keys, System Keys and steps return to the layout Lumerist ships with. Your mouse, trackpad and Lightroom options and the Palette are kept. To keep the current keys, export the profile first.")
        }
    }

    // MARK: Startup

    private var startupSection: some View {
        let quits = store.preferences.quitWithLightroom
        return SettingsSection(title: "Startup", systemImage: "power") {
            SettingRow(title: "Open at Login",
                       detail: loginError ?? "Starts Lumerist each time you log in to this Mac.") {
                Toggle("Open at Login", isOn: Binding(
                    get: { opensAtLogin },
                    set: { value in
                        do {
                            try store.preferences.setOpensAtLogin(value)
                            loginError = nil
                        } catch {
                            loginError = error.localizedDescription
                        }
                        opensAtLogin = store.preferences.opensAtLogin
                    }
                ))
                .labelsHidden()
                .toggleStyle(.neon)
            }
            SettingRow(title: "Start with the window hidden",
                       detail: "Lumerist opens straight to the menu bar. Click its icon there, or in the Dock, when you want this window.") {
                Toggle("Start with the window hidden", isOn: Binding(
                    get: { store.preferences.startHidden },
                    set: { store.preferences.startHidden = $0 }
                ))
                .labelsHidden()
                .toggleStyle(.neon)
            }
            SettingRow(title: "Open Lumerist when Lightroom starts",
                       detail: "The plug-in launches Lumerist quietly, with no window. Applies from Lightroom's next start.") {
                Toggle("Open Lumerist when Lightroom starts", isOn: Binding(
                    get: { store.preferences.openWithLightroom },
                    set: { store.preferences.openWithLightroom = $0 }
                ))
                .labelsHidden()
                .toggleStyle(.neon)
            }
            SettingRow(title: "When Lightroom quits",
                       detail: quits
                           ? "Lumerist has nothing to do without Lightroom Classic, so it closes too, a moment later."
                           : "Lumerist stays in the menu bar until you quit it.") {
                SynthSegmented(items: QuitChoice.allCases,
                               selection: Binding(get: { quits ? .quitAfterPause : .keepRunning },
                                                  set: { store.preferences.quitWithLightroom = $0 == .quitAfterPause }),
                               title: \.title)
                    .fixedSize()
                    .accessibilityElement(children: .contain)
                    .accessibilityLabel("When Lightroom quits")
            }
            if quits {
                SettingRow(title: "Pause length", detail: "Seconds between Lightroom closing and Lumerist closing.") {
                    Stepper(value: Binding(
                        get: { store.preferences.quitDelaySeconds },
                        set: { store.preferences.quitDelaySeconds = $0 }
                    ), in: AppPreferences.quitDelayRange, step: 1) {
                        Text("\(Int(store.preferences.quitDelaySeconds)) s")
                            .font(SynthFont.font(.mono, size: 13, weight: .semibold))
                            .monospacedDigit()
                            .foregroundStyle(Synth.ice)
                            .frame(minWidth: 44, alignment: .trailing)
                    }
                    .accessibilityLabel("Pause length in seconds")
                    .accessibilityValue("\(Int(store.preferences.quitDelaySeconds)) seconds")
                }
            }
        }
    }

    // MARK: Profile

    private var profileSection: some View {
        SettingsSection(title: "Profile", systemImage: "doc.text",
                        footer: "A profile holds Dial, Nudge and Command Keys, System Keys, steps, the Palette, and the mouse, trackpad and Lightroom options.") {
            SettingRow(title: "Profile file",
                       detail: "Save everything to a JSON file, or load one.") {
                HStack(spacing: Synth.Space.s) {
                    Button("Export…") { exportProfile() }.buttonStyle(.neonSecondary)
                    Button("Import…") { importProfile() }.buttonStyle(.neonSecondary)
                }
            }
            SettingRow(title: "Standard layout", detail: "Brings back the keys Lumerist ships with. Your options stay as they are.") {
                Button("Restore…") { confirmsRestore = true }.buttonStyle(.neonDestructive)
            }
        }
    }

    // MARK: Diagnostics

    private var diagnosticsSection: some View {
        SettingsSection(title: "Diagnostics", systemImage: "stethoscope",
                        footer: "The log notes link changes, the commands sent and Lightroom's answers. Never photos or catalog contents.") {
            SettingRow(title: "Debug log", detail: DebugLog.fileURL.path(percentEncoded: false)) {
                HStack(spacing: Synth.Space.m) {
                    Button("Show in Finder") {
                        NSWorkspace.shared.activateFileViewerSelecting([DebugLog.fileURL])
                    }
                    .buttonStyle(.neonQuiet)
                    .disabled(!FileManager.default.fileExists(atPath: DebugLog.fileURL.path))
                    Toggle("Write a debug log", isOn: Binding(
                        get: { store.preferences.debugLogEnabled },
                        set: { store.preferences.debugLogEnabled = $0 }
                    ))
                    .labelsHidden()
                    .toggleStyle(.neon)
                }
            }
        }
    }

    private func exportProfile() {
        let panel = NSSavePanel()
        panel.allowedContentTypes = [.json]
        panel.nameFieldStringValue = "Lumerist Profile.json"
        guard panel.runModal() == .OK, let url = panel.url else { return }
        store.withKeymapStore { try $0.export(to: url) }
    }

    private func importProfile() {
        let panel = NSOpenPanel()
        panel.allowedContentTypes = [.json]
        panel.allowsMultipleSelection = false
        guard panel.runModal() == .OK, let url = panel.url else { return }
        store.withKeymapStore { try $0.importFile(from: url) }
    }
}

private enum QuitChoice: CaseIterable, Hashable {
    case keepRunning
    case quitAfterPause

    var title: String {
        switch self {
        case .keepRunning: "Keep running"
        case .quitAfterPause: "Quit after a pause"
        }
    }
}

/// General → macOS permission: whether macOS allows Lumerist to read the
/// keyboard, mouse and trackpad (Accessibility), and the shortcuts switch.
private struct PermissionSection: View {
    let store: CommandStore
    /// `AXIsProcessTrusted()` is not observable, so it is read again every
    /// second while the screen is visible.
    @State private var allowed = AccessibilityPermission.isGranted

    var body: some View {
        SettingsSection(title: "macOS permission", systemImage: "lock.shield") {
            SettingRow(title: "Accessibility", detail: allowed
                       ? "macOS lets Lumerist read the keyboard, mouse and trackpad while Lightroom Classic is in front."
                       : "Switch Lumerist on in System Settings → Privacy & Security → Accessibility. On macOS 27 the list is called “Device control and data access”.") {
                HStack(spacing: Synth.Space.s) {
                    if allowed {
                        StatusPill(text: "Allowed", systemImage: "checkmark", tone: .good)
                    } else {
                        StatusPill(text: "Not allowed yet", systemImage: "lock", tone: .waiting)
                    }
                    Button("Open Privacy & Security…") { AccessibilityPermission.openSettings() }
                        .buttonStyle(.neonQuiet)
                }
            }
            SettingRow(title: "Shortcuts", detail: shortcutsDetail) {
                Toggle("Shortcuts", isOn: Binding(
                    get: { store.shortcutsAreEnabled || store.isWaitingForPermission },
                    set: { wanted in
                        if wanted != (store.shortcutsAreEnabled || store.isWaitingForPermission) {
                            store.toggleShortcuts()
                        }
                    }
                ))
                .labelsHidden()
                .toggleStyle(.neon)
                .accessibilityValue(store.shortcutsAreEnabled ? "On" : store.isWaitingForPermission ? "Waiting for permission" : "Off")
            }
        }
        .task {
            while !Task.isCancelled {
                allowed = AccessibilityPermission.isGranted
                try? await Task.sleep(for: .seconds(1))
            }
        }
    }

    private var shortcutsDetail: String {
        if store.shortcutsAreEnabled {
            return "Your keys, mouse and trackpad drive Lightroom's sliders whenever Lightroom Classic is in front."
        }
        if store.isWaitingForPermission {
            return "Waiting for macOS: shortcuts start by themselves once Accessibility is allowed."
        }
        return "Every key goes straight to Lightroom, as if Lumerist were not there."
    }
}
