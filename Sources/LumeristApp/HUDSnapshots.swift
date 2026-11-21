import AppKit
import ImageIO
import SwiftUI
import UniformTypeIdentifiers
import LumeristCore

/// Development aid: renders the Value HUD styles (and the sample shown by
/// "Place the HUD") to PNG files, plus an animated GIF of a Ruler gesture.
/// Every image is made once per theme, with the theme in its name
/// (`ruler-exposure-galerist.png`, `ruler-exposure-miami.png`…).
/// Also renders the floating panels (`panels-*.png`: Palette, Palette over a
/// selected mask with ⇧ held, Key Reference) and the settings that show them
/// (`keyboard-*`, `hud-settings-*`, `palette-settings-*`; Galerist in light
/// and dark). No window is shown and the store is never started: no event
/// tap, no Lightroom connection.
/// Run the app binary with `LUMERIST_RENDER_HUD=<folder>`.
@MainActor
enum HUDSnapshots {
    static func render(to directory: URL) {
        try? FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
        let previous = Synth.theme
        defer { Synth.theme = previous }
        let store = CommandStore()
        for theme in AppTheme.allCases {
            Synth.theme = theme
            renderStills(to: directory, suffix: "-\(theme.rawValue)")
            renderAnimations(to: directory, suffix: "-\(theme.rawValue)")
            renderPanels(store: store, to: directory, suffix: "-\(theme.rawValue)")
            let schemes: [ColorScheme] = theme == .miami ? [.dark] : [.light, .dark]
            for scheme in schemes {
                let suffix = theme == .miami ? "-miami" : "-galerist-\(scheme == .dark ? "dark" : "light")"
                renderSettings(store: store, scheme: scheme, to: directory, suffix: suffix)
            }
        }
    }

    /// Palette, Palette with a mask selected and ⇧ held, Key Reference, over
    /// a Lightroom-grey backdrop.
    private static func renderPanels(store: CommandStore, to directory: URL, suffix: String) {
        let options = PaletteOptions(keepVisible: true, favourites: [
            PaletteEntry(kind: .preset, id: "preview-portrait", title: "Portrait · soft light"),
            PaletteEntry(kind: .preset, id: "preview-bw", title: "B&W · contrast"),
            PaletteEntry(kind: .tool, id: LightroomCommandName.createMask.rawValue, argument: "aiSelection/landscape",
                         title: "Landscape"),
        ])
        let panels = HStack(alignment: .top, spacing: 28) {
            PaletteView(store: store, preview: PaletteView.Preview(options: PaletteOptions(favourites: options.favourites)))
            PaletteView(store: store, preview: PaletteView.Preview(masking: true, held: .add, options: options))
            KeyReferenceView(store: store)
        }
        .padding(32)
        .background(Color(white: 0.17)) // Lightroom's backdrop around the photo
        .environment(\.comboKey, store.keymap.keymap.systemKeys.comboKey)
        .environment(\.colorScheme, .dark)
        writePNG(panels, to: directory.appending(path: "panels\(suffix).png"))
    }

    /// Floating panels → Value HUD and → Palette, and Develop → Keyboard.
    private static func renderSettings(store: CommandStore, scheme: ColorScheme, to directory: URL, suffix: String) {
        func page(_ view: some View, width: CGFloat) -> some View {
            view
                .padding(Synth.Space.xxl)
                .frame(width: width)
                .background(Synth.abyss)
                .environment(\.comboKey, store.keymap.keymap.systemKeys.comboKey)
                .environment(\.colorScheme, scheme)
        }
        writePNG(page(KeyboardTab(store: store, scope: .develop, reveal: { _ in }), width: 1000),
                 to: directory.appending(path: "keyboard\(suffix).png"))
        writePNG(page(HUDScreen(store: store), width: 820), to: directory.appending(path: "hud-settings\(suffix).png"))
        writePNG(page(KeysPaletteScreen(store: store), width: 900),
                 to: directory.appending(path: "palette-settings\(suffix).png"))
    }

