import AppKit
import CoreText
import QuartzCore
import SwiftUI
import LumeristCore

/// User preferences for the on-screen Value HUD.
struct OverlaySettings: Codable, Sendable, Equatable {
    /// The band of the screen the HUD is measured from. Set by dragging the
    /// HUD (the nearest band wins), so a spot near the top stays near the top
    /// on a smaller display.
    enum Placement: String, Codable, Sendable, CaseIterable, Identifiable {
        case bottom
        case center
        case top

        var id: String { rawValue }
    }

    /// Ruler: name, value and a scale with numbers. Compact: one slim line.
    enum Style: String, Codable, Sendable, CaseIterable, Identifiable {
        case ruler
        case compact

        var id: String { rawValue }

        /// Also reads the names written by earlier builds; a style that no
        /// longer exists ("arc") comes back as Ruler.
        init(from decoder: Decoder) throws {
            let raw = try decoder.singleValueContainer().decode(String.self)
            switch raw {
            case "standard", "arc": self = .ruler
            case "minimal": self = .compact
            default: self = Style(rawValue: raw) ?? .ruler
            }
        }

        var displayName: String {
            switch self {
            case .ruler: "Ruler"
            case .compact: "Compact"
            }
        }

        var detail: String {
            switch self {
            case .ruler: "Name and value above a numbered scale"
            case .compact: "Name, value and a thin bar on one slim line"
            }
        }
    }

    /// Which display shows the HUD when there is more than one.
    enum Screen: String, Sendable, CaseIterable, Identifiable {
        case lightroom
        case pointer

        var id: String { rawValue }

        var displayName: String {
            switch self {
            case .lightroom: "With Lightroom"
            case .pointer: "With the pointer"
            }
        }
    }

    /// Where the HUD sits: a band of the screen plus an offset in points.
    struct Position: Sendable, Equatable {
        var placement: Placement
        var verticalOffset: Double
        var horizontalOffset: Double
    }

    static let storageKey = "org.lumerist.overlay-settings"
    static let durationRange: ClosedRange<Double> = 0.4...4
    /// Sanity limits only: the HUD is always kept inside the visible screen.
    static let verticalOffsetRange: ClosedRange<Double> = -4_000...4_000
    static let horizontalOffsetRange: ClosedRange<Double> = -8_000...8_000
    static let backgroundOpacityRange: ClosedRange<Double> = 0...1

    var isEnabled = true
    var placement: Placement = .bottom
    /// Points above (positive) or below (negative) the placement's position.
    var verticalOffset: Double = 0
    /// Points right (positive) or left (negative) of the screen's centre.
    var horizontalOffset: Double = 0
    /// Seconds the HUD stays visible after the last change.
    var duration: Double = 1.2
    var style: Style = .ruler
    /// 0…1 opacity of the dark panel behind the read-out. 88 % keeps text
    /// ≥ 4.5:1 over bright skies; lower values let more of the photo through.
    static let defaultBackgroundOpacity = 0.88
    var backgroundOpacity: Double = OverlaySettings.defaultBackgroundOpacity
    /// The HUD stays on screen after a change ("Hide after: Never").
    var keepVisible = false
    /// The display of Lightroom's window (true) or of the pointer (false).
    var followsLightroomWindow = true

    init() {}

    /// Missing keys keep their defaults, so future fields never wipe settings.
    init(from decoder: Decoder) throws {
        let container = try decoder.container(keyedBy: CodingKeys.self)
        isEnabled = try container.decodeIfPresent(Bool.self, forKey: .isEnabled) ?? true
        placement = try container.decodeIfPresent(Placement.self, forKey: .placement) ?? .bottom
        verticalOffset = try container.decodeIfPresent(Double.self, forKey: .verticalOffset) ?? 0
        horizontalOffset = try container.decodeIfPresent(Double.self, forKey: .horizontalOffset) ?? 0
        duration = try container.decodeIfPresent(Double.self, forKey: .duration) ?? 1.2
        style = try container.decodeIfPresent(Style.self, forKey: .style) ?? .ruler
        backgroundOpacity = try container.decodeIfPresent(Double.self, forKey: .backgroundOpacity) ?? OverlaySettings.defaultBackgroundOpacity
        keepVisible = try container.decodeIfPresent(Bool.self, forKey: .keepVisible) ?? false
        followsLightroomWindow = try container.decodeIfPresent(Bool.self, forKey: .followsLightroomWindow) ?? true
    }

    var screen: Screen {
        get { followsLightroomWindow ? .lightroom : .pointer }
        set { followsLightroomWindow = newValue == .lightroom }
    }

    var position: Position {
        get { Position(placement: placement, verticalOffset: verticalOffset, horizontalOffset: horizontalOffset) }
        set {
            placement = newValue.placement
            verticalOffset = newValue.verticalOffset
            horizontalOffset = newValue.horizontalOffset
        }
    }

    var clampedDuration: Double {
        guard duration.isFinite else { return 1.2 }
        return min(max(duration, Self.durationRange.lowerBound), Self.durationRange.upperBound)
    }

    var clampedVerticalOffset: Double {
        guard verticalOffset.isFinite else { return 0 }
        return min(max(verticalOffset, Self.verticalOffsetRange.lowerBound), Self.verticalOffsetRange.upperBound)
    }

    var clampedHorizontalOffset: Double {
        guard horizontalOffset.isFinite else { return 0 }
        return min(max(horizontalOffset, Self.horizontalOffsetRange.lowerBound), Self.horizontalOffsetRange.upperBound)
    }

    var clampedBackgroundOpacity: Double {
        guard backgroundOpacity.isFinite else { return OverlaySettings.defaultBackgroundOpacity }
        return min(max(backgroundOpacity, 0), 1)
    }

    /// Back to the default spot: bottom centre.
    mutating func resetPosition() {
        placement = .bottom
        verticalOffset = 0
        horizontalOffset = 0
    }

    static func load(from defaults: UserDefaults) -> OverlaySettings {
        guard let data = defaults.data(forKey: storageKey),
              let settings = try? JSONDecoder().decode(OverlaySettings.self, from: data) else {
            return OverlaySettings()
        }
        return settings
    }

    func save(to defaults: UserDefaults) {
        if let data = try? JSONEncoder().encode(self) {
            defaults.set(data, forKey: Self.storageKey)
        }
    }
}

/// The Value HUD: a small instrument read-out that shows the slider being
/// adjusted. It never becomes key or main, so Lightroom stays frontmost and
/// the keys keep working.
///
/// "Place the HUD" in the settings puts a sample on screen that the mouse
/// can drag anywhere; each drop is stored as a band of the screen (bottom,
/// centre, top) plus offsets in points.
@MainActor
@Observable
final class ValueHUD {
    /// Room around the read-out for its neon glow.
    private static let glowMargin: CGFloat = 18
    private static let edgeInset: CGFloat = 80
    /// A drop this close to the screen's centre line snaps onto it.
    private static let centreSnap: CGFloat = 12

