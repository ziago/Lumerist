import SwiftUI
import LumeristCore

/// The three panels that float over Lightroom: Value HUD, Palette and Key
/// Reference. Each section view brings its own rows.
struct FloatingPanelsScreen: View {
    let store: CommandStore

    var body: some View {
        VStack(alignment: .leading, spacing: Synth.Space.xl) {
            ScreenHeader(title: "Floating panels", readout: "above Lightroom · never take focus")
            HUDScreen(store: store)
            KeysPaletteScreen(store: store)
            KeyReferenceScreen(store: store)
        }
    }
}
