import SwiftUI
import LumeristCore

struct ContentView: View {
    let store: CommandStore
    /// The last section viewed, remembered between launches (a convenience only).
    @AppStorage(Navigation.sectionKey) private var sectionID = MainSection.develop.rawValue
    /// The optional "Stay in touch" sheet and when it may appear.
    @State private var stayInTouch = StayInTouchPrompter.shared

    private var section: MainSection { MainSection(rawValue: sectionID) ?? .develop }

    var body: some View {
        themed
            // A theme change rebuilds the whole tree, toolbar and window
            // chrome included: layout and fonts are chosen per theme.
            .id(store.preferences.themeKey)
            .keymapEditorAlerts()
            .onAppear(perform: previewOverlayForDevelopment)
            // Asked only while the user is looking at this window (the
            // prompter checks again after a pause; see StayInTouchPrompter).
            .sheet(item: $stayInTouch.presented) { model in
                StayInTouchSheet(model: model)
            }
            .onAppear { stayInTouch.considerPrompting() }
            .onReceive(NotificationCenter.default.publisher(for: NSWindow.didBecomeKeyNotification)) { _ in
                stayInTouch.considerPrompting()
            }
            .onReceive(NotificationCenter.default.publisher(for: NSApplication.didBecomeActiveNotification)) { _ in
                stayInTouch.considerPrompting()
            }
    }

    private var themed: some View {
        VStack(spacing: 0) {
            SectionTabs(selection: Binding(
                get: { section },
                set: { sectionID = $0.rawValue }
            ))
            ScrollView {
                screen(for: section)
                    .padding(.horizontal, Synth.isFlat ? 32 : Synth.Space.xxl)
                    .padding(.vertical, Synth.isFlat ? 28 : Synth.Space.xl)
                    .frame(maxWidth: 1040, alignment: .leading)
                    .frame(maxWidth: .infinity, alignment: .leading)
            }
            .scrollContentBackground(.hidden)
        }
        // The header row lives in the window toolbar: the title bar becomes
        // 52 pt tall, the traffic lights sit centred beside the wordmark and
        // pills, and clicks and window dragging work natively. The toolbar's
        // own background is hidden, so the Synth surface runs to the top edge.
        .overlay(alignment: .top) {
            Rectangle().fill(Synth.gridLine).frame(height: 1).accessibilityHidden(true)
        }
        .toolbar { HeaderToolbar(store: store) }
        .toolbarBackground(.hidden, for: .windowToolbar)
        .navigationTitle("")
        .background(SynthBackground())
        .background(SynthWindowChrome(onConfigure: { MainWindow.window = $0 }))
        .synthColorScheme()
        .tint(Synth.teal)
        .frame(minWidth: 860, minHeight: 600)
        // Every key cap reads combos with the current Combo Key ("Tab + K").
        .environment(\.comboKey, store.keymap.keymap.systemKeys.comboKey)
    }

    /// Attaches the floating panels, plus two development aids for visual
    /// checks without clicking:
    /// - `LUMERIST_PREVIEW_PANELS=1` opens the Palette preview and the Key Reference;
    /// - `LUMERIST_PREVIEW_OVERLAY=ruler|compact` shows the Value HUD preview once.
    private func previewOverlayForDevelopment() {
        FloatingPanels.shared.attach(store)
        let environment = ProcessInfo.processInfo.environment
        if environment["LUMERIST_PREVIEW_PANELS"] == "1" {
            FloatingPanels.shared.previewPalette()
            store.setKeyReferenceVisible(true)
        }
        guard let raw = environment["LUMERIST_PREVIEW_OVERLAY"],
              let style = OverlaySettings.Style(rawValue: raw) else { return }
        var settings = store.overlaySettings
        settings.style = style
        settings.duration = OverlaySettings.durationRange.upperBound
        store.overlay.preview(settings: settings)
    }

    @ViewBuilder
    private func screen(for section: MainSection) -> some View {
        switch section {
        case .develop: ModulePage(store: store, scope: .develop)
        case .library: ModulePage(store: store, scope: .library)
        case .mouseTrackpad: MouseTrackpadScreen(store: store)
        case .floatingPanels: FloatingPanelsScreen(store: store)
        case .general: GeneralScreen(store: store)
        }
    }
}

