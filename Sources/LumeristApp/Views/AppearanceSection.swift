import AppKit
import SwiftUI

/// General → Appearance: the theme (two preview cards) and, for Galerist,
/// light or dark. Both apply at once; the window is rebuilt in the new look.
struct AppearanceSection: View {
    let preferences: AppPreferences

    var body: some View {
        // The look in effect. In the app it always equals `preferences.theme`
        // (setting it applies it); snapshots draw each theme without saving one.
        let _ = preferences.themeKey // observe changes
        let theme = Synth.theme
        let appearance = ThemeController.appearance
        SettingsSection(title: "Appearance", systemImage: "paintpalette") {
            VStack(alignment: .leading, spacing: Synth.Space.m) {
                SettingText(title: "Theme",
                            detail: "The look of this window and of the panels over Lightroom. Your keys and settings stay the same.")
                HStack(spacing: Synth.Space.l) {
                    ForEach(AppTheme.allCases) { option in
                        ThemeCard(theme: option, isSelected: option == theme) {
                            if preferences.theme != option { preferences.theme = option }
                        }
                    }
                }
                .accessibilityElement(children: .contain)
                .accessibilityLabel("Theme")
            }
            .padding(.horizontal, Synth.isFlat ? 18 : 14)
            .padding(.top, Synth.isFlat ? 14 : 12)
            .padding(.bottom, Synth.isFlat ? 18 : 14)
            .frame(maxWidth: .infinity, alignment: .leading)
            .overlay(alignment: .bottom) {
                Rectangle().fill(Synth.rowDivider).frame(height: 1)
            }

            SettingRow(title: "Light or dark",
                       detail: theme == .miami
                           ? "Miami is always dark. Galerist can follow your Mac or stay light or dark."
                           : "Galerist follows your Mac unless you pick one here. Miami is always dark.") {
                SynthSegmented(items: ThemeAppearance.allCases,
                               selection: Binding(get: { appearance },
                                                  set: { value in
                                                      if preferences.themeAppearance != value { preferences.themeAppearance = value }
                                                  }),
                               title: \.title)
                    .fixedSize()
                    .disabled(theme == .miami)
                    .accessibilityElement(children: .contain)
                    .accessibilityLabel("Light or dark")
            }
        }
    }
}

/// One selectable theme: a small window preview drawn in that theme's own
/// colours (whatever the current theme), its name and a short read-out.
private struct ThemeCard: View {
    let theme: AppTheme
    let isSelected: Bool
    let select: () -> Void

    var body: some View {
        Button(action: select) {
            VStack(alignment: .leading, spacing: 0) {
                preview
                    .frame(height: 128)
                    .frame(maxWidth: .infinity)
                    .clipped()
                HStack(spacing: Synth.Space.s) {
                    Text(theme.title).synthBody(14, weight: .semibold, color: Synth.ice)
                    if isSelected {
                        Image(systemName: "checkmark.circle.fill")
                            .font(.system(size: 12, weight: .semibold))
                            .foregroundStyle(accent)
                            .accessibilityHidden(true)
                    }
                    Spacer(minLength: Synth.Space.s)
                    Text(theme.readout).synthLabel(9)
                }
                .padding(.horizontal, 12)
                .padding(.vertical, 11)
            }
            .background(Synth.panel)
            .clipShape(RoundedRectangle(cornerRadius: Synth.Radius.container))
            .overlay(
                RoundedRectangle(cornerRadius: Synth.Radius.container)
                    .strokeBorder(isSelected ? accent : Synth.gridLine, lineWidth: isSelected ? 2 : 1)
            )
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .accessibilityLabel("\(theme.title) theme")
        .accessibilityValue(theme.readout)
        .accessibilityAddTraits(isSelected ? [.isSelected] : [])
        .accessibilityHint(isSelected ? "" : "Switches Lumerist to this look")
    }

    /// The selection colour of the theme in use (not of the card).
    private var accent: Color { Synth.isFlat ? Synth.ice : Synth.magenta }

    @ViewBuilder
    private var preview: some View {
        switch theme {
        case .galerist: GaleristPreview()
        case .miami: MiamiPreview()
        }
    }
}

private extension AppTheme {
    var readout: String {
        switch self {
        case .galerist: "Light · dark"
        case .miami: "Neon · dark only"
        }
    }
}

// MARK: - Previews (fixed colours: they show each theme as it is)

private struct GaleristPreview: View {
    var body: some View {
        let ink = Color(hex: 0x0A0A0A)
        let line = Color(hex: 0xE7E7E7)
        VStack(spacing: 0) {
            HStack(spacing: 0) {
                (Text("Lumerist") + Text(".").foregroundStyle(Color(hex: 0xE11D2A)))
                    .font(PreviewFont.outfit(size: 10, weight: .semibold))
                    .foregroundStyle(ink)
                Spacer()
            }
            .padding(.horizontal, 10)
            .frame(height: 24)
            .background(Color(hex: 0xFAFAFA))
            .overlay(alignment: .bottom) { Rectangle().fill(line).frame(height: 1) }
            VStack(alignment: .leading, spacing: 8) {
                Text("Develop")
                    .font(PreviewFont.outfit(size: 16, weight: .semibold))
                    .tracking(-0.3)
                    .foregroundStyle(ink)
                row(key: "Q", filled: false, ink: ink, line: line)
                row(key: "W", filled: true, ink: ink, line: line)
            }
            .padding(12)
            .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
            .background(Color.white)
        }
        .overlay(alignment: .bottom) { Rectangle().fill(line).frame(height: 1) }
        .accessibilityHidden(true)
    }

