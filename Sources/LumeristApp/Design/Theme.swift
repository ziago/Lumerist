import AppKit
import CoreText
import SwiftUI

/// The look of the app. Every token below resolves through the current theme
/// and the appearance it is drawn in, so views keep writing `Synth.teal`,
/// `Synth.abyss`… and get the right colour for the theme:
///
/// - **Miami** (the original "Synth Grid"): dark abyss background, thin neon
///   outlines, teal for primary/active, magenta for selection and the main
///   action, amber for warnings and focus. Always dark.
/// - **Galerist**: the editorial monochrome of Galerist (galerist.photo):
///   white and light grey, 1 px hairlines, square corners, Outfit and
///   JetBrains Mono, and the REC red only for what is live. Light or dark; the
///   dark side uses Lightroom-like charcoal greys, so the floating panels sit
///   naturally over Lightroom.
///
/// Token names are Miami's; their roles: `abyss` window background, `panel`
/// cards and tables, `gridLine` dividers, `outline` control borders, `teal`
/// primary / active / connected, `magenta` selection fills, `onMagenta` text on
/// them, `amber` attention, `ice`/`body`/`steel` text, `live` the one accent for
/// something happening now (a live link, the value moving).
enum Synth {
    // MARK: Theme

    /// The theme in use. Set by the app at launch and when the user picks
    /// another one; views are rebuilt afterwards (`id` on their roots).
    nonisolated(unsafe) static var theme: AppTheme = .galerist

    /// No glow, gradients, grid or rounded corners.
    static var isFlat: Bool { theme == .galerist }

    // MARK: Colour tokens

    static var abyss: Color { tone(\.abyss) }
    static var night: Color { tone(\.night) }
    static var dusk: Color { tone(\.dusk) }
    static var panel: Color { tone(\.panel) }
    static var panelRaised: Color { tone(\.panelRaised) }
    static var gridLine: Color { tone(\.gridLine) }
    static var outline: Color { tone(\.outline) }
    static var teal: Color { tone(\.teal) }
    static var tealLight: Color { tone(\.tealLight) }
    static var magenta: Color { tone(\.magenta) }
    static var magentaLight: Color { tone(\.magentaLight) }
    static var onMagenta: Color { tone(\.onMagenta) }
    static var amber: Color { tone(\.amber) }
    static var ice: Color { tone(\.ice) }
    static var body: Color { tone(\.body) }
    static var steel: Color { tone(\.steel) }
    static var disabled: Color { tone(\.disabled) }
    static var error: Color { tone(\.error) }
    static var success: Color { tone(\.success) }
    /// The single accent for something live: magenta in Miami, REC red in Galerist.
    static var live: Color { tone(\.live) }
    /// The window's header and section-tab band (Galerist: a step lighter
    /// than the page; Miami draws its gradient instead).
    static var chrome: Color { tone(\.chrome) }
    /// Text and icons on a `teal` fill.
    static var onTeal: Color { tone(\.onTeal) }
    /// A soft tint behind Command Keys on the drawn keyboard (Galerist's rec-soft).
    static var tint: Color { tone(\.tint) }

    /// A colour that follows the theme and the light/dark appearance it is drawn in.
    static func tone(_ key: KeyPath<SynthPalette, UInt32>) -> Color {
        Color(nsColor: nsTone(key))
    }

    static func nsTone(_ key: KeyPath<SynthPalette, UInt32>) -> NSColor {
        NSColor(name: nil) { appearance in
            NSColor(hex: palette(for: appearance)[keyPath: key])
        }
    }

    static func palette(for appearance: NSAppearance) -> SynthPalette {
        switch theme {
        case .miami: .miami
        case .galerist:
            appearance.bestMatch(from: [.darkAqua, .aqua]) == .darkAqua ? .galeristDark : .galeristLight
        }
    }

    // MARK: Space, radius, stroke

    /// 4 pt grid.
    enum Space {
        static let xs: CGFloat = 4
        static let s: CGFloat = 8
        static let m: CGFloat = 12
        static let l: CGFloat = 16
        static let xl: CGFloat = 20
        static let xxl: CGFloat = 24
    }

