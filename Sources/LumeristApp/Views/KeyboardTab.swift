import SwiftUI
import LumeristCore

/// A drawing of the keyboard for one module: every key coloured by what it
/// does here (Dial Key, Nudge − / +, Command Key, system key), with the
/// combos on a second layer. Below it: conflicts, keys that are not on the
/// drawing, and the Clear… menu.
struct KeyboardTab: View {
    let store: CommandStore
    let scope: ParameterScope
    /// Opens the slider or Command Key behind a clicked key in its own tab.
    let reveal: (KeyboardReveal) -> Void

    @State private var layer: Layer = .plain
    @State private var confirmClear: ClearTarget?

    enum Layer: Hashable { case plain, combos }
    enum ClearTarget: Hashable { case dial, nudge }

    var body: some View {
        let keymap = store.keymap.keymap
        let all = KeyAssignment.all(in: keymap, scope: scope)
        let conflicts = ConflictIndex(keymap, scope: scope)
        let shape = KeyboardLayout.physicalShape
        let spots = KeySpot.spots(for: shape)
        let drawn = Set(spots.compactMap(\.keyCode))
        let comboKey = keymap.systemKeys.comboKey
        VStack(alignment: .leading, spacing: Synth.Space.l) {
            HStack(spacing: Synth.Space.m) {
                SynthSegmented(items: [Layer.plain, .combos], selection: $layer) { layer in
                    switch layer {
                    case .plain: "Plain keys"
                    case .combos: comboKey.map { "Combos (hold \($0.displayText))" } ?? "Combos"
                    }
                }
                .themedTooltip(KeyKindHelp.combos(comboKey: comboKey))
                Spacer(minLength: Synth.Space.m)
                KeyboardLegend(scope: scope)
            }

            keyboardCard(
                KeyboardDrawing(
                    spots: spots,
                    assignments: Dictionary(grouping: all.filter { $0.chord.isCombo == (layer == .combos) }, by: \.chord.keyCode),
                    conflicts: conflicts,
                    heldKeyCode: layer == .combos ? comboKey?.keyCode : nil,
                    comboKey: comboKey,
                    reveal: reveal
                )
                .accessibilityElement(children: .contain)
                .accessibilityLabel(layer == .plain ? "Keyboard, plain keys" : "Keyboard, combos")
                // Key names follow the input source: draw again when it changes.
                .id(store.keyboardLayoutRevision)
            )

            if layer == .combos && comboKey == nil {
                Text("There is no Combo Key, so combos are off. Set one to use them.")
                    .synthBody(12, color: Synth.amber)
            }

            HStack(spacing: Synth.Space.m) {
                Text("Click a coloured key to open its slider or command.")
                    .synthBody(12, color: Synth.steel)
                Spacer(minLength: Synth.Space.m)
                clearMenu(keymap)
            }

            conflictList(keymap)
            offDrawingList(all.filter { !drawn.contains($0.chord.keyCode) }, keymap: keymap, conflicts: conflicts)
        }
        .confirmationDialog(confirmClear == .nudge ? "Clear every Nudge Key in \(moduleName)?" : "Clear every Dial Key in \(moduleName)?",
                            isPresented: Binding(get: { confirmClear != nil }, set: { if !$0 { confirmClear = nil } })) {
            Button("Clear", role: .destructive) {
                switch confirmClear {
                case .dial: store.editKeymap { $0.clearDialKeys(in: scope) }
                case .nudge: store.editKeymap { $0.clearNudgeKeys(in: scope) }
                case nil: break
                }
                confirmClear = nil
            }
        } message: {
            Text("Only those keys go: steps and Command Keys stay as they are.")
        }
    }

    private var moduleName: String { scope == .develop ? "Develop" : "Library" }

    /// Galerist: the keyboard sits in a white card (charcoal in dark) on the
    /// grey page. Miami draws it straight on the grid.
    @ViewBuilder
    private func keyboardCard(_ drawing: some View) -> some View {
        if Synth.isFlat {
            drawing
                .padding(Synth.Space.xl)
                .frame(maxWidth: .infinity)
                .background(Synth.panel)
                .overlay(Rectangle().strokeBorder(Synth.gridLine, lineWidth: 1))
        } else {
            drawing
        }
    }

    // MARK: Clear

