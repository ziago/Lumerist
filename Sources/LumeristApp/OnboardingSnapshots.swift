import AppKit
import SwiftUI
import LumeristCore

/// Development aid: renders the setup guide, the General plug-in, updates and
/// support sections (with "Stay in touch" before and after a sign-up) and the
/// menu bar window to PNG files without opening any window, installing the
/// event tap, connecting to Lightroom or sending anything (nothing is
/// installed or saved either). Run the app binary with
/// `LUMERIST_RENDER_SETUP=<folder>`.
///
/// `<folder>/themes/{miami,galerist-light,galerist-dark}/` also gets every
/// main-window screen at full length (header and tabs drawn above it), the
/// Develop Commands tab, the setup guide, the menu bar window and the
/// "Stay in touch" sheet (editing, error, success, and as opened from About &
/// Support) and the update window (`update-*`) in each look. The stored theme is not changed. Native AppKit
/// controls (stepper, slider, text field) show as ImageRenderer placeholders.
@MainActor
enum OnboardingSnapshots {
    static func render(to directory: URL) {
        try? FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
        let store = CommandStore()
        save(OnboardingView(store: store), name: "setup-guide", to: directory)
        save(StatusMenu(store: store), name: "status-menu", to: directory)
        let sections = VStack(alignment: .leading, spacing: Synth.Space.xl) {
            PluginSection()
            UpdatesSection()
            AboutSupportSection(stayInTouch: notAsked)
        }
        .padding(Synth.Space.xxl)
        .frame(width: 860)
        .background(SynthBackground())
        save(sections, name: "general-support", to: directory)
        save(aboutSection(StayInTouchPrompter(preview: StayInTouchSchedule(timesShown: 1, lastShown: .now,
                                                                           subscribedAt: .now))),
             name: "general-support-subscribed", to: directory)
        renderThemes(to: directory.appending(path: "themes"), store: store)
    }

    /// A schedule that never asked; nothing is read from or saved to the defaults.
    private static var notAsked: StayInTouchPrompter { StayInTouchPrompter(preview: StayInTouchSchedule()) }

    private static func aboutSection(_ prompter: StayInTouchPrompter) -> some View {
        AboutSupportSection(stayInTouch: prompter)
            .padding(Synth.Space.xxl)
            .frame(width: 860)
            .background(SynthBackground())
    }

    /// The "Stay in touch" sheet in each state, as the main window shows it.
    private static func stayInTouchSheets(to folder: URL, scheme: ColorScheme) {
        let states: [(String, StayInTouchModel)] = [
            ("editing", StayInTouchModel(preview: .editing)),
            ("error", StayInTouchModel(preview: .failed(GaleristSubscriptionOutcome.unreachable.message),
                                       email: "name@example.com", dontAskAgain: true)),
            ("sending", StayInTouchModel(preview: .sending, email: "name@example.com")),
            ("success", StayInTouchModel(preview: .subscribed(.now))),
            ("about", StayInTouchModel(preview: .editing, prompt: .about, email: "name@example.com")),
        ]
        for (name, model) in states {
            save(StayInTouchSheet(model: model), name: "stay-in-touch-\(name)", to: folder, scheme: scheme)
        }
    }

    /// Release notes as a CHANGELOG section makes them (the 0.9.2 Highlights).
    private static let sampleNotes = """
        ### Highlights
        - Culling keys for Lightroom's Import window: include, exclude and toggle
          photos with keys you pick.
        - A note when a key you assign is one Lightroom already uses.
        - The same behaviour on every keyboard layout, following Lightroom's own
          language.
        - A private connection to Lightroom: the plug-in answers only Lumerist.
        - From now on, a new version installs with one click.

        ### Added
        - More.
        """

