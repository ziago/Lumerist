import AppKit
import SwiftUI

/// Puts the chosen theme into effect: `Synth.theme`, the app-wide light/dark
/// appearance, and `.synthThemeDidChange` for the floating panels.
///
/// SwiftUI roots (main window, setup guide, menu bar window) rebuild
/// themselves with `.id(preferences.themeKey)`: `AppPreferences` is
/// observable, so the change reaches them after `apply` has run.
@MainActor
enum ThemeController {
    /// The Light / Dark choice in effect (Galerist only; Miami is always dark).
    private(set) static var appearance: ThemeAppearance = .system

    /// At launch, before any window: the stored choice, without a notification.
    static func applyStored(_ defaults: UserDefaults = .standard) {
        let stored = AppPreferences.storedTheme(defaults)
        apply(stored.theme, appearance: stored.appearance, notify: false)
    }

    static func apply(_ theme: AppTheme, appearance: ThemeAppearance, notify: Bool = true) {
        Synth.theme = theme
        self.appearance = appearance
        NSApplication.shared.appearance = nsAppearance
        applyDockIcon()
        if notify {
            NotificationCenter.default.post(name: .synthThemeDidChange, object: nil)
        }
    }

    /// The Dock icon while Lumerist runs: the app's own (Galerist) icon, or
    /// the neon one in Miami. Finder and Launchpad always show the app's icon.
    private static func applyDockIcon() {
        switch Synth.theme {
        case .galerist:
            NSApplication.shared.applicationIconImage = nil
        case .miami:
            if let url = Bundle.main.url(forResource: "Lumerist-Miami", withExtension: "icns"),
               let image = NSImage(contentsOf: url) {
                NSApplication.shared.applicationIconImage = image
            }
        }
    }

    /// App-wide: dark for Miami; for Galerist the user's choice, or nil to follow the Mac.
    private static var nsAppearance: NSAppearance? {
        switch Synth.theme {
        case .miami: NSAppearance(named: .darkAqua)
        case .galerist:
            switch appearance {
            case .system: nil
            case .light: NSAppearance(named: .aqua)
            case .dark: NSAppearance(named: .darkAqua)
            }
        }
    }

    /// For a window configured by `SynthWindowChrome`: Miami forces dark;
    /// Galerist windows inherit the app's appearance.
    static var windowAppearance: NSAppearance? {
        Synth.isFlat ? nil : NSAppearance(named: .darkAqua)
    }

    /// For `.preferredColorScheme`: dark in Miami; Galerist's forced choice,
    /// or nil to follow the Mac.
    static var preferredColorScheme: ColorScheme? {
        switch Synth.theme {
        case .miami: .dark
        case .galerist:
            switch appearance {
            case .system: nil
            case .light: .light
            case .dark: .dark
            }
        }
    }
}

extension View {
    /// The theme's colour scheme for a window, sheet or popover root:
    /// always dark in Miami, the user's Light / Dark choice in Galerist.
    func synthColorScheme() -> some View {
        preferredColorScheme(ThemeController.preferredColorScheme)
    }

    /// For roots that can't set the window's scheme (the menu bar window):
    /// forces dark in Miami, otherwise keeps what the window has.
    func synthColorSchemeEnvironment() -> some View {
        modifier(ThemeColorSchemeEnvironment())
    }
}

private struct ThemeColorSchemeEnvironment: ViewModifier {
    @Environment(\.colorScheme) private var current

    func body(content: Content) -> some View {
        content.environment(\.colorScheme, ThemeController.preferredColorScheme ?? current)
    }
}
