import CryptoKit
import Darwin
import Foundation
import Security

public struct TrustedSnapshot: Equatable {
    public let installation: InstallationEvidence
    public let process: ProcessEvidence
}

public protocol TrustedDiscovering {
    func discover(bundlePath: String) throws -> TrustedSnapshot
}

public protocol PortQuerying {
    func listeners(port: Int) throws -> [PortListener]
}

public struct PortListener: Equatable {
    public let processId: Int32
    public let address: String
}

public struct SystemDiscovery: TrustedDiscovering {
    public init() {}

    public func discover(bundlePath: String) throws -> TrustedSnapshot {
        let bundleURL = URL(fileURLWithPath: bundlePath).standardizedFileURL
        try PathPolicy.requireDirectory(bundleURL)
        guard bundleURL.path == bundlePath,
              let bundle = Bundle(url: bundleURL),
              bundle.bundleIdentifier == HelperConstants.bundleIdentifier,
              let executableURL = bundle.executableURL,
              let version = bundle.object(
                  forInfoDictionaryKey: "CFBundleShortVersionString") as? String,
              let buildVersion = bundle.object(
                  forInfoDictionaryKey: "CFBundleVersion") as? String
        else {
            throw HelperFailure("bundle.identity_mismatch", stage: "discovery")
        }

        let executable = try PathPolicy.requireRegularFile(executableURL)
        guard executable.path.hasPrefix(bundleURL.path + "/") else {
            throw HelperFailure("bundle.executable_invalid", stage: "discovery")
        }
        let hash = try StableHasher.sha256(executable)
        let signature = try SignatureInspector.inspect(bundleURL)
        guard signature.identifier == HelperConstants.bundleIdentifier,
              signature.teamIdentifier == HelperConstants.teamIdentifier,
              signature.valid,
              signature.hardenedRuntime
        else {
            throw HelperFailure("bundle.signature_invalid", stage: "signature")
        }
        try CommandRunner.requireSuccess(
            executable: "/usr/bin/codesign",
            arguments: ["--verify", "--strict", "--verbose=0", bundleURL.path],
            timeout: 5)
        try CommandRunner.requireSuccess(
            executable: "/usr/sbin/spctl",
            arguments: [
                "--assess", "--type", "execute", "--no-cache", bundleURL.path,
            ],
            timeout: 8)

        let architecture = try MachOInspector.architecture(executable)
        let processes = try ProcessInspector.find(executablePath: executable.path)
        guard processes.count == 1 else {
            throw HelperFailure(
                processes.isEmpty ? "process.not_running" : "process.ambiguous",
                stage: "process")
        }
        let process = processes[0]
        guard process.executablePath == executable.path else {
            throw HelperFailure("process.identity_changed", stage: "process")
        }
        let installation = InstallationEvidence(
            platform: "macOS",
            productIdentifier: HelperConstants.bundleIdentifier,
            publisherIdentifier: HelperConstants.teamIdentifier,
            version: version,
            buildVersion: buildVersion,
            executablePath: executable.path,
            executableSha256: hash,
            signatureValid: true,
            hardenedRuntime: true,
            gatekeeperAccepted: true)
        return TrustedSnapshot(
            installation: installation,
            process: ProcessEvidence(
                processId: process.processId,
                parentProcessId: process.parentProcessId,
                startedAtUtc: process.startedAtUtc,
                executablePath: process.executablePath,
                architecture: architecture))
    }
}

public struct LsofPortQuery: PortQuerying {
    public init() {}

    public func listeners(port: Int) throws -> [PortListener] {
        let result = try CommandRunner.run(
            executable: "/usr/sbin/lsof",
            arguments: [
                "-nP", "-a", "-iTCP:\(port)", "-sTCP:LISTEN", "-Fpn",
            ],
            timeout: 3,
            maximumBytes: 32 * 1024)
        if result.exitCode == 1 && result.stdout.isEmpty {
            return []
        }
        guard result.exitCode == 0 else {
            throw HelperFailure("port.query_failed", stage: "port")
        }
        var processId: Int32?
        var listeners: [PortListener] = []
        for line in result.stdout.split(separator: "\n") {
            if line.first == "p" {
                processId = Int32(line.dropFirst())
            } else if line.first == "n", let processId {
                let endpoint = String(line.dropFirst())
                let address = endpoint.hasPrefix("[")
                    ? String(endpoint.prefix(while: { $0 != "]" })) + "]"
                    : String(endpoint.split(separator: ":").first ?? "")
                listeners.append(PortListener(
                    processId: processId,
                    address: address))
            }
        }
        return listeners
    }
}

