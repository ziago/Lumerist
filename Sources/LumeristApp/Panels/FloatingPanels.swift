import AppKit
import Observation
import SwiftUI
import LumeristCore

/// A floating, non-activating panel: Lightroom stays the active app while it
/// is used. It is dragged by its background and remembers where it was left.
@MainActor
final class SynthPanel: NSPanel {
    /// The Key Reference search field needs keyboard focus; the Palette does not.
    var allowsKey = false
    override var canBecomeKey: Bool { allowsKey }
    override var canBecomeMain: Bool { false }
}

/// Lets the first click in an inactive panel act immediately, and tells
/// its owner when the SwiftUI content changes size.
final class FirstMouseHostingView<Content: View>: NSHostingView<Content> {
    /// Called when the content's ideal size may have changed (a row came or went).
    var onContentSizeChange: (() -> Void)?

    override func acceptsFirstMouse(for event: NSEvent?) -> Bool { true }

    override func invalidateIntrinsicContentSize() {
        super.invalidateIntrinsicContentSize()
        onContentSizeChange?()
    }
}

@MainActor
private final class PanelHost {
    let panel: SynthPanel
    let positionKey: String
    private var moveObserver: NSObjectProtocol?
    private let hosting: FirstMouseHostingView<AnyView>
    /// Builds the panel's SwiftUI root; called again after a theme change.
    private let makeContent: () -> AnyView

    init(positionKey: String, allowsKey: Bool, content makeContent: @escaping () -> AnyView) {
        self.positionKey = positionKey
        self.makeContent = makeContent
        let hosting = FirstMouseHostingView(rootView: AnyView(ThemedPanelRoot(theme: Synth.theme, content: makeContent())))
        self.hosting = hosting
        hosting.sizingOptions = [.intrinsicContentSize]
        let panel = SynthPanel(
            contentRect: NSRect(x: 0, y: 0, width: 320, height: 300),
            styleMask: [.borderless, .nonactivatingPanel],
            backing: .buffered,
            defer: true
        )
        panel.allowsKey = allowsKey
        panel.becomesKeyOnlyIfNeeded = true
        panel.level = .floating
        panel.isOpaque = false
        panel.backgroundColor = .clear
        panel.hasShadow = true
        panel.isMovableByWindowBackground = true
        panel.hidesOnDeactivate = false
        panel.isReleasedWhenClosed = false
        panel.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary]
        panel.appearance = NSAppearance(named: .darkAqua)
        panel.contentView = hosting
        self.panel = panel
        moveObserver = NotificationCenter.default.addObserver(
            forName: NSWindow.didMoveNotification, object: panel, queue: .main
        ) { [weak self] _ in
            MainActor.assumeIsolated { self?.savePosition() }
        }
        hosting.onContentSizeChange = { [weak self] in
            // After the layout pass that reported it, not inside it.
            Task { @MainActor in self?.fitToContent() }
        }
    }

    /// Draws the content again in the current theme (colours, fonts and
    /// corners are read when the views are built), keeping the top edge.
    func rebuild() {
        hosting.rootView = AnyView(ThemedPanelRoot(theme: Synth.theme, content: makeContent()))
        fitToContent()
        panel.invalidateShadow()
    }

    /// Follows the content's size while the panel is up, keeping its top
    /// edge in place (e.g. the Palette's mask hint appearing).
    private func fitToContent() {
        guard panel.isVisible, let content = panel.contentView else { return }
        let size = content.fittingSize
        let frame = panel.frame
        guard size.width > 0, size.height > 0,
              abs(size.height - frame.height) > 0.5 || abs(size.width - frame.width) > 0.5 else { return }
        panel.setFrame(NSRect(x: frame.minX, y: frame.maxY - size.height, width: size.width, height: size.height),
                       display: true)
    }

    var savedOrigin: NSPoint? {
        guard let value = UserDefaults.standard.string(forKey: positionKey) else { return nil }
        let point = NSPointFromString(value)
        // Ignore a position on a monitor that is no longer connected.
        return NSScreen.screens.contains { $0.frame.insetBy(dx: -20, dy: -20).contains(point) } ? point : nil
    }

    func savePosition() {
        guard panel.isVisible else { return }
        UserDefaults.standard.set(NSStringFromPoint(panel.frame.origin), forKey: positionKey)
    }

    func resetPosition() {
        UserDefaults.standard.removeObject(forKey: positionKey)
    }

    /// Shows the panel at its saved place, or at `fallback` (a top-left point).
    func show(fallbackTopLeft fallback: NSPoint) {
        panel.contentView?.layoutSubtreeIfNeeded()
        let size = panel.contentView?.fittingSize ?? panel.frame.size
        var origin = savedOrigin ?? NSPoint(x: fallback.x, y: fallback.y - size.height)
        if let screen = NSScreen.screens.first(where: { $0.frame.contains(NSPoint(x: origin.x + 10, y: origin.y + 10)) }) ?? NSScreen.main {
            let visible = screen.visibleFrame
            origin.x = min(max(origin.x, visible.minX), visible.maxX - size.width)
            origin.y = min(max(origin.y, visible.minY), visible.maxY - size.height)
        }
        panel.setFrame(NSRect(origin: origin, size: size), display: true)
        panel.orderFrontRegardless()
    }

    func hide() {
        panel.orderOut(nil)
    }
}