    /// Miami: radius 4 almost everywhere, 3 for menu rows, 6 for tabs and
    /// tables. Galerist: square corners.
    enum Radius {
        static var control: CGFloat { Synth.isFlat ? 0 : 4 }
        static var row: CGFloat { Synth.isFlat ? 0 : 3 }
        static var container: CGFloat { Synth.isFlat ? 0 : 6 }
    }

    /// Outlines instead of fills.
    static let stroke: CGFloat = 1

    // MARK: Motion

    enum Motion {
        /// Overlay entrance: a 120 ms scan-line wipe (plain fade with Reduce Motion).
        static let overlayIn: TimeInterval = 0.12
        /// Overlay exit fade.
        static let overlayOut: TimeInterval = 0.25
        /// UI state changes (tab highlight, toggles).
        static let quick: Animation = .easeOut(duration: 0.12)
    }

    // MARK: Glow

    /// Neon glow is decoration only (none in Galerist): state is always
    /// spelled out in words or icons.
    static func glow(_ color: Color, radius: CGFloat = 10, opacity: Double = 0.45) -> some ViewModifier {
        GlowModifier(color: color, radius: radius, opacity: Synth.isFlat ? 0 : opacity)
    }
}

/// The available looks.
enum AppTheme: String, CaseIterable, Identifiable, Sendable {
    case galerist
    case miami

    var id: String { rawValue }
    var title: String {
        switch self {
        case .galerist: "Galerist"
        case .miami: "Miami"
        }
    }
}

/// Light or dark, for themes that have both (Galerist). Miami is always dark.
enum ThemeAppearance: String, CaseIterable, Identifiable, Sendable {
    case system
    case light
    case dark

    var id: String { rawValue }
    var title: String {
        switch self {
        case .system: "Like the Mac"
        case .light: "Light"
        case .dark: "Dark"
        }
    }
}

/// Every colour of a theme, as 0xRRGGBB.
struct SynthPalette: Sendable {
    var abyss, night, dusk, panel, panelRaised, gridLine, outline: UInt32
    var teal, tealLight, magenta, magentaLight, onMagenta, amber: UInt32
    var ice, body, steel, disabled, error, success, live: UInt32
    /// Header and tab band of the main window.
    var chrome: UInt32
    var onTeal: UInt32
    var tint: UInt32

    /// The original neon theme. Text colours are ≥ 4.5:1 on `abyss`, `panel` and `panelRaised`.
    static let miami = SynthPalette(
        abyss: 0x070B18, night: 0x0B0F24, dusk: 0x1A0C2A, panel: 0x0D1428, panelRaised: 0x111A33,
        gridLine: 0x1B2A4A, outline: 0x2A3B63,
        teal: 0x2DE2E6, tealLight: 0x9BF6F8, magenta: 0xFF3EA5, magentaLight: 0xFF8FCB, onMagenta: 0x12030B,
        amber: 0xFFB347,
        ice: 0xE6F6FF, body: 0xC3D3EA, steel: 0x8FA6C8, disabled: 0x6B80A3, error: 0xFF6B81, success: 0x5CF2A6,
        live: 0xFF3EA5,
        chrome: 0x070B18,
        onTeal: 0x070B18, tint: 0x1B2A4A
    )

    /// Galerist, light: grey page (#f4f4f4), white surfaces, ink text; a
    /// Lightroom-like blue for keys and secondary actions, the red for
    /// selections, attention and what is live.
    static let galeristLight = SynthPalette(
        abyss: 0xF4F4F4, night: 0xF4F4F4, dusk: 0xF4F4F4, panel: 0xFFFFFF, panelRaised: 0xFAFAFA,
        gridLine: 0xE7E7E7, outline: 0xDCDCDC,
        teal: 0x1A66D9, tealLight: 0x1557BA, magenta: 0xD0141F, magentaLight: 0xD0141F, onMagenta: 0xFFFFFF,
        // A shade deeper than the REC red: as text it stays ≥ 4.5:1 on the grey page too.
        amber: 0xD0141F,
        ice: 0x0A0A0A, body: 0x1A1A1A, steel: 0x525252, disabled: 0x8A8A8A, error: 0xD0141F, success: 0x0A0A0A,
        live: 0xE11D2A,
        chrome: 0xFAFAFA,
        onTeal: 0xFFFFFF, tint: 0xFCE8EA
    )

