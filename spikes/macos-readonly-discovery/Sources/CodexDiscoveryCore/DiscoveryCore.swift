import AppKit
import CryptoKit
import Darwin
import Foundation
import Security

public enum DiscoveryConstants {
    public static let schemaVersion = 1
    public static let bundleIdentifier = "com.openai.codex"
    public static let teamIdentifier = "2DC432GLL2"
    public static let inspectorPort = 9229
    public static let codesignPath = "/usr/bin/codesign"
    public static let spctlPath = "/usr/sbin/spctl"
    public static let lsofPath = "/usr/sbin/lsof"
}

private enum DarwinContract {
    // Security.framework/Headers/CSCommon.h defines kSecCodeSignatureRuntime as 0x10000.
    static let codeSignatureRuntimeFlag: UInt32 = 0x0001_0000

    // sys/proc_info.h defines PROC_PIDPATHINFO_MAXSIZE as 4 * MAXPATHLEN,
    // and sys/param.h defines MAXPATHLEN as PATH_MAX.
    static let processPathBufferSize = 4 * Int(PATH_MAX)
}

public struct DiscoveryFailure: Error, Equatable {
    public let code: String
    public let stage: String
    public let message: String
    public let exitCode: Int32

    public init(
        code: String,
        stage: String,
        message: String,
        exitCode: Int32
    ) {
        self.code = code
        self.stage = stage
        self.message = message
        self.exitCode = exitCode
    }
}

public struct ErrorBody: Codable, Equatable {
    public let code: String
    public let stage: String
    public let message: String
}

public struct DiscoveryDocument: Codable, Equatable {
    public let schemaVersion: Int
    public let status: String
    public let observedAtUtc: String
    public let result: DiscoveryResult?
    public let error: ErrorBody?

    public static func success(_ result: DiscoveryResult, now: Date = Date()) -> Self {
        Self(
            schemaVersion: DiscoveryConstants.schemaVersion,
            status: "ok",
            observedAtUtc: Timestamp.format(now),
            result: result,
            error: nil)
    }

    public static func failure(_ failure: DiscoveryFailure, now: Date = Date()) -> Self {
        Self(
            schemaVersion: DiscoveryConstants.schemaVersion,
            status: "error",
            observedAtUtc: Timestamp.format(now),
            result: nil,
            error: ErrorBody(
                code: failure.code,
                stage: failure.stage,
                message: failure.message))
    }
}

public struct DiscoveryResult: Codable, Equatable {
    public let bundle: BundleObservation
    public let signature: SignatureObservation
    public let processes: ProcessObservation
    public let port9229: PortObservation
}

public struct BundleObservation: Codable, Equatable {
    public let path: String
    public let bundleIdentifier: String
    public let shortVersion: String
    public let bundleVersion: String
    public let mainExecutablePath: String
    public let mainExecutableSha256: String
}

public struct SignatureObservation: Codable, Equatable {
    public let valid: Bool
    public let signingIdentifier: String
    public let teamIdentifier: String
    public let subjectSummary: String
    public let hardenedRuntime: Bool
    public let gatekeeperAccepted: Bool
    public let notarizationStatus: String
}

public enum ProcessKind: String, Codable, CaseIterable {
    case main
    case renderer
    case service
    case gpu
    case alerts
    case resourcesCodex
    case otherBundleProcess
}

public enum ProcessArchitecture: String, Codable {
    case arm64
    case x86_64
}

public struct ProcessRecord: Codable, Equatable {
    public let kind: ProcessKind
    public let processId: Int32
    public let parentProcessId: Int32
    public let startedAtUtc: String
    public let architecture: ProcessArchitecture
    public let executablePath: String

    public init(
        kind: ProcessKind,
        processId: Int32,
        parentProcessId: Int32,
        startedAtUtc: String,
        architecture: ProcessArchitecture,
        executablePath: String
    ) {
        self.kind = kind
        self.processId = processId
        self.parentProcessId = parentProcessId
        self.startedAtUtc = startedAtUtc
        self.architecture = architecture
        self.executablePath = executablePath
    }
}

public struct ProcessObservation: Codable, Equatable {
    public let state: String
    public let main: ProcessRecord?
    public let children: [ProcessRecord]
}

public struct PortListener: Codable, Equatable {
    public let family: String
    public let localAddress: String
    public let port: Int
    public let processId: Int32
}

public struct PortObservation: Codable, Equatable {
    public let state: String
    public let listeners: [PortListener]
}

public enum Timestamp {
    private static let formatter: ISO8601DateFormatter = {
        let formatter = ISO8601DateFormatter()
        formatter.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
        formatter.timeZone = TimeZone(secondsFromGMT: 0)
        return formatter
    }()

    public static func format(_ date: Date) -> String {
        formatter.string(from: date)
    }
}

public enum JSONOutput {
    public static let forbiddenKeys: Set<String> = [
        "argv",
        "commandline",
        "environment",
        "url",
        "page",
        "dom",
        "token",
        "credential",
        "conversation",
    ]

    public static func encode<T: Encodable>(
        _ value: T,
        pretty: Bool = false
    ) throws -> Data {
        let encoder = JSONEncoder()
        encoder.outputFormatting = pretty
            ? [.prettyPrinted, .sortedKeys, .withoutEscapingSlashes]
            : [.sortedKeys, .withoutEscapingSlashes]
        let data = try encoder.encode(value)
        let object = try JSONSerialization.jsonObject(with: data)
        try validatePrivacyKeys(object)
        return data
    }

    public static func validatePrivacyKeys(_ value: Any) throws {
        if let dictionary = value as? [String: Any] {
            for (key, child) in dictionary {
                if forbiddenKeys.contains(key.lowercased()) {
                    throw DiscoveryFailure(
                        code: "output.privacy_field_forbidden",
                        stage: "output",
                        message: "JSON output contains a forbidden field.",
                        exitCode: 80)
                }
                try validatePrivacyKeys(child)
            }
        } else if let array = value as? [Any] {
            for child in array {
                try validatePrivacyKeys(child)
            }
        }
    }
}