    private static func renderStills(to directory: URL, suffix: String) {
        let samples: [(name: String, content: OverlayContent)] = [
            ("exposure", sample("Exposure", value: 0.35, text: "+0.35", unit: "EV", minimum: -5, maximum: 5)),
            ("contrast", sample("Contrast", value: 12, text: "+12", unit: "pt", minimum: -100, maximum: 100)),
            ("temperature", sample("Temperature", value: 5_600, text: "5600", unit: "K", minimum: 2_000, maximum: 50_000)),
            ("notice", notice("Vibrance is not available in a mask")),
        ]
        for sample in samples {
            for style in OverlaySettings.Style.allCases {
                let view = OverlayView(content: sample.content, style: style, backgroundOpacity: 0.88)
                    .background(Color(white: 0.55)) // a mid-grey "photo" behind it
                writePNG(view, to: directory.appending(path: "\(style.rawValue)-\(sample.name)\(suffix).png"))
            }
        }
        // Over a bright sky, at the lowest opacity people tend to pick.
        let bright = OverlayView(content: samples[0].content, style: .ruler, backgroundOpacity: 0.5)
            .background(Color(white: 0.92))
        writePNG(bright, to: directory.appending(path: "ruler-exposure-bright\(suffix).png"))
        // The draggable sample, as "Place the HUD" shows it.
        for style in OverlaySettings.Style.allCases {
            let placing = OverlayView.Placing(onDrop: {}, onDone: {}, showsDragArea: false)
            let view = OverlayView(content: samples[0].content, style: style, backgroundOpacity: 0.88, placing: placing)
                .background(Color(white: 0.55))
            writePNG(view, to: directory.appending(path: "placing-\(style.rawValue)\(suffix).png"))
        }
    }

    private static func writePNG(_ view: some View, to url: URL) {
        let renderer = ImageRenderer(content: view)
        renderer.scale = 2
        guard let png = renderer.nsImage?.pngData else { return }
        try? png.write(to: url)
    }

    /// Animated GIF of an exposure gesture in the Ruler style.
    private static func renderAnimations(to directory: URL, suffix: String) {
        // A short scroll: up past +1.5 EV, back down below zero, settling at +0.35.
        var values: [Double] = []
        func sweep(from start: Double, to end: Double, step: Double) {
            var v = start
            let direction: Double = end >= start ? 1 : -1
            while (v - end) * direction < 0.0001 {
                values.append((v * 100).rounded() / 100)
                v += step * direction
            }
        }
        sweep(from: 0, to: 1.5, step: 0.05)
        sweep(from: 1.45, to: -0.8, step: 0.05)
        sweep(from: -0.75, to: 0.35, step: 0.05)
        var frames: [CGImage] = []
        for value in values {
            let content = sample("Exposure", value: value, text: OverlayFormatting.number(value, unit: "EV"),
                                 unit: "EV", minimum: -5, maximum: 5)
            let view = OverlayView(content: content, style: .ruler, backgroundOpacity: 0.88)
                .background(Color(white: 0.55))
            let renderer = ImageRenderer(content: view)
            renderer.scale = 2
            if let image = renderer.cgImage { frames.append(image) }
        }
        writeGIF(frames, holdLast: 1.6, to: directory.appending(path: "value-hud-ruler\(suffix).gif"))
    }

    private static func writeGIF(_ frames: [CGImage], holdLast: Double, to url: URL) {
        guard !frames.isEmpty,
              let destination = CGImageDestinationCreateWithURL(url as CFURL, UTType.gif.identifier as CFString, frames.count, nil)
        else { return }
        let loop = [kCGImagePropertyGIFDictionary: [kCGImagePropertyGIFLoopCount: 0]] as CFDictionary
        CGImageDestinationSetProperties(destination, loop)
        for (index, frame) in frames.enumerated() {
            let delay = index == frames.count - 1 ? holdLast : 0.04
            let properties = [kCGImagePropertyGIFDictionary: [kCGImagePropertyGIFDelayTime: delay]] as CFDictionary
            CGImageDestinationAddImage(destination, frame, properties)
        }
        CGImageDestinationFinalize(destination)
    }

    private static func sample(_ title: String, value: Double, text: String, unit: String,
                               minimum: Double, maximum: Double) -> OverlayContent {
        var content = OverlayContent(
            title: title,
            valueText: text,
            unit: unit,
            accent: .teal,
            bar: OverlayContent.Bar(value: value, minimum: minimum, maximum: maximum, unit: unit)
        )
        content.keyLabel = "Q"
        content.stepLabel = "Normal"
        return content
    }

    private static func notice(_ text: String) -> OverlayContent {
        var content = OverlayContent(notice: text)
        content.title = "Vibrance"
        return content
    }
}

extension NSImage {
    /// The image as PNG data, for the development renders.
    var pngData: Data? {
        guard let tiff = tiffRepresentation, let bitmap = NSBitmapImageRep(data: tiff) else { return nil }
        return bitmap.representation(using: .png, properties: [:])
    }
}
