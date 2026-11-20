// Renders the README banner and the GitHub social preview. Original artwork,
// drawn with CoreGraphics, in the look of one of the app's themes:
//
//   swift Scripts/docs-render-banner.swift            # Galerist (the default theme)
//   swift Scripts/docs-render-banner.swift miami      # Miami: neon grid and sunset
//
// Writes docs/images/banner.png (1600×560) and docs/images/social-preview.png
// (1280×640). Run from the repository root.

import AppKit
import CoreText
import Foundation

let root = URL(fileURLWithPath: FileManager.default.currentDirectoryPath)
let fonts = root.appending(path: "Resources/Fonts")
for name in ["ChakraPetch-Bold.ttf", "ChakraPetch-SemiBold.ttf", "JetBrainsMono-Variable.ttf", "Outfit-Variable.ttf"] {
    CTFontManagerRegisterFontsForURL(fonts.appending(path: name) as CFURL, .process, nil)
}

func color(_ hex: UInt32, _ alpha: CGFloat = 1) -> CGColor {
    CGColor(srgbRed: CGFloat((hex >> 16) & 0xFF) / 255,
            green: CGFloat((hex >> 8) & 0xFF) / 255,
            blue: CGFloat(hex & 0xFF) / 255,
            alpha: alpha)
}

// Theme tokens (Sources/LumeristApp/Design/Theme.swift).
let abyss: UInt32 = 0x070B18, night: UInt32 = 0x0B0F24, dusk: UInt32 = 0x1A0C2A
let teal: UInt32 = 0x2DE2E6, magenta: UInt32 = 0xFF3EA5, amber: UInt32 = 0xFFB347
let ice: UInt32 = 0xE6F6FF, body: UInt32 = 0xC3D3EA, steel: UInt32 = 0x8FA6C8, deepViolet: UInt32 = 0x2A0F4A

func font(_ name: String, _ size: CGFloat) -> CTFont {
    CTFontCreateWithName(name as CFString, size, nil)
}

@discardableResult
func drawText(_ string: String, in context: CGContext, font: CTFont, color fill: CGColor,
              at point: CGPoint, glow: CGColor? = nil, kern: CGFloat = 0, centered: Bool = false) -> CGFloat {
    let attributes: [NSAttributedString.Key: Any] = [
        NSAttributedString.Key(kCTFontAttributeName as String): font,
        NSAttributedString.Key(kCTForegroundColorAttributeName as String): fill,
        NSAttributedString.Key(kCTKernAttributeName as String): kern,
    ]
    let line = CTLineCreateWithAttributedString(NSAttributedString(string: string, attributes: attributes))
    let width = CTLineGetTypographicBounds(line, nil, nil, nil)
    context.saveGState()
    if let glow { context.setShadow(offset: .zero, blur: 18, color: glow) }
    context.textPosition = CGPoint(x: centered ? point.x - CGFloat(width) / 2 : point.x, y: point.y)
    CTLineDraw(line, context)
    context.restoreGState()
    return CGFloat(width)
}