public enum DiscoverySchema {
    public static func data(pretty: Bool) throws -> Data {
        let object = try JSONSerialization.jsonObject(with: Data(document.utf8))
        let options: JSONSerialization.WritingOptions = pretty
            ? [.prettyPrinted, .sortedKeys, .withoutEscapingSlashes]
            : [.sortedKeys, .withoutEscapingSlashes]
        return try JSONSerialization.data(withJSONObject: object, options: options)
    }

    private static let document = """
    {
      "$schema": "https://json-schema.org/draft/2020-12/schema",
      "$id": "https://codexthemestudio.local/schema/macos-discovery-v1.json",
      "type": "object",
      "additionalProperties": false,
      "required": ["schemaVersion", "status", "observedAtUtc", "result", "error"],
      "properties": {
        "schemaVersion": { "const": 1 },
        "status": { "enum": ["ok", "error"] },
        "observedAtUtc": { "type": "string", "format": "date-time" },
        "result": {
          "type": ["object", "null"],
          "additionalProperties": false,
          "required": ["bundle", "signature", "processes", "port9229"],
          "properties": {
            "bundle": {
              "type": "object",
              "additionalProperties": false,
              "required": ["path", "bundleIdentifier", "shortVersion", "bundleVersion", "mainExecutablePath", "mainExecutableSha256"],
              "properties": {
                "path": { "type": "string", "minLength": 1 },
                "bundleIdentifier": { "const": "com.openai.codex" },
                "shortVersion": { "type": "string", "minLength": 1 },
                "bundleVersion": { "type": "string", "minLength": 1 },
                "mainExecutablePath": { "type": "string", "minLength": 1 },
                "mainExecutableSha256": { "type": "string", "pattern": "^[a-f0-9]{64}$" }
              }
            },
            "signature": {
              "type": "object",
              "additionalProperties": false,
              "required": ["valid", "signingIdentifier", "teamIdentifier", "subjectSummary", "hardenedRuntime", "gatekeeperAccepted", "notarizationStatus"],
              "properties": {
                "valid": { "const": true },
                "signingIdentifier": { "const": "com.openai.codex" },
                "teamIdentifier": { "const": "2DC432GLL2" },
                "subjectSummary": { "type": "string", "minLength": 1 },
                "hardenedRuntime": { "const": true },
                "gatekeeperAccepted": { "const": true },
                "notarizationStatus": { "const": "accepted" }
              }
            },
            "processes": {
              "type": "object",
              "additionalProperties": false,
              "required": ["state", "main", "children"],
              "properties": {
                "state": { "enum": ["notRunning", "running"] },
                "main": { "oneOf": [{ "$ref": "#/$defs/process" }, { "type": "null" }] },
                "children": { "type": "array", "items": { "$ref": "#/$defs/process" } }
              }
            },
            "port9229": {
              "type": "object",
              "additionalProperties": false,
              "required": ["state", "listeners"],
              "properties": {
                "state": { "enum": ["free", "knownCodexListener"] },
                "listeners": {
                  "type": "array",
                  "items": {
                    "type": "object",
                    "additionalProperties": false,
                    "required": ["family", "localAddress", "port", "processId"],
                    "properties": {
                      "family": { "enum": ["IPv4", "IPv6"] },
                      "localAddress": { "enum": ["127.0.0.1", "::1"] },
                      "port": { "const": 9229 },
                      "processId": { "type": "integer", "minimum": 1 }
                    }
                  }
                }
              }
            }
          }
        },
        "error": {
          "type": ["object", "null"],
          "additionalProperties": false,
          "required": ["code", "stage", "message"],
          "properties": {
            "code": { "type": "string", "minLength": 1 },
            "stage": { "type": "string", "minLength": 1 },
            "message": { "type": "string", "minLength": 1 }
          }
        }
      },
      "$defs": {
        "process": {
          "type": "object",
          "additionalProperties": false,
          "required": ["kind", "processId", "parentProcessId", "startedAtUtc", "architecture", "executablePath"],
          "properties": {
            "kind": { "enum": ["main", "renderer", "service", "gpu", "alerts", "resourcesCodex", "otherBundleProcess"] },
            "processId": { "type": "integer", "minimum": 1 },
            "parentProcessId": { "type": "integer", "minimum": 0 },
            "startedAtUtc": { "type": "string", "format": "date-time" },
            "architecture": { "enum": ["arm64", "x86_64"] },
            "executablePath": { "type": "string", "minLength": 1 }
          }
        }
      }
    }
    """
}

public protocol BundleLocating {
    func candidates(bundleIdentifier: String) throws -> [String]
}

public struct LaunchServicesBundleLocator: BundleLocating {
    public init() {}

    public func candidates(bundleIdentifier: String) throws -> [String] {
        NSWorkspace.shared
            .urlsForApplications(withBundleIdentifier: bundleIdentifier)
            .map(\.path)
    }
}

public enum BundleSelectionPolicy {
    public static func select(_ candidates: [String]) throws -> String {
        let canonical = try Set(candidates.map(PathSecurity.realPath))
            .sorted()
        if canonical.isEmpty {
            throw DiscoveryFailure(
                code: "bundle.not_found",
                stage: "bundle",
                message: "No registered Codex bundle was found.",
                exitCode: 10)
        }
        guard canonical.count == 1, let path = canonical.first else {
            throw DiscoveryFailure(
                code: "bundle.ambiguous",
                stage: "bundle",
                message: "Multiple Codex bundles were found.",
                exitCode: 11)
        }
        return path
    }
}