    private func clearMenu(_ keymap: Keymap) -> some View {
        let slots = keymap.slots(for: scope).values
        var targets: [ClearTarget] = []
        if slots.contains(where: { $0.dial != nil }) { targets.append(.dial) }
        if slots.contains(where: { $0.nudgeDown != nil || $0.nudgeUp != nil }) { targets.append(.nudge) }
        return NeonMenu(
            title: "Clear…",
            accessibilityLabel: "Clear keys in \(moduleName)",
            items: targets,
            selection: nil,
            itemTitle: { $0 == .dial ? "Clear all Dial Keys" : "Clear all Nudge Keys" },
            onSelect: { confirmClear = $0 }
        )
        .fixedSize()
        .disabled(targets.isEmpty)
    }

    // MARK: Conflicts

    @ViewBuilder
    private func conflictList(_ keymap: Keymap) -> some View {
        let conflicts = keymap.conflicts().filter { $0.scope == scope || $0.scope == nil }
        if !conflicts.isEmpty {
            section("Conflicts") {
                ForEach(Array(conflicts.enumerated()), id: \.offset) { _, conflict in
                    HStack(alignment: .top, spacing: Synth.Space.s) {
                        Image(systemName: conflict.severity == .error ? "xmark.octagon.fill" : "exclamationmark.triangle.fill")
                            .foregroundStyle(conflict.severity == .error ? Synth.error : Synth.amber)
                            .accessibilityLabel(conflict.severity == .error ? "Error" : "Warning")
                        VStack(alignment: .leading, spacing: 2) {
                            Text(conflict.message).synthBody(13, weight: .medium, color: Synth.ice)
                            Text("Involves " + conflict.usages.map(\.label).joined(separator: ", "))
                                .synthBody(12, color: Synth.steel)
                        }
                        Spacer()
                    }
                    .listRow()
                }
            }
        }
    }

    // MARK: Keys not on the drawing

    @ViewBuilder
    private func offDrawingList(_ assignments: [KeyAssignment], keymap: Keymap, conflicts: ConflictIndex) -> some View {
        if !assignments.isEmpty {
            section("Other keys") {
                ForEach(assignments) { assignment in
                    HStack(spacing: Synth.Space.m) {
                        KeyCap(text: keymap.displayText(for: assignment.chord),
                               state: conflicts[assignment.usage] == nil ? .assigned : .conflict, minWidth: 54)
                        Text(assignment.name).synthBody(13, weight: .medium, color: Synth.ice)
                        Spacer()
                        Text(assignment.kind.title).synthLabel(9, color: Synth.isFlat ? Synth.steel : assignment.kind.textColor)
                    }
                    .listRow()
                    .themedTooltip(assignment.description(keymap: keymap))
                    .accessibilityElement(children: .combine)
                }
            }
        }
    }

    private func section<Content: View>(_ title: String, @ViewBuilder content: () -> Content) -> some View {
        VStack(alignment: .leading, spacing: Synth.Space.s) {
            Text(title).synthTitle(15).accessibilityAddTraits(.isHeader)
            NeonPanel {
                VStack(spacing: 0) { content() }
            }
        }
    }
}

/// Where a click on the keyboard leads.
enum KeyboardReveal {
    /// The Sliders (Quick Develop) tab, searching for this slider.
    case slider(String)
    /// The Commands tab, searching for this Command Key.
    case command(String)
    /// Where a system key is changed: Floating panels for the Palette and
    /// Key Reference keys, General for the others.
    case system(SystemKeys.Role)
}

// MARK: - What each key does

/// One key binding of the module, ready to draw.
struct KeyAssignment: Identifiable {
    enum Kind: Int, Comparable {
        case dial, nudgeDown, nudgeUp, command, system

        static func < (lhs: Kind, rhs: Kind) -> Bool { lhs.rawValue < rhs.rawValue }

        var title: String {
            switch self {
            case .dial: "Dial Key"
            case .nudgeDown: "Nudge −"
            case .nudgeUp: "Nudge +"
            case .command: "Command Key"
            case .system: "System key"
            }
        }

        /// Outline and fill.
        var color: Color {
            switch self {
            case .dial: Synth.teal
            case .nudgeDown, .nudgeUp: Synth.magenta
            case .command: Synth.success
            case .system: Synth.amber
            }
        }

        /// Text on the dark key (≥ 4.5:1 on panel colours).
        var textColor: Color {
            switch self {
            case .dial: Synth.tealLight
            case .nudgeDown, .nudgeUp: Synth.magentaLight
            case .command: Synth.success
            case .system: Synth.amber
            }
        }
    }

