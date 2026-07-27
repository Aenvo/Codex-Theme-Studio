// swift-tools-version: 6.0

import PackageDescription

let package = Package(
    name: "CodexThemeStudioMacHelper",
    platforms: [
        .macOS(.v14),
    ],
    products: [
        .library(
            name: "CodexThemeStudioMacHelperCore",
            targets: ["CodexThemeStudioMacHelperCore"]),
        .executable(
            name: "codex-theme-studio-mac-helper",
            targets: ["CodexThemeStudioMacHelperCLI"]),
        .executable(
            name: "codex-theme-studio-runtime-manifest",
            targets: ["CodexThemeStudioRuntimeManifestCLI"]),
    ],
    dependencies: [],
    targets: [
        .target(
            name: "CodexThemeStudioMacHelperCore",
            linkerSettings: [
                .linkedFramework("Security"),
            ]),
        .executableTarget(
            name: "CodexThemeStudioMacHelperCLI",
            dependencies: ["CodexThemeStudioMacHelperCore"]),
        .executableTarget(
            name: "CodexThemeStudioRuntimeManifestCLI",
            dependencies: ["CodexThemeStudioMacHelperCore"]),
        .testTarget(
            name: "CodexThemeStudioMacHelperCoreTests",
            dependencies: ["CodexThemeStudioMacHelperCore"]),
    ],
    swiftLanguageModes: [.v5])
