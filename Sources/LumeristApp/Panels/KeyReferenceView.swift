import SwiftUI
import LumeristCore

/// The floating list of the current module's keys: System Keys, Dial Keys,
/// Nudge Keys, and Command Keys, then the combos in their own group
/// ("Combos (hold Tab)"). Searchable and paged.
struct KeyReferenceView: View {
    let store: CommandStore
    @State private var search = ""
    @State private var page = 0

    private static let pageSize = 14

    struct Entry: Identifiable, Hashable {
        let id: String
        /// Combos show only their own key: the group title names the Combo Key.
        let keys: [String]
        let title: String
        let kind: String
        /// Also matched by the search: a combo's full name ("Tab + K").
        var searchText = ""
        /// The title row of a group rather than a key.
        var isHeader = false
    }

    var body: some View {
        let scope: ParameterScope = store.context.module == "library" ? .library : .develop
        let all = entries(for: scope)
        let combos = filtered(all.combos)
        let entries = filtered(all.regular) + (combos.isEmpty ? [] : [comboHeader] + combos)
        let pages = max(1, Int(ceil(Double(entries.count) / Double(Self.pageSize))))
        let current = min(page, pages - 1)
        let visible = Array(entries.dropFirst(current * Self.pageSize).prefix(Self.pageSize))
        PanelChrome(
            title: "Key Reference",
            subtitle: scope == .develop ? (store.context.isMasking ? "Develop · mask" : "Develop") : "Library",
            trailing: AnyView(
                Button { store.setKeyReferenceVisible(false) } label: {
                    Image(systemName: "xmark").foregroundStyle(Synth.steel).frame(width: 22, height: 22).contentShape(Rectangle())
                }
                .buttonStyle(.plain)
                .accessibilityLabel("Hide the Key Reference")
            )
        ) {
            let flat = Synth.isFlat
            VStack(alignment: .leading, spacing: flat ? 10 : Synth.Space.s) {
                SynthSearchField(prompt: "Search", text: $search)
                    .onChange(of: search) { page = 0 }
                VStack(spacing: 0) {
                    if visible.isEmpty {
                        Text(search.isEmpty ? "No keys assigned in this module." : "Nothing matches.")
                            .synthBody(12, color: Synth.steel)
                            .frame(maxWidth: .infinity, alignment: .leading)
                            .padding(.vertical, 10)
                    }
                    ForEach(visible) { entry in
                        if entry.isHeader {
                            if flat {
                                Text(entry.title)
                                    .panelCaps(9, color: Synth.ice, weight: .semibold)
                                    .frame(maxWidth: .infinity, minHeight: 30, alignment: .bottomLeading)
                                    .padding(.bottom, 4)
                                    .accessibilityAddTraits(.isHeader)
                            } else {
                                Text(entry.title)
                                    .synthLabel(9, color: Synth.amber)
                                    .frame(maxWidth: .infinity, minHeight: 26, alignment: .bottomLeading)
                                    .accessibilityAddTraits(.isHeader)
                            }
                        } else if flat {
                            flatRow(entry)
                        } else {
                            row(entry)
                        }
                    }
                }
                if pages > 1 {
                    HStack {
                        Button { page = max(0, current - 1) } label: { Image(systemName: "chevron.left") }
                            .buttonStyle(.neonQuiet).disabled(current == 0).accessibilityLabel("Previous page")
                        Spacer()
                        Text("\(current + 1) / \(pages)").synthLabel(10)
                        Spacer()
                        Button { page = min(pages - 1, current + 1) } label: { Image(systemName: "chevron.right") }
                            .buttonStyle(.neonQuiet).disabled(current >= pages - 1).accessibilityLabel("Next page")
                    }
                    .padding(.top, flat ? 2 : 0)
                }
            }
            .padding(.horizontal, flat ? 14 : 12)
            .padding(.vertical, 12)
        }
        .frame(width: 340)
    }