    enum StepMode: Sendable {
        case normal
        /// The larger step, used with Shift.
        case coarse

        var label: String {
            switch self {
            case .normal: "Normal"
            case .coarse: "Coarse"
            }
        }
    }

    /// True while the sample is on screen to be dragged into place.
    private(set) var isPlacing = false

    @ObservationIgnored private var panel: OverlayPanel?
    @ObservationIgnored private var hostingView: FirstMouseHostingView<OverlayView>?
    @ObservationIgnored private var hideGeneration = 0
    /// The panel shows the preview sample rather than a real adjustment.
    @ObservationIgnored private var showsSample = false
    @ObservationIgnored private var placing: PlacingSession?
    /// What is on screen, so a theme change can draw it again in place.
    @ObservationIgnored private var lastShown: (content: OverlayContent, settings: OverlaySettings)?
    @ObservationIgnored private var themeObserver: NSObjectProtocol?

    private struct PlacingSession {
        var settings: OverlaySettings
        let onPlaced: (OverlaySettings.Position) -> Void
        /// Where the sample is shown: the display under the pointer at the
        /// start (where the settings are), then the one it was dropped on.
        /// Offsets are relative, so they carry over to the HUD's own display.
        var screen: NSScreen?
    }

    init() {
        themeObserver = NotificationCenter.default.addObserver(
            forName: .synthThemeDidChange, object: nil, queue: .main
        ) { [weak self] _ in
            MainActor.assumeIsolated { self?.themeDidChange() }
        }
    }

    /// Redraws a HUD that is on screen in the new theme, where it is, without
    /// changing when it hides.
    private func themeDidChange() {
        guard let panel, panel.isVisible else { return }
        if placing != nil {
            presentPlacing()
            return
        }
        guard let hostingView, let shown = lastShown else { return }
        hostingView.rootView = OverlayView(content: shown.content, style: shown.settings.style,
                                           backgroundOpacity: shown.settings.clampedBackgroundOpacity)
        hostingView.layoutSubtreeIfNeeded()
        let fitting = hostingView.fittingSize
        let size = NSSize(width: ceil(fitting.width), height: ceil(fitting.height))
        panel.setFrame(NSRect(origin: Self.origin(for: size, settings: shown.settings), size: size), display: true)
    }

    /// Shows the result of an adjustment. Does nothing when the HUD is off
    /// or while it is being placed. `local_` parameters get a "Mask ·" badge.
    /// - Parameters:
    ///   - keyLabel: the key that drove the change ("Q"), shown as "[Q]".
    ///   - stepMode: normal or coarse step, shown beside the key.
    func show(
        ack: LightroomAck,
        settings: OverlaySettings,
        keyLabel: String? = nil,
        stepMode: StepMode? = nil
    ) {
        guard settings.isEnabled, !isPlacing else { return }
        var content = OverlayContent(ack: ack)
        content.keyLabel = keyLabel
        content.stepLabel = stepMode?.label
        showsSample = false
        present(content, settings: settings)
    }

    /// Shows a neutral notice such as "Vibrance is not available in a mask".
    func showNotice(_ text: String, title: String? = nil, settings: OverlaySettings) {
        guard settings.isEnabled, !isPlacing else { return }
        var content = OverlayContent(notice: text)
        if let title { content.title = title }
        showsSample = false
        present(content, settings: settings)
    }

    /// Shows a sample adjustment so style and position can be judged while
    /// they change. Works even when the HUD is turned off. While the HUD is
    /// being placed, it restyles the draggable sample instead.
    func preview(settings: OverlaySettings) {
        if var session = placing {
            session.settings = settings
            placing = session
            presentPlacing()
            return
        }
        showsSample = true
        present(Self.sample, settings: settings, forceHide: !settings.isEnabled)
    }

    /// Hides the preview sample; a real read-out stays.
    func hidePreview() {
        guard showsSample else { return }
        hide()
    }

    func hide() {
        if isPlacing { finishPlacing() }
        hideGeneration += 1
        showsSample = false
        panel?.orderOut(nil)
    }

    // MARK: Placing

    /// Puts the sample on screen, draggable, until `endPlacing()` or its own
    /// Done button. Each drop reports the new position through `onPlaced`.
    func beginPlacing(settings: OverlaySettings, onPlaced: @escaping (OverlaySettings.Position) -> Void) {
        placing = PlacingSession(settings: settings, onPlaced: onPlaced, screen: Self.pointerScreen())
        isPlacing = true
        presentPlacing()
    }

    /// Stops placing: the sample shows once more at its new spot, then hides
    /// as it normally would.
    func endPlacing() {
        guard let session = placing else { return }
        finishPlacing()
        preview(settings: session.settings)
    }

    private func finishPlacing() {
        placing = nil
        isPlacing = false
        panel?.ignoresMouseEvents = true
    }

    private func presentPlacing() {
        guard let session = placing else { return }
        let settings = session.settings
        let (panel, hostingView) = makePanelIfNeeded()
        hostingView.rootView = OverlayView(
            content: Self.sample,
            style: settings.style,
            backgroundOpacity: settings.clampedBackgroundOpacity,
            placing: OverlayView.Placing(
                // Deferred: both replace the view that is handling the click.
                onDrop: { [weak self] in Task { @MainActor in self?.placingDropped() } },
                onDone: { [weak self] in Task { @MainActor in self?.endPlacing() } }
            )
        )
        hostingView.layoutSubtreeIfNeeded()
        let fitting = hostingView.fittingSize
        let size = NSSize(width: ceil(fitting.width), height: ceil(fitting.height))
        // The caption hangs under the read-out, which sits exactly where the
        // real HUD appears on that display.
        let caption = OverlayView.placingCaptionHeight
        var origin = Self.origin(for: NSSize(width: size.width, height: size.height - caption), settings: settings,
                                 on: session.screen)
        origin.y -= caption
        panel.ignoresMouseEvents = false
        panel.setFrame(NSRect(origin: origin, size: size), display: true)
        showsSample = true
        hideGeneration += 1 // No fade while placing.
        reveal(panel)
    }

    /// The sample was dropped: store where it is, then settle it where the
    /// real HUD will appear (kept inside the screen).
    private func placingDropped() {
        guard let panel, var session = placing else { return }
        var readout = panel.frame
        readout.origin.y += OverlayView.placingCaptionHeight
        readout.size.height -= OverlayView.placingCaptionHeight
        guard let screen = Self.screen(containing: readout) else { return }
        let position = Self.position(of: readout, on: screen)
        session.settings.position = position
        session.screen = screen
        placing = session
        session.onPlaced(position)
        presentPlacing()
    }

    // MARK: Showing

