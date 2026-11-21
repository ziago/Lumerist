import SwiftUI
import LumeristCore

/// Command Keys of a module, grouped, with add / edit / duplicate / delete / run.
struct CommandsTab: View {
    let store: CommandStore
    let scope: ParameterScope
    let search: String
    @State private var collapsed: Set<String> = []
    @State private var editing: CommandKey?
    @State private var isNew = false
    @State private var deleting: CommandKey?

    var body: some View {
        let keymap = store.keymap.keymap
        let conflicts = ConflictIndex(keymap, scope: scope)
        let actions = filtered(keymap.commandKeys(in: scope))
        let groups = orderedGroups(actions)
        VStack(alignment: .leading, spacing: Synth.Space.s) {
            HStack(spacing: Synth.Space.m) {
                Button {
                    isNew = true
                    editing = CommandKey(scope: scope == .develop ? .develop : .library, chord: KeyChord(-1),
                                        title: "", action: .command(name: LightroomCommandName.autoTone.rawValue, argument: nil))
                } label: {
                    Label("Add Command Key", systemImage: "plus")
                }
                .buttonStyle(.neonPrimary)
                Text(hint(comboKey: keymap.systemKeys.comboKey))
                    .synthBody(12, color: Synth.steel)
                    .fixedSize(horizontal: false, vertical: true)
                Spacer(minLength: 0)
                ExpandCollapseButtons(expandAll: { collapsed = [] }, collapseAll: { collapsed = Set(groups.map(\.0)) })
            }

            if actions.isEmpty {
                NeonPanel {
                    Text(search.isEmpty ? "No Command Keys in \(scope == .develop ? "Develop" : "Library") yet." : "Nothing matches “\(search)”.")
                        .synthBody(13, color: Synth.steel)
                        .frame(maxWidth: .infinity, alignment: .leading)
                        .padding(14)
                }
            }

            if Synth.isFlat {
                // Galerist: open groups stand apart; closed ones form one hairline list.
                VStack(alignment: .leading, spacing: 0) {
                    ForEach(groups, id: \.0) { group, items in
                        let isOpen = !collapsed.contains(group)
                        if isOpen {
                            VStack(alignment: .leading, spacing: 10) {
                                groupHeader(group, count: items.count, isOpen: true)
                                groupPanel(items, conflicts: conflicts)
                            }
                            .padding(.vertical, 10)
                        } else {
                            groupHeader(group, count: items.count, isOpen: false)
                                .padding(.top, -1)
                        }
                    }
                }
            } else {
                ForEach(groups, id: \.0) { group, items in
                    let isOpen = !collapsed.contains(group)
                    groupHeader(group, count: items.count, isOpen: isOpen)
                    if isOpen {
                        groupPanel(items, conflicts: conflicts)
                    }
                }
            }
        }
        .sheet(item: $editing) { action in
            ActionEditor(store: store, original: action, isNew: isNew) { editing = nil }
        }
        .confirmationDialog("Delete “\(deleting?.title ?? "")”?", isPresented: Binding(
            get: { deleting != nil }, set: { if !$0 { deleting = nil } }
        )) {
            Button("Delete", role: .destructive) {
                if let id = deleting?.id { store.editKeymap { $0.commandKeys.removeAll { $0.id == id } } }
                deleting = nil
            }
        }
    }

    private func groupHeader(_ group: String, count: Int, isOpen: Bool) -> some View {
        CollapsibleHeader(title: group, count: count, isExpanded: isOpen) {
            if collapsed.contains(group) { collapsed.remove(group) } else { collapsed.insert(group) }
        }
    }

    private func groupPanel(_ items: [CommandKey], conflicts: ConflictIndex) -> some View {
        NeonPanel {
            VStack(spacing: 0) {
                ForEach(items) { action in
                    row(action, conflict: conflicts[.action(action.id, title: action.title)])
                }
            }
        }
    }

    /// Explains Command Keys and how to record a combo with the current Combo Key.
    private func hint(comboKey: KeyChord?) -> String {
        let intro = "One press runs a Lightroom command, a preset, a Lightroom shortcut or a macro."
        guard let comboKey else { return intro }
        let combo = KeyChord(40, .combo).displayText(comboKey: comboKey)
        return intro + " For a combo, hold \(comboKey.displayText) while you press the key: \(combo)."
    }