public enum PathSecurity {
    public static func realPath(_ path: String) throws -> String {
        guard path.utf8.count < Int(PATH_MAX) else {
            throw pathFailure("Path exceeds PATH_MAX.")
        }
        var buffer = [CChar](repeating: 0, count: Int(PATH_MAX))
        guard Darwin.realpath(path, &buffer) != nil else {
            throw pathFailure("Path cannot be resolved.")
        }
        return String(cString: buffer)
    }

    public static func requireRegularNonSymlinkFile(
        _ path: String,
        within root: String
    ) throws -> String {
        var metadata = stat()
        guard Darwin.lstat(path, &metadata) == 0 else {
            throw pathFailure("Main executable metadata is unavailable.")
        }
        guard (metadata.st_mode & S_IFMT) == S_IFREG else {
            throw pathFailure("Main executable is not a regular file.")
        }
        let canonical = try realPath(path)
        let canonicalRoot = try realPath(root)
        let prefix = canonicalRoot.hasSuffix("/") ? canonicalRoot : canonicalRoot + "/"
        guard canonical.hasPrefix(prefix) else {
            throw pathFailure("Main executable resolves outside the bundle.")
        }
        return canonical
    }

    public static func isWithin(_ path: String, root: String) -> Bool {
        let prefix = root.hasSuffix("/") ? root : root + "/"
        return path.hasPrefix(prefix)
    }

    private static func pathFailure(_ message: String) -> DiscoveryFailure {
        DiscoveryFailure(
            code: "executable.path_invalid",
            stage: "executable",
            message: message,
            exitCode: 14)
    }
}

public struct BundleInspection: Equatable {
    public let bundlePath: String
    public let identifier: String
    public let shortVersion: String
    public let bundleVersion: String
    public let executablePath: String
}

public enum BundleInspector {
    public static func inspect(bundlePath: String) throws -> BundleInspection {
        let canonicalBundle = try PathSecurity.realPath(bundlePath)
        var bundleMetadata = stat()
        guard Darwin.lstat(bundlePath, &bundleMetadata) == 0,
              (bundleMetadata.st_mode & S_IFMT) == S_IFDIR
        else {
            throw DiscoveryFailure(
                code: "bundle.path_invalid",
                stage: "bundle",
                message: "Bundle path is not a directory.",
                exitCode: 12)
        }

        let infoPath = canonicalBundle + "/Contents/Info.plist"
        let data: Data
        do {
            data = try Data(contentsOf: URL(fileURLWithPath: infoPath), options: [.mappedIfSafe])
        } catch {
            throw metadataFailure("Info.plist cannot be read.")
        }
        let propertyList: Any
        do {
            propertyList = try PropertyListSerialization.propertyList(
                from: data,
                options: [],
                format: nil)
        } catch {
            throw metadataFailure("Info.plist is invalid.")
        }
        guard let values = propertyList as? [String: Any],
              let identifier = nonEmptyString(values["CFBundleIdentifier"]),
              let shortVersion = nonEmptyString(values["CFBundleShortVersionString"]),
              let bundleVersion = nonEmptyString(values["CFBundleVersion"]),
              let executable = nonEmptyString(values["CFBundleExecutable"])
        else {
            throw metadataFailure("Required Info.plist metadata is missing.")
        }
        guard identifier == DiscoveryConstants.bundleIdentifier else {
            throw DiscoveryFailure(
                code: "bundle.identifier_mismatch",
                stage: "bundle",
                message: "Bundle identifier does not match the trusted value.",
                exitCode: 13)
        }
        guard executable != ".",
              executable != "..",
              !executable.contains("/"),
              !executable.contains("\\")
        else {
            throw DiscoveryFailure(
                code: "executable.path_invalid",
                stage: "executable",
                message: "CFBundleExecutable is not a single file name.",
                exitCode: 14)
        }
        let executablePath = try PathSecurity.requireRegularNonSymlinkFile(
            canonicalBundle + "/Contents/MacOS/" + executable,
            within: canonicalBundle)
        return BundleInspection(
            bundlePath: canonicalBundle,
            identifier: identifier,
            shortVersion: shortVersion,
            bundleVersion: bundleVersion,
            executablePath: executablePath)
    }

    private static func nonEmptyString(_ value: Any?) -> String? {
        guard let string = value as? String,
              !string.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty
        else {
            return nil
        }
        return string
    }

    private static func metadataFailure(_ message: String) -> DiscoveryFailure {
        DiscoveryFailure(
            code: "bundle.metadata_missing",
            stage: "bundle",
            message: message,
            exitCode: 13)
    }
}

private struct FileIdentity: Equatable {
    let device: dev_t
    let inode: ino_t
    let size: off_t
    let modifiedSeconds: Int
    let modifiedNanoseconds: Int

    init(_ value: stat) {
        device = value.st_dev
        inode = value.st_ino
        size = value.st_size
        modifiedSeconds = value.st_mtimespec.tv_sec
        modifiedNanoseconds = value.st_mtimespec.tv_nsec
    }
}

public enum StableFileHasher {
    public static func sha256(
        path: String,
        afterRead: (() throws -> Void)? = nil
    ) throws -> String {
        let descriptor = Darwin.open(path, O_RDONLY | O_CLOEXEC | O_NOFOLLOW)
        guard descriptor >= 0 else {
            throw hashFailure("Main executable cannot be opened safely.")
        }
        defer { Darwin.close(descriptor) }

        var before = stat()
        guard Darwin.fstat(descriptor, &before) == 0,
              (before.st_mode & S_IFMT) == S_IFREG
        else {
            throw hashFailure("Main executable identity is unavailable.")
        }

        var hasher = SHA256()
        let handle = FileHandle(fileDescriptor: descriptor, closeOnDealloc: false)
        do {
            while let chunk = try handle.read(upToCount: 1024 * 1024),
                  !chunk.isEmpty
            {
                hasher.update(data: chunk)
            }
            try afterRead?()
        } catch let failure as DiscoveryFailure {
            throw failure
        } catch {
            throw hashFailure("Main executable could not be hashed.")
        }

        var after = stat()
        guard Darwin.fstat(descriptor, &after) == 0,
              FileIdentity(before) == FileIdentity(after)
        else {
            throw DiscoveryFailure(
                code: "executable.identity_changed",
                stage: "hash",
                message: "Main executable changed while it was being hashed.",
                exitCode: 20)
        }
        return hasher.finalize().map { String(format: "%02x", $0) }.joined()
    }