    private static var sample: OverlayContent {
        var sample = OverlayContent(
            title: "Exposure",
            valueText: "+0.35",
            unit: "EV",
            accent: .teal,
            bar: OverlayContent.Bar(value: 0.35, minimum: -5, maximum: 5, unit: "EV"),
            rangeText: ("-5.00", "+5.00")
        )
        sample.keyLabel = "Q"
        sample.stepLabel = StepMode.normal.label
        return sample
    }

    private func present(_ content: OverlayContent, settings: OverlaySettings, forceHide: Bool = false) {
        let (panel, hostingView) = makePanelIfNeeded()
        lastShown = (content, settings)
        hostingView.rootView = OverlayView(content: content, style: settings.style, backgroundOpacity: settings.clampedBackgroundOpacity)
        hostingView.layoutSubtreeIfNeeded()
        let fitting = hostingView.fittingSize
        let size = NSSize(width: ceil(fitting.width), height: ceil(fitting.height))
        panel.ignoresMouseEvents = true
        panel.setFrame(NSRect(origin: Self.origin(for: size, settings: settings), size: size), display: true)
        reveal(panel)
        if settings.keepVisible && !forceHide {
            hideGeneration += 1 // Cancel any pending fade.
        } else {
            scheduleHide(after: settings.clampedDuration)
        }
    }

    /// Brings the panel up if it is hidden or fading.
    private func reveal(_ panel: OverlayPanel) {
        guard !panel.isVisible || panel.alphaValue < 1 else { return }
        let wasHidden = !panel.isVisible
        if wasHidden { panel.alphaValue = 0 }
        panel.orderFrontRegardless()
        if wasHidden && !NSWorkspace.shared.accessibilityDisplayShouldReduceMotion {
            Self.scanLineWipe(panel.contentView)
            panel.alphaValue = 1
        } else {
            NSAnimationContext.beginGrouping()
            NSAnimationContext.current.duration = Synth.Motion.overlayIn
            panel.animator().alphaValue = 1
            NSAnimationContext.endGrouping()
        }
    }

    /// The read-out is revealed top to bottom in 120 ms, like a scan line.
    private static func scanLineWipe(_ view: NSView?) {
        guard let view else { return }
        view.wantsLayer = true
        guard let layer = view.layer else { return }
        let mask = CALayer()
        mask.backgroundColor = NSColor.black.cgColor
        mask.anchorPoint = CGPoint(x: 0.5, y: 1) // Grow down from the top edge.
        mask.frame = layer.bounds
        layer.mask = mask
        CATransaction.begin()
        CATransaction.setCompletionBlock { layer.mask = nil }
        let animation = CABasicAnimation(keyPath: "transform.scale.y")
        animation.fromValue = 0.02
        animation.toValue = 1
        animation.duration = Synth.Motion.overlayIn
        animation.timingFunction = CAMediaTimingFunction(name: .easeOut)
        mask.add(animation, forKey: "wipe")
        CATransaction.commit()
    }

    private func scheduleHide(after seconds: TimeInterval) {
        hideGeneration += 1
        let generation = hideGeneration
        Task { @MainActor [weak self] in
            try? await Task.sleep(for: .seconds(seconds))
            self?.fadeOut(generation: generation)
        }
    }

    private func fadeOut(generation: Int) {
        guard generation == hideGeneration, let panel else { return }
        NSAnimationContext.beginGrouping()
        NSAnimationContext.current.duration = Synth.Motion.overlayOut
        panel.animator().alphaValue = 0
        NSAnimationContext.endGrouping()
        Task { @MainActor [weak self] in
            try? await Task.sleep(for: .seconds(Synth.Motion.overlayOut + 0.05))
            // A newer show() in the meantime keeps the panel on screen.
            guard let self, generation == self.hideGeneration else { return }
            self.panel?.orderOut(nil)
            self.showsSample = false
        }
    }

    private func makePanelIfNeeded() -> (OverlayPanel, FirstMouseHostingView<OverlayView>) {
        if let panel, let hostingView { return (panel, hostingView) }

        let frame = NSRect(x: 0, y: 0, width: 320, height: 140)
        let panel = OverlayPanel(
            contentRect: frame,
            styleMask: [.borderless, .nonactivatingPanel],
            backing: .buffered,
            defer: true
        )
        panel.level = .statusBar
        panel.isOpaque = false
        panel.backgroundColor = .clear
        panel.hasShadow = false // The SwiftUI glow is drawn inside the transparent margin.
        panel.ignoresMouseEvents = true // Except while it is being placed.
        panel.hidesOnDeactivate = false
        panel.isReleasedWhenClosed = false
        panel.animationBehavior = .none
        panel.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary, .stationary, .ignoresCycle]
        panel.appearance = NSAppearance(named: .darkAqua)

        // Takes the first click, so the sample can be dragged at once while placing.
        let hostingView = FirstMouseHostingView(rootView: OverlayView(content: .empty, style: .ruler,
                                                                      backgroundOpacity: OverlaySettings.defaultBackgroundOpacity))
        hostingView.frame = frame
        hostingView.autoresizingMask = [.width, .height]
        hostingView.wantsLayer = true
        hostingView.layer?.backgroundColor = NSColor.clear.cgColor
        panel.contentView = hostingView