    private func row(_ action: CommandKey, conflict: String?) -> some View {
        let key = store.keymap.keymap.displayText(for: action.chord)
        return HStack(spacing: Synth.Space.m) {
            KeyCap(text: key, state: conflict == nil ? .assigned : .conflict, minWidth: 44)
                .help(conflict ?? key)
                .frame(width: 96, alignment: .leading)
            VStack(alignment: .leading, spacing: 2) {
                Text(action.title).synthBody(Synth.isFlat ? 14 : 13, weight: .semibold, color: Synth.ice)
                Text(action.action.summary).synthBody(Synth.isFlat ? 12.5 : 12, color: Synth.steel).lineLimit(1)
            }
            Spacer(minLength: Synth.Space.s)
            if action.scope == .any { Text("All modules").synthLabel(9) }
            Button("Run") { store.perform(action.action) }
                .buttonStyle(.neonQuiet)
                .help("Run it now in Lightroom")
            Button("Edit") { isNew = false; editing = action }
                .buttonStyle(.neonSecondary)
            MoreButton(accessibilityLabel: "More for \(action.title)", entries: [
                ("Duplicate", { duplicate(action) }),
                ("Run now", { store.perform(action.action) }),
                ("Delete…", { deleting = action }),
            ])
        }
        .padding(.horizontal, Synth.rowInset)
        .frame(minHeight: Synth.isFlat ? 52 : 48)
        .overlay(alignment: .bottom) { Rectangle().fill(Synth.rowDivider).frame(height: 1) }
    }

    /// A copy without a key, opened in the editor so the user picks a free one.
    private func duplicate(_ action: CommandKey) {
        var copy = action
        copy.id = UUID()
        copy.title = String((action.title + " copy").prefix(CommandKey.maximumTitleLength))
        copy.chord = KeyChord(-1)
        isNew = true
        editing = copy
    }

    private func filtered(_ actions: [CommandKey]) -> [CommandKey] {
        let query = search.trimmingCharacters(in: .whitespaces)
        guard !query.isEmpty else { return actions }
        return actions.filter {
            $0.title.localizedCaseInsensitiveContains(query) || $0.group.localizedCaseInsensitiveContains(query)
                || store.keymap.keymap.displayText(for: $0.chord).localizedCaseInsensitiveContains(query)
                || $0.action.summary.localizedCaseInsensitiveContains(query)
        }
    }

    private func orderedGroups(_ actions: [CommandKey]) -> [(String, [CommandKey])] {
        var order: [String] = []
        var map: [String: [CommandKey]] = [:]
        for action in actions {
            if map[action.group] == nil { order.append(action.group) }
            map[action.group, default: []].append(action)
        }
        return order.map { ($0, map[$0] ?? []) }
    }
}

/// "⋯" button that pops a small native menu.
struct MoreButton: View {
    let accessibilityLabel: String
    let entries: [(String, () -> Void)]
    @State private var anchor = MenuAnchor()

    var body: some View {
        Button {
            anchor.popUp([MenuAnchor.Section(title: nil, entries: entries.map { MenuAnchor.Entry(title: $0.0, isChecked: false, action: $0.1) })])
        } label: {
            Image(systemName: "ellipsis")
                .font(.system(size: 12, weight: .bold))
                .foregroundStyle(Synth.steel)
                .frame(width: 28, height: 28)
                .overlay(RoundedRectangle(cornerRadius: Synth.Radius.control).strokeBorder(Synth.isFlat ? Synth.outline : Synth.gridLine, lineWidth: 1))
                .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .background(MenuAnchorView(anchor: anchor))
        .accessibilityLabel(accessibilityLabel)
    }
}

// MARK: - Editor

/// Edits one Command Key. Validates on a copy of the keymap before saving.
struct ActionEditor: View {
    let store: CommandStore
    let original: CommandKey
    let isNew: Bool
    let close: () -> Void

