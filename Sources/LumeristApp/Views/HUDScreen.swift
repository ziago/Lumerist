import SwiftUI
import LumeristCore

/// Floating panels → Value HUD: on or off, style and opacity, how long it
/// stays, which display, and where on it (dragged into place).
struct HUDScreen: View {
    @Bindable var store: CommandStore

    /// The far end of the "Hide after" slider: the HUD stays on screen.
    private static let neverValue = OverlaySettings.durationRange.upperBound + 0.1

    var body: some View {
        let settings = store.overlaySettings
        let isPlacing = store.overlay.isPlacing
        SettingsSection(
            title: "Value HUD",
            systemImage: "gauge.with.needle",
            footer: "The HUD never takes focus from Lightroom. Colour is only a help: the slider's name and the key are always written out."
        ) {
            SettingRow(title: "Show the value while adjusting",
                       detail: "Every Dial Key or Nudge Key change shows the slider, its new value and where it sits in its range.") {
                Toggle("Show the value while adjusting", isOn: $store.overlaySettings.isEnabled)
                    .labelsHidden()
                    .toggleStyle(.neon)
            }

            styleRow(selected: settings.style)

            SettingRow(title: "Panel opacity", detail: "How much of the photo shows through the dark panel behind the value.") {
                NeonSlider(
                    value: Binding(
                        get: { store.overlaySettings.backgroundOpacity * 100 },
                        set: { store.overlaySettings.backgroundOpacity = ($0 / 100).rounded(toPlaces: 2) }
                    ),
                    range: 0...100,
                    step: 5,
                    readout: "\(Int((settings.clampedBackgroundOpacity * 100).rounded())) %",
                    accessibilityLabel: "HUD panel opacity"
                )
                .onChange(of: store.overlaySettings.backgroundOpacity) { preview() }
            }

            SettingRow(title: "Hide after", detail: "How long the HUD stays after your last change. Never keeps it on screen all the time.") {
                NeonSlider(
                    value: Binding(
                        get: { store.overlaySettings.keepVisible ? Self.neverValue : store.overlaySettings.duration },
                        set: { value in
                            if value >= Self.neverValue - 0.05 {
                                store.overlaySettings.keepVisible = true
                            } else {
                                store.overlaySettings.keepVisible = false
                                store.overlaySettings.duration = value
                            }
                        }
                    ),
                    range: OverlaySettings.durationRange.lowerBound...Self.neverValue,
                    step: 0.1,
                    readout: settings.keepVisible ? "Never" : Self.seconds(settings.duration),
                    accessibilityLabel: "Hide the HUD after",
                    lowLabel: Self.seconds(OverlaySettings.durationRange.lowerBound),
                    highLabel: "Never"
                )
                .onChange(of: store.overlaySettings.keepVisible) { preview() }
                .onChange(of: store.overlaySettings.duration) { preview() }
            }

            SettingRow(title: "Display", detail: "Only matters with two or more displays. With Lightroom: the HUD always appears on the display that shows Lightroom's window. With the pointer: it appears on whichever display the pointer is on.") {
                SynthSegmented(items: OverlaySettings.Screen.allCases,
                               selection: $store.overlaySettings.screen,
                               title: { $0.displayName })
                    .accessibilityElement(children: .contain)
                    .accessibilityLabel("Display for the HUD")
                    .onChange(of: store.overlaySettings.followsLightroomWindow) { preview() }
            }

            SettingRow(
                title: "Position",
                detail: isPlacing
                    ? "Drag the sample anywhere on the screen, then click Done here or under the sample."
                    : "Where the HUD sits on that display. Place the HUD shows a sample you drag to the spot you like; Reset position puts it back at the bottom centre."
            ) {
                HStack(spacing: Synth.Space.s) {
                    Button(isPlacing ? "Done" : "Place the HUD") { togglePlacing() }
                        .buttonStyle(NeonButtonStyle(kind: isPlacing ? .primary : .secondary))
                        .accessibilityHint(isPlacing ? "Keeps the HUD where it is now" : "Shows a sample HUD you can drag")
                    Button("Reset position") {
                        store.overlaySettings.resetPosition()
                        preview()
                    }
                    .buttonStyle(.neonSecondary)
                    .accessibilityHint("Puts the HUD back at the bottom centre of the screen")
                }
            }
        }
        // The sample, and a placing in progress, end with the page.
        .onDisappear { store.overlay.hidePreview() }
    }

