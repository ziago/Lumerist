import AppKit
import SwiftUI
import LumeristCore

/// What a mask tool does when it is clicked in the Palette.
///
/// With a mask selected in Lightroom, the modifiers held at the click decide,
/// as with selections in Photoshop: none creates a new mask, ⇧ adds to the
/// selected mask, ⌥ subtracts from it, ⇧⌥ intersects with it. Without a
/// selected mask a click always creates a new mask.
enum PaletteMaskMode: CaseIterable, Sendable {
    case new, add, subtract, intersect

    init(modifiers: NSEvent.ModifierFlags, masking: Bool) {
        guard masking else {
            self = .new
            return
        }
        switch (modifiers.contains(.shift), modifiers.contains(.option)) {
        case (true, true): self = .intersect
        case (true, false): self = .add
        case (false, true): self = .subtract
        case (false, false): self = .new
        }
    }

    /// Reads the keyboard as it is now. The Palette never takes focus from
    /// Lightroom, so it gets no key events of its own to track modifiers.
    @MainActor
    static func current(masking: Bool) -> PaletteMaskMode {
        PaletteMaskMode(modifiers: NSEvent.modifierFlags, masking: masking)
    }

    var command: LightroomCommandName {
        switch self {
        case .new: .createMask
        case .add: .addToMask
        case .subtract: .subtractFromMask
        case .intersect: .intersectWithMask
        }
    }

    /// Lightroom's own word for it.
    var title: String {
        switch self {
        case .new: "New"
        case .add: "Add"
        case .subtract: "Subtract"
        case .intersect: "Intersect"
        }
    }

    /// The modifiers that choose it.
    var keys: String {
        switch self {
        case .new: ""
        case .add: "⇧"
        case .subtract: "⌥"
        case .intersect: "⇧⌥"
        }
    }

    /// Marks the mask tools while its modifiers are held.
    var badge: String? {
        switch self {
        case .new: nil
        case .add: "+"
        case .subtract: "−"
        case .intersect: "∩"
        }
    }

    /// The VoiceOver action that does the same without modifiers.
    var actionName: String {
        switch self {
        case .new: "New mask"
        case .add: "Add to the selected mask"
        case .subtract: "Subtract from the selected mask"
        case .intersect: "Intersect with the selected mask"
        }
    }
}

/// The Palette: Lightroom's mask tools under "Create New Mask", then the
/// user's favourite presets and tools. Picking an item runs it in Lightroom
/// and closes the Palette unless it is pinned.
struct PaletteView: View {
    /// A fixed state for snapshots (development renders): Lightroom's module,
    /// a selected mask, the modifiers held and the favourites.
    struct Preview {
        var module = "develop"
        var masking = false
        var held: PaletteMaskMode = .new
        var options: PaletteOptions?
    }

    let store: CommandStore
    var preview: Preview?
    /// What a click would do with the modifiers held right now; drives the
    /// highlight only (the click reads the modifiers again).
    @State private var heldMode: PaletteMaskMode

    init(store: CommandStore, preview: Preview? = nil) {
        self.store = store
        self.preview = preview
        _heldMode = State(initialValue: preview?.held ?? .new)
    }

    private static let menuTools = ["aiSelection/subject", "aiSelection/sky", "aiSelection/background", "aiSelection/people",
                                    "aiSelection/objects", "brush", "gradient", "radialGradient"]

    var body: some View {
        let options = preview?.options ?? store.keymap.keymap.palette
        let masking = preview?.masking ?? store.context.isMasking
        let module = preview?.module ?? store.context.module
        PanelChrome(
            title: "Palette",
            subtitle: masking ? "Mask selected" : module.isEmpty ? nil : module.capitalized,
            accent: Synth.magenta,
            trailing: AnyView(HStack(spacing: 6) {
                Button {
                    store.editKeymap { $0.palette.keepVisible.toggle() }
                } label: {
                    Image(systemName: options.keepVisible ? "pin.fill" : "pin")
                        .foregroundStyle(options.keepVisible ? Synth.magenta : Synth.steel)
                        .frame(width: 22, height: 22)
                        .contentShape(Rectangle())
                }
                .buttonStyle(.plain)
                .help(options.keepVisible ? "Pinned: the Palette stays open after you release its key"
                                          : "Pin to keep the Palette open after you release its key")
                .accessibilityLabel("Pin the Palette")
                .accessibilityValue(options.keepVisible ? "On" : "Off")
                Button { FloatingPanels.shared.closePalette() } label: {
                    Image(systemName: "xmark").foregroundStyle(Synth.steel).frame(width: 22, height: 22).contentShape(Rectangle())
                }
                .buttonStyle(.plain)
                .accessibilityLabel("Close the Palette")
            })
        ) {
            if Synth.isFlat {
                flatContent(options, masking: masking)
            } else {
                neonContent(options, masking: masking)
            }
        }
        .frame(width: 330)
        // Follows the modifiers while the Palette is up over a selected mask.
        .task(id: masking && store.isPaletteOpen) {
            guard preview == nil else { return }
            heldMode = .new
            guard masking && store.isPaletteOpen else { return }
            while !Task.isCancelled {
                let mode = PaletteMaskMode.current(masking: true)
                if mode != heldMode { heldMode = mode }
                try? await Task.sleep(for: .milliseconds(50))
            }
        }
    }