    private static func hashFailure(_ message: String) -> DiscoveryFailure {
        DiscoveryFailure(
            code: "executable.hash_failed",
            stage: "hash",
            message: message,
            exitCode: 20)
    }
}

public struct CommandSpec: Equatable {
    public let executable: String
    public let arguments: [String]
    public let timeoutSeconds: TimeInterval
    public let maximumOutputBytes: Int

    public init(
        executable: String,
        arguments: [String],
        timeoutSeconds: TimeInterval = 10,
        maximumOutputBytes: Int = 64 * 1024
    ) {
        self.executable = executable
        self.arguments = arguments
        self.timeoutSeconds = timeoutSeconds
        self.maximumOutputBytes = maximumOutputBytes
    }
}

public struct CommandResult: Equatable {
    public let exitCode: Int32
    public let standardOutput: Data
    public let standardError: Data

    public init(exitCode: Int32, standardOutput: Data, standardError: Data) {
        self.exitCode = exitCode
        self.standardOutput = standardOutput
        self.standardError = standardError
    }
}

public protocol CommandRunning {
    func run(_ spec: CommandSpec) throws -> CommandResult
}

public enum ExternalCommandPolicy {
    public static let allowedExecutables: Set<String> = [
        DiscoveryConstants.codesignPath,
        DiscoveryConstants.spctlPath,
        DiscoveryConstants.lsofPath,
    ]

    public static func validate(_ spec: CommandSpec) throws {
        guard spec.executable.hasPrefix("/"),
              allowedExecutables.contains(spec.executable)
        else {
            throw commandFailure(
                code: "external_tool.path_rejected",
                message: "External command path is not allowed.")
        }
        guard spec.timeoutSeconds > 0,
              spec.timeoutSeconds <= 30,
              spec.maximumOutputBytes > 0,
              spec.maximumOutputBytes <= 256 * 1024,
              spec.arguments.allSatisfy({ !$0.contains("\0") })
        else {
            throw commandFailure(
                code: "external_tool.policy_invalid",
                message: "External command limits are invalid.")
        }
    }

    private static func commandFailure(
        code: String,
        message: String
    ) -> DiscoveryFailure {
        DiscoveryFailure(
            code: code,
            stage: "externalTool",
            message: message,
            exitCode: 70)
    }
}

public final class ProcessCommandRunner: CommandRunning {
    public init() {}

    public func run(_ spec: CommandSpec) throws -> CommandResult {
        try ExternalCommandPolicy.validate(spec)
        let process = Process()
        process.executableURL = URL(fileURLWithPath: spec.executable)
        process.arguments = spec.arguments
        process.environment = [
            "LANG": "C",
            "LC_ALL": "C",
            "PATH": "/usr/bin:/bin:/usr/sbin:/sbin",
        ]

        let outputPipe = Pipe()
        let errorPipe = Pipe()
        process.standardOutput = outputPipe
        process.standardError = errorPipe

        do {
            try process.run()
        } catch {
            throw DiscoveryFailure(
                code: "external_tool.launch_failed",
                stage: "externalTool",
                message: "An approved external tool could not be launched.",
                exitCode: 70)
        }

        let group = DispatchGroup()
        let lock = NSLock()
        var output = Data()
        var errorOutput = Data()
        var exceeded = false

        func collect(_ handle: FileHandle, into target: @escaping (Data) -> Void) {
            group.enter()
            DispatchQueue.global(qos: .utility).async {
                while true {
                    let chunk = handle.availableData
                    if chunk.isEmpty {
                        break
                    }
                    lock.lock()
                    target(chunk)
                    if output.count + errorOutput.count > spec.maximumOutputBytes {
                        exceeded = true
                        if process.isRunning {
                            process.terminate()
                        }
                    }
                    lock.unlock()
                    if exceeded {
                        break
                    }
                }
                group.leave()
            }
        }

        collect(outputPipe.fileHandleForReading) { output.append($0) }
        collect(errorPipe.fileHandleForReading) { errorOutput.append($0) }

        let deadline = DispatchTime.now() + spec.timeoutSeconds
        if process.isRunning {
            let semaphore = DispatchSemaphore(value: 0)
            process.terminationHandler = { _ in semaphore.signal() }
            if semaphore.wait(timeout: deadline) == .timedOut {
                process.terminate()
                group.wait()
                throw DiscoveryFailure(
                    code: "external_tool.timeout",
                    stage: "externalTool",
                    message: "An approved external tool timed out.",
                    exitCode: 70)
            }
        }
        process.waitUntilExit()
        group.wait()
        if exceeded {
            throw DiscoveryFailure(
                code: "external_tool.output_too_large",
                stage: "externalTool",
                message: "An approved external tool exceeded its output limit.",
                exitCode: 70)
        }
        return CommandResult(
            exitCode: process.terminationStatus,
            standardOutput: output,
            standardError: errorOutput)
    }
}

public struct NativeSignatureFacts: Equatable {
    public let valid: Bool
    public let signingIdentifier: String?
    public let teamIdentifier: String?
    public let subjectSummary: String?
    public let hardenedRuntime: Bool

    public init(
        valid: Bool,
        signingIdentifier: String?,
        teamIdentifier: String?,
        subjectSummary: String?,
        hardenedRuntime: Bool
    ) {
        self.valid = valid
        self.signingIdentifier = signingIdentifier
        self.teamIdentifier = teamIdentifier
        self.subjectSummary = subjectSummary
        self.hardenedRuntime = hardenedRuntime
    }
}