    @State private var title: String
    @State private var group: String
    @State private var chord: KeyChord?
    @State private var scope: CommandKey.Scope
    @State private var draft: ActionDraft
    @State private var error: String?

    init(store: CommandStore, original: CommandKey, isNew: Bool, close: @escaping () -> Void) {
        self.store = store
        self.original = original
        self.isNew = isNew
        self.close = close
        _title = State(initialValue: original.title)
        _group = State(initialValue: original.group)
        _chord = State(initialValue: original.chord.keyCode < 0 ? nil : original.chord)
        _scope = State(initialValue: original.scope)
        _draft = State(initialValue: ActionDraft(original.action))
    }

    var body: some View {
        let groups = Array(Set(store.keymap.keymap.commandKeys.map(\.group) + ["Lightroom"])).sorted()
        // The form scrolls and the buttons stay pinned at the bottom: a sheet
        // is never taller than the window it belongs to.
        VStack(spacing: 0) {
            ScrollView {
                form(groups: groups)
                    .padding(Synth.Space.xxl)
            }
            buttonBar
        }
        .frame(width: 720)
        .frame(minHeight: 420, idealHeight: 680)
        .background(Synth.abyss)
        .synthColorScheme()
        // A sheet is its own root: hand it the Combo Key so combos read "Tab + K".
        .environment(\.comboKey, store.keymap.keymap.systemKeys.comboKey)
        .onDisappear { ChordRecorder.shared.stop() }
    }

    private func form(groups: [String]) -> some View {
        VStack(alignment: .leading, spacing: Synth.Space.l) {
            Text(isNew ? "Add Command Key" : "Edit Command Key").synthTitle(20)

            NeonPanel {
                VStack(spacing: 0) {
                    SettingRow(title: "Name") {
                        TextField("e.g. Auto Tone", text: $title)
                            .textFieldStyle(NeonFieldStyle(width: 260))
                            .multilineTextAlignment(.leading)
                    }
                    SettingRow(title: "Group", detail: "Command Keys are listed under their group.") {
                        HStack(spacing: Synth.Space.s) {
                            TextField("Group", text: $group)
                                .textFieldStyle(NeonFieldStyle(width: 180))
                                .multilineTextAlignment(.leading)
                            NeonMenu(title: "Existing", accessibilityLabel: "Existing groups", items: groups, selection: group,
                                     itemTitle: { $0 }, onSelect: { group = $0 })
                                .frame(width: 110)
                        }
                    }
                    SettingRow(title: "Key", detail: keyDetail) {
                        KeySlot(id: "command-editor.key", chord: chord, minWidth: 60, accessibilityName: "Command Key",
                                onSet: { chord = $0 }, onClear: { chord = nil })
                    }
                    SettingRow(title: "Works in") {
                        SynthSegmented(items: CommandKey.Scope.allCases, selection: $scope, title: {
                            switch $0 { case .develop: "Develop"; case .library: "Library"; case .any: "All modules" }
                        })
                    }
                }
            }

            Text("What it does").synthTitle(15)
            ActionDraftEditor(store: store, draft: $draft, allowsMacro: true, slotID: "command-editor")

            if let error {
                Label(error, systemImage: "exclamationmark.triangle.fill").synthBody(12, color: Synth.error)
            }
        }
    }

    private var keyDetail: String {
        let base = "Click, then press the key (modifiers allowed). Esc cancels."
        guard let comboKey = store.keymap.keymap.systemKeys.comboKey else { return base }
        return base + " For a combo, hold \(comboKey.displayText) and press the key."
    }

    private var buttonBar: some View {
        HStack {
            Button("Run now") { if let action = draft.action { store.perform(action) } }
                .buttonStyle(.neonQuiet)
                .disabled(draft.action == nil)
            Spacer()
            Button("Cancel", action: close).buttonStyle(.neonSecondary).keyboardShortcut(.cancelAction)
            Button("Save") { save() }
                .buttonStyle(.neonPrimary)
                .disabled(chord == nil || title.trimmingCharacters(in: .whitespaces).isEmpty || draft.action == nil)
        }
        .padding(.horizontal, Synth.Space.xxl)
        .padding(.vertical, Synth.Space.l)
        .background(Synth.isFlat ? Synth.chrome : Synth.abyss)
        .overlay(alignment: .top) { Rectangle().fill(Synth.gridLine).frame(height: 1) }
    }

