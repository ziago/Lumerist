import SwiftUI
import LumeristCore

/// Every catalog parameter of a scope, grouped by panel: its Dial Key
/// (hold), its Nudge Keys (tap − / +) and the steps they move by. A Nudge
/// Key can be a combo on its own.
struct SlidersTab: View {
    let store: CommandStore
    let scope: ParameterScope
    let search: String
    @State private var expanded: Set<String> = ["Basic", "Quick Develop"]

    var body: some View {
        let keymap = store.keymap.keymap
        let conflicts = ConflictIndex(keymap, scope: scope)
        let sections = filteredSections(keymap)
        let swapped = keymap.input.swapNormalAndCoarse
        VStack(alignment: .leading, spacing: Synth.Space.s) {
            LargeStepToggle(store: store, isOn: swapped)
            HStack(alignment: .firstTextBaseline) {
                Text(legend(comboKey: keymap.systemKeys.comboKey))
                    .synthLabel(10)
                    .fixedSize(horizontal: false, vertical: true)
                Spacer(minLength: Synth.Space.m)
                ExpandCollapseButtons(
                    expandAll: { expanded = Set(ParameterCatalog.sections(for: scope).map(\.section)) },
                    collapseAll: { expanded = [] }
                )
            }
            if sections.isEmpty {
                Text("No slider or key matches “\(search)”.").synthBody(13, color: Synth.steel).padding(.vertical, 20)
            }
            if Synth.isFlat {
                // Galerist: open groups stand apart; closed ones form one
                // hairline list (rows share their lines).
                VStack(alignment: .leading, spacing: 0) {
                    ForEach(sections, id: \.section) { section in
                        let isOpen = !search.isEmpty || expanded.contains(section.section)
                        if isOpen {
                            VStack(alignment: .leading, spacing: 10) {
                                sectionHeader(section, isOpen: true, keymap: keymap)
                                table(section, keymap: keymap, conflicts: conflicts, swapped: swapped)
                            }
                            .padding(.vertical, 10)
                        } else {
                            sectionHeader(section, isOpen: false, keymap: keymap)
                                .padding(.top, -1)
                        }
                    }
                }
            } else {
                ForEach(sections, id: \.section) { section in
                    let isOpen = !search.isEmpty || expanded.contains(section.section)
                    sectionHeader(section, isOpen: isOpen, keymap: keymap)
                    if isOpen {
                        table(section, keymap: keymap, conflicts: conflicts, swapped: swapped)
                    }
                }
            }
            if let error = KeymapEditor.shared.lastError {
                Label(error, systemImage: "exclamationmark.triangle.fill").synthBody(12, color: Synth.error)
            }
        }
    }

    private func sectionHeader(_ section: (section: String, parameters: [ParameterDescriptor]), isOpen: Bool,
                               keymap: Keymap) -> some View {
        let assigned = section.parameters.filter { keymap.slot(for: $0.key).map { $0.dial != nil || $0.nudgeDown != nil || $0.nudgeUp != nil } ?? false }.count
        return CollapsibleHeader(title: section.section, count: assigned, isExpanded: isOpen) {
            if expanded.contains(section.section) { expanded.remove(section.section) } else { expanded.insert(section.section) }
        }
    }

    private func table(_ section: (section: String, parameters: [ParameterDescriptor]), keymap: Keymap,
                       conflicts: ConflictIndex, swapped: Bool) -> some View {
        NeonPanel {
            VStack(spacing: 0) {
                header
                ForEach(section.parameters) { descriptor in
                    ParameterRow(store: store, descriptor: descriptor, slot: keymap.slot(for: descriptor.key),
                                 conflicts: conflicts, swapped: swapped,
                                 isUnsupported: store.unsupportedParameters.contains(descriptor.id))
                }
            }
        }
    }