/// Sky, striped sun, horizon glow and a perspective grid floor.
func drawScene(_ context: CGContext, size: CGSize, horizon: CGFloat, sunCenterX: CGFloat, sunRadius: CGFloat) {
    let space = CGColorSpace(name: CGColorSpace.sRGB)!
    // Sky: night at the top, violet dusk at the horizon.
    let sky = CGGradient(colorsSpace: space, colors: [color(night), color(dusk), color(deepViolet)] as CFArray,
                         locations: [0, 0.65, 1])!
    context.drawLinearGradient(sky, start: CGPoint(x: 0, y: size.height), end: CGPoint(x: 0, y: horizon), options: [])
    // Floor.
    context.setFillColor(color(abyss))
    context.fill(CGRect(x: 0, y: 0, width: size.width, height: horizon))

    // Sun: amber → coral → magenta, only above the horizon, with a soft glow.
    let sunRect = CGRect(x: sunCenterX - sunRadius, y: horizon - sunRadius * 0.15,
                         width: sunRadius * 2, height: sunRadius * 2)
    context.saveGState()
    context.clip(to: CGRect(x: 0, y: horizon, width: size.width, height: size.height))
    context.setShadow(offset: .zero, blur: 46, color: color(magenta, 0.7))
    context.setFillColor(color(magenta, 0.5))
    context.fillEllipse(in: sunRect)
    context.setShadow(offset: .zero, blur: 0, color: nil)
    context.addEllipse(in: sunRect)
    context.clip()
    let sun = CGGradient(colorsSpace: space, colors: [color(amber), color(0xFF6A5E), color(magenta)] as CFArray,
                         locations: [0, 0.55, 1])!
    context.drawLinearGradient(sun, start: CGPoint(x: 0, y: sunRect.maxY), end: CGPoint(x: 0, y: horizon), options: [])
    // Stripes across the lower half of the sun.
    context.setFillColor(color(deepViolet))
    var stripeY = horizon + 6
    var stripeHeight: CGFloat = sunRadius * 0.09
    while stripeY < horizon + sunRadius * 0.62 {
        context.fill(CGRect(x: sunRect.minX - 4, y: stripeY, width: sunRect.width + 8, height: stripeHeight))
        stripeY += stripeHeight + sunRadius * 0.07
        stripeHeight *= 0.78
    }

    context.restoreGState()
    // Horizon glow.
    context.saveGState()
    context.setShadow(offset: .zero, blur: 16, color: color(magenta, 0.9))
    context.setFillColor(color(magenta))
    context.fill(CGRect(x: 0, y: horizon - 1.5, width: size.width, height: 3))
    context.restoreGState()

    // Perspective grid: converging verticals and horizontals that tighten
    // towards the horizon, fading with distance.
    context.saveGState()
    context.clip(to: CGRect(x: 0, y: 0, width: size.width, height: horizon - 2))
    let vanishing = CGPoint(x: size.width / 2, y: horizon)
    context.setLineWidth(1.6)
    for index in -24...24 {
        let bottomX = vanishing.x + CGFloat(index) * size.width / 14
        context.setStrokeColor(color(teal, 0.45))
        context.move(to: vanishing)
        context.addLine(to: CGPoint(x: bottomX, y: 0))
        context.strokePath()
    }
    var depth: CGFloat = 1
    while depth < 40 {
        let y = horizon - horizon / depth
        let alpha = min(0.55, 0.12 + 1.6 / depth)
        context.setStrokeColor(color(teal, alpha))
        context.move(to: CGPoint(x: 0, y: y))
        context.addLine(to: CGPoint(x: size.width, y: y))
        context.strokePath()
        depth *= 1.35
    }
    context.restoreGState()
}

/// A neon key cap with a letter, and a small mouse wheel next to it.
func drawKeyCap(_ context: CGContext, origin: CGPoint, size: CGFloat, letter: String) {
    let rect = CGRect(x: origin.x, y: origin.y, width: size, height: size)
    let path = CGPath(roundedRect: rect, cornerWidth: size * 0.18, cornerHeight: size * 0.18, transform: nil)
    context.saveGState()
    context.setShadow(offset: .zero, blur: 22, color: color(teal, 0.8))
    context.setFillColor(color(abyss, 0.85))
    context.addPath(path)
    context.fillPath()
    context.setStrokeColor(color(teal))
    context.setLineWidth(size * 0.055)
    context.addPath(path)
    context.strokePath()
    context.restoreGState()
    drawText(letter, in: context, font: font("ChakraPetch-Bold", size * 0.52), color: color(teal),
             at: CGPoint(x: rect.midX, y: rect.minY + size * 0.3), glow: color(teal, 0.8), centered: true)

    // Mouse wheel: a capsule with a ridge and an up arrow.
    let wheel = CGRect(x: rect.maxX + size * 0.35, y: rect.minY + size * 0.08, width: size * 0.42, height: size * 0.84)
    let wheelPath = CGPath(roundedRect: wheel, cornerWidth: wheel.width / 2, cornerHeight: wheel.width / 2, transform: nil)
    context.saveGState()
    context.setShadow(offset: .zero, blur: 18, color: color(magenta, 0.8))
    context.setStrokeColor(color(magenta))
    context.setLineWidth(size * 0.045)
    context.addPath(wheelPath)
    context.strokePath()
    for offset in stride(from: -0.22, through: 0.22, by: 0.11) {
        let y = wheel.midY + wheel.height * offset
        context.move(to: CGPoint(x: wheel.minX + wheel.width * 0.28, y: y))
        context.addLine(to: CGPoint(x: wheel.maxX - wheel.width * 0.28, y: y))
    }
    context.setLineWidth(size * 0.03)
    context.strokePath()
    // Arrow above the wheel.
    let tip = CGPoint(x: wheel.midX, y: wheel.maxY + size * 0.28)
    context.move(to: CGPoint(x: tip.x - size * 0.12, y: tip.y - size * 0.12))
    context.addLine(to: tip)
    context.addLine(to: CGPoint(x: tip.x + size * 0.12, y: tip.y - size * 0.12))
    context.setLineWidth(size * 0.045)
    context.setLineCap(.round)
    context.strokePath()
    context.restoreGState()
}

