import AppKit
import SwiftUI

// MARK: - Window background

/// Miami: abyss → night → dusk gradient with a faint perspective grid at the
/// bottom and a magenta horizon line; the grid never moves. Galerist: the
/// flat grey page.
struct SynthBackground: View {
    var body: some View {
        if Synth.isFlat {
            Synth.abyss
                .ignoresSafeArea()
                .accessibilityHidden(true)
        } else {
            neon
        }
    }

    private var neon: some View {
        ZStack(alignment: .bottom) {
            LinearGradient(
                stops: [
                    .init(color: Synth.abyss, location: 0),
                    .init(color: Synth.night, location: 0.7),
                    .init(color: Synth.dusk, location: 1),
                ],
                startPoint: .top,
                endPoint: .bottom
            )
            PerspectiveGrid()
                .frame(height: 220)
                .opacity(0.45)
                .allowsHitTesting(false)
            HorizonLine()
                .opacity(0.6)
                .padding(.bottom, 190)
                .allowsHitTesting(false)
        }
        .ignoresSafeArea()
        .accessibilityHidden(true)
    }
}

/// Teal grid lines converging to a vanishing point above the panel.
private struct PerspectiveGrid: View {
    var body: some View {
        Canvas { context, size in
            let teal = Color(hex: 0x2DE2E6, opacity: 0.22)
            let vanishing = CGPoint(x: size.width / 2, y: -size.height * 0.9)
            // Lines towards the vanishing point.
            let spacing: CGFloat = 64
            var x: CGFloat = -size.width
            while x <= size.width * 2 {
                var path = Path()
                path.move(to: CGPoint(x: x, y: size.height))
                let t = (0 - size.height) / (vanishing.y - size.height)
                path.addLine(to: CGPoint(x: x + (vanishing.x - x) * t, y: 0))
                context.stroke(path, with: .color(teal), lineWidth: 1)
                x += spacing
            }
            // Horizontal lines, closer together towards the horizon.
            var y = size.height
            var step: CGFloat = 46
            while y > 0 {
                var path = Path()
                path.move(to: CGPoint(x: 0, y: y))
                path.addLine(to: CGPoint(x: size.width, y: y))
                context.stroke(path, with: .color(teal), lineWidth: 1)
                y -= step
                step *= 0.72
                if step < 6 { break }
            }
        }
    }
}

private struct HorizonLine: View {
    var body: some View {
        LinearGradient(
            colors: [Synth.magenta.opacity(0), Synth.magenta.opacity(0.8), Synth.magenta.opacity(0)],
            startPoint: .leading,
            endPoint: .trailing
        )
        .frame(height: 1)
        .shadow(color: Synth.magenta.opacity(0.8), radius: 9)
    }
}

// MARK: - Panels and sections

/// A table or card: translucent panel fill with a 1 pt grid-line outline.
struct NeonPanel<Content: View>: View {
    @ViewBuilder var content: Content

    var body: some View {
        content
            .background(
                RoundedRectangle(cornerRadius: Synth.Radius.container)
                    .fill(Synth.isFlat ? Synth.panel : Synth.panel.opacity(0.82))
            )
            .overlay(
                RoundedRectangle(cornerRadius: Synth.Radius.container)
                    .strokeBorder(Synth.gridLine, lineWidth: Synth.stroke)
            )
    }
}

/// Screen title. Miami: uppercase Chakra Petch with a monospaced read-out
/// beside it. Galerist: the read-out as a small-caps eyebrow above a large
/// sentence-case title.
struct ScreenHeader: View {
    let title: String
    var readout: String?

    var body: some View {
        if Synth.isFlat {
            VStack(alignment: .leading, spacing: Synth.Space.s) {
                if let readout {
                    Text(readout).synthLabel(11)
                }
                Text(title)
                    .synthTitle(30)
                    .accessibilityAddTraits(.isHeader)
            }
            .frame(maxWidth: .infinity, alignment: .leading)
        } else {
            neon
        }
    }

