// swift-tools-version: 6.0

import PackageDescription

let package = Package(
    name: "CodexMacOSCSSCanary",
    platforms: [
        .macOS(.v13),
    ],
    products: [
        .library(
            name: "CodexCSSCanaryCore",
            targets: ["CodexCSSCanaryCore"]),
        .executable(
            name: "codex-macos-css-canary",
            targets: ["CodexCSSCanaryCLI"]),
    ],
    dependencies: [
        .package(path: "../macos-inspector-lifecycle"),
    ],
    targets: [
        .target(
            name: "CodexCSSCanaryCore",
            dependencies: [
                .product(
                    name: "CodexInspectorLifecycleCore",
                    package: "macos-inspector-lifecycle"),
            ],
            resources: [
                .copy("css-canary-client.mjs"),
            ]),
        .executableTarget(
            name: "CodexCSSCanaryCLI",
            dependencies: ["CodexCSSCanaryCore"]),
        .testTarget(
            name: "CodexCSSCanaryCoreTests",
            dependencies: ["CodexCSSCanaryCore"]),
    ],
    swiftLanguageModes: [.v5])
