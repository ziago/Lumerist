import AppKit
import SwiftUI
import LumeristCore

extension EnvironmentValues {
    /// The current Combo Key, so combos read "Tab + K". Set at the root of
    /// every window and panel from the keymap's system keys.
    var comboKey: KeyChord? {
        get { self[ComboKeyEnvironmentKey.self] }
        set { self[ComboKeyEnvironmentKey.self] = newValue }
    }
}

private struct ComboKeyEnvironmentKey: EnvironmentKey {
    static let defaultValue: KeyChord? = nil
}

/// Records the next key press (with ⌃ ⌥ ⇧ ⌘) in the Lumerist window.
/// Holding the Combo Key and pressing another key records a combo
/// ("Tab + K"); the Combo Key pressed and released alone records itself.
/// Esc alone cancels; Delete alone clears the slot. One slot records at a time.
@MainActor
@Observable
final class ChordRecorder {
    static let shared = ChordRecorder()

    private(set) var recordingID: String?
    /// The Combo Key is down: the next key makes a combo.
    private(set) var isComboKeyDown = false
    @ObservationIgnored private var comboKey: KeyChord?
    @ObservationIgnored private var monitor: Any?
    @ObservationIgnored private var onChord: ((KeyChord) -> Void)?
    @ObservationIgnored private var onClear: (() -> Void)?

    func isRecording(_ id: String) -> Bool { recordingID == id }

    func start(id: String, comboKey: KeyChord?, onChord: @escaping (KeyChord) -> Void, onClear: @escaping () -> Void) {
        stop()
        recordingID = id
        self.comboKey = comboKey
        self.onChord = onChord
        self.onClear = onClear
        monitor = NSEvent.addLocalMonitorForEvents(matching: [.keyDown, .keyUp]) { [weak self] event in
            MainActor.assumeIsolated { self?.handle(event) }
            // Key-ups go on to the window; key-downs never reach it while recording.
            return event.type == .keyUp ? event : nil
        }
    }

    func stop() {
        if let monitor { NSEvent.removeMonitor(monitor) }
        monitor = nil
        recordingID = nil
        isComboKeyDown = false
        comboKey = nil
        onChord = nil
        onClear = nil
    }

    private func handle(_ event: NSEvent) {
        let keyCode = Int64(event.keyCode)
        let chord = KeyChord(keyCode, Self.modifiers(of: event.modifierFlags))
        if event.type == .keyUp {
            // The Combo Key pressed and released alone records itself.
            if isComboKeyDown, let comboKey, keyCode == comboKey.keyCode { finish(comboKey) }
            return
        }
        if keyCode == 53 && chord.modifiers.isEmpty { stop(); return } // Esc cancels
        if isComboKeyDown {
            if let comboKey, keyCode == comboKey.keyCode { return }      // auto-repeat
            finish(chord.asCombo)
            return
        }
        if let comboKey, chord == comboKey, !event.isARepeat {
            isComboKeyDown = true // a combo or the Combo Key alone: the next event decides
            return
        }
        if keyCode == 51 && chord.modifiers.isEmpty { onClear?(); stop(); return } // Delete clears
        finish(chord)
    }

    private func finish(_ chord: KeyChord) {
        let handler = onChord
        stop()
        handler?(chord)
    }

    static func modifiers(of flags: NSEvent.ModifierFlags) -> KeyModifiers {
        var result: KeyModifiers = []
        if flags.contains(.shift) { result.insert(.shift) }
        if flags.contains(.control) { result.insert(.control) }
        if flags.contains(.option) { result.insert(.option) }
        if flags.contains(.command) { result.insert(.command) }
        return result
    }
}

/// One key slot: a KeyCap that records a chord on click, with a clear button
/// when assigned. States: assigned, empty "+", recording, conflict (⚠ + tooltip).
/// Combos read with the current Combo Key ("Tab + K").
struct KeySlot: View {
    let id: String
    let chord: KeyChord?
    var conflict: String?
    var minWidth: CGFloat = 52
    let accessibilityName: String
    let onSet: (KeyChord) -> Void
    let onClear: () -> Void

    @Environment(\.comboKey) private var comboKey
    @State private var isHovering = false
    private var recorder: ChordRecorder { .shared }

