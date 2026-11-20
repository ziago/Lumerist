import SwiftUI
import LumeristCore

/// The Develop or Library page: Sliders (Quick Develop in Library), Commands
/// and Keyboard tabs, with one search shared by the first two.
struct ModulePage: View {
    let store: CommandStore
    let scope: ParameterScope
    @AppStorage private var tabID: String
    @State private var search = ""

    /// `defaults`: where the open tab is remembered (snapshots pass their own suite).
    init(store: CommandStore, scope: ParameterScope, defaults: UserDefaults? = nil) {
        self.store = store
        self.scope = scope
        _tabID = AppStorage(wrappedValue: ModuleTab.sliders.rawValue, Navigation.moduleTabKey(scope), store: defaults)
    }

    private var tab: ModuleTab { ModuleTab(rawValue: tabID) ?? .sliders }

    var body: some View {
        VStack(alignment: .leading, spacing: Synth.Space.l) {
            ScreenHeader(
                title: scope == .develop ? "Develop" : "Library",
                readout: scope == .develop ? "active photo" : "all selected photos"
            )
            Text(scope == .develop
                 ? "Keep a Dial Key down and its slider follows the wheel, a drag or ← → until you let go. A Nudge Key moves it one step per press; add ⇧ for the other step."
                 : "These keys work like the Quick Develop buttons in Lightroom's Library: each step is added to what every selected photo already has, so their differences stay.")
                .synthBody(Synth.isFlat ? 14 : 12, color: Synth.steel)
                .lineSpacing(Synth.isFlat ? 3 : 0)
                .frame(maxWidth: Synth.isFlat ? 640 : nil, alignment: .leading)
                .fixedSize(horizontal: false, vertical: true)

            HStack(spacing: Synth.Space.m) {
                SynthSegmented(
                    items: ModuleTab.allCases,
                    selection: Binding(get: { tab }, set: { tabID = $0.rawValue }),
                    title: { $0.title(for: scope) }
                )
                Spacer(minLength: Synth.Space.m)
                if tab != .keyboard {
                    SynthSearchField(prompt: "Find a slider, key or command", text: $search)
                        .frame(maxWidth: Synth.isFlat ? 300 : 280)
                }
            }

            switch tab {
            case .sliders: SlidersTab(store: store, scope: scope, search: search)
            case .commands: CommandsTab(store: store, scope: scope, search: search)
            case .keyboard: KeyboardTab(store: store, scope: scope, reveal: reveal)
            }
        }
    }

    /// A click on the drawn keyboard: the slider or Command Key in its tab.
    private func reveal(_ target: KeyboardReveal) {
        switch target {
        case .slider(let label):
            search = label
            tabID = ModuleTab.sliders.rawValue
        case .command(let title):
            search = title
            tabID = ModuleTab.commands.rawValue
        case .system(let role):
            Navigation.go(to: role == .paletteKey || role == .keyReferenceToggle ? .floatingPanels : .general)
        }
    }
}

/// Usage → conflict message, computed once per render.
struct ConflictIndex {
    private var messages: [KeyUsage: String] = [:]

    init(_ keymap: Keymap, scope: ParameterScope) {
        for conflict in keymap.conflicts() where conflict.scope == scope || conflict.scope == nil {
            for usage in conflict.usages where messages[usage] == nil {
                messages[usage] = conflict.message
            }
        }
    }

    subscript(_ usage: KeyUsage) -> String? { messages[usage] }
}

/// Open/Close all groups.
struct ExpandCollapseButtons: View {
    let expandAll: () -> Void
    let collapseAll: () -> Void

    var body: some View {
        HStack(spacing: 4) {
            Button("Open all", action: expandAll).buttonStyle(.neonQuiet)
            Text("·").foregroundStyle(Synth.steel).accessibilityHidden(true)
            Button("Close all", action: collapseAll).buttonStyle(.neonQuiet)
        }
    }
}