    /// A full-width row: the two styles side by side, each with a miniature.
    private func styleRow(selected: OverlaySettings.Style) -> some View {
        VStack(alignment: .leading, spacing: Synth.Space.s) {
            Text("Style").synthBody(13, weight: .semibold, color: Synth.ice)
            HStack(alignment: Synth.isFlat ? .top : .center, spacing: Synth.Space.m) {
                ForEach(OverlaySettings.Style.allCases) { style in
                    StyleCard(style: style, isSelected: selected == style) {
                        store.overlaySettings.style = style
                        preview()
                    }
                }
            }
            // Galerist: both cards as tall as the taller one.
            .fixedSize(horizontal: false, vertical: Synth.isFlat)
        }
        .padding(.horizontal, 14)
        .padding(.vertical, 12)
        .frame(maxWidth: .infinity, alignment: .leading)
        .overlay(alignment: .bottom) {
            Rectangle().fill(Synth.gridLine.opacity(0.6)).frame(height: 1)
        }
        .accessibilityElement(children: .contain)
        .accessibilityLabel("HUD style")
    }

    private func togglePlacing() {
        if store.overlay.isPlacing {
            store.overlay.endPlacing()
        } else {
            store.overlay.beginPlacing(settings: store.overlaySettings) { position in
                store.overlaySettings.position = position
            }
        }
    }

    private func preview() {
        store.overlay.preview(settings: store.overlaySettings)
    }

    private static func seconds(_ value: Double) -> String {
        value.formatted(.number.precision(.fractionLength(1))) + " s"
    }
}

/// A selectable card with a miniature of each HUD style. The miniature looks
/// like the real HUD in the active theme, and like it is always dark.
private struct StyleCard: View {
    let style: OverlaySettings.Style
    let isSelected: Bool
    let action: () -> Void

    var body: some View {
        Button(action: action) {
            if Synth.isFlat { flat } else { neon }
        }
        .buttonStyle(.plain)
        .accessibilityLabel("\(style.displayName) style")
        .accessibilityValue(isSelected ? "Selected" : "")
        .accessibilityAddTraits(isSelected ? [.isSelected] : [])
    }

    // MARK: Miami

    private var neon: some View {
        VStack(alignment: .leading, spacing: Synth.Space.s) {
            neonMiniature
                .frame(maxWidth: .infinity, minHeight: 64)
            HStack(spacing: 6) {
                Image(systemName: isSelected ? "largecircle.fill.circle" : "circle")
                    .font(.system(size: 11, weight: .bold))
                    .foregroundStyle(isSelected ? Synth.magenta : Synth.steel)
                Text(style.displayName)
                    .font(SynthFont.font(.display, size: 13, weight: .bold))
                    .tracking(1)
                    .textCase(.uppercase)
                    .foregroundStyle(isSelected ? Synth.ice : Synth.body)
            }
            Text(style.detail).synthBody(11, color: Synth.steel)
        }
        .padding(12)
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(RoundedRectangle(cornerRadius: Synth.Radius.container)
            .fill(isSelected ? Synth.magenta.opacity(0.08) : Synth.panelRaised))
        .overlay(RoundedRectangle(cornerRadius: Synth.Radius.container)
            .strokeBorder(isSelected ? Synth.magenta : Synth.gridLine, lineWidth: 1))
        .shadow(color: isSelected ? Synth.magenta.opacity(0.35) : .clear, radius: 8)
        .contentShape(Rectangle())
    }

    @ViewBuilder
    private var neonMiniature: some View {
        switch style {
        case .ruler:
            VStack(alignment: .leading, spacing: 4) {
                Text("Exposure").synthLabel(8, color: Synth.teal)
                Text("+0.35").font(SynthFont.font(.mono, size: 16, weight: .bold)).foregroundStyle(.white)
                Rectangle().fill(Synth.steel.opacity(0.5)).frame(height: 1)
                    .overlay(alignment: .leading) {
                        Rectangle().fill(Synth.magenta).frame(width: 2, height: 8).offset(x: 46)
                    }
            }
            .padding(8)
            .overlay(RoundedRectangle(cornerRadius: 3).strokeBorder(Synth.teal, lineWidth: 1))
        case .compact:
            HStack {
                Text("Exposure").synthLabel(8, color: Synth.teal)
                Spacer()
                Text("+0.35").font(SynthFont.font(.mono, size: 11, weight: .bold)).foregroundStyle(.white)
            }
            .padding(6)
            .overlay(alignment: .bottom) {
                Rectangle().fill(Synth.magenta).frame(height: 2).padding(.horizontal, 6).padding(.bottom, 2)
            }
            .overlay(RoundedRectangle(cornerRadius: 3).strokeBorder(Synth.teal, lineWidth: 1))
        }
    }