    private func save() {
        guard let chord, let action = draft.action else { return }
        var updated = original
        updated.title = String(title.trimmingCharacters(in: .whitespaces).prefix(CommandKey.maximumTitleLength))
        updated.group = group.trimmingCharacters(in: .whitespaces).isEmpty ? "Lightroom" : group
        updated.chord = chord
        updated.scope = scope
        updated.action = action
        var proposed = store.keymap.keymap
        upsert(updated, into: &proposed)
        do {
            try proposed.validate()
            store.editKeymap { upsert(updated, into: &$0) }
            close()
        } catch {
            self.error = error.localizedDescription
        }
    }

    private func upsert(_ action: CommandKey, into keymap: inout Keymap) {
        if let index = keymap.commandKeys.firstIndex(where: { $0.id == action.id }) {
            keymap.commandKeys[index] = action
        } else {
            keymap.commandKeys.append(action)
        }
    }
}

/// Editable form of an action (and of one macro step).
struct ActionDraft: Equatable {
    enum Kind: String, CaseIterable { case command, preset, shortcut, macro }

    var kind: Kind = .command
    var command: LightroomCommandName = .autoTone
    var argument: String?
    var presetID: String?
    var presetName: String = ""
    var shortcut: KeyChord?
    var steps: [StepDraft] = []

    struct StepDraft: Equatable, Identifiable {
        let id = UUID()
        var action: ActionDraft
        var delay: Double = 0
    }

    init() {}

    init(_ action: CommandKey.Action) {
        switch action {
        case .command(let name, let argument):
            kind = .command
            command = LightroomCommandName(rawValue: name) ?? .autoTone
            self.argument = argument
        case .preset(let uuid, let name):
            kind = .preset
            presetID = uuid
            presetName = name
        case .shortcut(let chord):
            kind = .shortcut
            shortcut = chord
        case .macro(let steps):
            kind = .macro
            self.steps = steps.map { StepDraft(action: ActionDraft($0.action), delay: $0.delay) }
        }
    }

    /// The action, or nil while incomplete.
    var action: CommandKey.Action? {
        switch kind {
        case .command:
            guard command.accepts(argument: argument) else { return nil }
            return .command(name: command.rawValue, argument: argument)
        case .preset:
            guard let presetID else { return nil }
            return .preset(uuid: presetID, name: presetName.isEmpty ? presetID : presetName)
        case .shortcut:
            return shortcut.map { .shortcut($0) }
        case .macro:
            let actions = steps.compactMap { step in step.action.action.map { MacroStep(action: $0, delay: step.delay) } }
            guard !actions.isEmpty, actions.count == steps.count else { return nil }
            return .macro(actions)
        }
    }
}

/// The type picker plus the controls for the chosen type.
struct ActionDraftEditor: View {
    let store: CommandStore
    @Binding var draft: ActionDraft
    let allowsMacro: Bool
    /// Unique per editor, so only one key slot records at a time.
    let slotID: String
    /// Why the last recorded shortcut was refused (a combo can't be sent).
    @State private var shortcutError: String?

    var body: some View {
        VStack(alignment: .leading, spacing: Synth.Space.m) {
            SynthSegmented(
                items: allowsMacro ? ActionDraft.Kind.allCases : [.command, .preset, .shortcut],
                selection: $draft.kind,
                title: {
                    switch $0 { case .command: "Command"; case .preset: "Develop preset"; case .shortcut: "LR shortcut"; case .macro: "Macro" }
                }
            )
            switch draft.kind {
            case .command: CommandPicker(draft: $draft)
            case .preset: PresetPicker(store: store, draft: $draft)
            case .shortcut:
                HStack(spacing: Synth.Space.m) {
                    KeySlot(id: "\(slotID).shortcut", chord: draft.shortcut, minWidth: 70,
                            accessibilityName: "Lightroom shortcut", onSet: setShortcut,
                            onClear: { draft.shortcut = nil; shortcutError = nil })
                    Text("Lumerist sends this key combination to Lightroom, as if you pressed it.")
                        .synthBody(12, color: Synth.steel)
                }
                if let shortcutError {
                    Label(shortcutError, systemImage: "exclamationmark.triangle.fill").synthBody(12, color: Synth.error)
                        .fixedSize(horizontal: false, vertical: true)
                }
            case .macro: MacroEditor(store: store, steps: $draft.steps)
            }
        }
    }