/// Shows the Palette and the Key Reference when the store asks for them.
@MainActor
final class FloatingPanels {
    static let shared = FloatingPanels()

    private weak var store: CommandStore?
    private var palette: PanelHost?
    private var keyReference: PanelHost?
    private var isPreviewingPalette = false
    private var themeObserver: NSObjectProtocol?

    /// Called once at launch (from the menu bar item) and again harmlessly later.
    func attach(_ store: CommandStore) {
        guard self.store == nil else { return }
        self.store = store
        observe()
        sync()
        // Miami ↔ Galerist: the panels are rebuilt in the new look at once,
        // even while they are on screen.
        themeObserver = NotificationCenter.default.addObserver(
            forName: .synthThemeDidChange, object: nil, queue: .main
        ) { [weak self] _ in
            MainActor.assumeIsolated { self?.rebuildForTheme() }
        }
    }

    private func rebuildForTheme() {
        palette?.rebuild()
        keyReference?.rebuild()
    }

    func previewPalette() {
        guard store != nil else { return }
        isPreviewingPalette = true
        showPalette()
    }

    func closePalette() {
        isPreviewingPalette = false
        store?.closePalette()
        palette?.hide()
    }

    func resetPalettePosition() {
        palette?.resetPosition()
        if palette?.panel.isVisible == true { showPalette() }
    }

    func resetKeyReferencePosition() {
        keyReference?.resetPosition()
        if keyReference?.panel.isVisible == true { showKeyReference() }
    }

    private func observe() {
        guard let store else { return }
        withObservationTracking {
            _ = store.isPaletteOpen
            _ = store.isKeyReferenceVisible
        } onChange: { [weak self] in
            Task { @MainActor in
                self?.sync()
                self?.observe()
            }
        }
    }

    private func sync() {
        guard let store else { return }
        if store.isPaletteOpen || isPreviewingPalette {
            if palette?.panel.isVisible != true { showPalette() }
        } else {
            palette?.hide()
        }
        if store.isKeyReferenceVisible {
            if keyReference?.panel.isVisible != true { showKeyReference() }
        } else {
            keyReference?.hide()
        }
    }

    private func showPalette() {
        guard let store else { return }
        if palette == nil {
            palette = PanelHost(positionKey: "org.lumerist.palette-origin", allowsKey: false) {
                AnyView(ComboKeyRoot(store: store) { PaletteView(store: store) })
            }
        }
        // At the pointer, slightly offset so the cursor sits over the first tools.
        let pointer = NSEvent.mouseLocation
        palette?.show(fallbackTopLeft: NSPoint(x: pointer.x - 40, y: pointer.y + 30))
    }

    private func showKeyReference() {
        guard let store else { return }
        if keyReference == nil {
            keyReference = PanelHost(positionKey: "org.lumerist.key-reference-origin", allowsKey: true) {
                AnyView(ComboKeyRoot(store: store) { KeyReferenceView(store: store) })
            }
        }
        let visible = (NSScreen.main ?? NSScreen.screens.first)?.visibleFrame ?? .zero
        keyReference?.show(fallbackTopLeft: NSPoint(x: visible.maxX - 360, y: visible.maxY - 40))
    }
}

/// The outermost view of a floating panel. Panels are always dark (they sit
/// over Lightroom's dark interface), so the theme tokens resolve to their dark
/// side: Miami's neon, or Galerist's charcoal greys. A new theme gives the
/// content a new identity, so every view is built again in the new look.
private struct ThemedPanelRoot: View {
    let theme: AppTheme
    let content: AnyView

    var body: some View {
        content
            .id(theme)
            .environment(\.colorScheme, .dark)
    }
}

/// The root of a floating panel: hands its views the current Combo Key, so
/// combos read "Tab + K" and follow a change of Combo Key.
private struct ComboKeyRoot<Content: View>: View {
    let store: CommandStore
    @ViewBuilder let content: Content

