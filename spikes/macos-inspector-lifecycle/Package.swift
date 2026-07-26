// swift-tools-version: 6.0

import PackageDescription

let package = Package(
    name: "CodexMacOSInspectorLifecycle",
    platforms: [
        .macOS(.v13),
    ],
    products: [
        .library(
            name: "CodexInspectorLifecycleCore",
            targets: ["CodexInspectorLifecycleCore"]),
        .executable(
            name: "codex-macos-inspector-lifecycle",
            targets: ["CodexInspectorLifecycleCLI"]),
    ],
    dependencies: [
        .package(path: "../macos-readonly-discovery"),
    ],
    targets: [
        .target(
            name: "CodexInspectorLifecycleCore",
            dependencies: [
                .product(
                    name: "CodexDiscoveryCore",
                    package: "macos-readonly-discovery"),
            ],
            resources: [
                .copy("cdp-client.mjs"),
            ]),
        .executableTarget(
            name: "CodexInspectorLifecycleCLI",
            dependencies: ["CodexInspectorLifecycleCore"]),
        .testTarget(
            name: "CodexInspectorLifecycleCoreTests",
            dependencies: ["CodexInspectorLifecycleCore"]),
    ],
    swiftLanguageModes: [.v5])