    let id: String
    let chord: KeyChord
    let kind: Kind
    let usage: KeyUsage
    /// Full name: the slider, the Command Key's title or the system key.
    let name: String
    /// A few characters for the key face.
    let shortName: String
    /// What the key does, in a few words.
    let detail: String
    let reveal: KeyboardReveal?

    /// Every binding of a module: system keys, slider keys and Command Keys.
    static func all(in keymap: Keymap, scope: ParameterScope) -> [KeyAssignment] {
        var result: [KeyAssignment] = []
        for role in SystemKeys.Role.allCases {
            guard let chord = keymap.systemKeys.chord(for: role) else { continue }
            if role == .paletteKey && scope != .develop { continue } // the Palette is a Develop tool
            let (short, detail): (String, String) = switch role {
            case .comboKey: ("Combo", "hold it, then press a key for a combo")
            case .paletteKey: ("Palette", "hold it for the Palette: new masks and presets")
            case .keyReferenceToggle: ("Key list", "shows or hides the Key Reference")
            case .pauseToggle: ("Pause", "switches Lumerist off and back on")
            }
            result.append(KeyAssignment(id: "system.\(role.rawValue)", chord: chord, kind: .system, usage: .system(role),
                                        name: role.title, shortName: short, detail: detail, reveal: .system(role)))
        }
        for section in ParameterCatalog.sections(for: scope) {
            for descriptor in section.parameters {
                guard let slot = keymap.slot(for: descriptor.key) else { continue }
                let key = descriptor.key
                let label = displayName(descriptor.label)
                let short = abbreviated(label)
                let entries: [(KeyChord?, Kind, KeyUsage, String, String)] = [
                    (slot.dial, .dial, .dial(key), short, "hold it, then scroll, drag or press ← →"),
                    (slot.nudgeDown, .nudgeDown, .nudgeDown(key), "− " + short, "one step down per press"),
                    (slot.nudgeUp, .nudgeUp, .nudgeUp(key), "+ " + short, "one step up per press"),
                ]
                for (chord, kind, usage, shortName, detail) in entries {
                    guard let chord else { continue }
                    result.append(KeyAssignment(id: "\(kind.rawValue).\(key)", chord: chord, kind: kind, usage: usage,
                                                name: label, shortName: shortName, detail: detail,
                                                reveal: .slider(descriptor.label)))
                }
            }
        }
        for action in keymap.commandKeys(in: scope) {
            result.append(KeyAssignment(id: "command.\(action.id)", chord: action.chord, kind: .command,
                                        usage: .action(action.id, title: action.title),
                                        name: action.title, shortName: abbreviated(action.title),
                                        detail: action.action.summary, reveal: .command(action.title)))
        }
        return result
    }

    /// "Q · Exposure, Dial Key: hold it, then scroll, drag or press ← →".
    func description(keymap: Keymap) -> String {
        "\(keymap.displayText(for: chord)) · \(name), \(kind.title): \(detail)"
    }

    /// Library labels drop their "Quick" prefix on the key face.
    static func displayName(_ label: String) -> String {
        guard label.hasPrefix("Quick "), label.count > 6 else { return label }
        let rest = label.dropFirst(6)
        return rest.prefix(1).uppercased() + rest.dropFirst()
    }

    /// Shortens long words so a name fits on two lines of a key.
    static func abbreviated(_ name: String) -> String {
        guard name.count > 12 else { return name }
        var text = name.replacingOccurrences(of: "White Balance", with: "WB")
        // In order: longer words before the words they contain.
        let words: [(String, String)] = [
            ("saturation", "sat."), ("luminance", "lum."), ("highlights", "highl."), ("highlight", "highl."),
            ("temperature", "temp."), ("sharpening", "sharp."), ("amount", "amt."), ("settings", "sett."),
            ("previous", "prev."), ("distortion", "dist."), ("vignetting", "vign."), ("vignette", "vign."),
            ("roundness", "round."), ("smoothness", "smooth."), ("noise reduction", "NR"),
        ]
        for (long, short) in words {
            text = text.replacingOccurrences(of: long, with: short, options: .caseInsensitive)
        }
        // Keep the first letter's case ("Sat." at the start, "sat." after).
        guard let first = text.first, let original = name.first, first.isLowercase != original.isLowercase else { return text }
        return String(original) + text.dropFirst()
    }
}

