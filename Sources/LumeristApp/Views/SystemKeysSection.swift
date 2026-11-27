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
                SettingRow(title: role.title, detail: detail(role, systemKeys)) {
                    SystemKeySlot(store: store, role: role)
                }
            }
        }
    }

    /// Why the standard layout left the Palette Key empty on this keyboard
    /// (`PaletteKeyDefault`), when it is empty.
    private func detail(_ role: SystemKeys.Role, _ systemKeys: SystemKeys) -> String {
        let text = role.detail(comboKey: systemKeys.comboKey)
        guard role == .paletteKey, systemKeys.paletteKey == nil, let why = SystemKeys.Role.noPaletteKeyReason else { return text }
        return text + " " + why
    }
}

/// The key box of one System Key, here and in the Palette and Key Reference
/// settings. A key that takes one of Lightroom's own keys gets the warning
/// (⚠ and tooltip); it never blocks.
struct SystemKeySlot: View {
    let store: CommandStore
    let role: SystemKeys.Role

    var body: some View {
        let keymap = store.keymap.keymap
        KeySlot(
            id: "system.\(role.rawValue)",
            chord: keymap.systemKeys.chord(for: role),
            conflict: keymap.lightroomWarning(for: .system(role)),
            minWidth: 64,
            accessibilityName: role.title,
            onSet: { KeymapEditor.shared.setSystem(store, role: role, chord: $0) },
            onClear: { KeymapEditor.shared.setSystem(store, role: role, chord: nil) }
        )
    }
}
