// swift-tools-version: 6.0
import PackageDescription

let package = Package(
    name: "Lumerist",
    platforms: [.macOS(.v14)],
    products: [
        .library(name: "LumeristCore", targets: ["LumeristCore"]),
        .executable(name: "LumeristApp", targets: ["LumeristApp"]),
    ],
    targets: [
        .target(name: "LumeristCore"),
        .executableTarget(name: "LumeristApp", dependencies: ["LumeristCore"]),
        .testTarget(name: "LumeristCoreTests", dependencies: ["LumeristCore"]),
    ]
)