// MARK: - The drawing

/// One key position on the drawing, in key units (a letter key is 1 × 1).
struct KeySpot: Identifiable {
    let id: String
    /// nil: drawn for orientation only (modifiers, Caps Lock).
    let keyCode: Int64?
    /// Printed name for keys without a character ("tab", "return"…).
    let caption: String?
    let rect: CGRect
    /// The ISO Return key, two rows tall and narrower at the bottom.
    var isISOReturn = false

    static let width: CGFloat = 15
    static let height: CGFloat = 5

    static func spots(for shape: KeyboardShape) -> [KeySpot] {
        var spots: [KeySpot] = []
        func row(_ y: CGFloat, _ keys: [(Int64?, String?, CGFloat)]) {
            var x: CGFloat = 0
            for (code, caption, width) in keys {
                spots.append(KeySpot(id: "\(x)-\(y)", keyCode: code, caption: caption,
                                     rect: CGRect(x: x, y: y, width: width, height: 1)))
                x += width
            }
        }
        func letters(_ codes: [Int64]) -> [(Int64?, String?, CGFloat)] { codes.map { ($0, nil, 1) } }
        let digits: [Int64] = [18, 19, 20, 21, 23, 22, 26, 28, 25, 29, 27, 24]
        let top: [Int64] = [12, 13, 14, 15, 17, 16, 32, 34, 31, 35, 33, 30]
        let home: [Int64] = [0, 1, 2, 3, 5, 4, 38, 40, 37, 41, 39]
        let bottom: [Int64] = [6, 7, 8, 9, 11, 45, 46, 43, 47, 44]
        switch shape {
        case .ansi:
            row(0, [(50, nil, 1)] + letters(digits) + [(51, "delete", 2)])
            row(1, [(48, "tab", 1.5)] + letters(top) + [(42, nil, 1.5)])
            row(2, [(nil, "caps lock", 1.75)] + letters(home) + [(36, "return", 2.25)])
            row(3, [(nil, "shift", 2.25)] + letters(bottom) + [(nil, "shift", 2.75)])
        case .iso:
            row(0, [(10, nil, 1)] + letters(digits) + [(51, "delete", 2)])
            row(1, [(48, "tab", 1.5)] + letters(top))
            spots.append(KeySpot(id: "iso-return", keyCode: 36, caption: "return",
                                 rect: CGRect(x: 13.5, y: 1, width: 1.5, height: 2), isISOReturn: true))
            row(2, [(nil, "caps lock", 1.75)] + letters(home) + [(42, nil, 1)])
            row(3, [(nil, "shift", 1.25), (50, nil, 1)] + letters(bottom) + [(nil, "shift", 2.75)])
        case .jis:
            // No key left of 1 or left of Z; ¥ ends the number row, _ the
            // bottom row, and 英数 and かな flank the space bar.
            row(0, letters(digits) + [(93, nil, 1), (51, "delete", 2)])
            row(1, [(48, "tab", 1.5)] + letters(top))
            spots.append(KeySpot(id: "iso-return", keyCode: 36, caption: "return",
                                 rect: CGRect(x: 13.5, y: 1, width: 1.5, height: 2), isISOReturn: true))
            row(2, [(nil, "control", 1.75)] + letters(home) + [(42, nil, 1)])
            row(3, [(nil, "shift", 2.25)] + letters(bottom) + [(94, nil, 1), (nil, "shift", 1.75)])
        }
        if shape == .jis {
            row(4, [(nil, "caps", 1), (nil, "⌥", 1.25), (nil, "⌘", 1.5), (102, "英数", 1.25), (49, "space", 4),
                    (104, "かな", 1.25), (nil, "⌘", 1.25), (nil, "fn", 1.25)])
        } else {
            row(4, [(nil, "fn", 1), (nil, "⌃", 1.25), (nil, "⌥", 1.25), (nil, "⌘", 1.5), (49, "space", 5),
                    (nil, "⌘", 1.5), (nil, "⌥", 1.25)])
        }
        // Arrow keys: ← and → half height, ↑ over ↓.
        spots += [
            KeySpot(id: "left", keyCode: 123, caption: "←", rect: CGRect(x: 12.75, y: 4.5, width: 0.75, height: 0.5)),
            KeySpot(id: "up", keyCode: 126, caption: "↑", rect: CGRect(x: 13.5, y: 4, width: 0.75, height: 0.5)),
            KeySpot(id: "down", keyCode: 125, caption: "↓", rect: CGRect(x: 13.5, y: 4.5, width: 0.75, height: 0.5)),
            KeySpot(id: "right", keyCode: 124, caption: "→", rect: CGRect(x: 14.25, y: 4.5, width: 0.75, height: 0.5)),
        ]
        return spots
    }
}