        self.panel = panel
        self.hostingView = hostingView
        return (panel, hostingView)
    }

    // MARK: Position

    /// The display chosen in the settings: Lightroom's, or the pointer's.
    private static func screen(for settings: OverlaySettings) -> NSScreen? {
        (settings.followsLightroomWindow ? lightroomScreen() : nil) ?? pointerScreen()
    }

    /// The bottom edge of a read-out of this height placed in a band.
    private static func bandY(_ placement: OverlaySettings.Placement, height: CGFloat, in visible: NSRect) -> CGFloat {
        switch placement {
        case .bottom: visible.minY + edgeInset - glowMargin
        case .center: visible.midY - height / 2
        case .top: visible.maxY - height - edgeInset + glowMargin
        }
    }

    /// Centred on the display (by default the one chosen in the settings),
    /// in its band, moved by the offsets, kept on screen.
    private static func origin(for size: NSSize, settings: OverlaySettings, on display: NSScreen? = nil) -> NSPoint {
        guard let visible = (display ?? screen(for: settings))?.visibleFrame else { return .zero }
        var x = visible.midX - size.width / 2 + settings.clampedHorizontalOffset
        var y = bandY(settings.placement, height: size.height, in: visible) + settings.clampedVerticalOffset
        x = min(max(x, visible.minX), visible.maxX - size.width)
        y = min(max(y, visible.minY), visible.maxY - size.height)
        return NSPoint(x: x.rounded(), y: y.rounded())
    }

    /// The display that holds the centre of `frame`, else most of it.
    private static func screen(containing frame: NSRect) -> NSScreen? {
        let centre = NSPoint(x: frame.midX, y: frame.midY)
        return NSScreen.screens.first(where: { NSMouseInRect(centre, $0.frame, false) })
            ?? NSScreen.screens.max(by: { $0.frame.intersection(frame).area < $1.frame.intersection(frame).area })
    }

    /// The position that puts a read-out at `frame` on `screen`: the band of
    /// the screen (by thirds) its centre is in, and the offsets from that band.
    private static func position(of frame: NSRect, on screen: NSScreen) -> OverlaySettings.Position {
        let centre = NSPoint(x: frame.midX, y: frame.midY)
        let visible = screen.visibleFrame
        let third = visible.height / 3
        let placement: OverlaySettings.Placement =
            centre.y < visible.minY + third ? .bottom : centre.y > visible.maxY - third ? .top : .center
        var horizontal = frame.midX - visible.midX
        if abs(horizontal) < centreSnap { horizontal = 0 }
        let vertical = frame.minY - bandY(placement, height: frame.height, in: visible)
        return OverlaySettings.Position(placement: placement,
                                        verticalOffset: Double(vertical.rounded()),
                                        horizontalOffset: Double(horizontal.rounded()))
    }

    private static func pointerScreen() -> NSScreen? {
        let pointer = NSEvent.mouseLocation
        return NSScreen.screens.first { NSMouseInRect(pointer, $0.frame, false) }
            ?? NSScreen.main
            ?? NSScreen.screens.first
    }

    /// The monitor that holds most of Lightroom Classic's largest window.
    private static func lightroomScreen() -> NSScreen? {
        let pids = Set(NSRunningApplication.runningApplications(withBundleIdentifier: "com.adobe.LightroomClassicCC7")
            .map(\.processIdentifier))
        guard !pids.isEmpty,
              let windows = CGWindowListCopyWindowInfo([.optionOnScreenOnly, .excludeDesktopElements], kCGNullWindowID)
                as? [[String: Any]] else {
            return nil
        }
        var best: (area: CGFloat, rect: CGRect)?
        for info in windows {
            guard let pid = info[kCGWindowOwnerPID as String] as? pid_t, pids.contains(pid),
                  (info[kCGWindowLayer as String] as? Int) == 0,
                  let boundsInfo = info[kCGWindowBounds as String] as? NSDictionary,
                  let bounds = CGRect(dictionaryRepresentation: boundsInfo) else { continue }
            let area = bounds.width * bounds.height
            if area > (best?.area ?? 0) { best = (area, bounds) }
        }
        guard let quartzRect = best?.rect, let mainHeight = NSScreen.screens.first?.frame.height else { return nil }
        // Quartz window bounds have a top-left origin on the primary display.
        let appKitRect = CGRect(x: quartzRect.minX, y: mainHeight - quartzRect.maxY,
                                width: quartzRect.width, height: quartzRect.height)
        return NSScreen.screens.max { lhs, rhs in
            lhs.frame.intersection(appKitRect).area < rhs.frame.intersection(appKitRect).area
        }
    }
}

private extension CGRect {
    var area: CGFloat { isNull ? 0 : width * height }
}

/// Never takes focus away from Lightroom.
private final class OverlayPanel: NSPanel {
    override var canBecomeKey: Bool { false }
    override var canBecomeMain: Bool { false }
}

/// What the overlay shows, derived from a plug-in acknowledgement.
struct OverlayContent: Equatable {
    enum Accent: Equatable {
        /// Tone sliders.
        case teal
        /// White balance and colour.
        case magenta
        /// Notices.
        case neutral
        /// Rejections.
        case error
    }

    struct Bar: Equatable {
        /// Position of the value inside the slider range, 0…1.
        let fraction: Double
        /// Zero's position when the range spans it, else nil.
        let zero: Double?
        /// Scale marks: big labelled ones on round values (every EV for
        /// exposure), small ones in between.
        let ticks: [Tick]

        struct Tick: Equatable {
            let fraction: Double
            let isMajor: Bool
            let label: String?
        }

        init?(value: Double, minimum: Double, maximum: Double, unit: String? = nil) {
            guard value.isFinite, minimum.isFinite, maximum.isFinite, maximum > minimum else { return nil }
            let scale = Scale(minimum: minimum, maximum: maximum, unit: unit)
            fraction = scale.fraction(of: value)
            zero = (minimum < 0 && maximum > 0) ? scale.fraction(of: 0) : nil
            ticks = scale.ticks()
        }
    }

    /// Maps slider values to 0…1 and chooses the scale marks. Kelvin uses a
    /// logarithmic scale, like Lightroom's white-balance slider.
    struct Scale {
        let minimum: Double
        let maximum: Double
        let unit: String?

        private var isKelvin: Bool { unit == "K" && minimum > 0 }

        func fraction(of value: Double) -> Double {
            let raw: Double
            if isKelvin {
                raw = (log(max(value, minimum)) - log(minimum)) / (log(maximum) - log(minimum))
            } else {
                raw = (value - minimum) / (maximum - minimum)
            }
            return min(max(raw, 0), 1)
        }

        func ticks() -> [Bar.Tick] {
            if isKelvin {
                let majors: [Double] = [2_000, 3_000, 4_000, 5_000, 7_500, 10_000, 15_000, 20_000, 30_000, 50_000]
                let labelled: Set<Double> = [3_000, 5_000, 10_000, 20_000, 50_000]
                return majors.filter { $0 >= minimum && $0 <= maximum }.map {
                    Bar.Tick(fraction: fraction(of: $0), isMajor: labelled.contains($0),
                             label: labelled.contains($0) ? "\(Int($0 / 1_000))k" : nil)
                }
            }
            let span = maximum - minimum
            // Exposure: every whole EV is a big, labelled mark.
            let major: Double
            if unit == "EV" {
                major = span <= 12 ? 1 : 2
            } else {
                major = Self.niceStep(for: span / 8)
            }
            let minor = unit == "EV" ? major / 2 : major / (Self.isFiveLike(major) ? 5 : 2)
            // Label every major mark, or every other one when they would crowd.
            let majorCount = Int((span / major).rounded(.down)) + 1
            let labelStride = unit == "EV" || majorCount <= 9 ? 1 : 2

            var result: [Bar.Tick] = []
            var index = 0
            var value = (minimum / minor).rounded(.up) * minor
            while value <= maximum + minor * 0.001 {
                let isMajor = abs((value / major).rounded() - value / major) < 0.0001
                var label: String?
                if isMajor {
                    let majorIndex = Int((value / major).rounded())
                    if majorIndex % labelStride == 0 { label = Self.label(for: value, unit: unit) }
                }
                result.append(Bar.Tick(fraction: fraction(of: value), isMajor: isMajor, label: label))
                value += minor
                index += 1
                if index > 200 { break }
            }
            return result
        }

