import SwiftUI
import LumeristCore

/// Floating panels → Key Reference: its key (also in General → System Keys),
/// show it, and put it back in its corner.
struct KeyReferenceScreen: View {
    let store: CommandStore

    var body: some View {
        SettingsSection(title: "Key Reference", systemImage: "list.bullet.rectangle",
                        footer: "Move it by its title bar.") {
            SettingRow(title: "Key Reference key",
                       detail: "Press it in Develop or Library to show or hide the list. Click the box and press another key, or hold Tab with it for a combo.") {
                SystemKeySlot(store: store, role: .keyReferenceToggle)
            }
            SettingRow(title: "Show the Key Reference",
                       detail: "A list of the keys that work in Lightroom's current module, floating above Lightroom. Clicking it leaves Lightroom in front.") {
                Toggle("Show the Key Reference", isOn: Binding(
                    get: { store.isKeyReferenceVisible },
                    set: { store.setKeyReferenceVisible($0) }
                ))
                .labelsHidden()
                .toggleStyle(.neon)
            }
            SettingRow(title: "Position", detail: "Sends the panel back to the top-right corner of the screen.") {
                Button("Reset position") { FloatingPanels.shared.resetKeyReferencePosition() }
                    .buttonStyle(.neonSecondary)
            }
        }
    }
}