private struct KeyboardDrawing: View {
    let spots: [KeySpot]
    let assignments: [Int64: [KeyAssignment]]
    let conflicts: ConflictIndex
    /// The Combo Key, drawn as held while the combo layer shows.
    let heldKeyCode: Int64?
    let comboKey: KeyChord?
    let reveal: (KeyboardReveal) -> Void

    var body: some View {
        GeometryReader { proxy in
            let unit = proxy.size.width / KeySpot.width
            ZStack(alignment: .topLeading) {
                ForEach(spots) { spot in
                    let frame = CGRect(x: spot.rect.minX * unit, y: spot.rect.minY * unit,
                                       width: spot.rect.width * unit, height: spot.rect.height * unit)
                        .insetBy(dx: 2, dy: 2)
                    KeyFace(
                        spot: spot,
                        unit: unit,
                        notch: spot.isISOReturn ? CGSize(width: 0.25 * unit, height: unit - 4) : nil,
                        assignments: spot.keyCode.map { sorted(assignments[$0] ?? []) } ?? [],
                        conflict: spot.keyCode.flatMap { code in
                            (assignments[code] ?? []).lazy.compactMap { conflicts[$0.usage] }.first
                        },
                        isHeld: spot.keyCode != nil && spot.keyCode == heldKeyCode,
                        comboKey: comboKey,
                        reveal: reveal
                    )
                    .frame(width: frame.width, height: frame.height)
                    .position(x: frame.midX, y: frame.midY)
                }
            }
        }
        .aspectRatio(KeySpot.width / KeySpot.height, contentMode: .fit)
        .frame(maxWidth: 960)
    }

    /// The key's own binding first, then those with modifiers.
    private func sorted(_ list: [KeyAssignment]) -> [KeyAssignment] {
        list.sorted {
            let lhsPlain = $0.chord.withoutCombo.modifiers.isEmpty
            let rhsPlain = $1.chord.withoutCombo.modifiers.isEmpty
            if lhsPlain != rhsPlain { return lhsPlain }
            return $0.kind < $1.kind
        }
    }
}

private struct KeyFace: View {
    let spot: KeySpot
    let unit: CGFloat
    let notch: CGSize?
    let assignments: [KeyAssignment]
    let conflict: String?
    let isHeld: Bool
    let comboKey: KeyChord?
    let reveal: (KeyboardReveal) -> Void

    @State private var isHovering = false

    private var primary: KeyAssignment? { assignments.first }
    private var isInert: Bool { spot.keyCode == nil }
    private var isCompact: Bool { spot.rect.height < 1 }

    /// The printed key: the active layout's character, or the caption.
    private var keyName: String {
        if let caption = spot.caption { return caption }
        guard let code = spot.keyCode else { return "" }
        return KeyNames.name(for: code)
    }

    private var color: Color {
        if isHeld { return Synth.amber }
        return primary?.kind.color ?? (isInert ? Synth.gridLine : Synth.outline)
    }

    var body: some View {
        let target = primary?.reveal
        Group {
            if let target {
                Button { reveal(target) } label: { face(clickable: true) }
                    .buttonStyle(.plain)
                    .onHover { isHovering = $0 }
                    .accessibilityHint(target.hint)
            } else {
                face(clickable: false).accessibilityElement(children: .ignore)
            }
        }
        .themedTooltip(tooltip)
        .accessibilityLabel(isInert ? keyName : "Key \(keyName)")
        .accessibilityValue(accessibilityValue)
        .accessibilityHidden(isInert)
    }

    @ViewBuilder
    private func face(clickable: Bool) -> some View {
        if Synth.isFlat {
            let style = flatStyle(hovering: isHovering && clickable)
            let outline = KeyOutline(notch: notch, inset: style.borderWidth / 2)
            flatContent(style)
                .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
                .background(outline.fill(style.fill))
                .overlay(outline.stroke(style.border, lineWidth: style.borderWidth))
                .contentShape(outline)
        } else {
            let outline = KeyOutline(notch: notch)
            content
                .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
                .background(outline.fill(fill))
                .overlay(outline.stroke(color.opacity(isInert ? 0.6 : 1), lineWidth: 1))
                .shadow(color: primary == nil && !isHeld ? .clear : color.opacity(isHovering && clickable ? 0.6 : 0.3),
                        radius: isHovering && clickable ? 8 : 5)
                .contentShape(outline)
        }
    }

