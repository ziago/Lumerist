import AppKit
import SwiftUI

extension View {
    /// A tooltip drawn in the current theme, in place of the system's: it
    /// appears after `ThemedTooltip.delay` of hovering, below the view (above
    /// or shifted sideways near a screen edge), and goes away when the
    /// pointer leaves, on a click, a key press or scrolling. VoiceOver reads
    /// the text as the view's hint. nil or empty shows nothing.
    func themedTooltip(_ text: String?) -> some View {
        modifier(ThemedTooltipModifier(text: text ?? ""))
    }
}

enum ThemedTooltip {
    /// How long the pointer rests on a view before its tooltip appears.
    static let delay: Duration = .seconds(1.0)
    /// The text wraps at this width.
    static let maxWidth: CGFloat = 280
    /// Room around the bubble for its shadow or glow, inside the window.
    fileprivate static let margin: CGFloat = 10
    /// Space between the view and the bubble.
    fileprivate static let gap: CGFloat = 4
}

/// The bubble: in Galerist a white (or charcoal) box with a hairline and ink
/// text in its body font, square like its panels; in Miami the dark panel
/// with a teal neon edge, like its floating panels and key caps.
struct TooltipBubble: View {
    let text: String

    var body: some View {
        let font = SynthFont.nsFont(.text, size: 12)
        let padding: CGFloat = Synth.isFlat ? 10 : 9
        // Wrap at the maximum width, or hug a short text.
        let single = (text as NSString).size(withAttributes: [.font: font]).width
        let width = min(ceil(single) + 2, ThemedTooltip.maxWidth - 2 * padding)
        Text(text)
            .font(Font(font))
            .foregroundStyle(Synth.isFlat ? Synth.ice : Synth.body)
            .lineSpacing(2)
            .multilineTextAlignment(.leading)
            .fixedSize(horizontal: false, vertical: true)
            .frame(width: width, alignment: .leading)
            .padding(.horizontal, padding)
            .padding(.vertical, Synth.isFlat ? 8 : 7)
            .background(shape.fill(Synth.isFlat ? Synth.panel : Synth.abyss.opacity(0.97)))
            .overlay(shape.strokeBorder(Synth.isFlat ? Synth.outline : Synth.teal.opacity(0.85), lineWidth: 1))
            .shadow(color: Synth.isFlat ? Color.black.opacity(0.14) : Synth.teal.opacity(0.35),
                    radius: Synth.isFlat ? 6 : 8, y: Synth.isFlat ? 2 : 0)
    }

    private var shape: RoundedRectangle {
        RoundedRectangle(cornerRadius: Synth.Radius.container)
    }
}

// MARK: - Hover

private struct ThemedTooltipModifier: ViewModifier {
    let text: String
    @State private var anchor = TooltipAnchor()
    /// Snapshots draw no AppKit view (it would show as a placeholder).
    @Environment(\.isSnapshotRendering) private var isSnapshotRendering

    func body(content: Content) -> some View {
        content
            .background { if !isSnapshotRendering { TooltipAnchorView(anchor: anchor) } }
            .onHover { inside in
                if inside && !text.isEmpty {
                    TooltipController.shared.schedule(text, for: anchor)
                } else {
                    TooltipController.shared.cancel(anchor)
                }
            }
            .onDisappear { TooltipController.shared.cancel(anchor) }
            .accessibilityHint(text)
    }
}

/// The AppKit view behind a view with a tooltip, to place the bubble.
@MainActor
final class TooltipAnchor {
    weak var view: NSView?
}

private struct TooltipAnchorView: NSViewRepresentable {
    let anchor: TooltipAnchor

    func makeNSView(context: Context) -> NSView {
        let view = PassThroughView()
        anchor.view = view
        return view
    }

    func updateNSView(_ nsView: NSView, context: Context) {
        anchor.view = nsView
    }

    /// Never takes a click from the view it sits behind.
    private final class PassThroughView: NSView {
        override func hitTest(_ point: NSPoint) -> NSView? { nil }
    }
}

// MARK: - Window

/// One tooltip at a time, in a borderless, non-activating child panel of
/// the window it belongs to (`SynthPanel`, like the floating panels): never
/// clipped by a scroll view or table, never takes focus, ignores the mouse.
@MainActor
final class TooltipController {
    static let shared = TooltipController()

