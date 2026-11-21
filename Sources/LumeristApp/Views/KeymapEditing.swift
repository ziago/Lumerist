import SwiftUI
import LumeristCore

/// Which key of a parameter slot is being edited.
enum SlotRole: String {
    case dial
    case nudgeDown
    case nudgeUp

    var title: String {
        switch self {
        case .dial: "Dial Key"
        case .nudgeDown: "Nudge −"
        case .nudgeUp: "Nudge +"
        }
    }

    func chord(in slot: ParameterSlot?) -> KeyChord? {
        switch self {
        case .dial: slot?.dial
        case .nudgeDown: slot?.nudgeDown
        case .nudgeUp: slot?.nudgeUp
        }
    }

    func set(_ chord: KeyChord?, in slot: inout ParameterSlot) {
        switch self {
        case .dial: slot.dial = chord
        case .nudgeDown: slot.nudgeDown = chord
        case .nudgeUp: slot.nudgeUp = chord
        }
    }

    func usage(_ key: ParameterKey) -> KeyUsage {
        switch self {
        case .dial: .dial(key)
        case .nudgeDown: .nudgeDown(key)
        case .nudgeUp: .nudgeUp(key)
        }
    }
}

extension KeyUsage {
    /// "the Dial Key of Exposure", "the Nudge + key of Contrast", "the Command Key “Auto Tone”".
    var label: String {
        switch self {
        case .dial(let key): "the Dial Key of \(Self.parameterLabel(key))"
        case .nudgeDown(let key): "the Nudge − key of \(Self.parameterLabel(key))"
        case .nudgeUp(let key): "the Nudge + key of \(Self.parameterLabel(key))"
        case .action(_, let title): "the Command Key “\(title)”"
        case .system(let role): "the \(role.title)"
        }
    }

    static func parameterLabel(_ key: ParameterKey) -> String {
        ParameterCatalog.descriptor(for: key)?.label ?? key.id
    }

    var isSystem: Bool { if case .system = self { true } else { false } }
}

extension SystemKeys.Role {
    var title: String {
        switch self {
        case .comboKey: "Combo Key"
        case .paletteKey: "Palette Key"
        case .keyReferenceToggle: "Key Reference toggle"
        case .pauseToggle: "Pause toggle"
        }
    }

    /// The longer explanation; combos are written with the current Combo Key.
    func detail(comboKey: KeyChord?) -> String {
        switch self {
        case .comboKey:
            let combo = KeyChord(40, .combo).displayText(comboKey: comboKey) // "Tab + K"
            return "Gives every key a second job: hold the Combo Key, then press the other key (shown as \(combo)). "
                + "To assign a combo, click a key box and press the two keys together. Tapped alone, it still reaches Lightroom."
        case .paletteKey:
            return "Hold it in Develop to open the Palette under the pointer: pick a Create New Mask tool (Subject, Sky, "
                + "People…) or a favourite preset. Hold it for a quick look; tap it and the Palette stays open until you press the key again. Clear the key to turn the Palette off."
        case .keyReferenceToggle:
            let standard = KeyChord(4, .combo).displayText(comboKey: comboKey) // "Tab + H"
            return "Shows or hides the Key Reference, the floating list of your keys. The standard layout uses the combo \(standard)."
        case .pauseToggle:
            return "Hands every key back to Lightroom until you press it again. Handy when you need to type."
        }
    }
}

extension Keymap {
    /// Removes `chord` from every slot and action of `scope`, except the usage
    /// being assigned. A combo and the plain key are different chords.
    mutating func removeChord(_ chord: KeyChord, in scope: ParameterScope, keeping usage: KeyUsage?) {
        for id in slots(for: scope).keys {
            let key = ParameterKey(scope: scope, id: id)
            updateSlot(for: key) { slot in
                if slot.dial == chord, usage != .dial(key) { slot.dial = nil }
                if slot.nudgeDown == chord, usage != .nudgeDown(key) { slot.nudgeDown = nil }
                if slot.nudgeUp == chord, usage != .nudgeUp(key) { slot.nudgeUp = nil }
            }
        }
        commandKeys.removeAll { action in
            guard action.scope.includes(scope), action.chord == chord else { return false }
            if case .action(let id, _) = usage, id == action.id { return false }
            return true
        }
    }

    /// Error conflicts that involve `chord` in `scope` (or system keys).
    func blockingConflicts(for chord: KeyChord, in scope: ParameterScope?) -> [KeyConflict] {
        conflicts().filter { $0.severity == .error && $0.chord == chord && ($0.scope == nil || $0.scope == scope) }
    }
}

