// swift-tools-version: 6.0

import PackageDescription

let package = Package(
    name: "CodexMacOSReadOnlyDiscovery",
    platforms: [
        .macOS(.v13),
    ],
    products: [
        .library(
            name: "CodexDiscoveryCore",
            targets: ["CodexDiscoveryCore"]),
        .executable(
            name: "codex-macos-discovery",
            targets: ["CodexDiscoveryCLI"]),
    ],
    dependencies: [],
    targets: [
        .target(
            name: "CodexDiscoveryCore",
            linkerSettings: [
                .linkedFramework("AppKit"),
                .linkedFramework("Security"),
            ]),
        .executableTarget(
            name: "CodexDiscoveryCLI",
            dependencies: ["CodexDiscoveryCore"]),
        .testTarget(
            name: "CodexDiscoveryCoreTests",
            dependencies: ["CodexDiscoveryCore"]),
    ],
    swiftLanguageModes: [.v5])