    var body: some View {
        content.environment(\.comboKey, store.keymap.keymap.systemKeys.comboKey)
    }
}

/// Moves the panel when its title bar is dragged. SwiftUI content does not
/// hand clicks to the window background, so `isMovableByWindowBackground`
/// alone never starts a drag; this view starts it itself.
struct WindowDragArea: NSViewRepresentable {
    func makeNSView(context: Context) -> NSView { DragView() }
    func updateNSView(_ nsView: NSView, context: Context) {}

    final class DragView: NSView {
        override var mouseDownCanMoveWindow: Bool { true }
        override func acceptsFirstMouse(for event: NSEvent?) -> Bool { true }
        override func mouseDown(with event: NSEvent) { window?.performDrag(with: event) }
    }
}

/// Shared frame for floating panels, with a title bar row (drag handle,
/// title, context, trailing buttons).
/// - Miami: abyss fill, rounded corners, a neon outline in the panel's accent.
/// - Galerist: charcoal fill, square corners, a 1 px hairline, mono small
///   caps in the title bar, no glow.
struct PanelChrome<Content: View>: View {
    let title: String
    var subtitle: String?
    var accent: Color = Synth.teal
    let trailing: AnyView?
    @ViewBuilder var content: Content

    var body: some View {
        if Synth.isFlat {
            flat
        } else {
            neon
        }
    }

    private var neon: some View {
        VStack(alignment: .leading, spacing: 0) {
            HStack(spacing: Synth.Space.s) {
                // The labels let clicks through to the drag area behind them;
                // only the trailing buttons keep their own clicks.
                Group {
                    Image(systemName: "line.3.horizontal")
                        .font(.system(size: 10, weight: .bold))
                        .foregroundStyle(Synth.steel)
                        .accessibilityHidden(true)
                    Text(title).synthLabel(11, color: accent)
                    if let subtitle { Text(subtitle).synthLabel(9) }
                }
                .allowsHitTesting(false)
                Spacer(minLength: Synth.Space.s)
                trailing
            }
            .padding(.horizontal, 12)
            .frame(height: 34)
            .background(WindowDragArea().help("Drag to move"))
            .overlay(alignment: .bottom) { Rectangle().fill(Synth.gridLine).frame(height: 1) }
            content
        }
        .background(RoundedRectangle(cornerRadius: Synth.Radius.container).fill(Synth.abyss.opacity(0.96)))
        .overlay(RoundedRectangle(cornerRadius: Synth.Radius.container).strokeBorder(accent, lineWidth: 1))
        .clipShape(RoundedRectangle(cornerRadius: Synth.Radius.container))
        .environment(\.colorScheme, .dark)
    }

    private var flat: some View {
        VStack(alignment: .leading, spacing: 0) {
            HStack(spacing: 10) {
                Group {
                    Image(systemName: "line.3.horizontal")
                        .font(.system(size: 11, weight: .regular))
                        .foregroundStyle(Synth.steel)
                        .accessibilityHidden(true)
                    Text(title).panelCaps(11, color: Synth.ice, weight: .semibold)
                    if let subtitle { Text(subtitle).panelCaps(10).lineLimit(1) }
                }
                .allowsHitTesting(false)
                Spacer(minLength: Synth.Space.s)
                trailing
            }
            .padding(.leading, 14)
            .padding(.trailing, 8)
            .frame(height: 40)
            .background(WindowDragArea().help("Drag to move"))
            .overlay(alignment: .bottom) { Rectangle().fill(Synth.gridLine).frame(height: 1) }
            content
        }
        .background(Rectangle().fill(Synth.panel))
        .overlay(Rectangle().strokeBorder(Synth.gridLine, lineWidth: 1))
        .environment(\.colorScheme, .dark)
    }
}

/// Galerist's floating-panel buttons: no chrome, a raised fill while pressed.
/// (Hover never reaches a panel that does not take focus from Lightroom.)
struct PanelCellButtonStyle: ButtonStyle {
    var fill: Color = .clear

    func makeBody(configuration: Configuration) -> some View {
        configuration.label
            .background(configuration.isPressed ? Synth.panelRaised : fill)
    }
}

extension View {
    /// Galerist's mono small caps for panels and the HUD: JetBrains Mono,
    /// uppercase, tracked 0.18 em (the label signature of the design system).
    func panelCaps(_ size: CGFloat = 10, color: Color = Synth.steel, weight: Font.Weight = .medium,
                   tracking: CGFloat = 0.18) -> some View {
        font(SynthFont.font(.mono, size: size, weight: weight))
            .tracking(tracking * size)
            .textCase(.uppercase)
            .foregroundStyle(color)
    }
}