    /// Galerist, dark: Lightroom-like charcoal greys, light text, the same blue
    /// (lighter) and red. Also the look of the panels that float over Lightroom.
    static let galeristDark = SynthPalette(
        abyss: 0x161616, night: 0x161616, dusk: 0x161616, panel: 0x1E1E1E, panelRaised: 0x262626,
        gridLine: 0x333333, outline: 0x3D3D3D,
        teal: 0x5B9BFF, tealLight: 0x8AB8FF, magenta: 0xE11D2A, magentaLight: 0xFF5A63, onMagenta: 0xFFFFFF,
        amber: 0xFF5A63,
        ice: 0xF5F5F5, body: 0xE5E5E5, steel: 0xA3A3A3, disabled: 0x6B6B6B, error: 0xFF5A63, success: 0xF5F5F5,
        live: 0xE11D2A,
        chrome: 0x1C1C1C,
        onTeal: 0x0A0A0A, tint: 0x3A1C20
    )
}

private struct GlowModifier: ViewModifier {
    let color: Color
    let radius: CGFloat
    let opacity: Double

    func body(content: Content) -> some View {
        content.shadow(color: color.opacity(opacity), radius: radius)
    }
}

extension Color {
    init(hex: UInt32, opacity: Double = 1) {
        self.init(
            .sRGB,
            red: Double((hex >> 16) & 0xFF) / 255,
            green: Double((hex >> 8) & 0xFF) / 255,
            blue: Double(hex & 0xFF) / 255,
            opacity: opacity
        )
    }
}

extension NSColor {
    convenience init(hex: UInt32, alpha: CGFloat = 1) {
        self.init(
            srgbRed: CGFloat((hex >> 16) & 0xFF) / 255,
            green: CGFloat((hex >> 8) & 0xFF) / 255,
            blue: CGFloat(hex & 0xFF) / 255,
            alpha: alpha
        )
    }
}

// MARK: - Typography

/// Miami: Chakra Petch for uppercase titles, Inter for sentences. Galerist:
/// Outfit for both. JetBrains Mono for keys, values and status read-outs in
/// every theme. All are SIL OFL fonts bundled in Contents/Resources/Fonts; the
/// system fonts are used when they are missing.
enum SynthFont {
    enum Role {
        /// Chakra Petch, uppercase titles 24 / 20 / 15, 1 pt tracking.
        case display
        /// Inter, sentences and labels.
        case text
        /// JetBrains Mono, keys, values, status.
        case mono
    }

    nonisolated(unsafe) private static var didRegister = false
    nonisolated(unsafe) private static var available: Set<Role> = []
    nonisolated(unsafe) private static var hasOutfit = false

    /// Registers the bundled fonts once, on first use.
    static func registerBundledFonts() {
        guard !didRegister else { return }
        didRegister = true
        let candidates = [
            Bundle.main.resourceURL?.appending(path: "Fonts"),
            // `swift run` during development: the repository's Resources folder.
            URL(fileURLWithPath: FileManager.default.currentDirectoryPath).appending(path: "Resources/Fonts"),
        ].compactMap { $0 }
        guard let folder = candidates.first(where: { FileManager.default.fileExists(atPath: $0.path) }),
              let files = try? FileManager.default.contentsOfDirectory(at: folder, includingPropertiesForKeys: nil) else {
            return
        }
        for file in files where file.pathExtension.lowercased() == "ttf" {
            CTFontManagerRegisterFontsForURL(file as CFURL, .process, nil)
        }
        if NSFont(name: "ChakraPetch-Bold", size: 12) != nil { available.insert(.display) }
        if NSFontManager.shared.availableMembers(ofFontFamily: "Inter") != nil { available.insert(.text) }
        if NSFontManager.shared.availableMembers(ofFontFamily: "JetBrains Mono") != nil { available.insert(.mono) }
        hasOutfit = NSFontManager.shared.availableMembers(ofFontFamily: "Outfit") != nil
    }

