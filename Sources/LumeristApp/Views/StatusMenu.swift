import SwiftUI
import LumeristCore

/// The menu bar item's window: link state, shortcuts switch, Key Reference,
/// window, a quiet Galerist link, quit.
struct StatusMenu: View {
    let store: CommandStore
    @Environment(\.openWindow) private var openWindow
    @State private var updates = UpdateChecker.shared

    var body: some View {
        content
            // Rebuilt in the new look when the theme changes.
            .id(store.preferences.themeKey)
    }

    private var content: some View {
        VStack(alignment: .leading, spacing: 0) {
            HStack(alignment: .top) {
                VStack(alignment: .leading, spacing: 4) {
                    Label(store.isConnected ? "Lightroom linked" : "Waiting for Lightroom",
                          systemImage: store.isConnected ? "checkmark" : "hourglass")
                        .synthLabel(12, color: linkColor)
                    Text(contextLine).synthLabel(9)
                }
                if Synth.isFlat && store.isConnected {
                    Spacer(minLength: Synth.Space.s)
                    // Galerist: the live dot, as in the window header.
                    Circle().fill(Synth.live).frame(width: 7, height: 7).padding(.top, 4).accessibilityHidden(true)
                }
            }
            .padding(.horizontal, 12)
            .padding(.vertical, 10)

            if Synth.isFlat {
                flatShortcutsRow
            } else {
                neonShortcutsRow
            }

            // A newer version: announced here (Windows also shows a notification);
            // the click only opens its release page.
            if case .available(let version, _) = updates.state {
                divider
                item("Download Lumerist \(version)…") { updates.openReleasePage() }
            }

            divider
            item(store.isKeyReferenceVisible ? "Hide Key Reference" : "Show Key Reference") { store.toggleKeyReference() }
            item("Open Lumerist…") {
                NSApp.activate()
                openWindow(id: MainWindow.id)
            }
            item("Check connection") { store.sendConnectionCheck() }
            item("Setup guide…") {
                NSApp.activate()
                openWindow(id: Onboarding.windowID)
            }
            // GitHub's bug form, already filled in; opened in the browser, Lumerist sends nothing.
            item("Report a problem…") { NSWorkspace.shared.open(SupportLinks.bugReport) }
            item("Support Lumerist…") {
                Navigation.go(to: .general)
                NSApp.activate()
                openWindow(id: MainWindow.id)
            }
            // Opens the browser only when clicked; Lumerist sends nothing.
            item("Deliver photos to clients — Galerist", quiet: true) {
                NSWorkspace.shared.open(SupportLinks.galeristFromMenu)
            }
            divider
            item("Quit Lumerist", trailing: "⌘Q") { NSApp.terminate(nil) }
                .keyboardShortcut("q")
        }
        .padding(.vertical, 6)
        .frame(width: 270)
        .background(Synth.isFlat ? Synth.panel : Synth.abyss)
        .overlay(RoundedRectangle(cornerRadius: Synth.Radius.control)
            .strokeBorder(Synth.isFlat ? Synth.gridLine : Synth.teal.opacity(0.7), lineWidth: 1))
        .synthColorSchemeEnvironment()
        .onAppear { FloatingPanels.shared.attach(store) }
    }

    private var linkColor: Color {
        if Synth.isFlat { return store.isConnected ? Synth.ice : Synth.steel }
        return store.isConnected ? Synth.teal : Synth.amber
    }