/// Header row in the title bar: wordmark on the left; Lightroom state and the
/// shortcut switch on the right. On macOS 26+ the items lose the shared glass
/// background so they read as part of the Synth surface.
private struct HeaderToolbar: ToolbarContent {
    let store: CommandStore

    var body: some ToolbarContent {
        // The macOS 26 SDK APIs need Xcode 26+ to compile; older toolchains
        // (Xcode 16) build the plain variant, which every macOS 14+ runs.
        #if compiler(>=6.2)
        if #available(macOS 26, *) {
            ToolbarItem(placement: .navigation) { Wordmark() }
                .sharedBackgroundVisibility(.hidden)
            ToolbarSpacer(.flexible)
            ToolbarItem(placement: .primaryAction) { HeaderStatus(store: store) }
                .sharedBackgroundVisibility(.hidden)
        } else {
            ToolbarItem(placement: .navigation) { Wordmark() }
            ToolbarItem(placement: .primaryAction) { HeaderStatus(store: store) }
        }
        #else
        ToolbarItem(placement: .navigation) { Wordmark() }
        ToolbarItem(placement: .primaryAction) { HeaderStatus(store: store) }
        #endif
    }
}

private struct Wordmark: View {
    var body: some View {
        if Synth.isFlat {
            // Galerist: Outfit semibold, and a full stop in the live colour.
            (Text("Lumerist") + Text(".").foregroundStyle(Synth.live))
                .font(SynthFont.font(.display, size: 17, weight: .semibold))
                .tracking(-0.34)
                .foregroundStyle(Synth.ice)
                .fixedSize()
                .padding(.leading, 6)
                .accessibilityLabel("Lumerist")
                .accessibilityAddTraits(.isHeader)
        } else {
            neon
        }
    }

    private var neon: some View {
        Text("Lumerist")
            .font(SynthFont.font(.display, size: 16, weight: .bold))
            .tracking(2)
            .textCase(.uppercase)
            .foregroundStyle(Synth.teal)
            .shadow(color: Synth.teal.opacity(0.6), radius: 8)
            .fixedSize()
            .padding(.leading, 6)
            .accessibilityAddTraits(.isHeader)
    }
}

private struct HeaderStatus: View {
    let store: CommandStore

    var body: some View {
        HStack(spacing: Synth.isFlat ? 20 : 14) {
            if Synth.isFlat {
                LinkState(isLinked: store.isConnected)
            } else if store.isConnected {
                StatusPill(text: "Lightroom · linked", systemImage: "checkmark", tone: .good)
            } else {
                StatusPill(text: "Lightroom · waiting", systemImage: "hourglass", tone: .waiting)
            }
            ShortcutSwitch(store: store)
        }
        .fixedSize()
        .padding(.trailing, 4)
    }
}

/// Galerist: "Lightroom · linked" with the live dot, or muted "waiting".
private struct LinkState: View {
    let isLinked: Bool

    var body: some View {
        HStack(spacing: Synth.Space.s) {
            if isLinked {
                Circle().fill(Synth.live).frame(width: 7, height: 7).accessibilityHidden(true)
            }
            Text(isLinked ? "Lightroom · linked" : "Lightroom · waiting")
        }
        .font(SynthFont.font(.mono, size: 11, weight: .medium))
        .tracking(1.8)
        .textCase(.uppercase)
        .foregroundStyle(isLinked ? Synth.ice : Synth.steel)
        .padding(.horizontal, 12)
        .frame(height: 28)
        .overlay(Rectangle().strokeBorder(Synth.gridLine, lineWidth: 1))
        .accessibilityElement(children: .combine)
        .accessibilityLabel(isLinked ? "Lightroom linked" : "Waiting for Lightroom")
    }
}

/// "SHORTCUTS [ON]" switch: magenta when active, amber while macOS permission is pending.
private struct ShortcutSwitch: View {
    let store: CommandStore

    var body: some View {
        if Synth.isFlat {
            flat
        } else {
            neon
        }
    }