    private var neon: some View {
        HStack(alignment: .firstTextBaseline, spacing: Synth.Space.m) {
            Text(title)
                .synthTitle(24)
                .accessibilityAddTraits(.isHeader)
            if let readout {
                Text(readout).synthLabel(12)
            }
            Spacer(minLength: 0)
        }
    }
}

/// A titled group of settings rows inside a panel. Galerist: small-caps
/// title, with its SF Symbol when one is given (Miami shows no icon).
struct SettingsSection<Content: View>: View {
    let title: String
    var systemImage: String? = nil
    var footer: String?
    @ViewBuilder var content: Content

    var body: some View {
        VStack(alignment: .leading, spacing: Synth.isFlat ? 10 : Synth.Space.s) {
            if Synth.isFlat {
                SectionTitle(title: title, systemImage: systemImage)
                    .accessibilityAddTraits(.isHeader)
            } else {
                Text(title)
                    .synthTitle(15)
                    .accessibilityAddTraits(.isHeader)
            }
            NeonPanel {
                VStack(alignment: .leading, spacing: 0) {
                    content
                }
            }
            if let footer {
                Text(footer)
                    .synthBody(12, color: Synth.steel)
                    .fixedSize(horizontal: false, vertical: true)
            }
        }
    }
}

/// One setting: title and description on the left, the control on the right.
struct SettingRow<Control: View>: View {
    let title: String
    var detail: String?
    @ViewBuilder var control: Control

    var body: some View {
        HStack(alignment: .center, spacing: Synth.Space.l) {
            SettingText(title: title, detail: detail)
            Spacer(minLength: Synth.Space.m)
            control
        }
        .padding(.horizontal, Synth.isFlat ? 18 : 14)
        .padding(.vertical, Synth.isFlat ? 13 : 12)
        .frame(maxWidth: .infinity, alignment: .leading)
        .overlay(alignment: .bottom) {
            Rectangle().fill(Synth.rowDivider).frame(height: 1)
        }
    }
}

/// A setting's title and description, as in `SettingRow`.
struct SettingText: View {
    let title: String
    var detail: String?

    var body: some View {
        VStack(alignment: .leading, spacing: 3) {
            Text(title)
                .synthBody(Synth.isFlat ? 14 : 13, weight: .semibold, color: Synth.ice)
            if let detail {
                Text(detail)
                    .synthBody(Synth.isFlat ? 12.5 : 12, color: Synth.steel)
                    .fixedSize(horizontal: false, vertical: true)
            }
        }
    }
}

/// Galerist section title: an optional SF Symbol and the name in mono small caps.
struct SectionTitle: View {
    let title: String
    var systemImage: String?

    private static let size: CGFloat = 12

    var body: some View {
        HStack(spacing: Synth.Space.s) {
            if let systemImage {
                Image(systemName: systemImage)
                    .font(.system(size: Self.size - 0.5, weight: .medium))
                    .frame(width: 16)
                    .foregroundStyle(Synth.teal)
                    .accessibilityHidden(true)
            }
            Text(title)
                .font(SynthFont.font(.mono, size: Self.size, weight: .semibold))
                .tracking(0.16 * Self.size)
                .textCase(.uppercase)
        }
        .foregroundStyle(Synth.ice)
    }
}

extension Synth {
    /// The line between rows inside a panel: faint in Miami, a full hairline in Galerist.
    static var rowDivider: Color { isFlat ? gridLine : gridLine.opacity(0.6) }
    /// Left and right inset of a row inside a panel (as in `SettingRow`).
    static var rowInset: CGFloat { isFlat ? 18 : 14 }
}

// MARK: - Key cap

/// A keyboard key. States: assigned, empty ("+"), recording, conflict.
/// Miami: neon outline. Galerist: a white key with a 1 px ink border and
/// mono text; an empty slot is a hairline box with "+"; recording is an ink
/// key with the live dot.
struct KeyCap: View {
    enum State {
        case assigned
        case empty
        case recording
        case conflict
    }