    // MARK: Miami

    private func neonContent(_ options: PaletteOptions, masking: Bool) -> some View {
        let rowHeight: CGFloat = 34
        return VStack(alignment: .leading, spacing: Synth.Space.m) {
            VStack(alignment: .leading, spacing: 6) {
                Text("Create New Mask").synthLabel(10)
                    .accessibilityAddTraits(.isHeader)
                if masking { modeHint }
            }
            LazyVGrid(columns: Array(repeating: GridItem(.flexible(), spacing: 6), count: 4), spacing: 6) {
                ForEach(Self.menuTools, id: \.self) { id in
                    toolButton(id, masking: masking)
                }
            }

            if !options.favourites.isEmpty {
                Text("Favourites").synthLabel(10)
                    .accessibilityAddTraits(.isHeader)
                VStack(spacing: 0) {
                    ForEach(options.favourites) { entry in
                        Button { run(action(for: entry)) } label: {
                            HStack(spacing: Synth.Space.s) {
                                Image(systemName: entry.kind == .preset ? "slider.horizontal.3" : "circle.dashed")
                                    .foregroundStyle(entry.kind == .preset ? Synth.magentaLight : Synth.teal)
                                Text(entry.title).synthBody(13, weight: .medium, color: Synth.ice).lineLimit(1)
                                Spacer()
                                if entry.isMaskTool, masking, let badge = heldMode.badge {
                                    Text(badge).font(SynthFont.font(.mono, size: 12, weight: .bold))
                                        .foregroundStyle(Synth.magenta)
                                        .accessibilityHidden(true)
                                }
                            }
                            .padding(.horizontal, 8)
                            .frame(height: rowHeight)
                            .contentShape(Rectangle())
                        }
                        .buttonStyle(.plain)
                        .overlay(alignment: .bottom) { Rectangle().fill(Synth.gridLine.opacity(0.6)).frame(height: 1) }
                        .accessibilityLabel(entry.kind == .preset ? "Apply preset \(entry.title)" : entry.title)
                        .accessibilityActions {
                            if entry.isMaskTool, masking { maskActions(argument: entry.argument) }
                        }
                    }
                }
            } else {
                Text("No favourites yet. Add presets and mask tools in Floating panels → Palette.")
                    .synthBody(11, color: Synth.steel)
                    .fixedSize(horizontal: false, vertical: true)
            }

            Text(footerText(options))
                .synthLabel(9)
        }
        .padding(12)
    }

    // MARK: Galerist

    /// Mask tool cells: 4 per row, 74 pt wide, 1 pt hairlines between them
    /// (whole points, so the hairlines stay sharp).
    private static let cellWidth: CGFloat = 74

