import Foundation

// This source checkout is intentionally unconfigured. The packaging pipeline
// writes the SHA-256 of runtime-manifest.json here before compiling the helper.
// Until then, every stateful operation fails closed before launching Node.
public enum GeneratedRuntimeIdentity {
    public static let runtimeManifestSha256 = ""

    public static var isConfigured: Bool {
        runtimeManifestSha256.count == 64 &&
            runtimeManifestSha256.allSatisfy(\.isHexDigit)
    }
}