    // MARK: Galerist

    /// Galerist key colours: Dial Key in ink (light) or white (dark) with the
    /// opposite text; Command Key light grey; System key with a 1 pt ink
    /// border; Nudge Keys with a 2 pt ink border; plain keys white with a
    /// hairline; modifiers on the page grey.
    private struct FlatKeyStyle {
        var fill: Color
        var border: Color
        var borderWidth: CGFloat = 1
        var glyph: Color
        var name: Color
    }

    private func flatStyle(hovering: Bool) -> FlatKeyStyle {
        if isHeld {
            return FlatKeyStyle(fill: Synth.magenta, border: Synth.magenta, glyph: Synth.onMagenta, name: Synth.onMagenta)
        }
        if isInert {
            return FlatKeyStyle(fill: Synth.abyss, border: Synth.gridLine, glyph: Synth.steel, name: Synth.steel)
        }
        switch primary?.kind {
        case .dial:
            return FlatKeyStyle(fill: Synth.teal.opacity(hovering ? 0.85 : 1), border: Synth.teal,
                                glyph: Synth.onTeal, name: Synth.onTeal)
        case .command:
            return FlatKeyStyle(fill: Synth.tint, border: hovering ? Synth.magenta : Synth.magenta.opacity(0.45),
                                glyph: Synth.ice, name: Synth.ice)
        case .system:
            return FlatKeyStyle(fill: hovering ? Synth.gridLine : Synth.panel, border: Synth.ice,
                                glyph: Synth.ice, name: Synth.ice)
        case .nudgeDown, .nudgeUp:
            return FlatKeyStyle(fill: hovering ? Synth.gridLine : Synth.panel, border: Synth.teal, borderWidth: 2,
                                glyph: Synth.teal, name: Synth.ice)
        case nil:
            return FlatKeyStyle(fill: Synth.panel, border: Synth.outline, glyph: Synth.steel, name: Synth.steel)
        }
    }