    let text: String
    var state: State = .assigned
    var minWidth: CGFloat = 36

    var body: some View {
        if Synth.isFlat {
            flat
        } else {
            neon
        }
    }

    private var flat: some View {
        let foreground: Color = switch state {
        case .assigned: Synth.teal
        case .conflict: Synth.ice
        case .empty: Synth.steel
        case .recording: Synth.onMagenta
        }
        let border: Color = switch state {
        case .assigned: Synth.teal
        case .empty: Synth.outline
        case .recording: Synth.magenta
        case .conflict: Synth.amber
        }
        return HStack(spacing: 5) {
            if state == .conflict {
                Image(systemName: "exclamationmark.triangle.fill")
                    .font(.system(size: 9, weight: .bold))
                    .foregroundStyle(Synth.amber)
            }
            if state == .recording {
                Circle().fill(Synth.live).frame(width: 6, height: 6).accessibilityHidden(true)
            }
            Text(state == .empty ? "+" : text)
                .font(SynthFont.font(.mono, size: state == .recording ? 11 : (state == .empty ? 14 : 13),
                                     weight: state == .empty ? .regular : .medium))
                .tracking(state == .recording ? 1 : 0)
                .lineLimit(1)
                .minimumScaleFactor(0.7)
        }
        .foregroundStyle(foreground)
        .padding(.horizontal, 8)
        .frame(minWidth: minWidth, minHeight: 28)
        .background(Rectangle().fill(state == .recording ? Synth.magenta : (state == .empty ? Color.clear : Synth.panel)))
        .overlay(Rectangle().strokeBorder(border, lineWidth: 1))
        .contentShape(Rectangle())
    }

    @ViewBuilder
    private var neon: some View {
        let color: Color = switch state {
        case .assigned: Synth.teal
        case .empty: Synth.steel
        case .recording: Synth.magenta
        case .conflict: Synth.amber
        }
        HStack(spacing: 4) {
            if state == .conflict {
                Image(systemName: "exclamationmark.triangle.fill").font(.system(size: 9, weight: .bold))
            }
            Text(state == .empty ? "+" : text)
                .font(SynthFont.font(.mono, size: state == .recording ? 11 : 14, weight: .bold))
                .lineLimit(1)
                .minimumScaleFactor(0.7)
        }
        .foregroundStyle(color)
        .padding(.horizontal, 8)
        .frame(minWidth: minWidth, minHeight: 28)
        .background(
            RoundedRectangle(cornerRadius: Synth.Radius.control)
                .fill(state == .recording ? Synth.magenta.opacity(0.12) : Color.clear)
        )
        .overlay(
            RoundedRectangle(cornerRadius: Synth.Radius.control)
                .strokeBorder(color, style: StrokeStyle(lineWidth: 1, dash: state == .empty ? [3, 3] : []))
        )
        // The clear fill is not hit-testable on its own: without this, only
        // the letters and the border answer a click.
        .contentShape(RoundedRectangle(cornerRadius: Synth.Radius.control))
        .shadow(color: state == .empty ? .clear : color.opacity(0.35), radius: 6)
    }
}

// MARK: - Buttons

/// Primary (magenta fill), secondary (teal outline), destructive (error outline),
/// and quiet (borderless steel text).
struct NeonButtonStyle: ButtonStyle {
    enum Kind {
        case primary
        case secondary
        case destructive
        case quiet
    }

    var kind: Kind = .secondary
    @Environment(\.isEnabled) private var isEnabled

    @ViewBuilder
    func makeBody(configuration: Configuration) -> some View {
        if Synth.isFlat {
            flatBody(configuration: configuration)
        } else {
            neonBody(configuration: configuration)
        }
    }