        private static func niceStep(for rough: Double) -> Double {
            guard rough > 0, rough.isFinite else { return 1 }
            let exponent = floor(log10(rough))
            let base = pow(10, exponent)
            for multiplier in [1.0, 2.0, 2.5, 5.0, 10.0] where rough <= multiplier * base {
                return multiplier * base
            }
            return 10 * base
        }

        private static func isFiveLike(_ step: Double) -> Bool {
            let leading = step / pow(10, floor(log10(step)))
            return abs(leading - 1) < 0.001 || abs(leading - 5) < 0.001
        }

        private static func label(for value: Double, unit: String?) -> String {
            if abs(value) < 0.0001 { return "0" }
            let whole = abs(value - value.rounded()) < 0.0001
            let number = whole ? String(Int(value.rounded())) : String(format: "%.1f", value)
            return value > 0 ? "+" + number : number.replacingOccurrences(of: "-", with: "−")
        }
    }

    var title: String
    var isMask = false
    var valueText: String?
    var unit: String?
    var detail: String?
    var accent: Accent
    var bar: Bar?
    var rangeText: (low: String, high: String)?
    var keyLabel: String?
    var stepLabel: String?
    var noticeIcon: String?

    init(
        title: String,
        valueText: String?,
        unit: String?,
        accent: Accent,
        bar: Bar? = nil,
        rangeText: (low: String, high: String)? = nil,
        detail: String? = nil
    ) {
        self.title = title
        self.valueText = valueText
        self.unit = unit
        self.accent = accent
        self.bar = bar
        self.rangeText = rangeText
        self.detail = detail
    }

    /// A neutral note, e.g. "Vibrance is not available in a mask".
    init(notice: String) {
        title = "Lumerist"
        detail = notice
        accent = .neutral
        noticeIcon = "info.circle"
    }

    init(ack: LightroomAck) {
        let id = ack.parameter
        isMask = id?.hasPrefix("local_") ?? false
        title = Self.label(for: id, scope: ack.scope) ?? "Lightroom"
        accent = ack.accepted ? Self.accent(for: id ?? "") : .error
        if ack.accepted, let value = ack.value {
            let unit = ack.unit ?? "pt"
            valueText = OverlayFormatting.number(value, unit: unit)
            self.unit = OverlayFormatting.unitSuffix(unit)
            // A note such as "at the Exposure slider limit" stays visible.
            detail = ack.message.flatMap { $0 == "accepted" || $0.isEmpty ? nil : $0 }
            if let minimum = ack.minimum, let maximum = ack.maximum {
                bar = Bar(value: value, minimum: minimum, maximum: maximum, unit: unit)
                rangeText = (OverlayFormatting.rangeNumber(minimum, unit: unit),
                             OverlayFormatting.rangeNumber(maximum, unit: unit))
            }
        } else {
            detail = ack.message ?? (ack.accepted ? "Accepted" : "Rejected by Lightroom")
            noticeIcon = ack.accepted ? "checkmark" : "xmark"
        }
    }

    static let empty = OverlayContent(title: "", valueText: nil, unit: nil, accent: .neutral)

    static func == (lhs: OverlayContent, rhs: OverlayContent) -> Bool {
        lhs.title == rhs.title && lhs.isMask == rhs.isMask && lhs.valueText == rhs.valueText
            && lhs.unit == rhs.unit && lhs.detail == rhs.detail && lhs.accent == rhs.accent
            && lhs.bar == rhs.bar && lhs.rangeText?.low == rhs.rangeText?.low
            && lhs.rangeText?.high == rhs.rangeText?.high && lhs.keyLabel == rhs.keyLabel
            && lhs.stepLabel == rhs.stepLabel && lhs.noticeIcon == rhs.noticeIcon
    }

    /// Catalog label when known; otherwise "local_LuminanceNoise" → "Luminance Noise".
    static func label(for id: String?, scope: ParameterScope? = nil) -> String? {
        guard let id else { return nil }
        let base = id.hasPrefix("local_") ? String(id.dropFirst("local_".count)) : id
        if let descriptor = ParameterCatalog.descriptor(scope: scope ?? .develop, id: id)
            ?? ParameterCatalog.descriptor(scope: .develop, id: id)
            ?? ParameterCatalog.descriptor(scope: .develop, id: base) {
            return descriptor.label
        }
        var words = ""
        for character in base {
            if character.isUppercase, !words.isEmpty, words.last != " " { words.append(" ") }
            words.append(character)
        }
        return words.isEmpty ? nil : words
    }

    /// Magenta frame for white balance and colour, teal for tone.
    static func accent(for id: String) -> Accent {
        let base = id.hasPrefix("local_") ? String(id.dropFirst("local_".count)) : id
        let colourWords = ["Temperature", "Tint", "Vibrance", "Saturation", "Hue", "ColorGrade", "SplitToning", "GrayMixer"]
        return colourWords.contains { base.localizedCaseInsensitiveContains($0) } ? .magenta : .teal
    }
}

enum OverlayFormatting {
    /// EV: sign and two decimals; K: whole kelvin; deg: one decimal; pt: sign
    /// and a whole number, or one decimal when fractional. Uses the user's locale.
    static func number(_ value: Double, unit: String) -> String {
        switch unit {
        case "EV":
            return value.formatted(.number.sign(strategy: .always(includingZero: false)).precision(.fractionLength(2)))
        case "K":
            return value.rounded().formatted(.number.grouping(.never).precision(.fractionLength(0)))
        case "deg", "°", "px":
            return value.formatted(.number.sign(strategy: .always(includingZero: false)).precision(.fractionLength(1)))
        default:
            let isWhole = abs(value - value.rounded()) < 0.05
            return value.formatted(
                .number.sign(strategy: .always(includingZero: false)).precision(.fractionLength(isWhole ? 0 : 1))
            )
        }
    }

    static func rangeNumber(_ value: Double, unit: String) -> String {
        switch unit {
        case "EV": value.formatted(.number.sign(strategy: .always(includingZero: false)).precision(.fractionLength(2)))
        case "K": value.rounded().formatted(.number.grouping(.never).precision(.fractionLength(0)))
        default: value.formatted(.number.precision(.fractionLength(0...1)))
        }
    }

    static func unitSuffix(_ unit: String) -> String? {
        switch unit {
        case "EV", "K", "px", "%": unit
        case "deg": "°"
        default: nil
        }
    }

    /// Kept for callers that want one string.
    static func valueText(_ value: Double, unit: String) -> String {
        [number(value, unit: unit), unitSuffix(unit)].compactMap { $0 }.joined(separator: " ")
    }
}

// MARK: - Views

struct OverlayView: View {
    /// What the sample does while it is being placed.
    struct Placing {
        /// The sample was dragged and let go.
        var onDrop: () -> Void
        var onDone: () -> Void
        /// Off only for snapshots: AppKit views do not render to images.
        var showsDragArea = true
    }

