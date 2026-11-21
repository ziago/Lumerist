import SwiftUI
import LumeristCore

/// General → System Keys: Combo Key, Palette Key, Key Reference toggle and
/// Pause toggle. Key boxes read combos with the current Combo Key.
struct SystemKeysSection: View {
    let store: CommandStore

    var body: some View {
        let systemKeys = store.keymap.keymap.systemKeys
        SettingsSection(title: "System Keys", systemImage: "command",
                        footer: "Click a box and press the new key: Esc cancels, Delete clears. System Keys work in every module, so no slider or Command Key can share them.") {
            ForEach(SystemKeys.Role.allCases, id: \.self) { role in
                SettingRow(title: role.title, detail: role.detail(comboKey: systemKeys.comboKey)) {
                    SystemKeySlot(store: store, role: role)
                }
            }
        }
    }
}

/// The key box of one System Key, here and in the Palette and Key Reference
/// settings.
struct SystemKeySlot: View {
    let store: CommandStore
    let role: SystemKeys.Role

    var body: some View {
        KeySlot(
            id: "system.\(role.rawValue)",
            chord: store.keymap.keymap.systemKeys.chord(for: role),
            minWidth: 64,
            accessibilityName: role.title,
            onSet: { KeymapEditor.shared.setSystem(store, role: role, chord: $0) },
            onClear: { KeymapEditor.shared.setSystem(store, role: role, chord: nil) }
        )
    }
}