    private func row(key: String, filled: Bool, ink: Color, line: Color) -> some View {
        HStack(spacing: 6) {
            Text(key)
                .font(SynthFont.font(.mono, size: 9, weight: .medium))
                .foregroundStyle(filled ? Color.white : ink)
                .frame(width: 24, height: 18)
                .background(Rectangle().fill(filled ? ink : Color.white))
                .overlay(Rectangle().strokeBorder(ink, lineWidth: 1))
            Rectangle().fill(line).frame(height: 1)
        }
    }
}

private struct MiamiPreview: View {
    var body: some View {
        let palette = SynthPalette.miami
        let teal = Color(hex: palette.teal)
        let line = Color(hex: palette.outline)
        VStack(spacing: 0) {
            HStack {
                Text("Lumerist")
                    .font(PreviewFont.chakra(size: 10))
                    .tracking(1.5)
                    .textCase(.uppercase)
                    .foregroundStyle(teal)
                Spacer()
            }
            .padding(.horizontal, 10)
            .frame(height: 24)
            .overlay(alignment: .bottom) { Rectangle().fill(line).frame(height: 1) }
            VStack(alignment: .leading, spacing: 8) {
                Text("Develop")
                    .font(PreviewFont.chakra(size: 13))
                    .tracking(1)
                    .textCase(.uppercase)
                    .foregroundStyle(Color(hex: palette.ice))
                row(key: "Q", filled: false, palette: palette)
                row(key: "W", filled: true, palette: palette)
            }
            .padding(12)
            .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
        }
        .background(
            LinearGradient(colors: [Color(hex: palette.abyss), Color(hex: palette.dusk)],
                           startPoint: .top, endPoint: .bottom)
        )
        .accessibilityHidden(true)
    }

    private func row(key: String, filled: Bool, palette: SynthPalette) -> some View {
        let teal = Color(hex: palette.teal)
        let magenta = Color(hex: palette.magenta)
        return HStack(spacing: 6) {
            Text(key)
                .font(SynthFont.font(.mono, size: 9, weight: .bold))
                .foregroundStyle(filled ? Color(hex: palette.onMagenta) : teal)
                .frame(width: 24, height: 18)
                .background(RoundedRectangle(cornerRadius: 3).fill(filled ? magenta : Color.clear))
                .overlay(RoundedRectangle(cornerRadius: 3).strokeBorder(filled ? magenta : teal, lineWidth: 1))
                .shadow(color: (filled ? magenta : teal).opacity(0.5), radius: 4)
            Rectangle().fill(Color(hex: palette.gridLine)).frame(height: 1)
        }
    }
}

/// Each preview keeps its own theme's typeface, whatever the current theme.
private enum PreviewFont {
    static func outfit(size: CGFloat, weight: Font.Weight) -> Font {
        SynthFont.registerBundledFonts()
        let descriptor = NSFontDescriptor(fontAttributes: [
            .family: "Outfit",
            .variation: [NSNumber(value: 0x7767_6874): NSNumber(value: weight.cssWeight)], // 'wght'
        ])
        if let font = NSFont(descriptor: descriptor, size: size), font.familyName == "Outfit" {
            return Font(font)
        }
        return .system(size: size, weight: weight)
    }

    static func chakra(size: CGFloat) -> Font {
        SynthFont.registerBundledFonts()
        if let font = NSFont(name: "ChakraPetch-Bold", size: size) { return Font(font) }
        return .system(size: size, weight: .bold)
    }
}