    /// Galerist: square, mono small caps. Primary is ink filled; secondary
    /// and destructive are hairline boxes (the REC red never goes on a
    /// button, the confirmation dialog marks what is destructive); quiet is
    /// text only.
    private func flatBody(configuration: Configuration) -> some View {
        let pressed = configuration.isPressed
        let foreground: Color = {
            guard isEnabled else { return Synth.disabled }
            switch kind {
            case .primary: return Synth.onMagenta
            case .secondary, .destructive: return Synth.ice
            case .quiet: return pressed ? Synth.steel : Synth.body
            }
        }()
        let fill: Color = {
            switch kind {
            case .primary: return isEnabled ? Synth.magenta.opacity(pressed ? 0.82 : 1) : .clear
            case .secondary, .destructive: return pressed && isEnabled ? Synth.gridLine : Synth.panel
            case .quiet: return .clear
            }
        }()
        let border: Color = {
            switch kind {
            case .primary: return isEnabled ? .clear : Synth.gridLine
            case .secondary, .destructive: return isEnabled ? Synth.outline : Synth.gridLine
            case .quiet: return .clear
            }
        }()
        return configuration.label
            .font(SynthFont.font(.mono, size: 11, weight: .medium))
            .tracking(1.6)
            .textCase(.uppercase)
            .foregroundStyle(foreground)
            .padding(.horizontal, kind == .quiet ? 4 : 14)
            .frame(minHeight: kind == .quiet ? 26 : 30)
            .background(Rectangle().fill(fill))
            .overlay(Rectangle().strokeBorder(border, lineWidth: 1))
            .contentShape(Rectangle())
    }

    private func neonBody(configuration: Configuration) -> some View {
        let pressed = configuration.isPressed
        let foreground: Color = {
            guard isEnabled else { return Synth.disabled }
            switch kind {
            case .primary: return Synth.onMagenta
            case .secondary: return pressed ? Synth.tealLight : Synth.teal
            case .destructive: return Synth.error
            case .quiet: return pressed ? Synth.ice : Synth.steel
            }
        }()
        let border: Color = {
            guard isEnabled else { return Synth.gridLine }
            switch kind {
            case .primary: return .clear
            case .secondary: return pressed ? Synth.tealLight : Synth.teal
            case .destructive: return Synth.error
            case .quiet: return .clear
            }
        }()
        return configuration.label
            .font(SynthFont.font(.display, size: 12, weight: .bold))
            .tracking(1)
            .textCase(.uppercase)
            .foregroundStyle(foreground)
            .padding(.horizontal, kind == .quiet ? 6 : 14)
            .frame(minHeight: kind == .quiet ? 26 : 32)
            .background(
                RoundedRectangle(cornerRadius: Synth.Radius.control)
                    .fill(kind == .primary && isEnabled
                          ? (pressed ? Synth.magentaLight : Synth.magenta)
                          : Color.clear)
            )
            .overlay(
                RoundedRectangle(cornerRadius: Synth.Radius.control)
                    .strokeBorder(border, lineWidth: 1)
            )
            .shadow(color: kind == .primary && isEnabled ? Synth.magenta.opacity(0.55) : .clear, radius: 9)
            .contentShape(Rectangle())
    }
}

extension ButtonStyle where Self == NeonButtonStyle {
    static var neonPrimary: NeonButtonStyle { NeonButtonStyle(kind: .primary) }
    static var neonSecondary: NeonButtonStyle { NeonButtonStyle(kind: .secondary) }
    static var neonDestructive: NeonButtonStyle { NeonButtonStyle(kind: .destructive) }
    static var neonQuiet: NeonButtonStyle { NeonButtonStyle(kind: .quiet) }
}

// MARK: - Toggle

/// A switch that spells its state ("ON"/"OFF"); still a native Toggle for
/// VoiceOver and keyboard. The visible title comes from the surrounding row
/// (`SettingRow`); the toggle's own label is used for VoiceOver only.
struct NeonToggleStyle: ToggleStyle {
    @Environment(\.isEnabled) private var isEnabled

