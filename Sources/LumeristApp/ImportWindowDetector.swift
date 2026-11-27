import AppKit
import ApplicationServices
import LumeristCore

/// Tells whether Lightroom Classic's Import window is in front, so the Import
/// keys work there and the Develop and Library keys pause. Lightroom's SDK
/// can't tell: with the Import window open its module stays "library".
///
/// While Lightroom is the frontmost app, every 0.25 s, it asks macOS
/// Accessibility (the permission the keyboard monitor already has) about
/// Lightroom's windows: role, subrole, size, and whether the title is empty.
/// A title is turned into "has one" or "has none" at once and never kept;
/// no other text, title or file name is read. Nothing is stored or sent:
/// the debug log gets one line, "Import window opened" or "closed", per
/// change. `ImportWindowRule` decides from the shapes alone.
@MainActor
final class ImportWindowDetector {
    private static let interval: TimeInterval = 0.25
    /// How long Lightroom gets to answer each question. A late answer ends
    /// the round and the last state stands: the event tap runs on this
    /// thread too.
    private static let messagingTimeout: Float = 0.03
    private static let attributes = [
        kAXRoleAttribute, kAXSubroleAttribute, kAXTitleAttribute, kAXSizeAttribute,
    ] as CFArray

    private let onChange: (Bool) -> Void
    private var observer: NSObjectProtocol?
    private var timer: Timer?
    private(set) var isOpen = false

    init(onChange: @escaping (Bool) -> Void) {
        self.onChange = onChange
    }

    /// Looks while Lightroom is frontmost, and only then.
    func start() {
        guard observer == nil else { return }
        observer = NSWorkspace.shared.notificationCenter.addObserver(
            forName: NSWorkspace.didActivateApplicationNotification, object: nil, queue: .main
        ) { [weak self] _ in
            MainActor.assumeIsolated { self?.frontmostAppChanged() }
        }
        frontmostAppChanged()
    }

    func stop() {
        if let observer { NSWorkspace.shared.notificationCenter.removeObserver(observer) }
        observer = nil
        stopTimer()
        report(false)
    }

    // MARK: Polling

    private var frontmostLightroom: NSRunningApplication? {
        let app = NSWorkspace.shared.frontmostApplication
        return app?.bundleIdentifier == LightroomLifecycle.bundleIdentifier ? app : nil
    }

    /// Another app in front: the state stands until Lightroom is back (the
    /// engine lets every event through meanwhile), then it is checked at once.
    private func frontmostAppChanged() {
        guard let lightroom = frontmostLightroom else {
            stopTimer()
            return
        }
        check(lightroom)
        guard timer == nil else { return }
        let timer = Timer(timeInterval: Self.interval, repeats: true) { [weak self] _ in
            MainActor.assumeIsolated { self?.tick() }
        }
        timer.tolerance = 0.05
        RunLoop.main.add(timer, forMode: .common)
        self.timer = timer
    }

    private func tick() {
        guard let lightroom = frontmostLightroom else {
            stopTimer()
            return
        }
        check(lightroom)
    }

    private func stopTimer() {
        timer?.invalidate()
        timer = nil
    }

    private func check(_ lightroom: NSRunningApplication) {
        guard let windows = Self.windowShapes(of: lightroom.processIdentifier) else { return }
        report(ImportWindowRule.isImportWindowOpen(windows))
    }

    private func report(_ open: Bool) {
        guard open != isOpen else { return }
        isOpen = open
        DebugLog.write(open ? "Import window opened" : "Import window closed")
        onChange(open)
    }

    // MARK: Accessibility

    /// Every window's shape, or nil when Lightroom did not answer in time.
    private static func windowShapes(of pid: pid_t) -> [WindowShape]? {
        let app = AXUIElementCreateApplication(pid)
        AXUIElementSetMessagingTimeout(app, messagingTimeout)
        var list: CFTypeRef?
        guard AXUIElementCopyAttributeValue(app, kAXWindowsAttribute as CFString, &list) == .success,
              let windows = list as? [AXUIElement] else { return nil }
        var focusedValue: CFTypeRef?
        var focused: AXUIElement?
        if AXUIElementCopyAttributeValue(app, kAXFocusedWindowAttribute as CFString, &focusedValue) == .success,
           let focusedValue, CFGetTypeID(focusedValue) == AXUIElementGetTypeID() {
            focused = unsafeDowncast(focusedValue, to: AXUIElement.self)
        }
        var shapes: [WindowShape] = []
        for window in windows {
            switch shape(of: window, focused: focused) {
            case .shape(let shape): shapes.append(shape)
            case .gone: continue
            case .late: return nil
            }
        }
        return shapes
    }

    private enum Answer {
        case shape(WindowShape)
        /// The window closed meanwhile, or gave no usable answer.
        case gone
        /// Lightroom is busy: give up this round.
        case late
    }

    /// One question per window for its four attributes.
    private static func shape(of window: AXUIElement, focused: AXUIElement?) -> Answer {
        AXUIElementSetMessagingTimeout(window, messagingTimeout)
        var result: CFArray?
        let error = AXUIElementCopyMultipleAttributeValues(window, attributes, AXCopyMultipleAttributeOptions(rawValue: 0),
                                                           &result)
        if error == .cannotComplete { return .late }
        guard error == .success, let values = result as? [AnyObject], values.count == 4 else { return .gone }
        let size = cgSize(values[3])
        return .shape(WindowShape(
            role: values[0] as? String ?? "",
            subrole: values[1] as? String,
            hasTitle: isNonEmptyString(values[2]),
            width: size.width,
            height: size.height,
            isFocused: focused.map { CFEqual($0, window) } ?? false
        ))
    }

    /// Only the length of the title is looked at.
    private static func isNonEmptyString(_ value: AnyObject) -> Bool {
        guard CFGetTypeID(value) == CFStringGetTypeID() else { return false }
        return CFStringGetLength(unsafeDowncast(value, to: CFString.self)) > 0
    }

    /// A missing or unreadable size counts as 0 × 0, which never matches.
    private static func cgSize(_ value: AnyObject) -> CGSize {
        guard CFGetTypeID(value) == AXValueGetTypeID() else { return .zero }
        let axValue = unsafeDowncast(value, to: AXValue.self)
        var size = CGSize.zero
        guard AXValueGetType(axValue) == .cgSize, AXValueGetValue(axValue, .cgSize, &size) else { return .zero }
        return size
    }
}
