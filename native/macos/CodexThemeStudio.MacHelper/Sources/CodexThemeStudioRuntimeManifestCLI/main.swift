import CodexThemeStudioMacHelperCore
import Darwin
import Foundation

func emit(_ value: Any, exitCode: Int32) -> Never {
    let data = try! JSONSerialization.data(
        withJSONObject: value,
        options: [.sortedKeys])
    FileHandle.standardOutput.write(data)
    FileHandle.standardOutput.write(Data([0x0A]))
    exit(exitCode)
}

func value(after name: String, in arguments: [String]) -> String? {
    guard let index = arguments.firstIndex(of: name),
          index + 1 < arguments.count
    else {
        return nil
    }
    return arguments[index + 1]
}

let arguments = Array(CommandLine.arguments.dropFirst())
if arguments == ["schema"] {
    emit([
        "schemaVersion": RuntimeManifest.schemaVersion,
        "runtimeVersion": RuntimeManifest.runtimeVersion,
        "toolVersion": RuntimeManifest.toolVersion,
        "commands": ["generate", "verify", "sync"],
        "files": RuntimeManifest.expectedFiles.map {
            ["path": $0.path, "role": $0.role]
        },
    ], exitCode: 0)
}

do {
    if arguments.first == "generate",
       let contents = value(after: "--contents", in: arguments),
       let output = value(after: "--output", in: arguments),
       arguments.count == 5
    {
        guard contents.hasPrefix("/"), output.hasPrefix("/") else {
            throw HelperFailure("manifest.path_invalid", stage: "manifest")
        }
        let outputURL = URL(fileURLWithPath: output).standardizedFileURL
        guard outputURL.path == output,
              !FileManager.default.fileExists(atPath: output)
        else {
            throw HelperFailure("manifest.output_exists", stage: "manifest")
        }
        let manifest = try RuntimeManifest.generate(
            contentsURL: URL(fileURLWithPath: contents).standardizedFileURL)
        let data = try RuntimeManifest.canonicalData(manifest)
        try data.write(to: outputURL, options: [.withoutOverwriting])
        emit([
            "schemaVersion": RuntimeManifest.schemaVersion,
            "toolVersion": RuntimeManifest.toolVersion,
            "status": "ok",
            "fileCount": manifest.files.count,
        ], exitCode: 0)
    }

    if arguments.first == "verify",
       let contents = value(after: "--contents", in: arguments),
       let manifestPath = value(after: "--manifest", in: arguments),
       let expectedHash = value(after: "--expected-sha256", in: arguments),
       arguments.count == 7
    {
        guard contents.hasPrefix("/"), manifestPath.hasPrefix("/") else {
            throw HelperFailure("manifest.path_invalid", stage: "manifest")
        }
        let manifest = try RuntimeManifest.load(
            URL(fileURLWithPath: manifestPath).standardizedFileURL,
            expectedSha256: expectedHash)
        try manifest.verify(
            contentsURL: URL(fileURLWithPath: contents).standardizedFileURL)
        emit([
            "schemaVersion": RuntimeManifest.schemaVersion,
            "toolVersion": RuntimeManifest.toolVersion,
            "status": "ok",
            "manifestMatch": true,
            "runtimeFilesMatch": true,
        ], exitCode: 0)
    }

    if arguments.first == "sync",
       let root = value(after: "--root", in: arguments),
       arguments.count == 3
    {
        guard root.hasPrefix("/") else {
            throw HelperFailure("manifest.path_invalid", stage: "manifest")
        }
        let rootURL = URL(fileURLWithPath: root).standardizedFileURL
        guard rootURL.path == root,
              let enumerator = FileManager.default.enumerator(
                  at: rootURL,
                  includingPropertiesForKeys: [.isRegularFileKey, .isDirectoryKey],
                  options: [])
        else {
            throw HelperFailure("manifest.path_invalid", stage: "manifest")
        }
        var synced = 0
        for case let url as URL in enumerator {
            var info = stat()
            guard lstat(url.path, &info) == 0,
                  (info.st_mode & S_IFMT) != S_IFLNK
            else {
                throw HelperFailure("manifest.path_invalid", stage: "manifest")
            }
            if (info.st_mode & S_IFMT) == S_IFREG {
                let descriptor = open(url.path, O_RDONLY)
                guard descriptor >= 0 else {
                    throw HelperFailure("manifest.sync_failed", stage: "manifest")
                }
                let result = fsync(descriptor)
                close(descriptor)
                guard result == 0 else {
                    throw HelperFailure("manifest.sync_failed", stage: "manifest")
                }
                synced += 1
            }
        }
        let rootDescriptor = open(rootURL.path, O_RDONLY)
        guard rootDescriptor >= 0 else {
            throw HelperFailure("manifest.sync_failed", stage: "manifest")
        }
        let rootResult = fsync(rootDescriptor)
        close(rootDescriptor)
        guard rootResult == 0 else {
            throw HelperFailure("manifest.sync_failed", stage: "manifest")
        }
        emit([
            "schemaVersion": RuntimeManifest.schemaVersion,
            "toolVersion": RuntimeManifest.toolVersion,
            "status": "ok",
            "syncedFileCount": synced,
        ], exitCode: 0)
    }

    emit([
        "status": "error",
        "error": ["code": "usage.invalid", "stage": "usage"],
    ], exitCode: 2)
} catch let failure as HelperFailure {
    emit([
        "status": "error",
        "error": ["code": failure.code, "stage": failure.stage],
    ], exitCode: 1)
} catch {
    emit([
        "status": "error",
        "error": ["code": "manifest.failed", "stage": "manifest"],
    ], exitCode: 1)
}