public enum SignaturePolicy {
    public static func validate(_ facts: NativeSignatureFacts) throws {
        guard facts.valid else {
            throw signatureFailure("signature.invalid", "Code signature validation failed.", 30)
        }
        guard facts.signingIdentifier == DiscoveryConstants.bundleIdentifier else {
            throw signatureFailure(
                "signature.identifier_mismatch",
                "Signing identifier does not match the trusted value.",
                31)
        }
        guard facts.teamIdentifier == DiscoveryConstants.teamIdentifier else {
            throw signatureFailure(
                "signature.team_mismatch",
                "Team identifier does not match the trusted value.",
                31)
        }
        guard let subject = facts.subjectSummary, !subject.isEmpty else {
            throw signatureFailure(
                "signature.subject_missing",
                "Leaf certificate subject is unavailable.",
                31)
        }
        guard facts.hardenedRuntime else {
            throw signatureFailure(
                "signature.hardened_runtime_missing",
                "Hardened Runtime is not enabled.",
                32)
        }
    }

    private static func signatureFailure(
        _ code: String,
        _ message: String,
        _ exitCode: Int32
    ) -> DiscoveryFailure {
        DiscoveryFailure(
            code: code,
            stage: "signature",
            message: message,
            exitCode: exitCode)
    }
}

public enum NativeSignatureInspector {
    public static func inspect(path: String) throws -> NativeSignatureFacts {
        var code: SecStaticCode?
        let createStatus = SecStaticCodeCreateWithPath(
            URL(fileURLWithPath: path) as CFURL,
            SecCSFlags(),
            &code)
        guard createStatus == errSecSuccess, let code else {
            return NativeSignatureFacts(
                valid: false,
                signingIdentifier: nil,
                teamIdentifier: nil,
                subjectSummary: nil,
                hardenedRuntime: false)
        }

        let requirementText =
            "anchor apple generic and identifier \"\(DiscoveryConstants.bundleIdentifier)\" " +
            "and certificate leaf[subject.OU] = \"\(DiscoveryConstants.teamIdentifier)\""
        var requirement: SecRequirement?
        let requirementStatus = SecRequirementCreateWithString(
            requirementText as CFString,
            SecCSFlags(),
            &requirement)
        guard requirementStatus == errSecSuccess, let requirement else {
            throw DiscoveryFailure(
                code: "signature.requirement_invalid",
                stage: "signature",
                message: "Trusted code requirement could not be created.",
                exitCode: 30)
        }

        var validityError: Unmanaged<CFError>?
        let validationFlags = SecCSFlags(
            rawValue: UInt32(kSecCSStrictValidate | kSecCSCheckAllArchitectures))
        let validityStatus = SecStaticCodeCheckValidityWithErrors(
            code,
            validationFlags,
            requirement,
            &validityError)

        var information: CFDictionary?
        let informationStatus = SecCodeCopySigningInformation(
            code,
            SecCSFlags(rawValue: UInt32(kSecCSSigningInformation)),
            &information)
        guard informationStatus == errSecSuccess,
              let values = information as? [String: Any]
        else {
            return NativeSignatureFacts(
                valid: false,
                signingIdentifier: nil,
                teamIdentifier: nil,
                subjectSummary: nil,
                hardenedRuntime: false)
        }

        let identifier = values[kSecCodeInfoIdentifier as String] as? String
        let team = values[kSecCodeInfoTeamIdentifier as String] as? String
        let flags = (values[kSecCodeInfoFlags as String] as? NSNumber)?.uint32Value ?? 0
        let hardened = (flags & DarwinContract.codeSignatureRuntimeFlag) != 0
        var subject: String?
        if let certificates = values[kSecCodeInfoCertificates as String] as? [SecCertificate],
           let leaf = certificates.first,
           let summary = SecCertificateCopySubjectSummary(leaf)
        {
            subject = summary as String
        }

        return NativeSignatureFacts(
            valid: validityStatus == errSecSuccess,
            signingIdentifier: identifier,
            teamIdentifier: team,
            subjectSummary: subject,
            hardenedRuntime: hardened)
    }
}

public struct GatekeeperFacts: Equatable {
    public let accepted: Bool
    public let notarized: Bool

    public init(accepted: Bool, notarized: Bool) {
        self.accepted = accepted
        self.notarized = notarized
    }
}

public enum GatekeeperParser {
    public static func parse(_ data: Data, exitCode: Int32) throws -> GatekeeperFacts {
        guard exitCode == 0 else {
            throw DiscoveryFailure(
                code: "gatekeeper.denied",
                stage: "gatekeeper",
                message: "Gatekeeper denied execution.",
                exitCode: 33)
        }
        let object: Any
        do {
            object = try PropertyListSerialization.propertyList(
                from: data,
                options: [],
                format: nil)
        } catch {
            throw formatFailure()
        }
        guard let dictionary = object as? [String: Any],
              let verdict = dictionary["assessment:verdict"] as? Bool,
              let authority = dictionary["assessment:authority"] as? [String: Any],
              let source = authority["assessment:authority:source"] as? String
        else {
            throw formatFailure()
        }
        guard verdict else {
            throw DiscoveryFailure(
                code: "gatekeeper.denied",
                stage: "gatekeeper",
                message: "Gatekeeper verdict was not accepted.",
                exitCode: 33)
        }
        guard source == "Notarized Developer ID" else {
            throw DiscoveryFailure(
                code: "notarization.unconfirmed",
                stage: "notarization",
                message: "Notarization could not be confirmed.",
                exitCode: 34)
        }
        return GatekeeperFacts(accepted: true, notarized: true)
    }

    private static func formatFailure() -> DiscoveryFailure {
        DiscoveryFailure(
            code: "gatekeeper.assessment_failed",
            stage: "gatekeeper",
            message: "Gatekeeper returned an unknown assessment format.",
            exitCode: 33)
    }
}

