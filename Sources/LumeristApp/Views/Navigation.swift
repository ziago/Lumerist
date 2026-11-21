import SwiftUI
import LumeristCore

/// Top-level sections of the main window.
enum MainSection: String, CaseIterable, Identifiable {
    case develop
    case library
    case mouseTrackpad
    case floatingPanels
    case general

    var id: String { rawValue }

    var title: String {
        switch self {
        case .develop: "Develop"
        case .library: "Library"
        case .mouseTrackpad: "Mouse & Trackpad"
        case .floatingPanels: "Floating panels"
        case .general: "General"
        }
    }

    /// Shown beside the tab title in the Galerist theme.
    var systemImage: String {
        switch self {
        case .develop: "slider.horizontal.3"
        case .library: "square.grid.2x2"
        case .mouseTrackpad: "computermouse"
        case .floatingPanels: "rectangle.on.rectangle"
        case .general: "gearshape"
        }
    }

    /// Module pages left of the divider, settings on the right.
    var isModulePage: Bool { self == .develop || self == .library }

    var scope: ParameterScope? {
        switch self {
        case .develop: .develop
        case .library: .library
        default: nil
        }
    }
}

/// Tabs inside a module page (Develop or Library).
enum ModuleTab: String, CaseIterable {
    case sliders
    case commands
    case keyboard

    func title(for scope: ParameterScope) -> String {
        switch self {
        case .sliders: scope == .develop ? "Sliders" : "Quick Develop"
        case .commands: "Commands"
        case .keyboard: "Keyboard"
        }
    }
}

/// Persisted navigation, writable from any screen (links between pages, the menu bar).
enum Navigation {
    static let sectionKey = "org.lumerist.main-section"
    static func moduleTabKey(_ scope: ParameterScope) -> String { "org.lumerist.module-tab.\(scope.rawValue)" }

    @MainActor
    static func go(to section: MainSection, tab: ModuleTab? = nil) {
        UserDefaults.standard.set(section.rawValue, forKey: sectionKey)
        if let tab, let scope = section.scope {
            UserDefaults.standard.set(tab.rawValue, forKey: moduleTabKey(scope))
        }
    }
}