    private var anchor: TooltipAnchor?
    private var pending: Task<Void, Never>?
    private var monitor: Any?
    private lazy var panel: SynthPanel = {
        let panel = SynthPanel(contentRect: .zero, styleMask: [.borderless, .nonactivatingPanel],
                               backing: .buffered, defer: true)
        panel.isOpaque = false
        panel.backgroundColor = .clear
        panel.hasShadow = false // the bubble draws its own shadow or glow
        panel.ignoresMouseEvents = true
        // Hidden on deactivation by the observer below: a panel that hides
        // itself would come back by itself when the app is active again.
        panel.hidesOnDeactivate = false
        panel.isReleasedWhenClosed = false
        panel.collectionBehavior = [.transient, .ignoresCycle, .fullScreenAuxiliary]
        panel.contentView = hosting
        return panel
    }()
    private let hosting = NSHostingView(rootView: AnyView(EmptyView()))
    private var resignObserver: NSObjectProtocol?

    private init() {
        resignObserver = NotificationCenter.default.addObserver(
            forName: NSApplication.didResignActiveNotification, object: nil, queue: .main
        ) { [weak self] _ in
            MainActor.assumeIsolated { self?.hide() }
        }
    }

    /// The pointer came onto a view: its tooltip appears after the delay.
    func schedule(_ text: String, for anchor: TooltipAnchor) {
        hide()
        self.anchor = anchor
        installMonitor()
        pending = Task { [weak self] in
            try? await Task.sleep(for: ThemedTooltip.delay)
            guard !Task.isCancelled else { return }
            self?.show(text, for: anchor)
        }
    }

    /// The pointer left the view (or it went away).
    func cancel(_ anchor: TooltipAnchor) {
        guard self.anchor === anchor else { return }
        hide()
    }

    func hide() {
        pending?.cancel()
        pending = nil
        anchor = nil
        if let monitor { NSEvent.removeMonitor(monitor) }
        monitor = nil
        if panel.isVisible {
            panel.parent?.removeChildWindow(panel)
            panel.orderOut(nil)
        }
    }

    /// A click, a key or scrolling ends the tooltip, shown or still waiting.
    private func installMonitor() {
        guard monitor == nil else { return }
        monitor = NSEvent.addLocalMonitorForEvents(
            matching: [.leftMouseDown, .rightMouseDown, .otherMouseDown, .keyDown, .scrollWheel]
        ) { [weak self] event in
            MainActor.assumeIsolated { self?.hide() }
            return event
        }
    }

    private func show(_ text: String, for anchor: TooltipAnchor) {
        // Only for the app in front, and only while the pointer is still there.
        guard NSApp.isActive, let view = anchor.view, let window = view.window, window.isVisible else { return }
        let target = window.convertToScreen(view.convert(view.bounds, to: nil))
        let mouse = NSEvent.mouseLocation
        guard target.insetBy(dx: -2, dy: -2).contains(mouse) else { return }

        panel.appearance = window.effectiveAppearance
        let scheme: ColorScheme = window.effectiveAppearance.bestMatch(from: [.darkAqua, .aqua]) == .darkAqua ? .dark : .light
        hosting.rootView = AnyView(
            TooltipBubble(text: text)
                .padding(ThemedTooltip.margin)
                .environment(\.colorScheme, scheme)
        )
        let size = hosting.fittingSize
        let margin = ThemedTooltip.margin
        let screen = (window.screen ?? NSScreen.main)?.visibleFrame ?? target.insetBy(dx: -1000, dy: -1000)

        // Below the view, starting near the pointer; above it when there is
        // no room below; shifted sideways to stay on the screen.
        var x = min(max(mouse.x - 12, target.minX), target.maxX - 12) - margin
        var y = target.minY - ThemedTooltip.gap - size.height + margin
        if y < screen.minY - margin { y = target.maxY + ThemedTooltip.gap - margin }
        x = min(max(x, screen.minX - margin), screen.maxX - size.width + margin)
        y = min(max(y, screen.minY - margin), screen.maxY - size.height + margin)

        panel.setFrame(NSRect(x: x, y: y, width: size.width, height: size.height), display: true)
        panel.level = window.level
        if panel.parent !== window {
            panel.parent?.removeChildWindow(panel)
            window.addChildWindow(panel, ordered: .above)
        }
        panel.orderFront(nil)
    }
}