    @ViewBuilder
    private func flatContent(_ style: FlatKeyStyle) -> some View {
        if isInert || isCompact {
            Text(keyName)
                .font(SynthFont.font(.mono, size: isCompact ? 10 : 9, weight: .medium))
                .foregroundStyle(style.glyph)
                .lineLimit(1)
                .minimumScaleFactor(0.6)
                .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: isCompact ? .center : .bottomLeading)
                .padding(isCompact ? 0 : 7)
        } else {
            VStack(alignment: .leading, spacing: 1) {
                HStack(alignment: .top, spacing: 2) {
                    Text(keyName)
                        .font(SynthFont.font(.mono, size: spot.caption == nil ? min(14, max(10, unit * 0.2)) : 9,
                                             weight: .medium))
                        .foregroundStyle(style.glyph)
                        .lineLimit(1)
                        .minimumScaleFactor(0.6)
                    Spacer(minLength: 0)
                    if conflict != nil {
                        // The one red: a key that clashes with another.
                        Circle().fill(Synth.live)
                            .frame(width: 7, height: 7)
                            .padding(.top, 2)
                    } else if assignments.count > 1 {
                        Text("+\(assignments.count - 1)")
                            .font(SynthFont.font(.mono, size: 9, weight: .medium))
                            .foregroundStyle(style.name.opacity(0.7))
                    }
                }
                Spacer(minLength: 0)
                if isHeld {
                    Text("held").font(SynthFont.font(.text, size: 10, weight: .medium)).foregroundStyle(style.name)
                } else if let primary {
                    Text(faceText(primary))
                        .font(SynthFont.font(.text, size: min(11, max(8.5, unit * 0.155)), weight: .medium))
                        .foregroundStyle(style.name)
                        .lineLimit(2)
                        .minimumScaleFactor(0.7)
                        .fixedSize(horizontal: false, vertical: true)
                }
            }
            .padding(.horizontal, 6)
            .padding(.vertical, 5)
        }
    }

    // MARK: Miami

    private var fill: Color {
        if isHeld { return Synth.amber.opacity(0.22) }
        if let primary { return primary.kind.color.opacity(isHovering ? 0.2 : 0.11) }
        return isInert ? Color.clear : Synth.abyss.opacity(0.55)
    }

    @ViewBuilder
    private var content: some View {
        if isInert || isCompact {
            Text(keyName)
                .font(SynthFont.font(.mono, size: isCompact ? 10 : 9, weight: .semibold))
                .foregroundStyle(primary?.kind.textColor ?? Synth.steel)
                .lineLimit(1)
                .minimumScaleFactor(0.6)
                .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: isCompact ? .center : .bottomLeading)
                .padding(isCompact ? 0 : 6)
        } else {
            VStack(alignment: .leading, spacing: 1) {
                HStack(alignment: .top, spacing: 2) {
                    Text(keyName)
                        .font(SynthFont.font(.mono, size: spot.caption == nil ? min(14, max(10, unit * 0.21)) : 9,
                                             weight: spot.caption == nil ? .bold : .semibold))
                        .foregroundStyle(primary == nil && !isHeld ? Synth.steel : Synth.ice)
                        .lineLimit(1)
                        .minimumScaleFactor(0.6)
                    Spacer(minLength: 0)
                    if conflict != nil {
                        Image(systemName: "exclamationmark.triangle.fill")
                            .font(.system(size: 9, weight: .bold))
                            .foregroundStyle(Synth.error)
                    } else if assignments.count > 1 {
                        Text("+\(assignments.count - 1)")
                            .font(SynthFont.font(.mono, size: 9, weight: .bold))
                            .foregroundStyle(Synth.steel)
                    }
                }
                Spacer(minLength: 0)
                if isHeld {
                    Text("held").font(SynthFont.font(.text, size: 10, weight: .semibold)).foregroundStyle(Synth.amber)
                } else if let primary {
                    Text(faceText(primary))
                        .font(SynthFont.font(.text, size: min(11, max(8.5, unit * 0.155)), weight: .semibold))
                        .foregroundStyle(primary.kind.textColor)
                        .lineLimit(2)
                        .minimumScaleFactor(0.7)
                        .fixedSize(horizontal: false, vertical: true)
                }
            }
            .padding(.horizontal, 5)
            .padding(.vertical, 4)
        }
    }

    /// Modifiers held with the key come first: "⌥ Auto Tone".
    private func faceText(_ assignment: KeyAssignment) -> String {
        let symbols = KeyNames.modifierSymbols(assignment.chord.withoutCombo.modifiers)
        return symbols.isEmpty ? assignment.shortName : "\(symbols) \(assignment.shortName)"
    }

    private var tooltip: String {
        if isHeld { return "\(keyName): the Combo Key. Hold it, then press one of the coloured keys." }
        if assignments.isEmpty { return isInert ? "" : "\(keyName): nothing here" }
        var lines = assignments.map { assignment in
            let chord = assignment.chord.displayText(comboKey: comboKey)
            return "\(chord) · \(assignment.name), \(assignment.kind.title): \(assignment.detail)"
        }
        if let conflict { lines.append("⚠ " + conflict) }
        return lines.joined(separator: "\n")
    }

    private var accessibilityValue: String {
        if isHeld { return "Combo Key, held" }
        if assignments.isEmpty { return "Not assigned" }
        var parts = assignments.map { assignment in
            let chord = assignment.chord.displayText(comboKey: comboKey)
            return "\(chord), \(assignment.kind.title) of \(assignment.name): \(assignment.detail)"
        }
        if let conflict { parts.append("Conflict: " + conflict) }
        return parts.joined(separator: "; ")
    }
}

private extension KeyboardReveal {
    var hint: String {
        switch self {
        case .slider: "Shows this slider in the Sliders tab"
        case .command: "Shows this Command Key in the Commands tab"
        case .system(let role): role == .paletteKey || role == .keyReferenceToggle
            ? "Opens Floating panels, where you can change this key"
            : "Opens General, where you can change this key"
        }
    }
}

/// A key's outline: a rounded rectangle, or the ISO Return's shape (the
/// lower part is narrower on the left by `notch.width`, starting
/// `notch.height` from the top).
private struct KeyOutline: Shape {
    var notch: CGSize?
    /// Half the border width, so the stroke stays inside the key.
    var inset: CGFloat = 0.5