    private var header: some View {
        HStack(spacing: Self.columnSpacing) {
            Text(scope == .develop ? "Adjustment" : "Quick Develop").frame(maxWidth: .infinity, alignment: .leading)
            Text("Dial Key").frame(width: Self.dialColumnWidth, alignment: .leading)
            Text("Nudge −").frame(width: Self.nudgeColumnWidth, alignment: .leading)
            Text("Nudge +").frame(width: Self.nudgeColumnWidth, alignment: .leading)
            Text("Step").frame(width: Self.stepColumnWidth, alignment: .leading)
        }
        .synthLabel(10)
        .padding(.horizontal, Synth.rowInset)
        .padding(.vertical, Synth.isFlat ? 11 : 8)
        // Galerist: a soft band behind the column names.
        .background(Synth.isFlat ? Synth.panelRaised : Color.clear)
        .overlay(alignment: .bottom) { Rectangle().fill(Synth.gridLine).frame(height: 1) }
        .accessibilityHidden(true)
    }

    static let columnSpacing: CGFloat = 10
    static let dialColumnWidth: CGFloat = 86
    /// Wide enough for a combo such as "Tab + ⇧]".
    static let nudgeColumnWidth: CGFloat = 96
    /// Wide enough for "0.05 · ⇧0.33".
    static let stepColumnWidth: CGFloat = 112

    /// "Click a box, then press a key · hold Tab as well for a combo".
    private func legend(comboKey: KeyChord?) -> String {
        let combo = comboKey.map { "hold \($0.displayText) as well for a combo" } ?? "combos need a Combo Key"
        return "Click a box, then press a key · \(combo)"
    }

    private func filteredSections(_ keymap: Keymap) -> [(section: String, parameters: [ParameterDescriptor])] {
        let all = ParameterCatalog.sections(for: scope)
        let query = search.trimmingCharacters(in: .whitespaces)
        guard !query.isEmpty else { return all }
        return all.compactMap { section in
            let matches = section.parameters.filter { descriptor in
                if descriptor.label.localizedCaseInsensitiveContains(query) || section.section.localizedCaseInsensitiveContains(query) {
                    return true
                }
                let slot = keymap.slot(for: descriptor.key)
                return [slot?.dial, slot?.nudgeDown, slot?.nudgeUp].compactMap { $0 }
                    .contains { keymap.displayText(for: $0).localizedCaseInsensitiveContains(query) }
            }
            return matches.isEmpty ? nil : (section.section, matches)
        }
    }
}

/// "Large step by default": the keys use the large step and ⇧ the small one.
private struct LargeStepToggle: View {
    let store: CommandStore
    let isOn: Bool

    var body: some View {
        NeonPanel {
            HStack(spacing: Synth.Space.l) {
                VStack(alignment: .leading, spacing: Synth.isFlat ? 3 : 2) {
                    Text("Large step by default").synthBody(Synth.isFlat ? 14 : 13, weight: .semibold, color: Synth.ice)
                    Text("When on, Dial and Nudge Keys move by the large step and ⇧ gives the small one.")
                        .synthBody(Synth.isFlat ? 12.5 : 12, color: Synth.steel)
                        .fixedSize(horizontal: false, vertical: true)
                }
                Spacer(minLength: Synth.Space.m)
                Toggle("Large step by default", isOn: Binding(
                    get: { isOn },
                    set: { value in store.editKeymap { $0.input.swapNormalAndCoarse = value } }
                ))
                .toggleStyle(.neon)
            }
            .padding(.horizontal, Synth.rowInset)
            .padding(.vertical, Synth.isFlat ? 14 : 10)
        }
    }
}

private struct ParameterRow: View {
    let store: CommandStore
    let descriptor: ParameterDescriptor
    let slot: ParameterSlot?
    let conflicts: ConflictIndex
    let swapped: Bool
    let isUnsupported: Bool

    var body: some View {
        let key = descriptor.key
        HStack(spacing: SlidersTab.columnSpacing) {
            HStack(spacing: 6) {
                Text(descriptor.label).synthBody(13, weight: .medium, color: Synth.ice).lineLimit(1)
                Text(unitLabel).synthLabel(9)
                if isUnsupported {
                    Label("not in this Lightroom", systemImage: "exclamationmark.triangle")
                        .labelStyle(.iconOnly)
                        .foregroundStyle(Synth.amber)
                        .help("This Lightroom did not accept \(descriptor.id) in the last catalog check.")
                }
            }
            .frame(maxWidth: .infinity, alignment: .leading)

            slotView(.dial, key: key).frame(width: SlidersTab.dialColumnWidth, alignment: .leading)
            slotView(.nudgeDown, key: key).frame(width: SlidersTab.nudgeColumnWidth, alignment: .leading)
            slotView(.nudgeUp, key: key).frame(width: SlidersTab.nudgeColumnWidth, alignment: .leading)
            StepCell(store: store, descriptor: descriptor, slot: slot, swapped: swapped)
                .frame(width: SlidersTab.stepColumnWidth, alignment: .leading)
        }
        .padding(.horizontal, Synth.rowInset)
        .frame(minHeight: Synth.isFlat ? 44 : 40)
        .overlay(alignment: .bottom) { Rectangle().fill(Synth.rowDivider).frame(height: 1) }
    }