    private func flatContent(_ options: PaletteOptions, masking: Bool) -> some View {
        VStack(alignment: .leading, spacing: Synth.Space.m) {
            VStack(alignment: .leading, spacing: Synth.Space.s) {
                Text("Create New Mask").panelCaps(10)
                    .accessibilityAddTraits(.isHeader)
                if masking { flatModeHint }
            }
            Grid(horizontalSpacing: 1, verticalSpacing: 1) {
                ForEach(Array(stride(from: 0, to: Self.menuTools.count, by: 4)), id: \.self) { start in
                    GridRow {
                        ForEach(Self.menuTools[start..<min(start + 4, Self.menuTools.count)], id: \.self) { id in
                            flatToolCell(id, masking: masking)
                        }
                    }
                }
            }
            .padding(1)
            .background(Synth.gridLine)

            if !options.favourites.isEmpty {
                Text("Favourites").panelCaps(10)
                    .accessibilityAddTraits(.isHeader)
                VStack(spacing: 0) {
                    ForEach(Array(options.favourites.enumerated()), id: \.offset) { index, entry in
                        Button { run(action(for: entry)) } label: {
                            HStack(spacing: 10) {
                                Image(systemName: entry.symbolName)
                                    .font(.system(size: 12, weight: .regular))
                                    .foregroundStyle(Synth.steel)
                                    .frame(width: 16)
                                Text(entry.title).synthBody(13, color: Synth.ice).lineLimit(1)
                                Spacer(minLength: 6)
                                if entry.isMaskTool, masking, let badge = heldMode.badge {
                                    Text(badge).panelCaps(11, color: Synth.ice, weight: .semibold, tracking: 0)
                                        .accessibilityHidden(true)
                                }
                            }
                            .padding(.horizontal, 12)
                            .frame(height: 34)
                            .contentShape(Rectangle())
                        }
                        .buttonStyle(PanelCellButtonStyle())
                        .overlay(alignment: .top) {
                            if index > 0 { Rectangle().fill(Synth.gridLine).frame(height: 1) }
                        }
                        .accessibilityLabel(entry.kind == .preset ? "Apply preset \(entry.title)" : entry.title)
                        .accessibilityActions {
                            if entry.isMaskTool, masking { maskActions(argument: entry.argument) }
                        }
                    }
                }
                .overlay(Rectangle().strokeBorder(Synth.gridLine, lineWidth: 1))
            } else {
                Text("No favourites yet. Add presets and mask tools in Floating panels → Palette.")
                    .synthBody(12, color: Synth.steel)
                    .fixedSize(horizontal: false, vertical: true)
            }

            Text(footerText(options))
                .panelCaps(9)
                .fixedSize(horizontal: false, vertical: true)
        }
        .padding(14)
    }

    /// "⇧ Add  ⌥ Subtract  ⇧⌥ Intersect": the held one in a light fill.
    private var flatModeHint: some View {
        HStack(spacing: 6) {
            ForEach([PaletteMaskMode.add, .subtract, .intersect], id: \.self) { mode in
                let lit = mode == heldMode
                Text("\(mode.keys) \(mode.title)")
                    // The panel's own background on the light fill: onMagenta is white
                    // here, as the fill nearly is (panels are dark in every theme).
                    .panelCaps(9, color: lit ? Synth.abyss : Synth.steel, weight: lit ? .semibold : .medium, tracking: 0.12)
                    .padding(.horizontal, 6)
                    .padding(.vertical, 3)
                    .background(lit ? Synth.ice : Color.clear)
            }
        }
        .accessibilityElement(children: .ignore)
        .accessibilityLabel("Hold Shift to add to the selected mask, Option to subtract, both to intersect")
    }

    private func flatToolCell(_ id: String, masking: Bool) -> some View {
        let tool = MaskTool.all.first { $0.id == id }
        let title = tool?.title ?? id
        let badge = masking ? heldMode.badge : nil
        return Button { runMask(argument: id) } label: {
            VStack(spacing: 7) {
                Image(systemName: tool?.systemImage ?? "circle.dashed")
                    .font(.system(size: 16, weight: .regular))
                    .frame(height: 18)
                Text(title)
                    .panelCaps(9, color: Synth.ice, tracking: 0.08)
                    .lineLimit(1)
                    .minimumScaleFactor(0.75)
                    .padding(.horizontal, 4)
            }
            .foregroundStyle(Synth.ice)
            .frame(width: Self.cellWidth, height: 62)
            .overlay(alignment: .topTrailing) {
                if let badge {
                    Text(badge)
                        .panelCaps(11, color: Synth.ice, weight: .semibold, tracking: 0)
                        .padding(.top, 3)
                        .padding(.trailing, 5)
                }
            }
            .contentShape(Rectangle())
        }
        .buttonStyle(PanelCellButtonStyle(fill: Synth.panel))
        .accessibilityLabel(masking ? "\(title) mask" : "New \(title) mask")
        .accessibilityHint(masking ? "Creates a new mask. Hold Shift to add to the selected mask, Option to subtract, both to intersect." : "")
        .accessibilityActions {
            if masking { maskActions(argument: id) }
        }
    }

    // MARK: Shared

