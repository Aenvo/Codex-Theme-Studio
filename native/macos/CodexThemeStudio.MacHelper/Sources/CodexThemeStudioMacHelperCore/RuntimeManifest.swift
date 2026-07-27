import CryptoKit
import Darwin
import Foundation

public struct RuntimeManifest: Codable, Equatable {
    public struct Target: Codable, Equatable {
        public let os: String
        public let architecture: String

        public init(os: String, architecture: String) {
            self.os = os
            self.architecture = architecture
        }
    }

    public struct FileEntry: Codable, Equatable {
        public let path: String
        public let role: String
        public let size: UInt64
        public let sha256: String

        public init(path: String, role: String, size: UInt64, sha256: String) {
            self.path = path
            self.role = role
            self.size = size
            self.sha256 = sha256
        }
    }

    public static let schemaVersion = 1
    public static let runtimeVersion = "1"
    public static let toolVersion = "0.2.0"
    public static let expectedFiles: [(path: String, role: String)] = [
        ("Helpers/node", "node-runtime"),
        ("Resources/runtime/macos/cdp-client.mjs", "cdp-client"),
        ("Resources/runtime/macos/renderer-runtime.mjs", "renderer-runtime"),
    ]

    public let schemaVersion: Int
    public let runtimeVersion: String
    public let toolVersion: String
    public let target: Target
    public let files: [FileEntry]

    public init(
        schemaVersion: Int = RuntimeManifest.schemaVersion,
        runtimeVersion: String = RuntimeManifest.runtimeVersion,
        toolVersion: String = RuntimeManifest.toolVersion,
        target: Target = Target(os: "darwin", architecture: "arm64"),
        files: [FileEntry])
    {
        self.schemaVersion = schemaVersion
        self.runtimeVersion = runtimeVersion
        self.toolVersion = toolVersion
        self.target = target
        self.files = files
    }

    public static func generate(contentsURL: URL) throws -> RuntimeManifest {
        let entries = try expectedFiles.map { expected in
            let url = try requireSafeRelativeFile(
                contentsURL: contentsURL,
                relativePath: expected.path)
            return FileEntry(
                path: expected.path,
                role: expected.role,
                size: try fileSize(url),
                sha256: try StableHasher.sha256(url).lowercased())
        }
        return RuntimeManifest(files: entries)
    }

    public static func canonicalData(_ manifest: RuntimeManifest) throws -> Data {
        try manifest.validateShape()
        let encoder = JSONEncoder()
        encoder.outputFormatting = [.sortedKeys, .withoutEscapingSlashes]
        var data = try encoder.encode(manifest)
        data.append(0x0A)
        return data
    }

    public static func load(
        _ url: URL,
        expectedSha256: String) throws -> RuntimeManifest
    {
        guard isSha256(expectedSha256) else {
            throw HelperFailure(
                "helper.runtime_identity_unconfigured",
                stage: "runtime")
        }
        let file = try requireRegularFileWithoutSymlink(url)
        guard try StableHasher.sha256(file)
            .caseInsensitiveCompare(expectedSha256) == .orderedSame
        else {
            throw HelperFailure(
                "helper.runtime_manifest_hash_mismatch",
                stage: "runtime")
        }
        let data = try Data(contentsOf: file)
        try rejectUnknownFields(data)
        let manifest: RuntimeManifest
        do {
            manifest = try JSONDecoder().decode(RuntimeManifest.self, from: data)
        } catch {
            throw HelperFailure(
                "helper.runtime_manifest_invalid",
                stage: "runtime")
        }
        try manifest.validateShape()
        return manifest
    }

    public func verify(contentsURL: URL) throws {
        try validateShape()
        for entry in files {
            let url = try RuntimeManifest.requireSafeRelativeFile(
                contentsURL: contentsURL,
                relativePath: entry.path)
            guard try RuntimeManifest.fileSize(url) == entry.size,
                  try StableHasher.sha256(url)
                    .caseInsensitiveCompare(entry.sha256) == .orderedSame
            else {
                throw HelperFailure(
                    "helper.runtime_hash_mismatch",
                    stage: "runtime")
            }
        }
        try verifyExactRuntimeInventory(contentsURL: contentsURL)
        try verifyNoUndeclaredExecutable(contentsURL: contentsURL)
    }

    private func validateShape() throws {
        guard schemaVersion == RuntimeManifest.schemaVersion,
              runtimeVersion == RuntimeManifest.runtimeVersion,
              toolVersion == RuntimeManifest.toolVersion,
              target == Target(os: "darwin", architecture: "arm64"),
              files.count == RuntimeManifest.expectedFiles.count
        else {
            throw HelperFailure(
                "helper.runtime_manifest_invalid",
                stage: "runtime")
        }

        var normalized = Set<String>()
        for (entry, expected) in zip(files, RuntimeManifest.expectedFiles) {
            guard entry.path == expected.path,
                  entry.role == expected.role,
                  entry.size > 0,
                  RuntimeManifest.isSha256(entry.sha256),
                  entry.sha256 == entry.sha256.lowercased(),
                  RuntimeManifest.isCanonicalRelativePath(entry.path),
                  normalized.insert(entry.path.lowercased()).inserted
            else {
                throw HelperFailure(
                    "helper.runtime_manifest_invalid",
                    stage: "runtime")
            }
        }
    }

    private static func rejectUnknownFields(_ data: Data) throws {
        guard let root = try? JSONSerialization.jsonObject(with: data)
                as? [String: Any],
              Set(root.keys).isSubset(of: [
                  "schemaVersion", "runtimeVersion", "toolVersion",
                  "target", "files",
              ]),
              let target = root["target"] as? [String: Any],
              Set(target.keys).isSubset(of: ["os", "architecture"]),
              let files = root["files"] as? [[String: Any]],
              files.allSatisfy({
                  Set($0.keys).isSubset(of: [
                      "path", "role", "size", "sha256",
                  ])
              })
        else {
            throw HelperFailure(
                "helper.runtime_manifest_invalid",
                stage: "runtime")
        }
    }