    /// Height of the strip with the Done button under a sample being placed.
    static let placingCaptionHeight: CGFloat = 42

    let content: OverlayContent
    let style: OverlaySettings.Style
    let backgroundOpacity: Double
    var placing: Placing?
    /// The theme it is drawn in; a new one rebuilds every view of the HUD.
    var theme: AppTheme = Synth.theme

    var body: some View {
        VStack(spacing: 0) {
            Group {
                switch (style, theme) {
                case (.ruler, .miami): RulerReadout(content: content, backgroundOpacity: backgroundOpacity)
                case (.compact, .miami): CompactReadout(content: content, backgroundOpacity: backgroundOpacity)
                case (.ruler, .galerist): FlatRulerReadout(content: content, backgroundOpacity: backgroundOpacity)
                case (.compact, .galerist): FlatCompactReadout(content: content, backgroundOpacity: backgroundOpacity)
                }
            }
            .padding(18) // Transparent room for the glow (Miami); same spot in every theme.
            .overlay {
                if let placing {
                    RoundedRectangle(cornerRadius: Synth.Radius.container)
                        .strokeBorder(Synth.magenta, style: StrokeStyle(lineWidth: 1.5, dash: [5, 4]))
                        .padding(7)
                        .accessibilityHidden(true)
                    if placing.showsDragArea {
                        HUDDragArea(onDrop: placing.onDrop)
                            .accessibilityLabel("Sample HUD: drag it where you want the HUD")
                    }
                }
            }
            if let placing {
                PlacingCaption(width: style.readoutWidth, onDone: placing.onDone)
                    .frame(height: Self.placingCaptionHeight - 10)
                    .padding(.bottom, 10)
            }
        }
        .fixedSize()
        .environment(\.colorScheme, .dark)
        .id(theme)
    }
}

private extension OverlaySettings.Style {
    var readoutWidth: CGFloat {
        switch self {
        case .ruler: 290
        case .compact: 240
        }
    }
}

/// Under the sample being placed: what to do, and the way out.
private struct PlacingCaption: View {
    let width: CGFloat
    let onDone: () -> Void

    var body: some View {
        HStack(spacing: Synth.Space.s) {
            Image(systemName: "arrow.up.and.down.and.arrow.left.and.right")
                .font(.system(size: 10, weight: .bold))
                .foregroundStyle(Synth.magentaLight)
                .accessibilityHidden(true)
            Text("Drag the HUD").synthLabel(10, color: Synth.ice)
            Spacer(minLength: Synth.Space.s)
            Button("Done", action: onDone)
                .buttonStyle(.neonPrimary)
                .accessibilityHint("Keeps the HUD where it is now")
        }
        .padding(.leading, 10)
        .frame(width: width)
        .background(RoundedRectangle(cornerRadius: Synth.Radius.control).fill(Synth.abyss.opacity(0.94)))
        .overlay(RoundedRectangle(cornerRadius: Synth.Radius.control).strokeBorder(Synth.magenta, lineWidth: 1))
    }
}

/// Moves the HUD panel with the mouse while it is being placed. It follows
/// the pointer on screen (the panel moves under the view, so local
/// coordinates would stand still) and reports the drop.
private struct HUDDragArea: NSViewRepresentable {
    let onDrop: () -> Void

    func makeNSView(context: Context) -> DragView { DragView() }

    func updateNSView(_ view: DragView, context: Context) {
        view.onDrop = onDrop
    }

    final class DragView: NSView {
        var onDrop: (() -> Void)?
        private var start: (mouse: NSPoint, origin: NSPoint)?

        override func acceptsFirstMouse(for event: NSEvent?) -> Bool { true }

        override func resetCursorRects() {
            addCursorRect(bounds, cursor: .openHand)
        }

        override func mouseDown(with event: NSEvent) {
            guard let window else { return }
            start = (NSEvent.mouseLocation, window.frame.origin)
            NSCursor.closedHand.set()
        }

        override func mouseDragged(with event: NSEvent) {
            guard let window, let start else { return }
            let mouse = NSEvent.mouseLocation
            window.setFrameOrigin(NSPoint(x: (start.origin.x + mouse.x - start.mouse.x).rounded(),
                                          y: (start.origin.y + mouse.y - start.mouse.y).rounded()))
        }

        override func mouseUp(with event: NSEvent) {
            guard start != nil else { return }
            start = nil
            NSCursor.openHand.set()
            onDrop?()
        }
    }
}

private extension OverlayContent {
    var frameColor: Color {
        switch accent {
        case .teal: Synth.teal
        case .magenta: Synth.magenta
        case .neutral: Synth.steel
        case .error: Synth.error
        }
    }

    var nameColor: Color {
        switch accent {
        case .teal: Synth.teal
        case .magenta: Synth.magentaLight
        case .neutral: Synth.ice
        case .error: Synth.error
        }
    }

    /// The cursor uses the opposite neon so it stands out from the frame.
    var cursorColor: Color { accent == .magenta ? Synth.teal : Synth.magenta }

    var displayTitle: String { isMask ? "Mask · \(title)" : title }
}

private struct ReadoutFrame: ViewModifier {
    let content: OverlayContent
    let opacity: Double

    func body(content view: Content) -> some View {
        view
            .background(RoundedRectangle(cornerRadius: Synth.Radius.control).fill(Synth.abyss.opacity(opacity)))
            .overlay(RoundedRectangle(cornerRadius: Synth.Radius.control).strokeBorder(content.frameColor, lineWidth: 1))
            .shadow(color: content.frameColor.opacity(0.45), radius: 11)
    }
}

private struct RulerReadout: View {
    let content: OverlayContent
    let backgroundOpacity: Double

    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            HStack(spacing: 8) {
                if let icon = content.noticeIcon {
                    Image(systemName: icon).font(.system(size: 10, weight: .heavy)).foregroundStyle(content.nameColor)
                }
                Text(content.displayTitle)
                    .synthLabel(11, color: content.nameColor)
                    .lineLimit(1)
                Spacer(minLength: 8)
                if let key = content.keyLabel {
                    Text("[\(key)] \(content.stepLabel ?? "")").synthLabel(11)
                }
            }
            if let value = content.valueText {
                ValueReadout(value: value, unit: content.unit, size: 34,
                             accent: content.accent == .magenta ? Synth.magentaLight : Synth.teal)
            }
            if let detail = content.detail {
                Text(detail)
                    .synthBody(12, color: content.accent == .error ? Synth.error : Synth.body)
                    .lineLimit(2)
                    .fixedSize(horizontal: false, vertical: true)
            }
            if let bar = content.bar {
                TickRuler(bar: bar, cursor: content.cursorColor)
                    .frame(height: 34)
                if let range = content.rangeText, !bar.ticks.contains(where: { $0.label != nil }) {
                    HStack {
                        Text(range.low)
                        Spacer()
                        Text(range.high)
                    }
                    .font(SynthFont.font(.mono, size: 10, weight: .medium))
                    .foregroundStyle(Synth.steel)
                }
            }
        }
        .padding(.horizontal, 14)
        .padding(.vertical, 12)
        .frame(width: OverlaySettings.Style.ruler.readoutWidth, alignment: .leading)
        .modifier(ReadoutFrame(content: content, opacity: max(backgroundOpacity, 0.15)))
    }
}