    /// Galerist: "SHORTCUTS" and the square switch in a hairline box.
    private var flatShortcutsRow: some View {
        let on = store.shortcutsAreEnabled
        let waiting = store.isWaitingForPermission
        return Button { store.toggleShortcuts() } label: {
            HStack {
                Text(waiting ? "Shortcuts · waiting" : "Shortcuts").synthLabel(11, color: on ? Synth.ice : Synth.steel)
                Spacer()
                FlatSwitch(isOn: on, isPending: waiting)
            }
            .padding(.horizontal, 10)
            .frame(height: 34)
            .overlay(Rectangle().strokeBorder(Synth.gridLine, lineWidth: 1))
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .padding(.horizontal, 8)
        .accessibilityLabel("Shortcuts")
        .accessibilityValue(on ? "On" : waiting ? "Waiting for permission" : "Off")
    }

    private var neonShortcutsRow: some View {
        Button { store.toggleShortcuts() } label: {
            HStack {
                Text("Shortcuts").synthLabel(11, color: store.shortcutsAreEnabled ? Synth.magentaLight : Synth.steel)
                Spacer()
                Text(store.shortcutsAreEnabled ? "ON" : store.isWaitingForPermission ? "WAIT" : "OFF")
                    .font(SynthFont.font(.mono, size: 10, weight: .bold))
                    .foregroundStyle(store.shortcutsAreEnabled ? Synth.onMagenta : Synth.abyss)
                    .padding(.horizontal, 6)
                    .frame(height: 18)
                    .background(RoundedRectangle(cornerRadius: 2)
                        .fill(store.shortcutsAreEnabled ? Synth.magenta : store.isWaitingForPermission ? Synth.amber : Synth.steel))
            }
            .padding(.horizontal, 10)
            .frame(height: 32)
            .background(RoundedRectangle(cornerRadius: Synth.Radius.row)
                .fill(store.shortcutsAreEnabled ? Synth.magenta.opacity(0.1) : .clear))
            .overlay(RoundedRectangle(cornerRadius: Synth.Radius.row)
                .strokeBorder(store.shortcutsAreEnabled ? Synth.magenta : Synth.outline, lineWidth: 1))
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .padding(.horizontal, 8)
        .accessibilityLabel("Shortcuts")
        .accessibilityValue(store.shortcutsAreEnabled ? "On" : "Off")
    }

    private var contextLine: String {
        guard store.isConnected else { return "Plug-in not connected" }
        let module = store.context.module.isEmpty ? "—" : store.context.module.capitalized
        return store.context.isMasking ? "\(module) · mask selected" : module
    }

    private var divider: some View {
        Rectangle().fill(Synth.gridLine).frame(height: 1).padding(.horizontal, 8).padding(.vertical, 6)
    }

    private func item(_ title: String, trailing: String? = nil, quiet: Bool = false,
                      action: @escaping () -> Void) -> some View {
        Button {
            action()
            NSApp.keyWindow?.orderOut(nil) // Close the menu bar window after acting.
        } label: {
            HStack {
                Text(title)
                    .font(quiet ? SynthFont.font(.text, size: 12)
                          : Synth.isFlat ? SynthFont.font(.text, size: 13) : SynthFont.font(.mono, size: 12, weight: .medium))
                    .foregroundStyle(quiet ? Synth.steel : Synth.ice)
                    .lineLimit(1)
                Spacer()
                if let trailing { Text(trailing).synthLabel(10) }
            }
            .padding(.horizontal, 12)
            .frame(height: 28)
            .contentShape(Rectangle())
        }
        .buttonStyle(MenuRowStyle())
    }
}

private struct MenuRowStyle: ButtonStyle {
    func makeBody(configuration: Configuration) -> some View {
        configuration.label
            .background(RoundedRectangle(cornerRadius: Synth.Radius.row)
                .fill(configuration.isPressed ? (Synth.isFlat ? Synth.gridLine : Synth.teal.opacity(0.18)) : .clear))
            .padding(.horizontal, 6)
    }
}

/// The menu bar icon. Rendered at launch, so it also starts the floating
/// panels (Palette, Key Reference) even when the window is never opened.
struct MenuBarLabel: View {
    let store: CommandStore

    var body: some View {
        Image(systemName: "keyboard")
            .onAppear { FloatingPanels.shared.attach(store) }
            .task {
                FloatingPanels.shared.attach(store)
                // The daily update check at launch, unless the user turned it off.
                UpdateChecker.shared.checkIfDue()
            }
    }
}