/// Applies key assignments through the store, asking before moving a key
/// that is already used elsewhere.
@MainActor
@Observable
final class KeymapEditor {
    static let shared = KeymapEditor()

    struct Pending: Identifiable {
        let id = UUID()
        let title: String
        let message: String
        let canMove: Bool
        let apply: () -> Void
    }

    var pending: Pending?
    var lastError: String?

    /// Validates `change` on a copy; commits it, or asks to move conflicting
    /// keys. Anything that moving keys can't fix (a combo on a Dial Key, a
    /// combo using the Combo Key…) is refused with its reason.
    func commit(_ store: CommandStore, chord: KeyChord?, scope: ParameterScope?,
                usage: KeyUsage?, change: @escaping (inout Keymap) -> Void) {
        var proposed = store.keymap.keymap
        change(&proposed)
        do {
            try proposed.validate()
            store.editKeymap { change(&$0) }
            lastError = nil
        } catch {
            guard let chord, case .conflicts? = error as? KeymapError else { refuse(error); return }
            let blocking = proposed.blockingConflicts(for: chord, in: scope)
            let others = blocking.flatMap(\.usages).filter { $0 != usage }
            guard !others.isEmpty else { refuse(error); return }
            let system = others.contains(where: \.isSystem)
            let names = Array(Set(others.map(\.label))).sorted().joined(separator: ", ")
            let text = proposed.displayText(for: chord)
            pending = Pending(
                title: "\(text) is already in use",
                message: system
                    ? "\(text) is \(names). Change that system key first (General → System Keys)."
                    : "\(text) is \(names). Move it here?",
                canMove: !system && scope != nil,
                apply: { [weak self] in
                    guard let scope else { return }
                    self?.commitAfterMoving(store, chord: chord, scope: scope, usage: usage, change: change)
                }
            )
        }
    }

    /// Shows why a key can't be used: inline (`lastError`) and in an alert.
    private func refuse(_ error: Error) {
        let message = error.localizedDescription
        lastError = message
        pending = Pending(title: "This key can't go here", message: message, canMove: false, apply: {})
    }

    private func commitAfterMoving(_ store: CommandStore, chord: KeyChord, scope: ParameterScope,
                                   usage: KeyUsage?, change: @escaping (inout Keymap) -> Void) {
        var proposed = store.keymap.keymap
        proposed.removeChord(chord, in: scope, keeping: usage)
        change(&proposed)
        do {
            try proposed.validate()
            store.editKeymap { keymap in
                keymap.removeChord(chord, in: scope, keeping: usage)
                change(&keymap)
            }
            lastError = nil
        } catch {
            lastError = error.localizedDescription
        }
    }

    /// Assigns `chord` (nil clears) to one key of a parameter slot. A combo
    /// on a Dial Key is refused by validation, with its reason.
    func assign(_ store: CommandStore, chord: KeyChord?, role: SlotRole, key: ParameterKey) {
        commit(store, chord: chord, scope: key.scope, usage: role.usage(key)) { keymap in
            keymap.updateSlot(for: key) { role.set(chord, in: &$0) }
        }
    }

    func setSystem(_ store: CommandStore, role: SystemKeys.Role, chord: KeyChord?) {
        commit(store, chord: chord, scope: nil, usage: .system(role)) { keymap in
            switch role {
            case .comboKey: keymap.systemKeys.comboKey = chord
            case .paletteKey: keymap.systemKeys.paletteKey = chord
            case .keyReferenceToggle: keymap.systemKeys.keyReferenceToggle = chord
            case .pauseToggle: keymap.systemKeys.pauseToggle = chord
            }
        }
    }
}

/// Alert for a pending move; attach once per screen.
struct KeymapEditorAlerts: ViewModifier {
    @Bindable private var editor = KeymapEditor.shared

    func body(content: Content) -> some View {
        content
            .alert(editor.pending?.title ?? "", isPresented: Binding(
                get: { editor.pending != nil },
                set: { if !$0 { editor.pending = nil } }
            ), presenting: editor.pending) { pending in
                if pending.canMove {
                    Button("Move here") { pending.apply(); editor.pending = nil }
                    Button("Cancel", role: .cancel) { editor.pending = nil }
                } else {
                    Button("OK", role: .cancel) { editor.pending = nil }
                }
            } message: { pending in
                Text(pending.message)
            }
    }
}

extension View {
    func keymapEditorAlerts() -> some View { modifier(KeymapEditorAlerts()) }
}

// MARK: - Commands, tools, presets