    private static func isCanonicalRelativePath(_ path: String) -> Bool {
        guard !path.isEmpty,
              path.unicodeScalars.allSatisfy({ $0.value >= 0x21 && $0.value <= 0x7E }),
              !path.hasPrefix("/"),
              !path.contains("\\")
        else {
            return false
        }
        let components = path.split(separator: "/", omittingEmptySubsequences: false)
        return components.allSatisfy { !$0.isEmpty && $0 != "." && $0 != ".." } &&
            components.map(String.init).joined(separator: "/") == path
    }

    private static func requireSafeRelativeFile(
        contentsURL: URL,
        relativePath: String) throws -> URL
    {
        guard isCanonicalRelativePath(relativePath) else {
            throw HelperFailure(
                "helper.runtime_manifest_invalid",
                stage: "runtime")
        }
        let root = contentsURL.standardizedFileURL
        var current = root
        for component in relativePath.split(separator: "/") {
            current.appendPathComponent(String(component), isDirectory: false)
            var info = stat()
            guard lstat(current.path, &info) == 0,
                  (info.st_mode & S_IFMT) != S_IFLNK
            else {
                throw HelperFailure(
                    "helper.runtime_path_invalid",
                    stage: "runtime")
            }
        }
        guard current.standardizedFileURL.path.hasPrefix(root.path + "/") else {
            throw HelperFailure(
                "helper.runtime_path_invalid",
                stage: "runtime")
        }
        return try requireRegularFileWithoutSymlink(current)
    }

    private static func requireRegularFileWithoutSymlink(_ url: URL) throws -> URL {
        var info = stat()
        guard lstat(url.path, &info) == 0,
              (info.st_mode & S_IFMT) == S_IFREG,
              (info.st_mode & S_IFMT) != S_IFLNK,
              url.resolvingSymlinksInPath().path == url.standardizedFileURL.path
        else {
            throw HelperFailure(
                "helper.runtime_path_invalid",
                stage: "runtime")
        }
        return url.standardizedFileURL
    }

    private static func fileSize(_ url: URL) throws -> UInt64 {
        var info = stat()
        guard stat(url.path, &info) == 0, info.st_size >= 0 else {
            throw HelperFailure(
                "helper.runtime_path_invalid",
                stage: "runtime")
        }
        return UInt64(info.st_size)
    }

    private static func isSha256(_ value: String) -> Bool {
        value.count == 64 &&
            value.unicodeScalars.allSatisfy {
                (0x30...0x39).contains($0.value) ||
                    (0x61...0x66).contains($0.value) ||
                    (0x41...0x46).contains($0.value)
            }
    }

    private func verifyExactRuntimeInventory(contentsURL: URL) throws {
        let manager = FileManager.default
        let runtimeDirectory = contentsURL
            .appendingPathComponent("Resources/runtime/macos", isDirectory: true)
        let helperDirectory = contentsURL
            .appendingPathComponent("Helpers", isDirectory: true)
        let runtimeItems = try manager.contentsOfDirectory(
            atPath: runtimeDirectory.path).sorted()
        let helperItems = try manager.contentsOfDirectory(
            atPath: helperDirectory.path).sorted()
        guard runtimeItems == ["cdp-client.mjs", "renderer-runtime.mjs"],
              helperItems == ["CodexThemeStudio.MacHelper", "node"]
        else {
            throw HelperFailure(
                "helper.runtime_inventory_invalid",
                stage: "runtime")
        }
    }

    private func verifyNoUndeclaredExecutable(contentsURL: URL) throws {
        let allowed = Set([
            "Helpers/CodexThemeStudio.MacHelper",
            "Helpers/node",
        ])
        guard let enumerator = FileManager.default.enumerator(
            at: contentsURL,
            includingPropertiesForKeys: nil,
            options: [])
        else {
            throw HelperFailure(
                "helper.runtime_inventory_invalid",
                stage: "runtime")
        }
        for case let url as URL in enumerator {
            var info = stat()
            guard lstat(url.path, &info) == 0,
                  (info.st_mode & S_IFMT) != S_IFLNK
            else {
                throw HelperFailure(
                    "helper.runtime_path_invalid",
                    stage: "runtime")
            }
            guard (info.st_mode & S_IFMT) == S_IFREG else {
                continue
            }
            let relative = String(url.path.dropFirst(contentsURL.path.count + 1))
            let executableBits = S_IXUSR | S_IXGRP | S_IXOTH
            if info.st_mode & executableBits != 0 && !allowed.contains(relative) {
                throw HelperFailure(
                    "helper.runtime_inventory_invalid",
                    stage: "runtime")
            }
        }
    }
}

public enum PackagedRuntimeVerifier {
    public static func verifyCurrentExecutableLayout() throws {
        guard GeneratedRuntimeIdentity.isConfigured else {
            throw HelperFailure(
                "helper.runtime_identity_unconfigured",
                stage: "runtime")
        }
        let helperURL = URL(fileURLWithPath: CommandLine.arguments[0])
            .standardizedFileURL
        let contentsURL = helperURL
            .deletingLastPathComponent()
            .deletingLastPathComponent()
        let manifestURL = contentsURL
            .appendingPathComponent("Resources/runtime-manifest.json")
        let manifest = try RuntimeManifest.load(
            manifestURL,
            expectedSha256: GeneratedRuntimeIdentity.runtimeManifestSha256)
        try manifest.verify(contentsURL: contentsURL)
    }
}