    private var unitLabel: String {
        switch descriptor.unit {
        case "temp": "pt · 50 K"
        case "deg": "°"
        case "choice": "choice"
        default: descriptor.unit
        }
    }

    private func slotView(_ role: SlotRole, key: ParameterKey) -> some View {
        let chord = role.chord(in: slot)
        return KeySlot(
            id: "\(key).\(role.rawValue)",
            chord: chord,
            conflict: chord == nil ? nil : conflicts[role.usage(key)],
            minWidth: role == .dial ? 52 : 40,
            accessibilityName: "\(descriptor.label) \(role.title)",
            onSet: { KeymapEditor.shared.assign(store, chord: $0, role: role, key: key) },
            onClear: { KeymapEditor.shared.assign(store, chord: nil, role: role, key: key) }
        )
    }
}

// MARK: - Steps

/// The steps a parameter moves by, as the keys use them now: the key alone,
/// then with ⇧ ("1 · ⇧5"). Highlighted when they differ from the defaults;
/// a click opens the step editor.
private struct StepCell: View {
    let store: CommandStore
    let descriptor: ParameterDescriptor
    let slot: ParameterSlot?
    let swapped: Bool
    @State private var isEditing = false

    var body: some View {
        let steps = StepPair(descriptor: descriptor, slot: slot, swapped: swapped)
        if descriptor.stepRange.lowerBound == descriptor.stepRange.upperBound {
            // Choice parameters move one option at a time.
            Text("1 option")
                .synthLabel(9)
                .help("Each step moves to the next option.")
        } else {
            Button { isEditing.toggle() } label: {
                HStack(spacing: 4) {
                    Text(StepPair.text(steps.plain))
                    Text("·").foregroundStyle(Synth.steel).accessibilityHidden(true)
                    Text("⇧" + StepPair.text(steps.shifted))
                }
                .font(SynthFont.font(.mono, size: 12, weight: Synth.isFlat && !steps.isCustom ? .regular : .semibold))
                .foregroundStyle(steps.isCustom ? Synth.magentaLight : Synth.body)
                .lineLimit(1)
                .minimumScaleFactor(0.8)
                .padding(.horizontal, 8)
                .frame(minHeight: 26)
                // Galerist: your own steps get the ink border of a key; the
                // defaults a hairline.
                .background(
                    RoundedRectangle(cornerRadius: Synth.Radius.control)
                        .fill(steps.isCustom && !Synth.isFlat ? Synth.magenta.opacity(0.10) : Color.clear)
                )
                .overlay(
                    RoundedRectangle(cornerRadius: Synth.Radius.control)
                        .strokeBorder(steps.isCustom ? Synth.magenta : (Synth.isFlat ? Synth.outline : Synth.gridLine), lineWidth: 1)
                )
                .contentShape(RoundedRectangle(cornerRadius: Synth.Radius.control))
            }
            .buttonStyle(.plain)
            .help(steps.isCustom ? "Your own steps. Click to change them." : "Click to change the steps")
            .accessibilityLabel("\(descriptor.label) steps")
            .accessibilityValue("\(StepPair.text(steps.plain)), with Shift \(StepPair.text(steps.shifted))"
                                + (steps.isCustom ? ", changed" : ""))
            .accessibilityHint("Opens the step editor")
            .popover(isPresented: $isEditing, arrowEdge: .bottom) {
                StepEditor(store: store, descriptor: descriptor)
            }
        }
    }
}

/// Effective steps of one parameter, in the order the keys use them.
private struct StepPair {
    let normal: Double
    let coarse: Double
    let defaultNormal: Double
    let defaultCoarse: Double
    let swapped: Bool
    let isCustom: Bool