func render(width: Int, height: Int, to url: URL, tagline: String, subtitle: String,
            wordmarkScale: CGFloat, sunX: CGFloat) {
    let space = CGColorSpace(name: CGColorSpace.sRGB)!
    guard let context = CGContext(data: nil, width: width, height: height, bitsPerComponent: 8, bytesPerRow: 0,
                                  space: space, bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue) else { return }
    let size = CGSize(width: width, height: height)
    let horizon = size.height * 0.36
    drawScene(context, size: size, horizon: horizon, sunCenterX: size.width * sunX, sunRadius: size.height * 0.30)

    // Wordmark and tagline on the left.
    let left = size.width * 0.07
    let wordmarkWidth = drawText("LUMERIST", in: context, font: font("ChakraPetch-Bold", size.height * wordmarkScale),
                                 color: color(ice), at: CGPoint(x: left, y: size.height * 0.62),
                                 glow: color(teal, 0.9), kern: size.height * 0.012)
    drawText(tagline, in: context, font: font("ChakraPetch-SemiBold", size.height * 0.052), color: color(body),
             at: CGPoint(x: left, y: size.height * 0.52))
    drawText(subtitle, in: context, font: font("JetBrains Mono", size.height * 0.034), color: color(steel),
             at: CGPoint(x: left, y: size.height * 0.455), kern: 1.5)

    let capSize = size.height * 0.15
    drawKeyCap(context, origin: CGPoint(x: left + wordmarkWidth + capSize * 0.6, y: size.height * 0.61),
               size: capSize, letter: "Q")

    guard let image = context.makeImage() else { return }
    let bitmap = NSBitmapImageRep(cgImage: image)
    guard let png = bitmap.representation(using: .png, properties: [:]) else { return }
    try? FileManager.default.createDirectory(at: url.deletingLastPathComponent(), withIntermediateDirectories: true)
    try? png.write(to: url)
    print("Wrote \(url.path(percentEncoded: false).replacingOccurrences(of: root.path(percentEncoded: false), with: ""))")
}

// MARK: - Galerist

/// A weight of a variable font (Outfit, JetBrains Mono).
func variableFont(_ name: String, _ size: CGFloat, weight: CGFloat) -> CTFont {
    let base = CTFontCreateWithName(name as CFString, size, nil)
    let wght = 0x7767_6874 as CFNumber   // 'wght'
    let descriptor = CTFontDescriptorCreateCopyWithVariation(CTFontCopyFontDescriptor(base), wght, weight)
    return CTFontCreateWithFontDescriptor(descriptor, size, nil)
}

func textWidth(_ string: String, font: CTFont, kern: CGFloat = 0) -> CGFloat {
    let attributes: [NSAttributedString.Key: Any] = [
        NSAttributedString.Key(kCTFontAttributeName as String): font,
        NSAttributedString.Key(kCTKernAttributeName as String): kern,
    ]
    let line = CTLineCreateWithAttributedString(NSAttributedString(string: string, attributes: attributes))
    return CGFloat(CTLineGetTypographicBounds(line, nil, nil, nil))
}