extension LightroomCommandName {
    var title: String {
        switch self {
        case .undo: "Undo"
        case .redo: "Redo"
        case .autoTone: "Auto Tone"
        case .autoWhiteBalance: "Auto White Balance"
        case .resetAll: "Reset all Develop settings"
        case .nextPhoto: "Next photo"
        case .previousPhoto: "Previous photo"
        case .flagPick: "Flag as Pick"
        case .flagReject: "Flag as Rejected"
        case .flagRemove: "Remove flag"
        case .rating: "Set star rating"
        case .colorLabel: "Set colour label"
        case .switchModule: "Switch module"
        case .selectTool: "Select Develop tool"
        case .revealPanel: "Open a slider's panel"
        case .zoomToggle: "Toggle zoom"
        case .showClipping: "Show clipping"
        case .createMask: "New mask"
        case .addToMask: "Add to mask"
        case .subtractFromMask: "Subtract from mask"
        case .intersectWithMask: "Intersect mask"
        case .invertMask: "Invert mask"
        case .toggleMaskOverlay: "Toggle mask overlay"
        case .applyPreset: "Apply preset"
        case .copySettings: "Copy settings"
        case .pasteSettings: "Paste settings"
        }
    }

    var group: String {
        switch self {
        case .undo, .redo, .copySettings, .pasteSettings, .resetAll: "Edit"
        case .autoTone, .autoWhiteBalance, .showClipping, .zoomToggle, .revealPanel, .selectTool: "Develop"
        case .nextPhoto, .previousPhoto, .flagPick, .flagReject, .flagRemove, .rating, .colorLabel, .switchModule: "Photos"
        case .createMask, .addToMask, .subtractFromMask, .intersectWithMask, .invertMask, .toggleMaskOverlay: "Masking"
        case .applyPreset: "Presets"
        }
    }

    /// Commands whose argument is a mask "type/subtype".
    var takesMask: Bool { [.createMask, .addToMask, .subtractFromMask, .intersectWithMask].contains(self) }

    /// A sensible default argument for a freshly picked command.
    var defaultArgument: String? {
        switch self {
        case .rating: "5"
        case .colorLabel: "red"
        case .switchModule: "develop"
        case .selectTool: "crop"
        case .revealPanel: "Exposure"
        case .createMask, .addToMask, .subtractFromMask, .intersectWithMask: "aiSelection/subject"
        default: nil
        }
    }
}

/// Mask tools offered by the Palette and the Command Key editor.
struct MaskTool: Identifiable, Hashable {
    let id: String          // "aiSelection/subject"
    let title: String
    let systemImage: String

    static let all: [MaskTool] = [
        MaskTool(id: "aiSelection/subject", title: "Subject", systemImage: "person.crop.rectangle"),
        MaskTool(id: "aiSelection/sky", title: "Sky", systemImage: "cloud.sun"),
        MaskTool(id: "aiSelection/background", title: "Background", systemImage: "rectangle.dashed"),
        MaskTool(id: "aiSelection/people", title: "People", systemImage: "person.2"),
        MaskTool(id: "aiSelection/objects", title: "Objects", systemImage: "cube"),
        MaskTool(id: "aiSelection/landscape", title: "Landscape", systemImage: "mountain.2"),
        MaskTool(id: "brush", title: "Brush", systemImage: "paintbrush.pointed"),
        MaskTool(id: "gradient", title: "Linear", systemImage: "square.bottomhalf.filled"),
        MaskTool(id: "radialGradient", title: "Radial", systemImage: "circle.dotted"),
        MaskTool(id: "rangeMask/color", title: "Color range", systemImage: "eyedropper"),
        MaskTool(id: "rangeMask/luminance", title: "Luminance range", systemImage: "sun.max"),
        MaskTool(id: "rangeMask/depth", title: "Depth range", systemImage: "cube.transparent"),
    ]

    static func title(for argument: String?) -> String {
        guard let argument else { return "" }
        return all.first { $0.id == argument }?.title ?? argument
    }
}

extension CommandKey.Action {
    /// One line describing the action.
    var summary: String {
        switch self {
        case .command(let name, let argument):
            let command = LightroomCommandName(rawValue: name)
            let title = command?.title ?? name
            guard let argument else { return title }
            if command?.takesMask == true { return "\(title): \(MaskTool.title(for: argument))" }
            if command == .rating { return "\(title): \(argument) ★" }
            if command == .revealPanel { return "\(title): \(ParameterCatalog.descriptor(scope: .develop, id: argument)?.label ?? argument)" }
            return "\(title): \(argument.capitalized)"
        case .preset(_, let name): return "Preset: \(name)"
        case .shortcut(let chord): return "Lightroom shortcut \(chord.displayText)" // never a combo
        case .macro(let steps): return "Macro · \(steps.count) step\(steps.count == 1 ? "" : "s")"
        }
    }
}