    init(descriptor: ParameterDescriptor, slot: ParameterSlot?, swapped: Bool) {
        defaultNormal = descriptor.normalStep
        defaultCoarse = descriptor.coarseStep
        normal = slot?.normalStep ?? descriptor.normalStep
        coarse = slot?.coarseStep ?? descriptor.coarseStep
        self.swapped = swapped
        isCustom = slot?.normalStep != nil || slot?.coarseStep != nil
    }

    /// The step of the key alone, and with ⇧.
    var plain: Double { swapped ? coarse : normal }
    var shifted: Double { swapped ? normal : coarse }
    var defaultPlain: Double { swapped ? defaultCoarse : defaultNormal }
    var defaultShifted: Double { swapped ? defaultNormal : defaultCoarse }

    static func text(_ value: Double) -> String {
        value.formatted(.number.precision(.fractionLength(0...2)))
    }
}

/// Popover: the step of the key alone and with ⇧, within the parameter's
/// allowed range, and a way back to the defaults.
private struct StepEditor: View {
    let store: CommandStore
    let descriptor: ParameterDescriptor

    var body: some View {
        let keymap = store.keymap.keymap
        let swapped = keymap.input.swapNormalAndCoarse
        let steps = StepPair(descriptor: descriptor, slot: keymap.slot(for: descriptor.key), swapped: swapped)
        VStack(alignment: .leading, spacing: Synth.Space.m) {
            VStack(alignment: .leading, spacing: 2) {
                Text(descriptor.label).synthTitle(14)
                Text("From \(StepPair.text(descriptor.stepRange.lowerBound)) to \(StepPair.text(descriptor.stepRange.upperBound)) \(unit)")
                    .synthLabel(9)
            }
            field("Key alone", normal: !swapped)
            field("With ⇧", normal: swapped)
            HStack(spacing: Synth.Space.s) {
                Text("Default \(StepPair.text(steps.defaultPlain)) · ⇧\(StepPair.text(steps.defaultShifted))")
                    .synthBody(11, color: Synth.steel)
                Spacer(minLength: Synth.Space.s)
                Button("Back to default") {
                    store.editKeymap { $0.updateSlot(for: descriptor.key) { $0.normalStep = nil; $0.coarseStep = nil } }
                }
                .buttonStyle(.neonQuiet)
                .disabled(!steps.isCustom)
                .accessibilityLabel("Back to the default steps of \(descriptor.label)")
            }
        }
        .padding(14)
        .frame(width: 280)
        .background(Synth.isFlat ? Synth.panel : Synth.panelRaised)
        .synthColorScheme()
    }

    private var unit: String {
        switch descriptor.unit {
        case "temp": "pt (50 K each)"
        case "deg": "°"
        default: descriptor.unit
        }
    }

    /// One step field; `normal` picks the stored value it edits (the small
    /// step, or the large one).
    private func field(_ title: String, normal: Bool) -> some View {
        let slot = store.keymap.keymap.slot(for: descriptor.key)
        let fallback = normal ? descriptor.normalStep : descriptor.coarseStep
        let current = (normal ? slot?.normalStep : slot?.coarseStep) ?? fallback
        return HStack(spacing: Synth.Space.s) {
            Text(title).synthBody(13, weight: .medium, color: Synth.ice)
            Spacer(minLength: Synth.Space.s)
            TextField(title, value: Binding(
                get: { current },
                set: { value in
                    guard value.isFinite else { return }
                    // Clamp into the allowed range; the default value means "no override".
                    let clamped = min(descriptor.stepRange.upperBound, max(descriptor.stepRange.lowerBound, value))
                    let stored: Double? = abs(clamped - fallback) < 1e-9 ? nil : clamped
                    store.editKeymap {
                        $0.updateSlot(for: descriptor.key) { slot in
                            if normal { slot.normalStep = stored } else { slot.coarseStep = stored }
                        }
                    }
                }
            ), format: .number.precision(.fractionLength(0...3)))
            .textFieldStyle(NeonFieldStyle(width: 84))
            .accessibilityLabel("\(descriptor.label) step, \(title == "With ⇧" ? "with Shift" : "key alone")")
        }
    }
}