enum PathPolicy {
    static func requireDirectory(_ url: URL) throws {
        var info = stat()
        guard lstat(url.path, &info) == 0,
              (info.st_mode & S_IFMT) == S_IFDIR,
              (info.st_mode & S_IFMT) != S_IFLNK
        else {
            throw HelperFailure("bundle.path_invalid", stage: "discovery")
        }
        guard url.resolvingSymlinksInPath().path == url.path else {
            throw HelperFailure("bundle.path_invalid", stage: "discovery")
        }
    }

    static func requireRegularFile(_ url: URL) throws -> URL {
        var info = stat()
        guard lstat(url.path, &info) == 0,
              (info.st_mode & S_IFMT) == S_IFREG,
              url.resolvingSymlinksInPath().path == url.path
        else {
            throw HelperFailure("bundle.executable_invalid", stage: "discovery")
        }
        return url
    }
}

enum StableHasher {
    static func sha256(_ url: URL) throws -> String {
        let before = try metadata(url)
        let handle = try FileHandle(forReadingFrom: url)
        defer { try? handle.close() }
        var hasher = SHA256()
        while true {
            let data = try handle.read(upToCount: 1024 * 1024) ?? Data()
            if data.isEmpty { break }
            hasher.update(data: data)
        }
        let after = try metadata(url)
        guard before == after else {
            throw HelperFailure("bundle.executable_changed", stage: "hash")
        }
        return hasher.finalize().map { String(format: "%02X", $0) }.joined()
    }

    private static func metadata(_ url: URL) throws -> [Int64] {
        var info = stat()
        guard stat(url.path, &info) == 0 else {
            throw HelperFailure("bundle.executable_invalid", stage: "hash")
        }
        return [
            Int64(info.st_dev),
            Int64(info.st_ino),
            Int64(info.st_size),
            Int64(info.st_mtimespec.tv_sec),
            Int64(info.st_mtimespec.tv_nsec),
        ]
    }
}

struct SignatureFacts {
    let identifier: String
    let teamIdentifier: String
    let valid: Bool
    let hardenedRuntime: Bool
}

enum SignatureInspector {
    // Security.framework exposes the signing flags dictionary to Swift, but the
    // C CS_RUNTIME macro is not imported. Its stable ABI value is defined by
    // <Security/SecCode.h>.
    private static let codeSignatureRuntimeFlag: UInt32 = 0x0001_0000

    static func inspect(_ bundleURL: URL) throws -> SignatureFacts {
        var staticCode: SecStaticCode?
        guard SecStaticCodeCreateWithPath(
            bundleURL as CFURL,
            SecCSFlags(),
            &staticCode) == errSecSuccess,
            let staticCode
        else {
            throw HelperFailure("bundle.signature_invalid", stage: "signature")
        }
        guard SecStaticCodeCheckValidity(
            staticCode,
            SecCSFlags(rawValue: kSecCSStrictValidate),
            nil) == errSecSuccess
        else {
            throw HelperFailure("bundle.signature_invalid", stage: "signature")
        }
        var information: CFDictionary?
        guard SecCodeCopySigningInformation(
            staticCode,
            SecCSFlags(rawValue: kSecCSSigningInformation),
            &information) == errSecSuccess,
            let values = information as? [String: Any],
            let identifier = values[kSecCodeInfoIdentifier as String] as? String,
            let team = values[kSecCodeInfoTeamIdentifier as String] as? String,
            let flags = values[kSecCodeInfoFlags as String] as? UInt32
        else {
            throw HelperFailure("bundle.signature_invalid", stage: "signature")
        }
        return SignatureFacts(
            identifier: identifier,
            teamIdentifier: team,
            valid: true,
            hardenedRuntime: flags & codeSignatureRuntimeFlag != 0)
    }
}

