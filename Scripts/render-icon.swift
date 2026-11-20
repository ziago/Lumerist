// Renders Resources/Icon/*.svg into .iconsets and builds the .icns files:
// - Lumerist.icns (icon-master.svg / icon-small.svg): the app's icon, Galerist theme;
// - Lumerist-Miami.icns (icon-miami-*.svg): the Dock icon while the Miami theme is on.
// Usage: swift Scripts/render-icon.swift   (run from the repository root)
import AppKit

let root = URL(fileURLWithPath: FileManager.default.currentDirectoryPath)
let iconDir = root.appending(path: "Resources/Icon")

func png(_ image: NSImage, pixels: Int) -> Data {
    let rep = NSBitmapImageRep(bitmapDataPlanes: nil, pixelsWide: pixels, pixelsHigh: pixels,
                               bitsPerSample: 8, samplesPerPixel: 4, hasAlpha: true, isPlanar: false,
                               colorSpaceName: .deviceRGB, bytesPerRow: 0, bitsPerPixel: 0)!
    rep.size = NSSize(width: pixels, height: pixels)
    NSGraphicsContext.saveGraphicsState()
    NSGraphicsContext.current = NSGraphicsContext(bitmapImageRep: rep)
    NSGraphicsContext.current?.imageInterpolation = .high
    image.draw(in: NSRect(x: 0, y: 0, width: pixels, height: pixels))
    NSGraphicsContext.restoreGraphicsState()
    return rep.representation(using: .png, properties: [:])!
}

/// Writes `<name>.iconset` from a master (128 px and up) and a small source (64 px and down),
/// then runs iconutil into Resources/<name>.icns.
func build(name: String, master masterFile: String, small smallFile: String, preview: String) throws {
    guard let master = NSImage(contentsOf: iconDir.appending(path: masterFile)),
          let small = NSImage(contentsOf: iconDir.appending(path: smallFile)) else {
        fatalError("NSImage could not load \(masterFile) / \(smallFile)")
    }
    let iconset = iconDir.appending(path: "\(name).iconset")
    try? FileManager.default.removeItem(at: iconset)
    try FileManager.default.createDirectory(at: iconset, withIntermediateDirectories: true)
    let entries: [(Int, Int)] = [(16, 1), (16, 2), (32, 1), (32, 2), (128, 1), (128, 2), (256, 1), (256, 2), (512, 1), (512, 2)]
    for (points, scale) in entries {
        let pixels = points * scale
        let source = pixels <= 64 ? small : master
        let file = scale == 1 ? "icon_\(points)x\(points).png" : "icon_\(points)x\(points)@2x.png"
        try png(source, pixels: pixels).write(to: iconset.appending(path: file))
    }
    try png(master, pixels: 1024).write(to: iconDir.appending(path: preview))
    let iconutil = Process()
    iconutil.executableURL = URL(fileURLWithPath: "/usr/bin/iconutil")
    iconutil.arguments = ["-c", "icns", iconset.path, "-o", root.appending(path: "Resources/\(name).icns").path]
    try iconutil.run()
    iconutil.waitUntilExit()
    print("Wrote Resources/\(name).icns")
}

try build(name: "Lumerist", master: "icon-master.svg", small: "icon-small.svg", preview: "icon-1024.png")
try build(name: "Lumerist-Miami", master: "icon-miami-master.svg", small: "icon-miami-small.svg", preview: "icon-miami-1024.png")