    /// The update window in each state: offered (with and without Highlights,
    /// not installable, not writable), downloading, installing, failed, and
    /// in the new version (with Lightroom still on the old plug-in or not).
    private static func updateWindows(to folder: URL, scheme: ColorScheme) {
        let notes = UpdateHighlights.parse(sampleNotes)
        let notWritable = "Lumerist can't replace itself in “/Applications”: this account may not change it. Download the new version instead."
        let states: [(String, UpdatePromptModel)] = [
            ("offer", UpdatePromptModel(preview: .offer, highlights: notes)),
            ("offer-no-highlights", UpdatePromptModel(preview: .offer, highlights: [])),
            ("offer-not-automatic", UpdatePromptModel(preview: .offer, highlights: notes, blocker: UpdateCopy.notAutomatic)),
            ("offer-not-writable", UpdatePromptModel(preview: .offer, highlights: notes, blocker: notWritable)),
            ("downloading", UpdatePromptModel(preview: .working(.downloading(received: 3_145_728, total: 7_002_867)), highlights: notes)),
            ("installing", UpdatePromptModel(preview: .working(.installing), highlights: notes)),
            ("failed-check", UpdatePromptModel(preview: .failed(UpdateFailure(
                kind: .cantInstall, detail: "The download doesn't match the checksums signed for this release.")), highlights: notes)),
            ("failed-network", UpdatePromptModel(preview: .failed(UpdateFailure(
                kind: .stopped, detail: "The Internet connection appears to be offline.")), highlights: notes)),
            ("installed", UpdatePromptModel(preview: .installed, current: nil, highlights: [])),
            ("installed-plugin-waits", UpdatePromptModel(preview: .installed, current: nil, highlights: [], pluginWaits: true)),
        ]
        for (name, model) in states {
            save(UpdatePromptView(model: model), name: "update-\(name)", to: folder, scheme: scheme)
        }
    }

    /// Every main-window screen, the setup guide and the menu bar window in
    /// each theme. Restores the theme in effect afterwards (nothing is saved).
    private static func renderThemes(to root: URL, store: CommandStore) {
        let saved = (theme: Synth.theme, appearance: ThemeController.appearance)
        defer { ThemeController.apply(saved.theme, appearance: saved.appearance, notify: false) }
        // The module tab to draw lives here, not in the user's defaults.
        let tabs = UserDefaults(suiteName: "lumerist.snapshots") ?? .standard
        let looks: [(name: String, theme: AppTheme, scheme: ColorScheme)] = [
            ("miami", .miami, .dark), ("galerist-light", .galerist, .light), ("galerist-dark", .galerist, .dark),
        ]
        for look in looks {
            ThemeController.apply(look.theme, appearance: look.scheme == .dark ? .dark : .light, notify: false)
            let folder = root.appending(path: look.name)
            try? FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
            tabs.set(ModuleTab.sliders.rawValue, forKey: Navigation.moduleTabKey(.develop))
            tabs.set(ModuleTab.sliders.rawValue, forKey: Navigation.moduleTabKey(.library))
            for section in MainSection.allCases {
                save(MainWindowSnapshot(store: store, section: section, defaults: tabs),
                     name: "main-\(section.rawValue)", to: folder, scheme: look.scheme)
            }
            tabs.set(ModuleTab.commands.rawValue, forKey: Navigation.moduleTabKey(.develop))
            save(MainWindowSnapshot(store: store, section: .develop, defaults: tabs),
                 name: "main-develop-commands", to: folder, scheme: look.scheme)
            save(OnboardingView(store: store), name: "setup-guide", to: folder, scheme: look.scheme)
            save(StatusMenu(store: store), name: "status-menu", to: folder, scheme: look.scheme)
            stayInTouchSheets(to: folder, scheme: look.scheme)
            updateWindows(to: folder, scheme: look.scheme)
            save(aboutSection(notAsked), name: "general-support-stay-in-touch", to: folder, scheme: look.scheme)
        }
    }

    /// The colour scheme the current theme draws in (Galerist "Like the Mac": this Mac's).
    private static var currentScheme: ColorScheme {
        if let forced = ThemeController.preferredColorScheme { return forced }
        return NSApplication.shared.effectiveAppearance.bestMatch(from: [.darkAqua, .aqua]) == .darkAqua ? .dark : .light
    }

    private static func save(_ view: some View, name: String, to directory: URL, scheme: ColorScheme? = nil) {
        let scheme = scheme ?? currentScheme
        let renderer = ImageRenderer(content: view
            .environment(\.colorScheme, scheme)
            .environment(\.isSnapshotRendering, true))
        renderer.scale = 2
        // Theme colours are dynamic NSColors: resolve them in the matching appearance.
        var rendered: NSImage?
        NSAppearance(named: scheme == .dark ? .darkAqua : .aqua)?.performAsCurrentDrawingAppearance {
            rendered = renderer.nsImage
        }
        guard let png = rendered?.pngData else { return }
        try? png.write(to: directory.appending(path: "\(name).png"))
    }
}
