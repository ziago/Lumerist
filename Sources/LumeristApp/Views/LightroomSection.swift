import SwiftUI
import LumeristCore

/// General → Lightroom: the plug-in link and its tests, how often a dial
/// updates Lightroom, and the options that change what Lightroom does.
struct LightroomSection: View {
    let store: CommandStore
    @State private var confirmsTestExposure = false
    @State private var checkedCatalog = false

    var body: some View {
        let input = store.keymap.keymap.input
        SettingsSection(title: "Lightroom", systemImage: "link",
                        footer: "Lumerist and the plug-in talk only through 127.0.0.1, inside this Mac.") {
            SettingRow(title: "Link", detail: linkDetail) {
                HStack(spacing: Synth.Space.s) {
                    if store.isConnected {
                        StatusPill(text: "Linked", systemImage: "checkmark", tone: .good)
                    } else {
                        StatusPill(text: "Waiting", systemImage: "hourglass", tone: .waiting)
                    }
                    Button("Reconnect") { store.reconnect() }.buttonStyle(.neonSecondary)
                }
            }
            SettingRow(title: "Try the link",
                       detail: "Check connection waits for the plug-in to answer. The test exposure adds +0.1 EV to the selected photo in Develop.") {
                HStack(spacing: Synth.Space.s) {
                    Button("Check connection") { store.sendConnectionCheck() }.buttonStyle(.neonSecondary)
                    Button("Send test exposure…") { confirmsTestExposure = true }.buttonStyle(.neonQuiet)
                }
            }
            if let latestError = store.errors.first {
                Label(latestError, systemImage: "exclamationmark.triangle.fill")
                    .synthBody(12, color: Synth.error)
                    .textSelection(.enabled)
                    .fixedSize(horizontal: false, vertical: true)
                    .padding(.horizontal, Synth.rowInset)
                    .padding(.vertical, 10)
            }

            smoothnessRow

            toggleRow("Follow the selected mask",
                      "With a mask selected, Dial Keys and Nudge Keys move that mask's own sliders. A slider masks don't have stays put and shows a notice.",
                      input.followSelectedMask) { $0.input.followSelectedMask = $1 }
            toggleRow("Open the slider's panel",
                      "As an adjustment starts, Lightroom expands the panel that holds the slider, so you can watch it move.",
                      input.revealPanels) { $0.input.revealPanels = $1 }

            SettingRow(title: "Parameter check", detail: catalogDetail) {
                Button("Check now") {
                    checkedCatalog = true
                    store.checkParameterCatalog()
                }
                .buttonStyle(.neonSecondary)
                .disabled(!store.isConnected)
            }
        }
        .confirmationDialog("Send a test exposure?", isPresented: $confirmsTestExposure, titleVisibility: .visible) {
            Button("Add +0.1 EV", role: .destructive) { store.sendTestExposure() }
        } message: {
            Text("Lightroom raises the Exposure of the selected photo by 0.1. It works in the Develop module, and Undo takes it back.")
        }
    }

    private var smoothnessRow: some View {
        let hz = store.preferences.updateRateHz
        return SettingRow(title: "Smoothness",
                          detail: "How often a turning dial updates Lightroom. Smooth follows your hand more closely; Light gives Lightroom more time to redraw. Steps in between are added up, never lost.") {
            VStack(alignment: .trailing, spacing: 4) {
                SynthSegmented(items: Smoothness.allCases,
                               selection: Binding(get: { Smoothness.nearest(to: hz) },
                                                  set: { store.preferences.updateRateHz = $0.hz }),
                               title: \.title)
                    .fixedSize()
                    .accessibilityElement(children: .contain)
                    .accessibilityLabel("Smoothness")
                    .accessibilityValue("\(Int(hz)) updates per second")
                Text("\(Int(hz)) Hz").synthLabel(10)
                    .accessibilityHidden(true)
            }
        }
    }

    /// The link in words. `store.status` also carries shortcut messages, so
    /// the sentence is built from the link state alone.
    private var linkDetail: String {
        if store.isConnected { return "Linked to the Lumerist plug-in inside Lightroom Classic." }
        if LightroomLifecycle.isLightroomRunning {
            return "Lightroom Classic is open; waiting for the Lumerist plug-in to answer."
        }
        return "Lightroom Classic is closed. Lumerist links up as soon as it opens."
    }

    private var catalogDetail: String {
        if !checkedCatalog { return "Asks Lightroom which sliders it knows. Open Lightroom in Develop first." }
        if store.unsupportedParameters.isEmpty { return "Lightroom knows every slider Lumerist can move." }
        return "Unknown to this Lightroom, so their keys show a warning: "
            + store.unsupportedParameters.joined(separator: ", ")
    }

    private func toggleRow(_ title: String, _ detail: String, _ value: Bool,
                           _ apply: @escaping (inout Keymap, Bool) -> Void) -> some View {
        SettingRow(title: title, detail: detail) {
            Toggle(title, isOn: Binding(get: { value }, set: { newValue in store.editKeymap { apply(&$0, newValue) } }))
                .labelsHidden()
                .toggleStyle(.neon)
        }
    }
}

/// Three named update rates instead of a raw number. Each one sits inside
/// `IntentCoalescer.rateRange`; a rate saved by an older version shows as
/// the nearest choice until the user picks one.
enum Smoothness: CaseIterable, Hashable {
    case light
    case balanced
    case smooth

    var title: String {
        switch self {
        case .light: "Light"
        case .balanced: "Balanced"
        case .smooth: "Smooth"
        }
    }

    var hz: Double {
        let range = IntentCoalescer.rateRange
        let wanted: Double = switch self {
        case .light: 15
        case .balanced: IntentCoalescer.defaultRate
        case .smooth: 50
        }
        return min(range.upperBound, max(range.lowerBound, wanted))
    }

    static func nearest(to hz: Double) -> Smoothness {
        allCases.min { abs($0.hz - hz) < abs($1.hz - hz) } ?? .balanced
    }
}
