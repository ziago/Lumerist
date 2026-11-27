/// What the Mac app learns about one of Lightroom's windows through
/// Accessibility: its kind and size, never its text. The title is known only
/// as "has one" or "has none".
public struct WindowShape: Sendable, Hashable {
    /// `kAXRoleAttribute`: "AXWindow" for real windows.
    public var role: String
    /// `kAXSubroleAttribute`: "AXStandardWindow", "AXDialog", "AXHelpTag"…
    public var subrole: String?
    public var hasTitle: Bool
    public var width: Double
    public var height: Double
    /// It is the application's focused window.
    public var isFocused: Bool

    public init(role: String = "AXWindow", subrole: String?, hasTitle: Bool, width: Double, height: Double,
                isFocused: Bool = false) {
        self.role = role
        self.subrole = subrole
        self.hasTitle = hasTitle
        self.width = width
        self.height = height
        self.isFocused = isFocused
    }

    var area: Double { width * height }
}

/// Recognises Lightroom Classic's Import window on the Mac from window
/// shapes alone, whatever Lightroom's language.
///
/// Seen in Lightroom Classic 16 (27 Nov 2026): the Import window is an
/// untitled window almost as large as the catalog window, and while it is
/// open the catalog window's subrole becomes "AXDialog". For about 2 s after
/// it opens the catalog window keeps the focus, and as it closes the untitled
/// window keeps it a moment longer, so either sign is enough once the large
/// untitled window exists. Export (a titled dialog) and Preferences (a
/// titled standard window) never match.
public enum ImportWindowRule {
    public static let minimumWidthRatio = 0.9
    public static let minimumHeightRatio = 0.8

    public static func isImportWindowOpen(_ windows: [WindowShape]) -> Bool {
        let realWindows = windows.filter { $0.role == "AXWindow" }
        // The catalog window: the largest titled window.
        guard let catalog = realWindows
            .filter({ $0.hasTitle && ($0.subrole == "AXStandardWindow" || $0.subrole == "AXDialog") })
            .max(by: { $0.area < $1.area }),
              catalog.width > 0, catalog.height > 0 else { return false }
        return realWindows.contains { window in
            !window.hasTitle && window.subrole != "AXHelpTag"
                && window.width >= minimumWidthRatio * catalog.width
                && window.height >= minimumHeightRatio * catalog.height
                && (window.isFocused || catalog.subrole == "AXDialog")
        }
    }
}