    // MARK: Galerist

    private var flat: some View {
        VStack(alignment: .leading, spacing: 14) {
            flatMiniature
                .environment(\.colorScheme, .dark)
            HStack(alignment: .firstTextBaseline, spacing: 8) {
                Image(systemName: isSelected ? "largecircle.fill.circle" : "circle")
                    .font(.system(size: 12, weight: .regular))
                    .foregroundStyle(isSelected ? Synth.ice : Synth.steel)
                    .accessibilityHidden(true)
                VStack(alignment: .leading, spacing: 4) {
                    Text(style.displayName).synthBody(15, weight: .semibold, color: Synth.ice)
                    Text(style.detail).synthBody(13, color: Synth.steel)
                        .fixedSize(horizontal: false, vertical: true)
                }
            }
        }
        .padding(18)
        .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
        .background(Synth.panel)
        .overlay(Rectangle().strokeBorder(isSelected ? Synth.ice : Synth.gridLine, lineWidth: isSelected ? 2 : 1))
        .contentShape(Rectangle())
    }

    /// A small HUD in the charcoal of the real one, with the red needle.
    @ViewBuilder
    private var flatMiniature: some View {
        switch style {
        case .ruler:
            VStack(alignment: .leading, spacing: 8) {
                HStack {
                    Text("Exposure").panelCaps(9)
                    Spacer()
                    Text("[Q] Normal").panelCaps(9)
                }
                HStack(alignment: .firstTextBaseline, spacing: 5) {
                    Text("+0.35").font(SynthFont.font(.text, size: 24, weight: .semibold)).monospacedDigit()
                        .foregroundStyle(Synth.ice)
                    Text("EV").panelCaps(10, tracking: 0.1)
                }
                MiniScale()
                    .frame(height: 14)
            }
            .padding(.horizontal, 14)
            .padding(.vertical, 12)
            .frame(maxWidth: .infinity, alignment: .leading)
            .background(Synth.abyss)
            .overlay(Rectangle().strokeBorder(Synth.gridLine, lineWidth: 1))
        case .compact:
            VStack(alignment: .leading, spacing: 8) {
                HStack(alignment: .firstTextBaseline) {
                    Text("Contrast").panelCaps(9)
                    Spacer()
                    Text("+12").font(SynthFont.font(.text, size: 17, weight: .semibold)).monospacedDigit()
                        .foregroundStyle(Synth.ice)
                }
                GeometryReader { geometry in
                    let width = geometry.size.width
                    ZStack(alignment: .leading) {
                        Rectangle().fill(Synth.steel.opacity(0.35)).frame(height: 2)
                        Rectangle().fill(Synth.live).frame(width: (width * 0.06).rounded(), height: 2)
                            .offset(x: (width * 0.5).rounded())
                    }
                }
                .frame(height: 2)
            }
            .padding(.horizontal, 14)
            .padding(.vertical, 12)
            .frame(maxWidth: .infinity, alignment: .leading)
            .background(Synth.abyss)
            .overlay(Rectangle().strokeBorder(Synth.gridLine, lineWidth: 1))
        }
    }
}

/// The Ruler miniature's scale: a hairline, a white zero, the red needle.
private struct MiniScale: View {
    var body: some View {
        GeometryReader { geometry in
            let width = geometry.size.width
            ZStack(alignment: .topLeading) {
                Rectangle().fill(Synth.gridLine).frame(width: width, height: 1)
                ForEach(0..<11, id: \.self) { index in
                    Rectangle().fill(index == 5 ? Synth.ice : Synth.disabled)
                        .frame(width: 1, height: index == 5 ? 11 : 5)
                        .offset(x: (width * Double(index) / 10).rounded(.down) - (index == 10 ? 1 : 0))
                }
                Rectangle().fill(Synth.live)
                    .frame(width: 2, height: 13)
                    .offset(x: (width * 0.535).rounded())
            }
        }
        .accessibilityHidden(true)
    }
}

private extension Double {
    func rounded(toPlaces places: Int) -> Double {
        let factor = pow(10, Double(places))
        return (self * factor).rounded() / factor
    }
}