struct RawProcess {
    let processId: Int32
    let parentProcessId: Int32
    let startedAtUtc: Date
    let executablePath: String
}

enum ProcessInspector {
    // PROC_PIDPATHINFO_MAXSIZE is a C macro that Swift cannot import.
    private static let processPathBufferSize = 4 * 1_024

    static func find(executablePath: String) throws -> [RawProcess] {
        var capacity = max(proc_listallpids(nil, 0), 0)
        guard capacity > 0 else {
            throw HelperFailure("process.enumeration_failed", stage: "process")
        }
        var pids = [pid_t](repeating: 0, count: Int(capacity))
        capacity = proc_listallpids(&pids, Int32(pids.count * MemoryLayout<pid_t>.size))
        guard capacity >= 0 else {
            throw HelperFailure("process.enumeration_failed", stage: "process")
        }
        return pids.prefix(Int(capacity)).compactMap { pid in
            guard pid > 0 else { return nil }
            var pathBuffer = [CChar](
                repeating: 0,
                count: processPathBufferSize)
            let pathLength = proc_pidpath(
                pid,
                &pathBuffer,
                UInt32(pathBuffer.count))
            guard pathLength > 0,
                  String(cString: pathBuffer) == executablePath
            else {
                return nil
            }
            var info = proc_bsdinfo()
            let expected = Int32(MemoryLayout<proc_bsdinfo>.size)
            guard proc_pidinfo(
                pid,
                PROC_PIDTBSDINFO,
                0,
                &info,
                expected) == expected
            else {
                return nil
            }
            return RawProcess(
                processId: pid,
                parentProcessId: Int32(info.pbi_ppid),
                startedAtUtc: Date(
                    timeIntervalSince1970:
                        TimeInterval(info.pbi_start_tvsec) +
                        TimeInterval(info.pbi_start_tvusec) / 1_000_000),
                executablePath: executablePath)
        }
    }
}

enum MachOInspector {
    static func architecture(_ url: URL) throws -> String {
        let data = try Data(contentsOf: url, options: [.mappedIfSafe])
        guard data.count >= 8 else {
            throw HelperFailure("bundle.architecture_invalid", stage: "hash")
        }
        let cpu = data.withUnsafeBytes {
            UInt32(littleEndian: $0.load(fromByteOffset: 4, as: UInt32.self))
        }
        guard cpu == UInt32(bitPattern: CPU_TYPE_ARM64) else {
            throw HelperFailure("bundle.architecture_invalid", stage: "hash")
        }
        return "arm64"
    }
}

struct CommandResult {
    let exitCode: Int32
    let stdout: String
}

enum CommandRunner {
    static func requireSuccess(
        executable: String,
        arguments: [String],
        timeout: TimeInterval) throws
    {
        let result = try run(
            executable: executable,
            arguments: arguments,
            timeout: timeout,
            maximumBytes: 16 * 1024)
        guard result.exitCode == 0 else {
            throw HelperFailure("external.command_failed", stage: "signature")
        }
    }

    static func run(
        executable: String,
        arguments: [String],
        timeout: TimeInterval,
        maximumBytes: Int) throws -> CommandResult
    {
        guard executable.hasPrefix("/") else {
            throw HelperFailure("external.command_invalid", stage: "command")
        }
        let process = Process()
        let output = Pipe()
        let error = Pipe()
        process.executableURL = URL(fileURLWithPath: executable)
        process.arguments = arguments
        process.standardOutput = output
        process.standardError = error
        try process.run()
        let deadline = Date().addingTimeInterval(timeout)
        while process.isRunning && Date() < deadline {
            Thread.sleep(forTimeInterval: 0.01)
        }
        if process.isRunning {
            process.terminate()
            throw HelperFailure("external.command_timeout", stage: "command")
        }
        let stdoutData = output.fileHandleForReading.readDataToEndOfFile()
        let stderrData = error.fileHandleForReading.readDataToEndOfFile()
        guard stdoutData.count <= maximumBytes,
              stderrData.count <= maximumBytes
        else {
            throw HelperFailure("external.output_too_large", stage: "command")
        }
        return CommandResult(
            exitCode: process.terminationStatus,
            stdout: String(decoding: stdoutData, as: UTF8.self))
    }
}