    /// Galerist: hairline rows, mono key caps with a light 1 px border.
    private func flatRow(_ entry: Entry) -> some View {
        HStack(spacing: 10) {
            HStack(spacing: 4) {
                ForEach(entry.keys, id: \.self) { key in
                    Text(key)
                        .font(SynthFont.font(.mono, size: 11, weight: .medium))
                        .monospacedDigit()
                        .foregroundStyle(Synth.ice)
                        .lineLimit(1)
                        .padding(.horizontal, 6)
                        .frame(minWidth: 30, minHeight: 20)
                        .overlay(Rectangle().strokeBorder(Synth.ice.opacity(0.85), lineWidth: 1))
                }
            }
            .frame(width: 88, alignment: .leading)
            Text(entry.title).synthBody(13, color: Synth.ice).lineLimit(1)
            Spacer(minLength: 4)
            Text(entry.kind).panelCaps(8)
        }
        .frame(height: 30)
        .overlay(alignment: .top) { Rectangle().fill(Synth.gridLine).frame(height: 1) }
        .accessibilityElement(children: .combine)
    }

    private func row(_ entry: Entry) -> some View {
        HStack(spacing: 6) {
            HStack(spacing: 3) {
                ForEach(entry.keys, id: \.self) { key in
                    Text(key)
                        .font(SynthFont.font(.mono, size: 11, weight: .bold))
                        .foregroundStyle(Synth.teal)
                        .padding(.horizontal, 5)
                        .frame(minWidth: 22, minHeight: 20)
                        .overlay(RoundedRectangle(cornerRadius: 3).strokeBorder(Synth.teal.opacity(0.8), lineWidth: 1))
                }
            }
            .frame(width: 104, alignment: .leading)
            Text(entry.title).synthBody(12, weight: .medium, color: Synth.ice).lineLimit(1)
            Spacer(minLength: 4)
            Text(entry.kind).synthLabel(8)
        }
        .frame(height: 26)
        .accessibilityElement(children: .combine)
    }

    private var comboHeader: Entry {
        let title = store.keymap.keymap.systemKeys.comboKey.map { "Combos (hold \($0.displayText))" }
            ?? "Combos (set a Combo Key)"
        return Entry(id: "header.combos", keys: [], title: title, kind: "", isHeader: true)
    }

    /// Keys that work on their own, then combos (each Nudge Key on its side).
    private func entries(for scope: ParameterScope) -> (regular: [Entry], combos: [Entry]) {
        let keymap = store.keymap.keymap
        var regular: [Entry] = []
        var combos: [Entry] = []
        for role in SystemKeys.Role.allCases {
            guard let chord = keymap.systemKeys.chord(for: role) else { continue }
            if role == .paletteKey && scope != .develop { continue }
            // A System Key may itself be a combo ("Tab + H"): spelled out in full here.
            regular.append(Entry(id: "system.\(role.rawValue)", keys: [keymap.displayText(for: chord)], title: role.title, kind: "System"))
        }
        let summary = keymap.summary(for: scope, sortedBy: .section)
        for entry in summary.dialKeys {
            guard let chord = entry.slot.dial else { continue }
            regular.append(Entry(id: "dial.\(entry.descriptor.id)", keys: [chord.displayText],
                                 title: entry.descriptor.label, kind: "Dial"))
        }
        for entry in summary.nudgeKeys {
            let nudges = [("−", entry.slot.nudgeDown), ("+", entry.slot.nudgeUp)]
            for isCombo in [false, true] {
                let chords = nudges.compactMap { sign, chord in chord.map { (sign, $0) } }.filter { $0.1.isCombo == isCombo }
                guard !chords.isEmpty else { continue }
                let item = Entry(id: "nudge.\(isCombo ? "combo." : "")\(entry.descriptor.id)",
                                 keys: chords.map { $0.0 + $0.1.withoutCombo.displayText },
                                 title: entry.descriptor.label, kind: "Nudge",
                                 searchText: chords.map { keymap.displayText(for: $0.1) }.joined(separator: " "))
                if isCombo { combos.append(item) } else { regular.append(item) }
            }
        }
        for action in keymap.commandKeys(in: scope) {
            let item = Entry(id: "command.\(action.id)", keys: [action.chord.withoutCombo.displayText],
                             title: action.title, kind: "Command", searchText: keymap.displayText(for: action.chord))
            if action.chord.isCombo { combos.append(item) } else { regular.append(item) }
        }
        return (regular, combos)
    }

    private func filtered(_ entries: [Entry]) -> [Entry] {
        let query = search.trimmingCharacters(in: .whitespaces)
        guard !query.isEmpty else { return entries }
        return entries.filter { entry in
            entry.title.localizedCaseInsensitiveContains(query)
                || entry.keys.contains { $0.localizedCaseInsensitiveContains(query) }
                || entry.searchText.localizedCaseInsensitiveContains(query)
        }
    }
}