/// The Value HUD, Ruler style, as the app draws it: slider name, key, value
/// and a scale from −5 to +5 EV with the red needle.
func drawHUDCard(_ context: CGContext, rect: CGRect) {
    let unit = rect.height / 300
    context.setFillColor(color(0x1E1E1E))
    context.fill(rect)
    context.setStrokeColor(color(0x333333))
    context.setLineWidth(max(1, unit * 1.5))
    context.stroke(rect.insetBy(dx: 0.5, dy: 0.5))

    let pad = 32 * unit
    let mono = variableFont("JetBrains Mono", 19 * unit, weight: 700)
    let monoLight = variableFont("JetBrains Mono", 19 * unit, weight: 500)
    let topY = rect.maxY - pad - 18 * unit
    drawText("EXPOSURE", in: context, font: mono, color: color(0xFFFFFF),
             at: CGPoint(x: rect.minX + pad, y: topY), kern: 19 * unit * 0.14)
    let keyText = "[Q] NORMAL"
    let keyWidth = textWidth(keyText, font: monoLight, kern: 19 * unit * 0.14)
    drawText(keyText, in: context, font: monoLight, color: color(0x8A8A8A),
             at: CGPoint(x: rect.maxX - pad - keyWidth, y: topY), kern: 19 * unit * 0.14)

    let valueFont = variableFont("Outfit-Thin", 84 * unit, weight: 600)
    let valueY = rect.maxY - pad - 120 * unit
    let valueWidth = drawText("+0.35", in: context, font: valueFont, color: color(0xFFFFFF),
                              at: CGPoint(x: rect.minX + pad - 4 * unit, y: valueY), kern: -84 * unit * 0.02)
    drawText("EV", in: context, font: variableFont("JetBrains Mono", 22 * unit, weight: 500), color: color(0x8A8A8A),
             at: CGPoint(x: rect.minX + pad + valueWidth + 10 * unit, y: valueY), kern: 2 * unit)

    // Scale: a tick every 0.5 EV, a longer one and a number at every EV.
    let left = rect.minX + pad + 12 * unit, right = rect.maxX - pad - 12 * unit
    let baseY = rect.minY + 74 * unit
    func x(_ value: CGFloat) -> CGFloat { left + (value + 5) / 10 * (right - left) }
    context.setStrokeColor(color(0x6B6B6B))
    context.setLineWidth(max(1, 1.5 * unit))
    context.move(to: CGPoint(x: left, y: baseY + 26 * unit))
    context.addLine(to: CGPoint(x: right, y: baseY + 26 * unit))
    context.strokePath()
    let labelFont = variableFont("JetBrains Mono", 16 * unit, weight: 500)
    for step in -10...10 {
        let value = CGFloat(step) / 2
        let major = step % 2 == 0
        context.setStrokeColor(color(step == 0 ? 0xFFFFFF : 0x6B6B6B))
        context.setLineWidth(max(1, (step == 0 ? 2.5 : 1.5) * unit))
        context.move(to: CGPoint(x: x(value), y: baseY + 26 * unit))
        context.addLine(to: CGPoint(x: x(value), y: baseY + 26 * unit - (step == 0 ? 26 : major ? 16 : 9) * unit))
        context.strokePath()
        if major {
            let label = step == 0 ? "0" : (step > 0 ? "+\(step / 2)" : "−\(-step / 2)")
            drawText(label, in: context, font: labelFont, color: color(step == 0 ? 0xFFFFFF : 0x9A9A9A),
                     at: CGPoint(x: x(value), y: baseY - 26 * unit), centered: true)
        }
    }
    // The needle at the current value.
    context.setStrokeColor(color(0xE11D2A))
    context.setLineWidth(4 * unit)
    context.move(to: CGPoint(x: x(0.35), y: baseY + 26 * unit))
    context.addLine(to: CGPoint(x: x(0.35), y: baseY - 4 * unit))
    context.strokePath()
}