public struct RawProcessIdentity: Equatable {
    public let processId: Int32
    public let parentProcessId: Int32
    public let startSeconds: UInt64
    public let startMicroseconds: UInt64
    public let executablePath: String
    public let architecture: ProcessArchitecture

    public init(
        processId: Int32,
        parentProcessId: Int32,
        startSeconds: UInt64,
        startMicroseconds: UInt64,
        executablePath: String,
        architecture: ProcessArchitecture
    ) {
        self.processId = processId
        self.parentProcessId = parentProcessId
        self.startSeconds = startSeconds
        self.startMicroseconds = startMicroseconds
        self.executablePath = executablePath
        self.architecture = architecture
    }
}

public enum ProcessPolicy {
    public static func classify(
        executablePath: String,
        bundlePath: String,
        mainExecutablePath: String
    ) -> ProcessKind? {
        if executablePath == mainExecutablePath {
            return .main
        }
        guard PathSecurity.isWithin(executablePath, root: bundlePath) else {
            return nil
        }
        if executablePath.contains(
            "/Helpers/Codex (Renderer).app/Contents/MacOS/Codex (Renderer)")
        {
            return .renderer
        }
        if executablePath.contains(
            "/Helpers/Codex (Service).app/Contents/MacOS/Codex (Service)")
        {
            return .service
        }
        if executablePath.contains(
            "/Helpers/Codex (GPU).app/Contents/MacOS/Codex (GPU)")
        {
            return .gpu
        }
        if executablePath.contains(
            "/Helpers/Codex (Alerts).app/Contents/MacOS/Codex (Alerts)")
        {
            return .alerts
        }
        if executablePath == bundlePath + "/Contents/Resources/codex" {
            return .resourcesCodex
        }
        return .otherBundleProcess
    }

    public static func evaluate(
        identities: [RawProcessIdentity],
        bundlePath: String,
        mainExecutablePath: String
    ) throws -> ProcessObservation {
        let records = identities.compactMap { identity -> ProcessRecord? in
            guard let kind = classify(
                executablePath: identity.executablePath,
                bundlePath: bundlePath,
                mainExecutablePath: mainExecutablePath)
            else {
                return nil
            }
            let date = Date(
                timeIntervalSince1970:
                    TimeInterval(identity.startSeconds) +
                    TimeInterval(identity.startMicroseconds) / 1_000_000)
            return ProcessRecord(
                kind: kind,
                processId: identity.processId,
                parentProcessId: identity.parentProcessId,
                startedAtUtc: Timestamp.format(date),
                architecture: identity.architecture,
                executablePath: identity.executablePath)
        }.sorted { $0.processId < $1.processId }

        let mains = records.filter { $0.kind == .main }
        guard mains.count <= 1 else {
            throw DiscoveryFailure(
                code: "process.main_ambiguous",
                stage: "process",
                message: "Multiple indistinguishable Codex main processes were found.",
                exitCode: 41)
        }
        return ProcessObservation(
            state: mains.isEmpty ? "notRunning" : "running",
            main: mains.first,
            children: records.filter { $0.kind != .main })
    }

    public static func requireStable(
        before: RawProcessIdentity,
        after: RawProcessIdentity
    ) throws {
        guard before == after else {
            throw DiscoveryFailure(
                code: "process.identity_changed",
                stage: "process",
                message: "A process identity changed during discovery.",
                exitCode: 42)
        }
    }
}

public enum NativeProcessInspector {
    public static func inspect(
        bundlePath: String,
        mainExecutablePath: String
    ) throws -> ProcessObservation {
        var capacity = max(Int(proc_listallpids(nil, 0)), 256)
        var processIds = [Int32](repeating: 0, count: capacity)
        let bytes = processIds.withUnsafeMutableBytes {
            proc_listallpids($0.baseAddress, Int32($0.count))
        }
        guard bytes >= 0 else {
            throw processFailure(
                "process.enumeration_failed",
                "Process enumeration failed.",
                40)
        }
        capacity = Int(bytes) / MemoryLayout<Int32>.size
        let candidateIds = processIds.prefix(capacity).filter { $0 > 0 }

        var identities: [RawProcessIdentity] = []
        for processId in candidateIds {
            guard let before = snapshot(processId: processId) else {
                continue
            }
            guard ProcessPolicy.classify(
                executablePath: before.executablePath,
                bundlePath: bundlePath,
                mainExecutablePath: mainExecutablePath) != nil
            else {
                continue
            }
            guard let after = snapshot(processId: processId) else {
                throw processFailure(
                    "process.identity_changed",
                    "A candidate process exited during discovery.",
                    42)
            }
            try ProcessPolicy.requireStable(before: before, after: after)
            identities.append(before)
        }
        return try ProcessPolicy.evaluate(
            identities: identities,
            bundlePath: bundlePath,
            mainExecutablePath: mainExecutablePath)
    }

    private static func snapshot(processId: Int32) -> RawProcessIdentity? {
        var pathBuffer = [CChar](
            repeating: 0,
            count: DarwinContract.processPathBufferSize)
        let pathLength = proc_pidpath(
            processId,
            &pathBuffer,
            UInt32(pathBuffer.count))
        guard pathLength > 0 else {
            return nil
        }
        let path = String(cString: pathBuffer)

        var info = proc_bsdinfo()
        let infoSize = proc_pidinfo(
            processId,
            PROC_PIDTBSDINFO,
            0,
            &info,
            Int32(MemoryLayout<proc_bsdinfo>.size))
        guard infoSize == MemoryLayout<proc_bsdinfo>.size,
              info.pbi_pid == UInt32(processId),
              info.pbi_start_tvsec > 0
        else {
            return nil
        }
        guard let architecture = architecture(processId: processId) else {
            return nil
        }
        return RawProcessIdentity(
            processId: processId,
            parentProcessId: Int32(info.pbi_ppid),
            startSeconds: info.pbi_start_tvsec,
            startMicroseconds: info.pbi_start_tvusec,
            executablePath: path,
            architecture: architecture)
    }