    @ViewBuilder
    func makeBody(configuration: Configuration) -> some View {
        if Synth.isFlat {
            flatBody(configuration: configuration)
        } else {
            neonBody(configuration: configuration)
        }
    }

    /// Galerist: a square switch, ink when on, outlined grey when off.
    private func flatBody(configuration: Configuration) -> some View {
        Button {
            configuration.isOn.toggle()
        } label: {
            FlatSwitch(isOn: configuration.isOn, isEnabled: isEnabled)
                .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .accessibilityRepresentation {
            Toggle(isOn: configuration.$isOn) { configuration.label }
        }
    }

    private func neonBody(configuration: Configuration) -> some View {
        let on = configuration.isOn
        let accent = isEnabled ? (on ? Synth.magenta : Synth.steel) : Synth.disabled
        return Button {
            configuration.isOn.toggle()
        } label: {
            HStack(spacing: Synth.Space.s) {
                HStack(spacing: 0) {
                    Text("OFF")
                        .padding(.horizontal, 7)
                        .frame(height: 20)
                        .foregroundStyle(on ? Synth.steel : Synth.abyss)
                        .background(on ? Color.clear : (isEnabled ? Synth.steel : Synth.disabled))
                    Text("ON")
                        .padding(.horizontal, 8)
                        .frame(height: 20)
                        .foregroundStyle(on ? Synth.onMagenta : Synth.steel)
                        .background(on ? (isEnabled ? Synth.magenta : Synth.disabled) : Color.clear)
                }
                .font(SynthFont.font(.mono, size: 10, weight: .bold))
                .tracking(1)
                .clipShape(RoundedRectangle(cornerRadius: 2))
                .padding(2)
                .overlay(
                    RoundedRectangle(cornerRadius: Synth.Radius.control)
                        .strokeBorder(accent, lineWidth: 1)
                )
                .shadow(color: on && isEnabled ? Synth.magenta.opacity(0.4) : .clear, radius: 8)
            }
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .animation(Synth.Motion.quick, value: on)
        .accessibilityRepresentation {
            Toggle(isOn: configuration.$isOn) { configuration.label }
        }
    }
}

extension ToggleStyle where Self == NeonToggleStyle {
    static var neon: NeonToggleStyle { NeonToggleStyle() }
}

/// Galerist's square switch (38 × 22): ink box with the knob on the right
/// when on, a grey outline with the knob on the left when off. Drawing only;
/// the caller supplies the button and accessibility.
struct FlatSwitch: View {
    let isOn: Bool
    var isEnabled = true
    /// Halfway, outlined in ink: on its way (e.g. waiting for a permission).
    var isPending = false

    var body: some View {
        let ink = isEnabled ? Synth.magenta : Synth.disabled
        let off = isEnabled ? Synth.steel : Synth.disabled
        ZStack(alignment: isPending ? .center : (isOn ? .trailing : .leading)) {
            Rectangle().fill(isOn && !isPending ? ink : Color.clear)
            Rectangle().strokeBorder(isOn || isPending ? ink : off, lineWidth: 1)
            Rectangle()
                .fill(isOn && !isPending ? (isEnabled ? Synth.onMagenta : Synth.panel) : (isPending ? ink : off))
                .frame(width: 14, height: 14)
                .padding(.horizontal, 4)
        }
        .frame(width: 38, height: 22)
        .animation(Synth.Motion.quick, value: isOn)
        .animation(Synth.Motion.quick, value: isPending)
    }
}

// MARK: - Fields and pickers

/// Right-aligned monospaced number field on abyss with a 1 pt outline.
struct NeonFieldStyle: TextFieldStyle {
    var width: CGFloat? = nil

    func _body(configuration: TextField<Self._Label>) -> some View {
        configuration
            .textFieldStyle(.plain)
            .font(SynthFont.font(.mono, size: 13, weight: .semibold))
            .foregroundStyle(Synth.ice)
            .multilineTextAlignment(.trailing)
            .padding(.horizontal, 8)
            .frame(width: width, height: 28)
            .background(RoundedRectangle(cornerRadius: Synth.Radius.control).fill(Synth.isFlat ? Synth.panel : Synth.abyss))
            .overlay(
                RoundedRectangle(cornerRadius: Synth.Radius.control)
                    .strokeBorder(Synth.outline, lineWidth: 1)
            )
    }
}

/// A menu button with a teal chevron, used to pick a slider or option.
/// The whole Synth-styled face is a real Button; clicking it pops up a native
/// NSMenu anchored below the button (dark appearance, check mark on the
/// current item), so the hit area and VoiceOver are those of a button.
struct NeonMenu<Item: Hashable>: View {
    struct Group {
        let title: String?
        let items: [Item]
    }

    let title: String
    let accessibilityLabel: String
    let groups: [Group]
    let selection: Item?
    let itemTitle: (Item) -> String
    let onSelect: (Item) -> Void

    @State private var anchor = MenuAnchor()

    init(title: String, accessibilityLabel: String, items: [Item], selection: Item?,
         itemTitle: @escaping (Item) -> String, onSelect: @escaping (Item) -> Void) {
        self.init(title: title, accessibilityLabel: accessibilityLabel, groups: [Group(title: nil, items: items)],
                  selection: selection, itemTitle: itemTitle, onSelect: onSelect)
    }

    init(title: String, accessibilityLabel: String, groups: [Group], selection: Item?,
         itemTitle: @escaping (Item) -> String, onSelect: @escaping (Item) -> Void) {
        self.title = title
        self.accessibilityLabel = accessibilityLabel
        self.groups = groups
        self.selection = selection
        self.itemTitle = itemTitle
        self.onSelect = onSelect
    }

    var body: some View {
        Button {
            anchor.popUp(groups.map { group in
                MenuAnchor.Section(title: group.title, entries: group.items.map { item in
                    MenuAnchor.Entry(title: itemTitle(item), isChecked: item == selection) { onSelect(item) }
                })
            })
        } label: {
            HStack(spacing: Synth.Space.s) {
                Text(title)
                    .synthBody(13, weight: .medium, color: Synth.ice)
                    .lineLimit(1)
                Spacer(minLength: Synth.Space.s)
                Image(systemName: "chevron.down")
                    .font(.system(size: 10, weight: .bold))
                    .foregroundStyle(Synth.isFlat ? Synth.steel : Synth.teal)
            }
            .padding(.horizontal, 10)
            .frame(height: 30)
            .overlay(
                RoundedRectangle(cornerRadius: Synth.Radius.control)
                    .strokeBorder(Synth.outline, lineWidth: 1)
            )
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .background(MenuAnchorView(anchor: anchor))
        .accessibilityLabel(accessibilityLabel)
        .accessibilityValue(title)
        .accessibilityHint("Opens a menu")
    }
}

/// Holds the AppKit view a NeonMenu pops its NSMenu from.
@MainActor
final class MenuAnchor {
    struct Entry {
        let title: String
        let isChecked: Bool
        let action: () -> Void
    }

    struct Section {
        let title: String?
        let entries: [Entry]
    }

    weak var view: NSView?

    func popUp(_ sections: [Section]) {
        guard let view else { return }
        let menu = NSMenu()
        menu.autoenablesItems = false
        // Miami's menus are always dark; Galerist's follow the app.
        menu.appearance = Synth.isFlat ? nil : NSAppearance(named: .darkAqua)
        for (index, section) in sections.enumerated() {
            if index > 0 { menu.addItem(.separator()) }
            if let title = section.title {
                let header = NSMenuItem(title: title, action: nil, keyEquivalent: "")
                header.isEnabled = false
                menu.addItem(header)
            }
            for entry in section.entries {
                let item = ClosureMenuItem(title: entry.title, handler: entry.action)
                item.state = entry.isChecked ? .on : .off
                menu.addItem(item)
            }
        }
        menu.minimumWidth = view.bounds.width
        let below = NSPoint(x: 0, y: view.isFlipped ? view.bounds.height + 4 : -4)
        menu.popUp(positioning: nil, at: below, in: view)
    }
}

/// An NSMenuItem that runs a closure.
final class ClosureMenuItem: NSMenuItem {
    private let handler: () -> Void

    init(title: String, handler: @escaping () -> Void) {
        self.handler = handler
        super.init(title: title, action: #selector(run), keyEquivalent: "")
        target = self
    }

    @available(*, unavailable)
    required init(coder: NSCoder) { fatalError("init(coder:) is not supported") }

    @objc private func run() { handler() }
}

/// The AppKit view behind a button that pops a `MenuAnchor` menu.
struct MenuAnchorView: NSViewRepresentable {
    let anchor: MenuAnchor

    func makeNSView(context: Context) -> NSView {
        let view = NSView()
        anchor.view = view
        return view
    }

    func updateNSView(_ nsView: NSView, context: Context) {
        anchor.view = nsView
    }
}

/// A slider with the native control (accessible) tinted teal and a monospaced read-out.
struct NeonSlider: View {
    @Binding var value: Double
    let range: ClosedRange<Double>
    var step: Double?
    let readout: String
    var accessibilityLabel: String
    var lowLabel: String?
    var highLabel: String?

    private static let trackWidth: CGFloat = 220

    var body: some View {
        VStack(alignment: .leading, spacing: 2) {
            HStack(spacing: Synth.Space.m) {
                // Continuous control (teal filled track, no tick hatching);
                // the value is rounded to the step instead.
                Slider(value: Binding(
                    get: { value },
                    set: { newValue in
                        if let step, step > 0 {
                            value = min(range.upperBound, max(range.lowerBound, (newValue / step).rounded() * step))
                        } else {
                            value = newValue
                        }
                    }
                ), in: range)
                .tint(Synth.teal)
                .controlSize(.small)
                .frame(width: Self.trackWidth)
                .accessibilityLabel(accessibilityLabel)
                .accessibilityValue(readout)
                Text(readout)
                    .font(SynthFont.font(.mono, size: 13, weight: .semibold))
                    .foregroundStyle(Synth.ice)
                    .monospacedDigit()
                    .frame(minWidth: 60, alignment: .trailing)
            }
            if lowLabel != nil || highLabel != nil {
                HStack {
                    Text(lowLabel ?? "").synthLabel(9)
                    Spacer()
                    Text(highLabel ?? "").synthLabel(9)
                }
                .frame(width: Self.trackWidth)
                .accessibilityHidden(true)
            }
        }
    }
}

// MARK: - Status

/// Outlined chip that always spells its state; glow is decoration only.
struct StatusPill: View {
    enum Tone {
        case good
        case waiting
        case off
    }

    let text: String
    let systemImage: String
    var tone: Tone

    var body: some View {
        if Synth.isFlat {
            flat
        } else {
            neon
        }
    }

    /// Galerist: mono small caps in a square box; ink for a good state, grey
    /// for waiting or off (the REC red is kept for what is live).
    private var flat: some View {
        let strong = tone == .good
        return Label(text, systemImage: systemImage)
            .labelStyle(PillLabelStyle())
            .font(SynthFont.font(.mono, size: 10.5, weight: .medium))
            .tracking(1.6)
            .textCase(.uppercase)
            .foregroundStyle(strong ? Synth.ice : Synth.steel)
            .padding(.horizontal, 10)
            .frame(height: 28)
            .overlay(Rectangle().strokeBorder(strong ? Synth.ice : Synth.outline, lineWidth: 1))
    }

    private var neon: some View {
        let color: Color = switch tone {
        case .good: Synth.teal
        case .waiting: Synth.amber
        case .off: Synth.steel
        }
        return Label(text, systemImage: systemImage)
            .labelStyle(PillLabelStyle())
            .font(SynthFont.font(.mono, size: 11, weight: .semibold))
            .tracking(1)
            .textCase(.uppercase)
            .foregroundStyle(color)
            .padding(.horizontal, 10)
            .frame(height: 28)
            .overlay(
                RoundedRectangle(cornerRadius: Synth.Radius.control)
                    .strokeBorder(color, lineWidth: 1)
            )
            .shadow(color: tone == .off ? .clear : color.opacity(0.3), radius: 6)
    }
}

private struct PillLabelStyle: LabelStyle {
    func makeBody(configuration: Configuration) -> some View {
        HStack(spacing: 6) {
            configuration.icon.font(.system(size: 10, weight: .heavy))
            configuration.title
        }
    }
}

/// Big monospaced value with its unit, as in the overlay and summaries.
struct ValueReadout: View {
    let value: String
    var unit: String?
    var size: CGFloat = 34
    var accent: Color = Synth.teal

    var body: some View {
        HStack(alignment: .firstTextBaseline, spacing: 4) {
            Text(value)
                .font(SynthFont.font(.mono, size: size, weight: Synth.isFlat ? .semibold : .bold))
                .monospacedDigit()
                .foregroundStyle(Synth.isFlat ? Synth.ice : Color.white)
                .shadow(color: Synth.isFlat ? .clear : accent.opacity(0.5), radius: 8)
            if let unit, !unit.isEmpty {
                Text(unit)
                    .font(SynthFont.font(.mono, size: size * 0.44, weight: .semibold))
                    .foregroundStyle(accent)
            }
        }
        .accessibilityElement(children: .combine)
    }
}

// MARK: - Window chrome

/// Makes the hosting window full-size content with a transparent title bar,
/// so the Synth top bar sits beside the traffic lights.
struct SynthWindowChrome: NSViewRepresentable {
    /// Called once with the hosting window after it is configured.
    var onConfigure: ((NSWindow) -> Void)? = nil

    final class Coordinator {
        weak var configuredWindow: NSWindow?
        var configuredTheme: AppTheme?
    }

    func makeCoordinator() -> Coordinator { Coordinator() }

    func makeNSView(context: Context) -> NSView {
        let view = NSView()
        DispatchQueue.main.async { configure(view.window, coordinator: context.coordinator) }
        return view
    }

    func updateNSView(_ nsView: NSView, context: Context) {
        DispatchQueue.main.async { configure(nsView.window, coordinator: context.coordinator) }
    }

    private func configure(_ window: NSWindow?, coordinator: Coordinator) {
        guard let window else { return }
        if coordinator.configuredWindow === window {
            // Same window: only a theme change needs its colours again.
            if coordinator.configuredTheme != Synth.theme { applyTheme(to: window, coordinator: coordinator) }
            return
        }
        coordinator.configuredWindow = window
        window.titlebarAppearsTransparent = true
        window.titleVisibility = .hidden
        window.styleMask.insert(.fullSizeContentView)
        // The SwiftUI toolbar (background hidden) makes the title bar 52 pt
        // tall so the traffic lights sit centred in the header row.
        window.toolbarStyle = .unified
        window.titlebarSeparatorStyle = .none
        // Dragging works from the title bar row; moving by the background
        // would compete with clicks on SwiftUI controls.
        window.isMovableByWindowBackground = false
        window.isOpaque = true
        applyTheme(to: window, coordinator: coordinator)
        // No text field starts focused: keystrokes must never land in a step field by accident.
        window.makeFirstResponder(nil)
        window.initialFirstResponder = nil
        onConfigure?(window)
    }

    /// Miami: always dark on the abyss colour. Galerist: the page colour, and
    /// the window follows the app's light/dark appearance.
    private func applyTheme(to window: NSWindow, coordinator: Coordinator) {
        coordinator.configuredTheme = Synth.theme
        window.backgroundColor = Synth.nsTone(\.abyss)
        window.appearance = ThemeController.windowAppearance
    }
}