func renderGalerist(width: Int, height: Int, to url: URL, taglineLines: [String], subtitle: String,
                    wordmarkScale: CGFloat, cardWidth: CGFloat) {
    let space = CGColorSpace(name: CGColorSpace.sRGB)!
    guard let context = CGContext(data: nil, width: width, height: height, bitsPerComponent: 8, bytesPerRow: 0,
                                  space: space, bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue) else { return }
    let size = CGSize(width: width, height: height)
    // Light grey page with a white band and hairlines, as in the app.
    context.setFillColor(color(0xF4F4F4))
    context.fill(CGRect(origin: .zero, size: size))
    let band = CGRect(x: 0, y: size.height * 0.08, width: size.width, height: size.height * 0.84)
    context.setFillColor(color(0xFFFFFF))
    context.fill(band)
    context.setFillColor(color(0xE7E7E7))
    context.fill(CGRect(x: 0, y: band.maxY, width: size.width, height: 2))
    context.fill(CGRect(x: 0, y: band.minY - 2, width: size.width, height: 2))

    let left = size.width * 0.07
    // App icon, then the "Lumerist." wordmark with the red full stop.
    let wordmarkFont = variableFont("Outfit-Thin", size.height * wordmarkScale, weight: 700)
    let wordmarkY = band.midY - size.height * 0.035
    let iconSize = size.height * wordmarkScale * 1.12
    if let icon = NSImage(contentsOf: root.appending(path: "Resources/Icon/icon-1024.png")),
       let cgIcon = icon.cgImage(forProposedRect: nil, context: nil, hints: nil) {
        // The icon art sits inside a 1024 canvas with a margin; trim it so the body lines up.
        let bleed = iconSize * 100 / 824
        context.draw(cgIcon, in: CGRect(x: left - bleed, y: wordmarkY - iconSize * 0.16 - bleed,
                                        width: iconSize + bleed * 2, height: iconSize + bleed * 2))
    }
    let textLeft = left + iconSize + size.height * 0.05
    let kern = -size.height * wordmarkScale * 0.03
    let wordWidth = drawText("Lumerist", in: context, font: wordmarkFont, color: color(0x0A0A0A),
                             at: CGPoint(x: textLeft, y: wordmarkY), kern: kern)
    drawText(".", in: context, font: wordmarkFont, color: color(0xE11D2A),
             at: CGPoint(x: textLeft + wordWidth, y: wordmarkY))

    let taglineFont = variableFont("Outfit-Thin", size.height * 0.056, weight: 500)
    var lineY = wordmarkY - size.height * 0.115
    for line in taglineLines {
        drawText(line, in: context, font: taglineFont, color: color(0x0A0A0A), at: CGPoint(x: left, y: lineY),
                 kern: -size.height * 0.056 * 0.01)
        lineY -= size.height * 0.072
    }
    let subtitleFont = variableFont("JetBrains Mono", size.height * 0.026, weight: 500)
    drawText(subtitle, in: context, font: subtitleFont, color: color(0x525252),
             at: CGPoint(x: left, y: lineY - size.height * 0.01), kern: size.height * 0.026 * 0.16)
    // Eyebrow above the wordmark.
    drawText("FOR ADOBE LIGHTROOM CLASSIC · macOS · WINDOWS", in: context, font: subtitleFont, color: color(0x525252),
             at: CGPoint(x: left, y: wordmarkY + iconSize * 0.84 + size.height * 0.06), kern: size.height * 0.026 * 0.16)

    // The Value HUD on the right, over the band.
    let cardHeight = cardWidth * 0.52
    let card = CGRect(x: size.width - size.width * 0.07 - cardWidth, y: band.midY - cardHeight / 2,
                      width: cardWidth, height: cardHeight)
    drawHUDCard(context, rect: card)

    guard let image = context.makeImage() else { return }
    let bitmap = NSBitmapImageRep(cgImage: image)
    guard let png = bitmap.representation(using: .png, properties: [:]) else { return }
    try? png.write(to: url)
    print("Wrote \(url.lastPathComponent)")
}

let images = root.appending(path: "docs/images")
if CommandLine.arguments.dropFirst().first == "miami" {
    render(width: 1600, height: 560, to: images.appending(path: "banner.png"),
           tagline: "Hold a key. Turn the wheel. Edit faster.",
           subtitle: "KEYBOARD + MOUSE-WHEEL EDITING FOR LIGHTROOM CLASSIC, MAC AND WINDOWS",
           wordmarkScale: 0.15, sunX: 0.76)
    render(width: 1280, height: 640, to: images.appending(path: "social-preview.png"),
           tagline: "Hold a key. Turn the wheel. Edit faster.",
           subtitle: "FREE & OPEN SOURCE · LIGHTROOM CLASSIC · macOS · WINDOWS",
           wordmarkScale: 0.115, sunX: 0.82)
} else {
    renderGalerist(width: 1600, height: 560, to: images.appending(path: "banner.png"),
                   taglineLines: ["Hold a key. Turn the wheel. Edit faster."],
                   subtitle: "KEYBOARD + MOUSE-WHEEL EDITING · FREE & OPEN SOURCE",
                   wordmarkScale: 0.2, cardWidth: 560)
    renderGalerist(width: 1280, height: 640, to: images.appending(path: "social-preview.png"),
                   taglineLines: ["Hold a key. Turn the wheel.", "Edit faster."],
                   subtitle: "FREE & OPEN SOURCE",
                   wordmarkScale: 0.17, cardWidth: 470)
}