    /// Galerist: "SHORTCUTS" in grey beside the square switch.
    private var flat: some View {
        let on = store.shortcutsAreEnabled
        let waiting = store.isWaitingForPermission
        return Button {
            store.toggleShortcuts()
        } label: {
            HStack(spacing: 10) {
                Text(waiting ? "Shortcuts · waiting" : "Shortcuts")
                    .font(SynthFont.font(.mono, size: 11, weight: .medium))
                    .tracking(1.8)
                    .textCase(.uppercase)
                    .foregroundStyle(Synth.steel)
                FlatSwitch(isOn: on, isPending: waiting)
            }
            .frame(height: 28)
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .help(on ? "Turn keyboard + scroll shortcuts off" : "Turn keyboard + scroll shortcuts on")
        .accessibilityLabel("Shortcuts")
        .accessibilityValue(on ? "On" : waiting ? "Waiting for permission" : "Off")
        .accessibilityAddTraits(.isToggle)
    }

    @ViewBuilder
    private var neon: some View {
        let on = store.shortcutsAreEnabled
        let waiting = store.isWaitingForPermission
        let state = on ? "ON" : waiting ? "WAIT" : "OFF"
        let color = on ? Synth.magenta : waiting ? Synth.amber : Synth.steel
        Button {
            store.toggleShortcuts()
        } label: {
            HStack(spacing: Synth.Space.s) {
                Text("Shortcuts")
                Text(state)
                    .font(SynthFont.font(.mono, size: 11, weight: .bold))
                    .padding(.horizontal, 8)
                    .frame(height: 20)
                    .foregroundStyle(on || waiting ? Synth.onMagenta : Synth.abyss)
                    .background(RoundedRectangle(cornerRadius: 2).fill(color))
            }
            .font(SynthFont.font(.mono, size: 11, weight: .semibold))
            .tracking(1)
            .textCase(.uppercase)
            .foregroundStyle(on ? Synth.magentaLight : waiting ? Synth.amber : Synth.steel)
            .padding(.leading, 10)
            .padding(.trailing, 4)
            .frame(height: 28)
            .background(RoundedRectangle(cornerRadius: Synth.Radius.control).fill(color.opacity(on ? 0.10 : 0)))
            .overlay(RoundedRectangle(cornerRadius: Synth.Radius.control).strokeBorder(color, lineWidth: 1))
            .shadow(color: on ? Synth.magenta.opacity(0.45) : .clear, radius: 8)
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .help(on ? "Turn keyboard + scroll shortcuts off" : "Turn keyboard + scroll shortcuts on")
        .accessibilityLabel("Shortcuts")
        .accessibilityValue(on ? "On" : waiting ? "Waiting for permission" : "Off")
        .accessibilityAddTraits(.isToggle)
    }
}

/// Uppercase tabs. Module pages on the left of the divider, settings on the
/// right. Miami: the current one is outlined in teal. Galerist: mono small
/// caps with their SF Symbols on the header band; the current one is ink
/// with a 2 pt underline.
private struct SectionTabs: View {
    @Binding var selection: MainSection

    var body: some View {
        if Synth.isFlat {
            flat
        } else {
            neon
        }
    }

    private var flat: some View {
        HStack(alignment: .center, spacing: 26) {
            ForEach(MainSection.allCases.filter(\.isModulePage)) { flatTab($0) }
            Rectangle()
                .fill(Synth.gridLine)
                .frame(width: 1, height: 18)
                .accessibilityHidden(true)
            ForEach(MainSection.allCases.filter { !$0.isModulePage }) { flatTab($0) }
            Spacer(minLength: 0)
        }
        .padding(.horizontal, 32)
        .frame(height: 46)
        // Under the tabs, so the current tab's underline covers it.
        .background(alignment: .bottom) {
            Rectangle().fill(Synth.gridLine).frame(height: 1).accessibilityHidden(true)
        }
        // The band runs up behind the header row in the title bar.
        .background(Synth.chrome.ignoresSafeArea(edges: .top).accessibilityHidden(true))
    }

    private func flatTab(_ section: MainSection) -> some View {
        let selected = section == selection
        return Button {
            selection = section
        } label: {
            HStack(spacing: 7) {
                Image(systemName: section.systemImage)
                    .font(.system(size: 11.5, weight: .medium))
                    .accessibilityHidden(true)
                Text(section.title)
                    .font(SynthFont.font(.mono, size: 11, weight: .medium))
                    .tracking(1.8)
                    .textCase(.uppercase)
                    .lineLimit(1)
                    .fixedSize()
            }
            .foregroundStyle(selected ? Synth.ice : Synth.steel)
            .frame(maxHeight: .infinity)
            .overlay(alignment: .bottom) {
                Rectangle().fill(selected ? Synth.magenta : Color.clear).frame(height: 2)
            }
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .accessibilityLabel(section.title)
        .accessibilityAddTraits(selected ? [.isSelected] : [])
    }

    private var neon: some View {
        HStack(alignment: .bottom, spacing: 2) {
            ForEach(MainSection.allCases.filter(\.isModulePage)) { tab($0) }
            Rectangle()
                .fill(Synth.gridLine)
                .frame(width: 1, height: 18)
                .padding(.horizontal, 6)
                .padding(.bottom, 10)
                .accessibilityHidden(true)
            ForEach(MainSection.allCases.filter { !$0.isModulePage }) { tab($0) }
            Spacer(minLength: 0)
        }
        .padding(.horizontal, 14)
        .padding(.top, 10)
        .overlay(alignment: .bottom) {
            Rectangle().fill(Synth.gridLine).frame(height: 1)
        }
    }

    private func tab(_ section: MainSection) -> some View {
        let selected = section == selection
        return Button {
            selection = section
        } label: {
            Text(section.title)
                .font(SynthFont.font(.display, size: 12, weight: .semibold))
                .tracking(0.8)
                .textCase(.uppercase)
                .lineLimit(1)
                .fixedSize()
                .foregroundStyle(selected ? Color.white : Synth.steel)
                .padding(.horizontal, 10)
                .padding(.top, 9)
                .padding(.bottom, 10)
                .background(
                    UnevenRoundedRectangle(topLeadingRadius: Synth.Radius.container, topTrailingRadius: Synth.Radius.container)
                        .fill(selected
                              ? LinearGradient(colors: [Synth.teal.opacity(0.18), Synth.teal.opacity(0.02)],
                                               startPoint: .top, endPoint: .bottom)
                              : LinearGradient(colors: [.clear], startPoint: .top, endPoint: .bottom))
                )
                .overlay(
                    UnevenRoundedRectangle(topLeadingRadius: Synth.Radius.container, topTrailingRadius: Synth.Radius.container)
                        .stroke(selected ? Synth.teal : .clear, lineWidth: 1)
                        .padding(.bottom, -1)
                )
                .shadow(color: selected ? Synth.teal.opacity(0.25) : .clear, radius: 7, y: -3)
                .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .accessibilityLabel(section.title)
        .accessibilityAddTraits(selected ? [.isSelected] : [])
        .animation(Synth.Motion.quick, value: selected)
    }
}

/// The main window drawn without the window around it (header row, tabs and
/// one screen at full length), for `OnboardingSnapshots`. The real header
/// lives in the window toolbar, which ImageRenderer cannot draw.
struct MainWindowSnapshot: View {
    let store: CommandStore
    let section: MainSection
    /// Remembers the module tab to show, instead of the user's defaults.
    var defaults: UserDefaults? = nil

    var body: some View {
        VStack(spacing: 0) {
            HStack(spacing: 20) {
                HStack(spacing: 8) {
                    ForEach(0..<3, id: \.self) { _ in
                        Circle().fill(Synth.isFlat ? Synth.gridLine : Synth.outline).frame(width: 12, height: 12)
                    }
                }
                .padding(.leading, 4)
                Wordmark()
                Spacer()
                HeaderStatus(store: store)
            }
            .padding(.horizontal, 16)
            .frame(height: 52)
            .background(Synth.isFlat ? Synth.chrome : Color.clear)
            Rectangle().fill(Synth.gridLine).frame(height: 1)
            SectionTabs(selection: .constant(section))
            Group {
                switch section {
                case .develop: ModulePage(store: store, scope: .develop, defaults: defaults)
                case .library: ModulePage(store: store, scope: .library, defaults: defaults)
                case .mouseTrackpad: MouseTrackpadScreen(store: store)
                case .floatingPanels: FloatingPanelsScreen(store: store)
                case .general: GeneralScreen(store: store)
                }
            }
            .padding(.horizontal, Synth.isFlat ? 32 : Synth.Space.xxl)
            .padding(.vertical, Synth.isFlat ? 28 : Synth.Space.xl)
            .frame(maxWidth: .infinity, alignment: .leading)
        }
        .frame(width: 960)
        .background(SynthBackground())
        .tint(Synth.teal)
        .environment(\.comboKey, store.keymap.keymap.systemKeys.comboKey)
    }
}
