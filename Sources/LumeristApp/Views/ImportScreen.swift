import SwiftUI
import LumeristCore

/// The Import page: keys for culling in Lightroom's Import window (Include,
/// Exclude, Toggle), "Move to the next photo", and what Lumerist looks at to
/// recognise the window. No key is assigned until the user picks one.
struct ImportScreen: View {
    let store: CommandStore

    var body: some View {
        let keymap = store.keymap.keymap
        // Drawn again when the input source or Lightroom's language changes:
        // the keys follow both.
        let _ = store.lightroomKeysRevision
        let sends = ImportShortcuts.forCurrentLayout
        let warnings = Self.warnings(in: keymap)
        VStack(alignment: .leading, spacing: Synth.Space.xl) {
            VStack(alignment: .leading, spacing: Synth.Space.l) {
                ScreenHeader(title: "Import", readout: "Culling in Lightroom's Import window")
                Text("These keys work only while Lightroom's Import window is in front. There the Develop and Library keys pause, and the mouse wheel and trackpad scroll Lightroom as usual.")
                    .synthBody(Synth.isFlat ? 14 : 12, color: Synth.steel)
                    .lineSpacing(Synth.isFlat ? 3 : 0)
                    .frame(maxWidth: Synth.isFlat ? 640 : nil, alignment: .leading)
                    .fixedSize(horizontal: false, vertical: true)
            }

            SettingsSection(title: "Import keys", systemImage: "checkmark.square",
                            footer: "Pick keys under one hand, for example Z, X and C. Click a box and press the key: Esc cancels, Delete clears.") {
                ForEach(ImportKeys.Role.allCases, id: \.self) { role in
                    SettingRow(title: role.title, detail: role.detail) {
                        HStack(spacing: Synth.Space.m) {
                            let lightroomKey = sends.chord(for: role)?.displayText
                            Text(lightroomKey.map { "Sends \($0)" } ?? "Sends nothing")
                                .synthBody(12, color: Synth.steel)
                                .accessibilityLabel(lightroomKey.map { "Sends Lightroom's \($0) key" } ?? "Sends nothing")
                            ImportKeySlot(store: store, role: role, warning: warnings[role])
                        }
                    }
                }
            }
            if sends.toggle == nil {
                Label("Toggle isn't available with this keyboard layout.", systemImage: "exclamationmark.triangle.fill")
                    .synthBody(12, color: Synth.amber)
                    .fixedSize(horizontal: false, vertical: true)
            }
            if let error = KeymapEditor.shared.lastError {
                Label(error, systemImage: "exclamationmark.triangle.fill").synthBody(12, color: Synth.error)
            }
            // Lightroom keys the Import keys take over: never blocking, shown while they last.
            ForEach(ImportKeys.Role.allCases.filter { warnings[$0] != nil }, id: \.self) { role in
                Label(warnings[role] ?? "", systemImage: "exclamationmark.triangle.fill")
                    .synthBody(12, color: Synth.amber)
                    .fixedSize(horizontal: false, vertical: true)
            }

            SettingsSection(title: "Options", systemImage: "arrow.right.square") {
                SettingRow(title: "Move to the next photo",
                           detail: "After Include or Exclude, Lumerist also presses → so the next photo is selected.") {
                    Toggle("Move to the next photo", isOn: Binding(
                        get: { keymap.importKeys.advance },
                        set: { value in store.editKeymap { $0.importKeys.advance = value } }
                    ))
                    .labelsHidden()
                    .toggleStyle(.neon)
                }
            }

            Text("Lumerist recognises the Import window by its type and size, through macOS Accessibility, and reads no text from Lightroom.")
                .synthBody(12, color: Synth.steel)
                .fixedSize(horizontal: false, vertical: true)
        }
    }

    /// What each key box warns about: one of Lightroom's own keys in the
    /// Import window. Errors never get here, since a keymap with errors is
    /// never saved.
    private static func warnings(in keymap: Keymap) -> [ImportKeys.Role: String] {
        var result: [ImportKeys.Role: String] = [:]
        for conflict in keymap.conflicts() {
            for case .importKey(let role) in conflict.usages where result[role] == nil {
                result[role] = conflict.message
            }
        }
        return result
    }
}

/// The key box of one Import key; empty until the user picks a key.
struct ImportKeySlot: View {
    let store: CommandStore
    let role: ImportKeys.Role
    /// Shown on the box (⚠ and tooltip).
    var warning: String?

    var body: some View {
        KeySlot(
            id: "import.\(role.rawValue)",
            chord: store.keymap.keymap.importKeys[role],
            conflict: warning,
            minWidth: 64,
            accessibilityName: "\(role.title) key",
            onSet: { KeymapEditor.shared.setImport(store, role: role, chord: $0) },
            onClear: { KeymapEditor.shared.setImport(store, role: role, chord: nil) }
        )
    }
}