    /// "⇧ Add · ⌥ Subtract · ⇧⌥ Intersect", the held one lit.
    private var modeHint: some View {
        HStack(spacing: 0) {
            ForEach(Array([PaletteMaskMode.add, .subtract, .intersect].enumerated()), id: \.offset) { index, mode in
                if index > 0 { Text(" · ").synthLabel(9) }
                Text("\(mode.keys) \(mode.title)")
                    .synthLabel(9, color: mode == heldMode ? Synth.magenta : Synth.steel)
            }
        }
        .accessibilityElement(children: .ignore)
        .accessibilityLabel("Hold Shift to add to the selected mask, Option to subtract, both to intersect")
    }

    private func toolButton(_ id: String, masking: Bool) -> some View {
        let tool = MaskTool.all.first { $0.id == id }
        let title = tool?.title ?? id
        let badge = masking ? heldMode.badge : nil
        return Button { runMask(argument: id) } label: {
            VStack(spacing: 4) {
                Image(systemName: tool?.systemImage ?? "circle.dashed").font(.system(size: 15, weight: .semibold))
                Text(title).font(SynthFont.font(.mono, size: 9, weight: .semibold)).textCase(.uppercase)
                    .lineLimit(1).minimumScaleFactor(0.8)
            }
            .foregroundStyle(Synth.teal)
            .frame(maxWidth: .infinity, minHeight: 54)
            .overlay(alignment: .topTrailing) {
                if let badge {
                    Text(badge)
                        .font(SynthFont.font(.mono, size: 11, weight: .bold))
                        .foregroundStyle(Synth.magenta)
                        .padding(.top, 3)
                        .padding(.trailing, 5)
                }
            }
            .overlay(RoundedRectangle(cornerRadius: Synth.Radius.control)
                .strokeBorder(badge == nil ? Synth.outline : Synth.magenta.opacity(0.7), lineWidth: 1))
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .accessibilityLabel(masking ? "\(title) mask" : "New \(title) mask")
        .accessibilityHint(masking ? "Creates a new mask. Hold Shift to add to the selected mask, Option to subtract, both to intersect." : "")
        .accessibilityActions {
            if masking { maskActions(argument: id) }
        }
    }

    /// Add, Subtract and Intersect as VoiceOver actions.
    @ViewBuilder
    private func maskActions(argument: String?) -> some View {
        ForEach([PaletteMaskMode.add, .subtract, .intersect], id: \.self) { mode in
            Button(mode.actionName) { run(.command(name: mode.command.rawValue, argument: argument)) }
        }
    }

    /// A mask tool, in the mode the modifiers give at the moment of the click.
    private func runMask(argument: String?) {
        let mode = PaletteMaskMode.current(masking: store.context.isMasking)
        run(.command(name: mode.command.rawValue, argument: argument))
    }

    private func action(for entry: PaletteEntry) -> CommandKey.Action {
        switch entry.kind {
        case .preset:
            return .preset(uuid: entry.id, name: entry.title)
        case .tool:
            // Mask tools follow the modifiers; other tools keep their command.
            if entry.isMaskTool {
                let mode = PaletteMaskMode.current(masking: store.context.isMasking)
                return .command(name: mode.command.rawValue, argument: entry.argument)
            }
            return .command(name: entry.id, argument: entry.argument)
        }
    }

    private func run(_ action: CommandKey.Action) {
        store.perform(action)
        if !store.keymap.keymap.palette.keepVisible {
            FloatingPanels.shared.closePalette()
        }
    }
}

private extension PaletteEntry {
    /// A mask tool (New / Add / Subtract / Intersect come from the click).
    var isMaskTool: Bool {
        kind == .tool && (LightroomCommandName(rawValue: id)?.takesMask ?? false)
    }

    /// Galerist rows: a mask tool shows its own symbol, a preset the sliders.
    var symbolName: String {
        switch kind {
        case .preset: "slider.horizontal.3"
        case .tool: MaskTool.all.first { $0.id == argument }?.systemImage ?? "circle.dashed"
        }
    }
}

extension PaletteView {
    /// The panel's last line names its own key, e.g. "Let go of < to close".
    func footerText(_ options: PaletteOptions) -> String {
        let keymap = store.keymap.keymap
        let key = keymap.systemKeys.paletteKey.map { keymap.displayText(for: $0) } ?? "its key"
        return options.keepVisible ? "Pinned · press \(key) or × to close"
                                   : "Hold \(key) for a look · tap it to keep it open"
    }
}