private struct CompactReadout: View {
    let content: OverlayContent
    let backgroundOpacity: Double

    var body: some View {
        VStack(alignment: .leading, spacing: 6) {
            HStack(alignment: .firstTextBaseline, spacing: 10) {
                Text(content.displayTitle)
                    .synthLabel(11, color: content.nameColor)
                    .lineLimit(1)
                Spacer(minLength: 8)
                if let value = content.valueText {
                    ValueReadout(value: value, unit: content.unit, size: 18,
                                 accent: content.accent == .magenta ? Synth.magentaLight : Synth.teal)
                } else if let detail = content.detail {
                    Text(detail).synthBody(12, color: Synth.body).lineLimit(1)
                }
            }
            if let bar = content.bar {
                ThinBar(bar: bar, color: content.cursorColor)
                    .frame(height: 22)
            }
        }
        .padding(.horizontal, 12)
        .padding(.vertical, 9)
        .frame(width: OverlaySettings.Style.compact.readoutWidth, alignment: .leading)
        .modifier(ReadoutFrame(content: content, opacity: max(backgroundOpacity, 0.15)))
    }
}

/// Scale marks for the read-outs: small ticks, big ticks with numbers on round
/// values (every EV for exposure), a bright zero, and a glowing cursor.
private struct ScaleMarks: View {
    let bar: OverlayContent.Bar
    let cursor: Color
    /// Height of the tick band; labels sit below it.
    let tickHeight: CGFloat
    let labelSize: CGFloat

    /// Room on each side so the first and last numbers are not squeezed.
    static let inset: CGFloat = 10

    var body: some View {
        GeometryReader { geometry in
            let inset = Self.inset
            let width = geometry.size.width - inset * 2
            ZStack(alignment: .topLeading) {
                Canvas { context, size in
                    for tick in bar.ticks {
                        let x = inset + (tick.fraction * width).rounded() + 0.5
                        let height = tick.isMajor ? tickHeight : tickHeight * 0.45
                        let lineWidth: CGFloat = tick.isMajor ? 2 : 1
                        let color = tick.isMajor ? Synth.ice.opacity(0.9) : Synth.steel.opacity(0.6)
                        context.fill(Path(CGRect(x: x - lineWidth / 2, y: tickHeight - height,
                                                 width: lineWidth, height: height)),
                                     with: .color(color))
                    }
                    if let zero = bar.zero {
                        let x = inset + (zero * width).rounded()
                        context.fill(Path(CGRect(x: x - 1.5, y: -3, width: 3, height: tickHeight + 3)),
                                     with: .color(Synth.ice))
                    }
                }
                .frame(height: tickHeight)
                ForEach(Array(bar.ticks.enumerated()), id: \.offset) { _, tick in
                    if let label = tick.label {
                        Text(label)
                            .font(SynthFont.font(.mono, size: labelSize, weight: .bold))
                            .foregroundStyle(tick.label == "0" ? Synth.ice : Synth.body)
                            .fixedSize()
                            .position(x: inset + tick.fraction * width,
                                      y: tickHeight + labelSize * 0.75 + 2)
                    }
                }
                Rectangle()
                    .fill(cursor)
                    .frame(width: 3, height: tickHeight + 8)
                    .shadow(color: cursor, radius: 5)
                    .offset(x: inset + min(max(bar.fraction * width - 1.5, 0), width - 3), y: -4)
            }
        }
        .accessibilityHidden(true)
    }
}

/// Ruler style: tall ticks with numbers.
private struct TickRuler: View {
    let bar: OverlayContent.Bar
    let cursor: Color

    var body: some View {
        ScaleMarks(bar: bar, cursor: cursor, tickHeight: 16, labelSize: 11)
    }
}

/// Compact style: a slim filled bar with small scale marks and numbers.
private struct ThinBar: View {
    let bar: OverlayContent.Bar
    let color: Color

    var body: some View {
        GeometryReader { geometry in
            let inset = ScaleMarks.inset
            let width = geometry.size.width - inset * 2
            let origin = bar.zero ?? 0
            let start = min(origin, bar.fraction) * width
            let end = max(origin, bar.fraction) * width
            ZStack(alignment: .topLeading) {
                ScaleMarks(bar: bar, cursor: color, tickHeight: 8, labelSize: 9)
                Rectangle().fill(Synth.steel.opacity(0.3))
                    .frame(width: width, height: 2)
                    .offset(x: inset, y: 8)
                Rectangle()
                    .fill(color)
                    .frame(width: max(end - start, 2), height: 2)
                    .offset(x: inset + min(start, width - 2), y: 8)
                    .shadow(color: color, radius: 3)
            }
        }
        .accessibilityHidden(true)
    }
}

// MARK: - Galerist

/// Galerist's HUD: charcoal panel at the chosen opacity, a 1 px hairline, no
/// glow. The slider's name and key in mono small caps, the value in Outfit
/// semibold with tabular figures, light grey ticks, a white zero, and the
/// needle in the REC red: the one red on screen, because it is what is live.
private struct FlatReadoutFrame: ViewModifier {
    let content: OverlayContent
    let opacity: Double

    func body(content view: Content) -> some View {
        view
            // The darkest charcoal: the most contrast over a bright photo.
            .background(Rectangle().fill(Synth.abyss.opacity(opacity)))
            .overlay(Rectangle().strokeBorder(content.accent == .error ? Synth.error : Synth.gridLine, lineWidth: 1))
    }
}

private extension OverlayContent {
    /// Galerist: white names; a rejection keeps the error colour.
    var flatNameColor: Color { accent == .error ? Synth.error : Synth.ice }
}

/// Outfit (the Galerist text face) with tabular figures, so the value does
/// not shift sideways while it changes.
private enum TabularFont {
    static func font(size: CGFloat, weight: Font.Weight) -> Font {
        let base = SynthFont.nsFont(.text, size: size, weight: weight)
        let descriptor = base.fontDescriptor.addingAttributes([
            .featureSettings: [[
                NSFontDescriptor.FeatureKey.typeIdentifier: kNumberSpacingType,
                NSFontDescriptor.FeatureKey.selectorIdentifier: kMonospacedNumbersSelector,
            ]],
        ])
        return Font(NSFont(descriptor: descriptor, size: size) ?? base)
    }
}