    /// A font for a role, size and weight. Variable fonts get a `wght` axis value.
    static func font(_ role: Role, size: CGFloat, weight: Font.Weight = .regular) -> Font {
        Font(nsFont(role, size: size, weight: weight))
    }

    static func nsFont(_ role: Role, size: CGFloat, weight: Font.Weight = .regular) -> NSFont {
        registerBundledFonts()
        if Synth.theme == .galerist, role != .mono, hasOutfit,
           let font = variable(family: "Outfit", size: size, weight: role == .display && weight.cssWeight < 600 ? .semibold : weight) {
            return font
        }
        switch role {
        case .display:
            if available.contains(.display) {
                let name: String
                switch weight {
                case .bold, .heavy, .black: name = "ChakraPetch-Bold"
                case .semibold: name = "ChakraPetch-SemiBold"
                default: name = "ChakraPetch-Medium"
                }
                if let font = NSFont(name: name, size: size) { return font }
            }
            return .systemFont(ofSize: size, weight: weight.nsWeight)
        case .text:
            if available.contains(.text), let font = variable(family: "Inter", size: size, weight: weight) {
                return font
            }
            return .systemFont(ofSize: size, weight: weight.nsWeight)
        case .mono:
            if available.contains(.mono), let font = variable(family: "JetBrains Mono", size: size, weight: weight) {
                return font
            }
            return .monospacedSystemFont(ofSize: size, weight: weight.nsWeight)
        }
    }

    private static func variable(family: String, size: CGFloat, weight: Font.Weight) -> NSFont? {
        let wghtAxis = 0x7767_6874 // 'wght'
        let descriptor = NSFontDescriptor(fontAttributes: [
            .family: family,
            .variation: [NSNumber(value: wghtAxis): NSNumber(value: weight.cssWeight)],
        ])
        return NSFont(descriptor: descriptor, size: size)
    }
}

extension Font.Weight {
    var cssWeight: Double {
        switch self {
        case .ultraLight: 200
        case .thin: 100
        case .light: 300
        case .medium: 500
        case .semibold: 600
        case .bold: 700
        case .heavy: 800
        case .black: 900
        default: 400
        }
    }

    var nsWeight: NSFont.Weight {
        switch self {
        case .ultraLight: .ultraLight
        case .thin: .thin
        case .light: .light
        case .medium: .medium
        case .semibold: .semibold
        case .bold: .bold
        case .heavy: .heavy
        case .black: .black
        default: .regular
        }
    }
}

extension View {
    /// A title: uppercase Chakra Petch with 1 pt tracking (Miami), or Outfit
    /// semibold in sentence case, tightly tracked (Galerist).
    func synthTitle(_ size: CGFloat = 24) -> some View {
        font(SynthFont.font(.display, size: size, weight: Synth.isFlat ? .semibold : .bold))
            .tracking(Synth.isFlat ? -0.02 * size : 1)
            .textCase(Synth.isFlat ? nil : .uppercase)
            .foregroundStyle(Synth.ice)
    }

    /// Small uppercase monospaced label (table headers, read-outs); Galerist
    /// spaces it wider, its small-caps signature.
    func synthLabel(_ size: CGFloat = 10, color: Color = Synth.steel) -> some View {
        font(SynthFont.font(.mono, size: size, weight: Synth.isFlat ? .medium : .semibold))
            .tracking(Synth.isFlat ? 0.18 * size : 1.5)
            .textCase(.uppercase)
            .foregroundStyle(color)
    }

    /// Body copy in Inter.
    func synthBody(_ size: CGFloat = 13, weight: Font.Weight = .regular, color: Color = Synth.body) -> some View {
        font(SynthFont.font(.text, size: size, weight: weight))
            .foregroundStyle(color)
    }
}

extension Notification.Name {
    /// Posted on the main thread after `Synth.theme` (or the Galerist light/dark
    /// choice) changes, so windows and floating panels can redraw.
    static let synthThemeDidChange = Notification.Name("org.lumerist.theme-did-change")
}