    var body: some View {
        let recording = recorder.isRecording(id)
        let state: KeyCap.State = recording ? .recording : chord == nil ? .empty : conflict != nil ? .conflict : .assigned
        let text = chord?.displayText(comboKey: comboKey)
        HStack(spacing: 2) {
            Button {
                if recording {
                    recorder.stop()
                } else {
                    recorder.start(id: id, comboKey: comboKey, onChord: onSet, onClear: onClear)
                }
            } label: {
                KeyCap(text: recording ? recordingPrompt : (text ?? "+"), state: state, minWidth: minWidth)
            }
            .buttonStyle(.plain)
            .help(conflict ?? (chord == nil ? "Click, then press a key (Esc cancels)" : "Click to record another key; Delete clears"))
            .accessibilityLabel(accessibilityName)
            .accessibilityValue(recording ? "Recording, press a key" : text ?? "Not assigned")
            .accessibilityHint(conflict ?? "Records a new key")

            if chord != nil && !recording {
                Button(action: onClear) {
                    Image(systemName: "xmark")
                        .font(.system(size: 8, weight: .heavy))
                        .foregroundStyle(isHovering ? Synth.ice : Synth.steel)
                        .frame(width: 14, height: 14)
                        .contentShape(Rectangle())
                }
                .buttonStyle(.plain)
                .opacity(isHovering ? 1 : 0.55)
                .accessibilityLabel("Clear \(accessibilityName)")
            } else {
                Color.clear.frame(width: 14, height: 14).accessibilityHidden(true)
            }
        }
        .onHover { isHovering = $0 }
    }

    /// "PRESS", or "TAB + …" while the Combo Key is held during recording.
    private var recordingPrompt: String {
        guard recorder.isComboKeyDown, let comboKey else { return "PRESS" }
        return "\(comboKey.displayText) + …"
    }
}

/// A search field in Synth style.
struct SynthSearchField: View {
    let prompt: String
    @Binding var text: String

    var body: some View {
        HStack(spacing: 6) {
            Image(systemName: "magnifyingglass")
                .font(.system(size: 11, weight: .bold))
                .foregroundStyle(Synth.steel)
            TextField(prompt, text: $text)
                .textFieldStyle(.plain)
                .font(SynthFont.font(.text, size: 13))
                .foregroundStyle(Synth.ice)
            if !text.isEmpty {
                Button { text = "" } label: {
                    Image(systemName: "xmark.circle.fill").foregroundStyle(Synth.steel)
                }
                .buttonStyle(.plain)
                .accessibilityLabel("Clear search")
            }
        }
        .padding(.horizontal, Synth.isFlat ? 12 : 10)
        .frame(height: Synth.isFlat ? 32 : 30)
        .background(RoundedRectangle(cornerRadius: Synth.Radius.control).fill(Synth.isFlat ? Synth.panel : Synth.abyss))
        .overlay(RoundedRectangle(cornerRadius: Synth.Radius.control).strokeBorder(Synth.outline, lineWidth: 1))
    }
}

/// Small uppercase segmented control (sub-tabs, sort, modes). Galerist:
/// hairline cells side by side, the selected one filled with ink.
struct SynthSegmented<Item: Hashable>: View {
    let items: [Item]
    @Binding var selection: Item
    let title: (Item) -> String
    @Environment(\.isEnabled) private var isEnabled

    var body: some View {
        if Synth.isFlat {
            flat
        } else {
            neon
        }
    }

    private var flat: some View {
        // Cells overlap by 1 pt so neighbouring borders read as one hairline;
        // the selected cell is drawn on top with its ink border.
        HStack(spacing: -1) {
            ForEach(items, id: \.self) { item in
                let selected = item == selection
                Button {
                    selection = item
                } label: {
                    Text(title(item))
                        .font(SynthFont.font(.mono, size: 11, weight: .medium))
                        .tracking(1.6)
                        .textCase(.uppercase)
                        .lineLimit(1)
                        .foregroundStyle(isEnabled ? (selected ? Synth.onMagenta : Synth.ice) : (selected ? Synth.panel : Synth.disabled))
                        .padding(.horizontal, 14)
                        .frame(height: 32)
                        .background(Rectangle().fill(selected ? (isEnabled ? Synth.magenta : Synth.disabled) : Synth.panel))
                        .overlay(Rectangle().strokeBorder(selected ? (isEnabled ? Synth.magenta : Synth.disabled) : Synth.outline,
                                                          lineWidth: 1))
                        .contentShape(Rectangle())
                }
                .buttonStyle(.plain)
                .zIndex(selected ? 1 : 0)
                .accessibilityLabel(title(item))
                .accessibilityAddTraits(selected ? [.isSelected] : [])
            }
        }
    }