    private static func architecture(processId: Int32) -> ProcessArchitecture? {
        var hostArm64: Int32 = 0
        var hostSize = MemoryLayout<Int32>.size
        guard sysctlbyname(
            "hw.optional.arm64",
            &hostArm64,
            &hostSize,
            nil,
            0) == 0
        else {
            return nil
        }
        if hostArm64 == 0 {
            return .x86_64
        }

        var processInfo = kinfo_proc()
        var processInfoSize = MemoryLayout<kinfo_proc>.size
        var mib = [CTL_KERN, KERN_PROC, KERN_PROC_PID, processId]
        let status = mib.withUnsafeMutableBufferPointer {
            sysctl(
                $0.baseAddress,
                u_int($0.count),
                &processInfo,
                &processInfoSize,
                nil,
                0)
        }
        guard status == 0 else {
            return nil
        }
        return (processInfo.kp_proc.p_flag & P_TRANSLATED) != 0
            ? .x86_64
            : .arm64
    }

    private static func processFailure(
        _ code: String,
        _ message: String,
        _ exitCode: Int32
    ) -> DiscoveryFailure {
        DiscoveryFailure(
            code: code,
            stage: "process",
            message: message,
            exitCode: exitCode)
    }
}

public struct ParsedPortListener: Equatable {
    public let processId: Int32
    public let family: String
    public let address: String

    public init(processId: Int32, family: String, address: String) {
        self.processId = processId
        self.family = family
        self.address = address
    }
}

public enum LsofParser {
    public static func parse(_ text: String) throws -> [ParsedPortListener] {
        var currentProcess: Int32?
        var currentFamily: String?
        var listeners: [ParsedPortListener] = []
        var sawListenState = false

        for rawLine in text.split(whereSeparator: \.isNewline) {
            let line = String(rawLine)
            guard let prefix = line.first else { continue }
            let value = String(line.dropFirst())
            switch prefix {
            case "p":
                guard let processId = Int32(value), processId > 0 else {
                    throw invalidFormat()
                }
                currentProcess = processId
                currentFamily = nil
            case "t":
                guard value == "IPv4" || value == "IPv6" else {
                    throw invalidFormat()
                }
                currentFamily = value
            case "T":
                if value == "ST=LISTEN" {
                    sawListenState = true
                }
            case "n":
                guard let processId = currentProcess,
                      let family = currentFamily,
                      let address = parseAddress(value, family: family)
                else {
                    throw invalidFormat()
                }
                listeners.append(ParsedPortListener(
                    processId: processId,
                    family: family,
                    address: address))
            case "c", "f":
                continue
            default:
                throw invalidFormat()
            }
        }
        if !listeners.isEmpty && !sawListenState {
            throw invalidFormat()
        }
        return listeners
    }

    private static func parseAddress(_ value: String, family: String) -> String? {
        if family == "IPv4", value == "127.0.0.1:\(DiscoveryConstants.inspectorPort)" {
            return "127.0.0.1"
        }
        if family == "IPv6", value == "[::1]:\(DiscoveryConstants.inspectorPort)" {
            return "::1"
        }
        if value.hasSuffix(":\(DiscoveryConstants.inspectorPort)") {
            return value
        }
        return nil
    }

    private static func invalidFormat() -> DiscoveryFailure {
        DiscoveryFailure(
            code: "external_tool.protocol_invalid",
            stage: "port",
            message: "lsof returned an unknown field format.",
            exitCode: 50)
    }
}

public enum PortPolicy {
    public static func evaluate(
        listeners: [ParsedPortListener],
        trustedProcessIds: Set<Int32>
    ) throws -> PortObservation {
        if listeners.isEmpty {
            return PortObservation(state: "free", listeners: [])
        }
        let owners = Set(listeners.map(\.processId))
        guard owners.count == 1 else {
            throw portFailure(
                "port.listener_ambiguous",
                "Port 9229 has multiple listener owners.")
        }
        guard owners.isSubset(of: trustedProcessIds) else {
            throw portFailure(
                "port.listener_owner_unknown",
                "Port 9229 is owned by an unknown process.")
        }
        var output: [PortListener] = []
        for listener in listeners {
            guard (listener.family == "IPv4" && listener.address == "127.0.0.1") ||
                    (listener.family == "IPv6" && listener.address == "::1")
            else {
                throw portFailure(
                    "port.listener_non_loopback",
                    "Port 9229 has a non-loopback listener.")
            }
            output.append(PortListener(
                family: listener.family,
                localAddress: listener.address,
                port: DiscoveryConstants.inspectorPort,
                processId: listener.processId))
        }
        return PortObservation(
            state: "knownCodexListener",
            listeners: Array(Set(output)).sorted {
                ($0.family, $0.processId) < ($1.family, $1.processId)
            })
    }

    private static func portFailure(
        _ code: String,
        _ message: String
    ) -> DiscoveryFailure {
        DiscoveryFailure(
            code: code,
            stage: "port",
            message: message,
            exitCode: 51)
    }
}

extension PortListener: Hashable {}

public final class DiscoveryCoordinator {
    private let bundleLocator: BundleLocating
    private let commandRunner: CommandRunning

    public init(
        bundleLocator: BundleLocating = LaunchServicesBundleLocator(),
        commandRunner: CommandRunning = ProcessCommandRunner()
    ) {
        self.bundleLocator = bundleLocator
        self.commandRunner = commandRunner
    }

