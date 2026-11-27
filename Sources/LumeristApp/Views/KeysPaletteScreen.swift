import SwiftUI
import LumeristCore

/// Floating panels → Palette: its key (also in General → System Keys),
/// the favourites it lists and where it opens.
struct KeysPaletteScreen: View {
    let store: CommandStore

    var body: some View {
        let keymap = store.keymap.keymap
        let menu = keymap.palette
        VStack(alignment: .leading, spacing: Synth.Space.xl) {
            demo(keymap)

            SettingsSection(title: "Palette", systemImage: "paintpalette", footer: keyNote(keymap)) {
                SettingRow(title: "Palette Key",
                           detail: "Hold it in Develop to open the Palette under the pointer; let go to close it. Click the box and press another key to change it.") {
                    SystemKeySlot(store: store, role: .paletteKey)
                }
                SettingRow(title: "Position",
                           detail: "If you dragged the Palette somewhere, it opens there. Reset sends it back under the pointer.") {
                    Button("Reset position") { FloatingPanels.shared.resetPalettePosition() }
                        .buttonStyle(.neonSecondary)
                        .accessibilityHint("The Palette opens under the pointer again")
                }
            }

            VStack(alignment: .leading, spacing: Synth.Space.s) {
                HStack {
                    Text("Palette favourites").synthTitle(15).accessibilityAddTraits(.isHeader)
                    Spacer()
                    Button("Refresh presets") { store.refreshPresets() }
                        .buttonStyle(.neonQuiet)
                        .accessibilityHint("Reloads the Develop presets from Lightroom")
                }
                NeonPanel {
                    VStack(spacing: 0) {
                        if menu.favourites.isEmpty {
                            Text("Nothing here yet. The main mask tools are always in the Palette under Create New Mask; add the presets and other mask tools you use most.")
                                .synthBody(12, color: Synth.steel)
                                .fixedSize(horizontal: false, vertical: true)
                                .frame(maxWidth: .infinity, alignment: .leading)
                                .padding(14)
                        }
                        ForEach(Array(menu.favourites.enumerated()), id: \.element.id) { index, entry in
                            favouriteRow(entry, index: index, count: menu.favourites.count)
                        }
                        HStack(spacing: Synth.Space.s) {
                            presetAdder
                            NeonMenu(
                                title: "Add mask tool…",
                                accessibilityLabel: "Add a mask tool to the Palette favourites",
                                items: MaskTool.all,
                                selection: nil,
                                itemTitle: { $0.title },
                                onSelect: { tool in
                                    store.editKeymap {
                                        $0.palette.favourites.append(PaletteEntry(kind: .tool, id: LightroomCommandName.createMask.rawValue,
                                                                                      argument: tool.id, title: tool.title))
                                    }
                                }
                            )
                            .frame(width: 200)
                            Spacer()
                        }
                        .padding(12)
                    }
                }
                Text("With a mask selected in Lightroom, hold ⇧ to add a mask tool to it, ⌥ to subtract, or both to intersect.")
                    .synthBody(12, color: Synth.steel)
                    .fixedSize(horizontal: false, vertical: true)
            }
        }
    }

    /// What the Palette looks like over Lightroom: the real panel with your
    /// favourites, shown here for reference only (clicks do nothing).
    @ViewBuilder
    private func demo(_ keymap: Keymap) -> some View {
        let key = keymap.systemKeys.paletteKey.map { keymap.displayText(for: $0) }
        if Synth.isFlat {
            flatDemo(key: key)
        } else {
            neonDemo(key: key)
        }
    }