    func path(in rect: CGRect) -> Path {
        let r = rect.insetBy(dx: inset, dy: inset)
        guard let notch else { return Path(roundedRect: r, cornerRadius: Synth.Radius.control) }
        let points = [
            CGPoint(x: r.minX, y: r.minY),
            CGPoint(x: r.maxX, y: r.minY),
            CGPoint(x: r.maxX, y: r.maxY),
            CGPoint(x: r.minX + notch.width, y: r.maxY),
            CGPoint(x: r.minX + notch.width, y: r.minY + notch.height),
            CGPoint(x: r.minX, y: r.minY + notch.height),
        ]
        var path = Path()
        let last = points[points.count - 1]
        path.move(to: CGPoint(x: (last.x + points[0].x) / 2, y: (last.y + points[0].y) / 2))
        for index in points.indices {
            path.addArc(tangent1End: points[index], tangent2End: points[(index + 1) % points.count],
                        radius: Synth.Radius.control)
        }
        path.closeSubpath()
        return path
    }
}

/// Colour key for the drawing; each name explains itself on hover.
private struct KeyboardLegend: View {
    let scope: ParameterScope

    var body: some View {
        if Synth.isFlat { flat } else { neon }
    }

    /// Galerist: the same fills and borders as the keys.
    private var flat: some View {
        HStack(spacing: Synth.Space.m) {
            item("Dial Key", KeyKindHelp.dialKey(scope)) { Rectangle().fill(Synth.teal).frame(width: 12, height: 12) }
            item("Nudge − / +", KeyKindHelp.nudgeKeys(scope)) {
                Rectangle().strokeBorder(Synth.teal, lineWidth: 2).frame(width: 12, height: 12)
            }
            item("Command Key", KeyKindHelp.commandKey) {
                Rectangle().fill(Synth.tint)
                    .overlay(Rectangle().strokeBorder(Synth.magenta.opacity(0.45), lineWidth: 1))
                    .frame(width: 12, height: 12)
            }
            item("System key", KeyKindHelp.systemKey) {
                Rectangle().fill(Synth.panel)
                    .overlay(Rectangle().strokeBorder(Synth.ice, lineWidth: 1))
                    .frame(width: 12, height: 12)
            }
            item("Conflict", KeyKindHelp.conflict) {
                Circle().fill(Synth.live).frame(width: 7, height: 7).frame(width: 12, height: 12)
            }
        }
        .accessibilityElement(children: .combine)
    }

    private func item<Swatch: View>(_ title: String, _ help: String, @ViewBuilder swatch: () -> Swatch) -> some View {
        HStack(spacing: 6) {
            swatch()
            Text(title).synthLabel(9)
        }
        .themedTooltip(help)
    }

    private var neon: some View {
        HStack(spacing: Synth.Space.m) {
            swatch(KeyAssignment.Kind.dial.color, "Dial Key", KeyKindHelp.dialKey(scope))
            swatch(KeyAssignment.Kind.nudgeUp.color, "Nudge − / +", KeyKindHelp.nudgeKeys(scope))
            swatch(KeyAssignment.Kind.command.color, "Command Key", KeyKindHelp.commandKey)
            swatch(KeyAssignment.Kind.system.color, "System key", KeyKindHelp.systemKey)
            HStack(spacing: 4) {
                Image(systemName: "exclamationmark.triangle.fill")
                    .font(.system(size: 9, weight: .bold))
                    .foregroundStyle(Synth.error)
                Text("Conflict").synthLabel(9)
            }
            .themedTooltip(KeyKindHelp.conflict)
        }
        .accessibilityElement(children: .combine)
    }

    private func swatch(_ color: Color, _ title: String, _ help: String) -> some View {
        HStack(spacing: 4) {
            RoundedRectangle(cornerRadius: 2)
                .fill(color.opacity(0.18))
                .overlay(RoundedRectangle(cornerRadius: 2).strokeBorder(color, lineWidth: 1))
                .frame(width: 12, height: 12)
            Text(title).synthLabel(9)
        }
        .themedTooltip(help)
    }
}

private extension View {
    func listRow() -> some View {
        padding(.horizontal, 14)
            .padding(.vertical, 6)
            .frame(maxWidth: .infinity, minHeight: 38, alignment: .leading)
            .overlay(alignment: .bottom) { Rectangle().fill(Synth.gridLine.opacity(0.6)).frame(height: 1) }
    }
}