/// The value and its unit, Galerist style.
private struct FlatValue: View {
    let value: String
    let unit: String?
    let size: CGFloat

    var body: some View {
        HStack(alignment: .firstTextBaseline, spacing: size * 0.16) {
            Text(value)
                .font(TabularFont.font(size: size, weight: .semibold))
                .tracking(-0.02 * size)
                .foregroundStyle(Synth.ice)
            if let unit, !unit.isEmpty {
                Text(unit).panelCaps(max(10, size * 0.3), color: Synth.steel, tracking: 0.1)
            }
        }
        .accessibilityElement(children: .combine)
    }
}

private struct FlatRulerReadout: View {
    let content: OverlayContent
    let backgroundOpacity: Double

    var body: some View {
        VStack(alignment: .leading, spacing: 6) {
            HStack(spacing: 8) {
                if let icon = content.noticeIcon {
                    Image(systemName: icon).font(.system(size: 11, weight: .semibold)).foregroundStyle(content.flatNameColor)
                }
                Text(content.displayTitle)
                    .panelCaps(11, color: content.flatNameColor, weight: .semibold)
                    .lineLimit(1)
                Spacer(minLength: 8)
                if let key = content.keyLabel {
                    Text("[\(key)] \(content.stepLabel ?? "")").panelCaps(10).lineLimit(1)
                }
            }
            if let value = content.valueText {
                FlatValue(value: value, unit: content.unit, size: 38)
            }
            if let detail = content.detail {
                Text(detail)
                    .synthBody(12, color: content.accent == .error ? Synth.error : Synth.body)
                    .lineLimit(2)
                    .fixedSize(horizontal: false, vertical: true)
            }
            if let bar = content.bar {
                FlatScale(bar: bar, kind: .ruler)
                    .frame(height: 32)
                    .padding(.top, 4)
                if let range = content.rangeText, !bar.ticks.contains(where: { $0.label != nil }) {
                    HStack {
                        Text(range.low)
                        Spacer()
                        Text(range.high)
                    }
                    .font(SynthFont.font(.mono, size: 9, weight: .medium))
                    .foregroundStyle(Synth.steel)
                }
            }
        }
        .padding(.horizontal, 16)
        .padding(.top, 13)
        .padding(.bottom, 10)
        .frame(width: OverlaySettings.Style.ruler.readoutWidth, alignment: .leading)
        .modifier(FlatReadoutFrame(content: content, opacity: max(backgroundOpacity, 0.15)))
    }
}

private struct FlatCompactReadout: View {
    let content: OverlayContent
    let backgroundOpacity: Double

    var body: some View {
        VStack(alignment: .leading, spacing: 7) {
            HStack(alignment: .firstTextBaseline, spacing: 10) {
                Text(content.displayTitle)
                    .panelCaps(10, color: content.flatNameColor, weight: .semibold)
                    .lineLimit(1)
                Spacer(minLength: 8)
                if let value = content.valueText {
                    FlatValue(value: value, unit: content.unit, size: 20)
                } else if let detail = content.detail {
                    Text(detail).synthBody(12, color: Synth.body).lineLimit(1)
                }
            }
            if let bar = content.bar {
                FlatScale(bar: bar, kind: .compact)
                    .frame(height: 20)
            }
        }
        .padding(.horizontal, 12)
        .padding(.top, 9)
        .padding(.bottom, 8)
        .frame(width: OverlaySettings.Style.compact.readoutWidth, alignment: .leading)
        .modifier(FlatReadoutFrame(content: content, opacity: max(backgroundOpacity, 0.15)))
    }
}

/// The Galerist scale. Ruler: ticks hang from a hairline, the needle is a
/// 2 pt red line. Compact: a 2 pt track filled in red from zero (or the
/// start) to the value, small ticks below. Numbers in mono either way.
private struct FlatScale: View {
    enum Kind { case ruler, compact }

    let bar: OverlayContent.Bar
    let kind: Kind

    private var zeroHeight: CGFloat { kind == .ruler ? 13 : 7 }
    private var majorHeight: CGFloat { kind == .ruler ? 8 : 5 }
    private var minorHeight: CGFloat { kind == .ruler ? 4 : 3 }
    private var labelSize: CGFloat { kind == .ruler ? 9 : 8 }
    /// Where the ticks start: under the hairline (ruler) or the track (compact).
    private var tickTop: CGFloat { kind == .ruler ? 1 : 3 }

    var body: some View {
        GeometryReader { geometry in
            let inset = ScaleMarks.inset
            let width = (geometry.size.width - inset * 2).rounded(.down)
            let x = { (fraction: Double) in inset + (fraction * width).rounded() }
            ZStack(alignment: .topLeading) {
                Canvas { context, _ in
                    switch kind {
                    case .ruler:
                        context.fill(Path(CGRect(x: inset, y: 0, width: width + 1, height: 1)),
                                     with: .color(Synth.steel.opacity(0.3)))
                    case .compact:
                        context.fill(Path(CGRect(x: inset, y: 0, width: width + 1, height: 2)),
                                     with: .color(Synth.steel.opacity(0.35)))
                        let origin = x(bar.zero ?? 0)
                        let end = x(bar.fraction)
                        context.fill(Path(CGRect(x: min(origin, end), y: 0, width: max(abs(end - origin), 2), height: 2)),
                                     with: .color(Synth.live))
                    }
                    for tick in bar.ticks {
                        let height = tick.isMajor ? majorHeight : minorHeight
                        context.fill(Path(CGRect(x: x(tick.fraction), y: tickTop, width: 1, height: height)),
                                     with: .color(tick.isMajor ? Synth.steel : Synth.disabled))
                    }
                    if let zero = bar.zero {
                        context.fill(Path(CGRect(x: x(zero), y: tickTop, width: 1, height: zeroHeight)),
                                     with: .color(Synth.ice))
                    }
                }
                ForEach(Array(bar.ticks.enumerated()), id: \.offset) { _, tick in
                    if let label = tick.label {
                        Text(label)
                            .font(SynthFont.font(.mono, size: labelSize, weight: .medium))
                            .monospacedDigit()
                            .foregroundStyle(label == "0" ? Synth.ice : Synth.steel)
                            .fixedSize()
                            .position(x: x(tick.fraction) + 0.5,
                                      y: tickTop + zeroHeight + 3 + labelSize * 0.6)
                    }
                }
                if kind == .ruler {
                    // The needle: what is live.
                    Rectangle()
                        .fill(Synth.live)
                        .frame(width: 2, height: zeroHeight + 3)
                        .offset(x: min(max(x(bar.fraction) - 0.5, inset - 0.5), inset + width - 1.5), y: 0)
                }
            }
        }
        .accessibilityHidden(true)
    }
}