    /// Galerist: the words on white, the Palette in its own grey well on the
    /// right, dark as it is over Lightroom.
    private func flatDemo(key: String?) -> some View {
        HStack(alignment: .top, spacing: 0) {
            VStack(alignment: .leading, spacing: Synth.Space.m) {
                Text("This is the Palette").synthTitle(20).accessibilityAddTraits(.isHeader)
                Text(key.map { "In Lightroom's Develop module, hold \($0) and this panel appears under the pointer." }
                     ?? "Set a Palette Key below and this panel appears under the pointer in Develop.")
                    .synthBody(15, color: Synth.ice)
                Text("Click a tool under Create New Mask to start a mask, or one of your favourite presets to apply it. With a mask already selected, hold ⇧ to add to it, ⌥ to subtract or both to intersect.")
                    .synthBody(13, color: Synth.steel)
                Text("Hold the key for a quick look: it closes when you let go. Tap it instead and the Palette stays open until you press the key again. The key never reaches Lightroom, so it does nothing else. Your favourites below show up in it straight away.")
                    .synthBody(13, color: Synth.steel)
            }
            .fixedSize(horizontal: false, vertical: true)
            .padding(Synth.Space.xxl)
            .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)

            Rectangle().fill(Synth.gridLine).frame(width: 1)
                .frame(maxHeight: .infinity)

            PaletteView(store: store)
                .environment(\.colorScheme, .dark)
                .allowsHitTesting(false)
                .accessibilityHidden(true)
                .padding(Synth.Space.xl)
                .frame(maxHeight: .infinity, alignment: .top)
                .background(Synth.abyss)
        }
        .fixedSize(horizontal: false, vertical: true)
        .background(Synth.panel)
        .overlay(Rectangle().strokeBorder(Synth.gridLine, lineWidth: 1))
    }

    private func neonDemo(key: String?) -> some View {
        NeonPanel {
            HStack(alignment: .top, spacing: Synth.Space.xl) {
                VStack(alignment: .leading, spacing: Synth.Space.s) {
                    Text("This is the Palette").synthTitle(15).accessibilityAddTraits(.isHeader)
                    Text(key.map { "In Lightroom's Develop module, hold \($0) and this panel appears under the pointer." }
                         ?? "Set a Palette Key below and this panel appears under the pointer in Develop.")
                        .synthBody(13, color: Synth.ice)
                    Text("Click a tool under Create New Mask to start a mask, or one of your favourite presets to apply it. With a mask already selected, hold ⇧ to add to it, ⌥ to subtract or both to intersect.")
                        .synthBody(12, color: Synth.steel)
                    Text("Hold the key for a quick look: it closes when you let go. Tap it instead and the Palette stays open until you press the key again. The key never reaches Lightroom, so it does nothing else. Your favourites below show up in it straight away.")
                        .synthBody(12, color: Synth.steel)
                }
                .fixedSize(horizontal: false, vertical: true)
                .frame(maxWidth: .infinity, alignment: .leading)

                PaletteView(store: store)
                    .allowsHitTesting(false)
                    .accessibilityHidden(true)
            }
            .padding(Synth.Space.l)
        }
    }

    /// One line on the Palette Key: what opens the Palette, and that clearing
    /// the key switches it off.
    private func keyNote(_ keymap: Keymap) -> String {
        guard let key = keymap.systemKeys.paletteKey else {
            let off = "The Palette is off: it has no key. Set one above."
            return SystemKeys.Role.noPaletteKeyReason ?? off
        }
        return "In Develop, hold \(keymap.displayText(for: key)) and the Palette opens under the pointer. Clear the key to switch the Palette off."
    }

    private var presetAdder: some View {
        let groups = Dictionary(grouping: store.presets, by: \.group)
        return NeonMenu(
            title: store.presets.isEmpty ? "Add preset… (refresh first)" : "Add preset…",
            accessibilityLabel: "Add a Develop preset to the Palette favourites",
            groups: groups.keys.sorted().map { NeonMenu<PresetItem>.Group(title: $0, items: groups[$0] ?? []) },
            selection: nil,
            itemTitle: { $0.name },
            onSelect: { preset in
                store.editKeymap { $0.palette.favourites.append(PaletteEntry(kind: .preset, id: preset.id, title: preset.name)) }
            }
        )
        .frame(width: 240)
        .onAppear { if store.presets.isEmpty { store.refreshPresets() } }
    }

    private func favouriteRow(_ entry: PaletteEntry, index: Int, count: Int) -> some View {
        HStack(spacing: Synth.Space.m) {
            Image(systemName: entry.kind == .preset ? "slider.horizontal.3" : "circle.dashed")
                .foregroundStyle(entry.kind == .preset ? Synth.magentaLight : Synth.teal)
                .accessibilityHidden(true)
            Text(entry.title).synthBody(13, weight: .medium, color: Synth.ice)
            Text(entry.kind == .preset ? "Preset" : "Mask tool").synthLabel(9)
            Spacer()
            Button { move(index, by: -1) } label: { Image(systemName: "arrow.up") }
                .buttonStyle(.neonQuiet).disabled(index == 0).accessibilityLabel("Move \(entry.title) up")
            Button { move(index, by: 1) } label: { Image(systemName: "arrow.down") }
                .buttonStyle(.neonQuiet).disabled(index == count - 1).accessibilityLabel("Move \(entry.title) down")
            Button { store.editKeymap { $0.palette.favourites.remove(at: index) } } label: { Image(systemName: "xmark") }
                .buttonStyle(.neonRemove).accessibilityLabel("Remove \(entry.title)")
        }
        .padding(.horizontal, 14)
        .frame(minHeight: 38)
        .overlay(alignment: .bottom) { Rectangle().fill(Synth.gridLine.opacity(0.6)).frame(height: 1) }
    }

    private func move(_ index: Int, by offset: Int) {
        store.editKeymap { keymap in
            let target = index + offset
            guard keymap.palette.favourites.indices.contains(target) else { return }
            keymap.palette.favourites.swapAt(index, target)
        }
    }
}