    public func discover(
        explicitBundlePath: String?,
        port: Int
    ) throws -> DiscoveryResult {
        guard port == DiscoveryConstants.inspectorPort else {
            throw DiscoveryFailure(
                code: "port.unsupported",
                stage: "usage",
                message: "Only port 9229 is supported.",
                exitCode: 2)
        }

        let bundlePath: String
        if let explicitBundlePath {
            guard explicitBundlePath.hasPrefix("/") else {
                throw DiscoveryFailure(
                    code: "bundle.path_invalid",
                    stage: "bundle",
                    message: "Bundle path must be absolute.",
                    exitCode: 12)
            }
            bundlePath = try PathSecurity.realPath(explicitBundlePath)
        } else {
            bundlePath = try BundleSelectionPolicy.select(
                try bundleLocator.candidates(
                    bundleIdentifier: DiscoveryConstants.bundleIdentifier))
        }

        let bundle = try BundleInspector.inspect(bundlePath: bundlePath)
        let hash = try StableFileHasher.sha256(path: bundle.executablePath)
        let nativeSignature = try NativeSignatureInspector.inspect(path: bundle.bundlePath)
        try SignaturePolicy.validate(nativeSignature)

        try requireSuccessfulCommand(CommandSpec(
            executable: DiscoveryConstants.codesignPath,
            arguments: [
                "--verify", "--deep", "--strict", "--verbose=4", bundle.bundlePath,
            ]), code: "signature.invalid", stage: "signature", exitCode: 30)
        try requireSuccessfulCommand(CommandSpec(
            executable: DiscoveryConstants.codesignPath,
            arguments: [
                "--verify", "--strict", "--verbose=4", bundle.executablePath,
            ]), code: "signature.invalid", stage: "signature", exitCode: 30)

        let gatekeeperCommand = try commandRunner.run(CommandSpec(
            executable: DiscoveryConstants.spctlPath,
            arguments: [
                "--assess",
                "--type", "execute",
                "--ignore-cache",
                "--no-cache",
                "--raw",
                bundle.bundlePath,
            ],
            timeoutSeconds: 15,
            maximumOutputBytes: 64 * 1024))
        let gatekeeper = try GatekeeperParser.parse(
            gatekeeperCommand.standardOutput,
            exitCode: gatekeeperCommand.exitCode)

        let processes = try NativeProcessInspector.inspect(
            bundlePath: bundle.bundlePath,
            mainExecutablePath: bundle.executablePath)
        let allTrustedPids = Set(
            ([processes.main].compactMap { $0 } + processes.children)
                .map(\.processId))

        let lsof = try commandRunner.run(CommandSpec(
            executable: DiscoveryConstants.lsofPath,
            arguments: [
                "-nP",
                "-a",
                "-iTCP:\(DiscoveryConstants.inspectorPort)",
                "-sTCP:LISTEN",
                "-FpcfntT",
            ],
            timeoutSeconds: 10,
            maximumOutputBytes: 64 * 1024))
        let listeners: [ParsedPortListener]
        if lsof.exitCode == 1,
           lsof.standardOutput.isEmpty,
           lsof.standardError.isEmpty
        {
            listeners = []
        } else {
            guard lsof.exitCode == 0,
                  let text = String(data: lsof.standardOutput, encoding: .utf8)
            else {
                throw DiscoveryFailure(
                    code: "port.query_failed",
                    stage: "port",
                    message: "Port 9229 listener state could not be queried.",
                    exitCode: 50)
            }
            listeners = try LsofParser.parse(text)
        }
        let portObservation = try PortPolicy.evaluate(
            listeners: listeners,
            trustedProcessIds: allTrustedPids)

        let bundleAfter = try BundleInspector.inspect(bundlePath: bundle.bundlePath)
        guard bundleAfter == bundle else {
            throw DiscoveryFailure(
                code: "executable.identity_changed",
                stage: "finalize",
                message: "Bundle identity changed during discovery.",
                exitCode: 20)
        }

        return DiscoveryResult(
            bundle: BundleObservation(
                path: bundle.bundlePath,
                bundleIdentifier: bundle.identifier,
                shortVersion: bundle.shortVersion,
                bundleVersion: bundle.bundleVersion,
                mainExecutablePath: bundle.executablePath,
                mainExecutableSha256: hash),
            signature: SignatureObservation(
                valid: nativeSignature.valid,
                signingIdentifier: nativeSignature.signingIdentifier!,
                teamIdentifier: nativeSignature.teamIdentifier!,
                subjectSummary: nativeSignature.subjectSummary!,
                hardenedRuntime: nativeSignature.hardenedRuntime,
                gatekeeperAccepted: gatekeeper.accepted,
                notarizationStatus: gatekeeper.notarized ? "accepted" : "unconfirmed"),
            processes: processes,
            port9229: portObservation)
    }

    private func requireSuccessfulCommand(
        _ spec: CommandSpec,
        code: String,
        stage: String,
        exitCode: Int32
    ) throws {
        let result = try commandRunner.run(spec)
        guard result.exitCode == 0 else {
            throw DiscoveryFailure(
                code: code,
                stage: stage,
                message: "Independent code signature verification failed.",
                exitCode: exitCode)
        }
    }
}

public struct SelfTestDocument: Codable {
    public let schemaVersion: Int
    public let status: String
    public let checks: [String]

    public init() throws {
        let checks = [
            "json-single-document",
            "privacy-key-policy",
            "absolute-command-allowlist",
            "schema-v1",
        ]
        let sample = [
            "schemaVersion": DiscoveryConstants.schemaVersion,
            "status": "ok",
        ] as [String: Any]
        try JSONOutput.validatePrivacyKeys(sample)
        try ExternalCommandPolicy.validate(CommandSpec(
            executable: DiscoveryConstants.codesignPath,
            arguments: ["--verify", "/nonexistent"],
            timeoutSeconds: 1,
            maximumOutputBytes: 1024))
        self.schemaVersion = DiscoveryConstants.schemaVersion
        self.status = "ok"
        self.checks = checks
    }
}