    private func setShortcut(_ chord: KeyChord) {
        guard !chord.isCombo else {
            shortcutError = ComboMisuse.lightroomShortcut.message
            return
        }
        draft.shortcut = chord
        shortcutError = nil
    }
}

private struct CommandPicker: View {
    @Binding var draft: ActionDraft

    var body: some View {
        let commands = LightroomCommandName.allCases.filter { $0 != .applyPreset }
        let groupOrder = ["Develop", "Edit", "Photos", "Masking"]
        VStack(alignment: .leading, spacing: Synth.Space.s) {
            NeonMenu(
                title: draft.command.title,
                accessibilityLabel: "Command",
                groups: groupOrder.map { group in
                    NeonMenu<LightroomCommandName>.Group(title: group, items: commands.filter { $0.group == group })
                },
                selection: draft.command,
                itemTitle: { $0.title },
                onSelect: { command in
                    draft.command = command
                    draft.argument = command.defaultArgument
                }
            )
            .frame(maxWidth: 320)
            ArgumentPicker(command: draft.command, argument: $draft.argument)
        }
    }
}

private struct ArgumentPicker: View {
    let command: LightroomCommandName
    @Binding var argument: String?

    var body: some View {
        switch command {
        case .rating:
            SynthSegmented(items: ["0", "1", "2", "3", "4", "5"], selection: binding("5"), title: { $0 == "0" ? "None" : "\($0) ★" })
        case .colorLabel:
            SynthSegmented(items: ["red", "yellow", "green", "blue", "purple", "none"], selection: binding("red"), title: { $0.capitalized })
        case .switchModule:
            choice(LightroomCommandName.modules.sorted(), label: "Module", fallback: "develop") { $0.capitalized }
        case .selectTool:
            choice(LightroomCommandName.tools.sorted(), label: "Tool", fallback: "crop") { $0.capitalized }
        case .revealPanel:
            NeonMenu(
                title: ParameterCatalog.descriptor(scope: .develop, id: argument ?? "")?.label ?? "Choose a slider",
                accessibilityLabel: "Slider",
                groups: ParameterCatalog.sections(for: .develop).map { NeonMenu<String>.Group(title: $0.section, items: $0.parameters.map(\.id)) },
                selection: argument,
                itemTitle: { ParameterCatalog.descriptor(scope: .develop, id: $0)?.label ?? $0 },
                onSelect: { argument = $0 }
            )
            .frame(maxWidth: 320)
        case .createMask, .addToMask, .subtractFromMask, .intersectWithMask:
            choice(MaskTool.all.map(\.id), label: "Mask tool", fallback: "aiSelection/subject") { MaskTool.title(for: $0) }
        default:
            Text("No options.").synthBody(12, color: Synth.steel)
        }
    }

    private func binding(_ fallback: String) -> Binding<String> {
        Binding(get: { argument ?? fallback }, set: { argument = $0 })
    }

    private func choice(_ items: [String], label: String, fallback: String, title: @escaping (String) -> String) -> some View {
        NeonMenu(title: title(argument ?? fallback), accessibilityLabel: label, items: items, selection: argument ?? fallback,
                 itemTitle: title, onSelect: { argument = $0 })
            .frame(maxWidth: 320)
            .onAppear { if argument == nil { argument = fallback } }
    }
}

/// Develop presets reported by the plug-in, searchable, with refresh.
struct PresetPicker: View {
    let store: CommandStore
    @Binding var draft: ActionDraft
    @State private var search = ""