    private var neon: some View {
        HStack(spacing: 0) {
            ForEach(items, id: \.self) { item in
                let selected = item == selection
                Button {
                    selection = item
                } label: {
                    Text(title(item))
                        .font(SynthFont.font(.mono, size: 11, weight: .semibold))
                        .tracking(1)
                        .textCase(.uppercase)
                        .foregroundStyle(selected ? Synth.onMagenta : (isEnabled ? Synth.steel : Synth.disabled))
                        .padding(.horizontal, 12)
                        .frame(height: 26)
                        .background(selected ? (isEnabled ? Synth.magenta : Synth.disabled) : Color.clear)
                        .contentShape(Rectangle())
                }
                .buttonStyle(.plain)
                .accessibilityLabel(title(item))
                .accessibilityAddTraits(selected ? [.isSelected] : [])
            }
        }
        .clipShape(RoundedRectangle(cornerRadius: Synth.Radius.control))
        .overlay(RoundedRectangle(cornerRadius: Synth.Radius.control).strokeBorder(Synth.outline, lineWidth: 1))
    }
}

/// A collapsible section header with a counter. Galerist: chevron and mono
/// small-caps title with a square ink badge; a closed section is a white
/// hairline row (stack closed rows with `padding(.top, -1)` to share lines).
struct CollapsibleHeader: View {
    let title: String
    let count: Int
    let isExpanded: Bool
    let toggle: () -> Void

    var body: some View {
        if Synth.isFlat {
            flat
        } else {
            neon
        }
    }

    private var flat: some View {
        Button(action: toggle) {
            HStack(spacing: 10) {
                Image(systemName: "chevron.right")
                    .font(.system(size: 10, weight: .bold))
                    .rotationEffect(.degrees(isExpanded ? 90 : 0))
                    .foregroundStyle(Synth.ice)
                SectionTitle(title: title)
                if count > 0 {
                    Text("\(count)")
                        .font(SynthFont.font(.mono, size: 10, weight: .medium))
                        .monospacedDigit()
                        .foregroundStyle(Synth.onMagenta)
                        .padding(.horizontal, 6)
                        .frame(height: 17)
                        .background(Rectangle().fill(Synth.magenta))
                        .accessibilityLabel("\(count) assigned")
                }
                Spacer()
            }
            .padding(.horizontal, isExpanded ? 0 : 18)
            .frame(height: isExpanded ? 32 : 44)
            .background(isExpanded ? Color.clear : Synth.panel)
            .overlay(Rectangle().strokeBorder(isExpanded ? Color.clear : Synth.gridLine, lineWidth: 1))
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .accessibilityLabel(title)
        .accessibilityValue(isExpanded ? "Expanded" : "Collapsed")
        .animation(Synth.Motion.quick, value: isExpanded)
    }

    private var neon: some View {
        Button(action: toggle) {
            HStack(spacing: Synth.Space.s) {
                Image(systemName: "chevron.right")
                    .font(.system(size: 10, weight: .bold))
                    .rotationEffect(.degrees(isExpanded ? 90 : 0))
                    .foregroundStyle(Synth.teal)
                Text(title).synthTitle(14)
                if count > 0 {
                    Text("\(count)")
                        .font(SynthFont.font(.mono, size: 10, weight: .bold))
                        .foregroundStyle(Synth.onMagenta)
                        .padding(.horizontal, 6)
                        .frame(height: 16)
                        .background(Capsule().fill(Synth.magenta))
                        .accessibilityLabel("\(count) assigned")
                }
                Spacer()
            }
            .padding(.vertical, 8)
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .accessibilityLabel(title)
        .accessibilityValue(isExpanded ? "Expanded" : "Collapsed")
        .animation(Synth.Motion.quick, value: isExpanded)
    }
}