    var body: some View {
        let presets = store.presets.filter {
            search.isEmpty || $0.name.localizedCaseInsensitiveContains(search) || $0.group.localizedCaseInsensitiveContains(search)
        }
        VStack(alignment: .leading, spacing: Synth.Space.s) {
            HStack {
                SynthSearchField(prompt: "Search presets", text: $search)
                Button("Refresh") { store.refreshPresets() }
                    .buttonStyle(.neonSecondary)
                    .help("Ask Lightroom for the current preset list")
            }
            if let name = draft.presetID.map({ _ in draft.presetName }) {
                Label("Selected: \(name)", systemImage: "checkmark").synthBody(12, color: Synth.teal)
            }
            NeonPanel {
                ScrollView {
                    LazyVStack(alignment: .leading, spacing: 0) {
                        if presets.isEmpty {
                            Text(store.presets.isEmpty ? "No presets loaded. Connect Lightroom and press Refresh." : "No preset matches.")
                                .synthBody(12, color: Synth.steel)
                                .padding(12)
                        }
                        ForEach(presets) { preset in
                            Button {
                                draft.presetID = preset.id
                                draft.presetName = preset.name
                            } label: {
                                HStack {
                                    Text(preset.name).synthBody(13, color: Synth.ice)
                                    Spacer()
                                    Text(preset.group).synthLabel(9)
                                    if draft.presetID == preset.id {
                                        Image(systemName: "checkmark").foregroundStyle(Synth.magenta)
                                    }
                                }
                                .padding(.horizontal, 12)
                                .frame(minHeight: 30)
                                .background(draft.presetID == preset.id ? (Synth.isFlat ? Synth.gridLine : Synth.magenta.opacity(0.1)) : .clear)
                                .contentShape(Rectangle())
                            }
                            .buttonStyle(.plain)
                            .accessibilityAddTraits(draft.presetID == preset.id ? [.isSelected] : [])
                        }
                    }
                }
                .frame(height: 180)
            }
        }
        .onAppear { if store.presets.isEmpty { store.refreshPresets() } }
    }
}

/// Ordered macro steps, each with a pause after it.
private struct MacroEditor: View {
    let store: CommandStore
    @Binding var steps: [ActionDraft.StepDraft]

    var body: some View {
        VStack(alignment: .leading, spacing: Synth.Space.s) {
            ForEach(Array(steps.enumerated()), id: \.element.id) { index, step in
                NeonPanel {
                    VStack(alignment: .leading, spacing: Synth.Space.s) {
                        HStack {
                            Text("Step \(index + 1)").synthLabel(10, color: Synth.teal)
                            Spacer()
                            Text("Then wait").synthLabel(9)
                            TextField("0", value: Binding(
                                get: { steps[index].delay },
                                set: { steps[index].delay = min(MacroStep.delayRange.upperBound, max(0, $0)) }
                            ), format: .number.precision(.fractionLength(0...1)))
                            .textFieldStyle(NeonFieldStyle(width: 56))
                            .accessibilityLabel("Seconds to wait after step \(index + 1)")
                            Text("s").synthLabel(9)
                            Button { move(index, by: -1) } label: { Image(systemName: "arrow.up") }
                                .buttonStyle(.neonQuiet).disabled(index == 0).accessibilityLabel("Move step up")
                            Button { move(index, by: 1) } label: { Image(systemName: "arrow.down") }
                                .buttonStyle(.neonQuiet).disabled(index == steps.count - 1).accessibilityLabel("Move step down")
                            Button { steps.remove(at: index) } label: { Image(systemName: "xmark") }
                                .buttonStyle(.neonQuiet).accessibilityLabel("Remove step \(index + 1)")
                        }
                        ActionDraftEditor(store: store, draft: Binding(
                            get: { steps[index].action },
                            set: { steps[index].action = $0 }
                        ), allowsMacro: false, slotID: "macro.\(step.id)")
                    }
                    .padding(12)
                }
            }
            Button {
                steps.append(ActionDraft.StepDraft(action: ActionDraft()))
            } label: { Label("Add step", systemImage: "plus") }
            .buttonStyle(.neonSecondary)
            .disabled(steps.count >= CommandKey.maximumMacroSteps)
        }
    }

    private func move(_ index: Int, by offset: Int) {
        let target = index + offset
        guard steps.indices.contains(target) else { return }
        steps.swapAt(index, target)
    }
}
