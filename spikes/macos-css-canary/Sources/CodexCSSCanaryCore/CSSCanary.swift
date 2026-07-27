import CodexInspectorLifecycleCore
import CryptoKit
import Darwin
import Foundation

public enum CSSCanaryConstants {
    public static let schemaVersion = 4
    public static let canaryVersion = 1
    public static let toolVersion = "0.4.0"
    public static let inspectorPort = 9229
    public static let plannedSessionCount = 2
    public static let maximumSessionCount = 3
    public static let openPollCount = 67
    public static let closePollCount = 40
    public static let pollIntervalMilliseconds = 75
    public static let stabilityDelayMilliseconds = 1_000
}

public struct CSSCanaryFailure: Error, Equatable {
    public let code: String
    public let stage: String
    public let message: String
    public let exitCode: Int32
    public let cleanupAttempted: Bool
    public let cleanupVerified: Bool
    public let signalCount: Int
    public let completedSessionCount: Int
    public let applyVerifiedCount: Int
    public let cleanupVerifiedCount: Int
    public let finalResidualCount: Int

    public init(
        code: String,
        stage: String,
        message: String,
        exitCode: Int32,
        cleanupAttempted: Bool = false,
        cleanupVerified: Bool = false,
        signalCount: Int = 0,
        completedSessionCount: Int = 0,
        applyVerifiedCount: Int = 0,
        cleanupVerifiedCount: Int = 0,
        finalResidualCount: Int = 0
    ) {
        self.code = code
        self.stage = stage
        self.message = message
        self.exitCode = exitCode
        self.cleanupAttempted = cleanupAttempted
        self.cleanupVerified = cleanupVerified
        self.signalCount = signalCount
        self.completedSessionCount = completedSessionCount
        self.applyVerifiedCount = applyVerifiedCount
        self.cleanupVerifiedCount = cleanupVerifiedCount
        self.finalResidualCount = finalResidualCount
    }
}

public struct CSSCanaryErrorBody: Codable, Equatable {
    public let code: String
    public let stage: String
    public let message: String
    public let cleanupAttempted: Bool
    public let cleanupVerified: Bool
    public let signalCount: Int
    public let completedSessionCount: Int
    public let applyVerifiedCount: Int
    public let cleanupVerifiedCount: Int
    public let finalResidualCount: Int
}

public struct CSSCanaryDocument: Codable, Equatable {
    public let schemaVersion: Int
    public let status: String
    public let result: CSSCanaryResult?
    public let error: CSSCanaryErrorBody?

    public static func success(_ result: CSSCanaryResult) -> Self {
        Self(
            schemaVersion: CSSCanaryConstants.schemaVersion,
            status: "ok",
            result: result,
            error: nil)
    }

    public static func failure(_ failure: CSSCanaryFailure) -> Self {
        Self(
            schemaVersion: CSSCanaryConstants.schemaVersion,
            status: "error",
            result: nil,
            error: CSSCanaryErrorBody(
                code: failure.code,
                stage: failure.stage,
                message: failure.message,
                cleanupAttempted: failure.cleanupAttempted,
                cleanupVerified: failure.cleanupVerified,
                signalCount: failure.signalCount,
                completedSessionCount: failure.completedSessionCount,
                applyVerifiedCount: failure.applyVerifiedCount,
                cleanupVerifiedCount: failure.cleanupVerifiedCount,
                finalResidualCount: failure.finalResidualCount))
    }
}

public struct CSSCanaryResult: Codable, Equatable {
    public let installationIdentityMatched: Bool
    public let processIdentityStable: Bool
    public let oldProcessAbsent: Bool
    public let newProcessObserved: Bool
    public let startedAtFingerprintChanged: Bool
    public let eligibleWindowCount: Int
    public let overlayWindowCount: Int
    public let unknownWindowCount: Int
    public let overlayUnmodified: Bool
    public let applyVerifiedCount: Int
    public let cleanupVerifiedCount: Int
    public let visualEffectVerifiedCount: Int
    public let blobContentVerifiedCount: Int
    public let blobURLCreatedCount: Int
    public let blobFetchAllowedCount: Int
    public let blobXHRAllowedCount: Int
    public let blobFetchBlockedByPolicyCount: Int
    public let blobPolicyEventObservedCount: Int
    public let blobPolicyDirectiveMatchedCount: Int
    public let blobPolicyBlockedURIExactCount: Int
    public let blobPolicyBlockedURISchemeOnlyCount: Int
    public let blobPolicyBlockedURIEmptyCount: Int
    public let blobPolicyBlockedURIOtherCount: Int
    public let blobFetchRejectedTypeErrorCount: Int
    public let blobFetchRejectedDOMExceptionCount: Int
    public let blobFetchRejectedOtherCount: Int
    public let blobXHRLoadCount: Int
    public let blobXHRErrorCount: Int
    public let blobXHRTimeoutCount: Int
    public let blobXHRAbortCount: Int
    public let blobXHRStatusZeroCount: Int
    public let blobRevokeInvokedCount: Int
    public let postRevokeDereferenceRejectedCount: Int
    public let blobReferenceClearedCount: Int
    public let policyListenerRemovedCount: Int
    public let finalResidualCount: Int
    public let completedSessionCount: Int
    public let signalCount: Int
    public let finalPortListenerCount: Int
    public let checkpointWritten: Bool
    public let requalificationPassed: Bool

    public init(
        installationIdentityMatched: Bool,
        processIdentityStable: Bool,
        oldProcessAbsent: Bool,
        newProcessObserved: Bool,
        startedAtFingerprintChanged: Bool,
        eligibleWindowCount: Int,
        overlayWindowCount: Int,
        unknownWindowCount: Int,
        overlayUnmodified: Bool,
        applyVerifiedCount: Int,
        cleanupVerifiedCount: Int,
        visualEffectVerifiedCount: Int,
        blobContentVerifiedCount: Int,
        blobURLCreatedCount: Int,
        blobFetchAllowedCount: Int,
        blobXHRAllowedCount: Int,
        blobFetchBlockedByPolicyCount: Int,
        blobPolicyEventObservedCount: Int = 0,
        blobPolicyDirectiveMatchedCount: Int = 0,
        blobPolicyBlockedURIExactCount: Int = 0,
        blobPolicyBlockedURISchemeOnlyCount: Int = 0,
        blobPolicyBlockedURIEmptyCount: Int = 0,
        blobPolicyBlockedURIOtherCount: Int = 0,
        blobFetchRejectedTypeErrorCount: Int = 0,
        blobFetchRejectedDOMExceptionCount: Int = 0,
        blobFetchRejectedOtherCount: Int = 0,
        blobXHRLoadCount: Int = 0,
        blobXHRErrorCount: Int = 0,
        blobXHRTimeoutCount: Int = 0,
        blobXHRAbortCount: Int = 0,
        blobXHRStatusZeroCount: Int = 0,
        blobRevokeInvokedCount: Int,
        postRevokeDereferenceRejectedCount: Int,
        blobReferenceClearedCount: Int,
        policyListenerRemovedCount: Int,
        finalResidualCount: Int,
        completedSessionCount: Int,
        signalCount: Int,
        finalPortListenerCount: Int,
        checkpointWritten: Bool,
        requalificationPassed: Bool
    ) {
        self.installationIdentityMatched = installationIdentityMatched
        self.processIdentityStable = processIdentityStable
        self.oldProcessAbsent = oldProcessAbsent
        self.newProcessObserved = newProcessObserved
        self.startedAtFingerprintChanged = startedAtFingerprintChanged
        self.eligibleWindowCount = eligibleWindowCount
        self.overlayWindowCount = overlayWindowCount
        self.unknownWindowCount = unknownWindowCount
        self.overlayUnmodified = overlayUnmodified
        self.applyVerifiedCount = applyVerifiedCount
        self.cleanupVerifiedCount = cleanupVerifiedCount
        self.visualEffectVerifiedCount = visualEffectVerifiedCount
        self.blobContentVerifiedCount = blobContentVerifiedCount
        self.blobURLCreatedCount = blobURLCreatedCount
        self.blobFetchAllowedCount = blobFetchAllowedCount
        self.blobXHRAllowedCount = blobXHRAllowedCount
        self.blobFetchBlockedByPolicyCount = blobFetchBlockedByPolicyCount
        self.blobPolicyEventObservedCount = blobPolicyEventObservedCount
        self.blobPolicyDirectiveMatchedCount =
            blobPolicyDirectiveMatchedCount
        self.blobPolicyBlockedURIExactCount =
            blobPolicyBlockedURIExactCount
        self.blobPolicyBlockedURISchemeOnlyCount =
            blobPolicyBlockedURISchemeOnlyCount
        self.blobPolicyBlockedURIEmptyCount =
            blobPolicyBlockedURIEmptyCount
        self.blobPolicyBlockedURIOtherCount =
            blobPolicyBlockedURIOtherCount
        self.blobFetchRejectedTypeErrorCount =
            blobFetchRejectedTypeErrorCount
        self.blobFetchRejectedDOMExceptionCount =
            blobFetchRejectedDOMExceptionCount
        self.blobFetchRejectedOtherCount = blobFetchRejectedOtherCount
        self.blobXHRLoadCount = blobXHRLoadCount
        self.blobXHRErrorCount = blobXHRErrorCount
        self.blobXHRTimeoutCount = blobXHRTimeoutCount
        self.blobXHRAbortCount = blobXHRAbortCount
        self.blobXHRStatusZeroCount = blobXHRStatusZeroCount
        self.blobRevokeInvokedCount = blobRevokeInvokedCount
        self.postRevokeDereferenceRejectedCount =
            postRevokeDereferenceRejectedCount
        self.blobReferenceClearedCount = blobReferenceClearedCount
        self.policyListenerRemovedCount = policyListenerRemovedCount
        self.finalResidualCount = finalResidualCount
        self.completedSessionCount = completedSessionCount
        self.signalCount = signalCount
        self.finalPortListenerCount = finalPortListenerCount
        self.checkpointWritten = checkpointWritten
        self.requalificationPassed = requalificationPassed
    }
}

public struct CanarySessionFacts: Codable, Equatable {
    public let initialResidualDetected: Bool
    public let diagnosticOnly: Bool
    public let cleanupOnly: Bool
    public let eligibleWindowCount: Int
    public let overlayWindowCount: Int
    public let unknownWindowCount: Int
    public let overlayUnmodified: Bool
    public let applyAttempted: Bool
    public let applyVerified: Bool
    public let cleanupVerified: Bool
    public let visualEffectApplied: Bool
    public let blobContentVerified: Bool
    public let blobURLCreated: Bool
    public let blobFetchAllowed: Bool
    public let blobXHRAllowed: Bool
    public let blobFetchBlockedByPolicy: Bool
    public let blobPolicyEventObserved: Bool
    public let blobPolicyDirectiveMatched: Bool
    public let blobPolicyBlockedURIExact: Bool
    public let blobPolicyBlockedURISchemeOnly: Bool
    public let blobPolicyBlockedURIEmpty: Bool
    public let blobPolicyBlockedURIOther: Bool
    public let blobFetchRejectedTypeError: Bool
    public let blobFetchRejectedDOMException: Bool
    public let blobFetchRejectedOther: Bool
    public let blobXHRLoad: Bool
    public let blobXHRError: Bool
    public let blobXHRTimeout: Bool
    public let blobXHRAbort: Bool
    public let blobXHRStatusZero: Bool
    public let blobRevokeInvoked: Bool
    public let postRevokeDereferenceRejected: Bool
    public let blobReferenceCleared: Bool
    public let policyListenerRemoved: Bool
    public let styleCountAfterApply: Int
    public let rootClassCountAfterApply: Int
    public let markerCountAfterApply: Int
    public let stateCountAfterApply: Int
    public let cssVariablePresentAfterApply: Bool
    public let finalResidualCount: Int
    public let timerCancelled: Bool
    public let mainJobCleared: Bool

    public init(
        initialResidualDetected: Bool,
        diagnosticOnly: Bool,
        cleanupOnly: Bool,
        eligibleWindowCount: Int,
        overlayWindowCount: Int,
        unknownWindowCount: Int,
        overlayUnmodified: Bool,
        applyAttempted: Bool,
        applyVerified: Bool,
        cleanupVerified: Bool,
        visualEffectApplied: Bool,
        blobContentVerified: Bool,
        blobURLCreated: Bool,
        blobFetchAllowed: Bool,
        blobXHRAllowed: Bool,
        blobFetchBlockedByPolicy: Bool,
        blobPolicyEventObserved: Bool = false,
        blobPolicyDirectiveMatched: Bool = false,
        blobPolicyBlockedURIExact: Bool = false,
        blobPolicyBlockedURISchemeOnly: Bool = false,
        blobPolicyBlockedURIEmpty: Bool = false,
        blobPolicyBlockedURIOther: Bool = false,
        blobFetchRejectedTypeError: Bool = false,
        blobFetchRejectedDOMException: Bool = false,
        blobFetchRejectedOther: Bool = false,
        blobXHRLoad: Bool = false,
        blobXHRError: Bool = false,
        blobXHRTimeout: Bool = false,
        blobXHRAbort: Bool = false,
        blobXHRStatusZero: Bool = false,
        blobRevokeInvoked: Bool,
        postRevokeDereferenceRejected: Bool,
        blobReferenceCleared: Bool,
        policyListenerRemoved: Bool,
        styleCountAfterApply: Int,
        rootClassCountAfterApply: Int,
        markerCountAfterApply: Int,
        stateCountAfterApply: Int,
        cssVariablePresentAfterApply: Bool,
        finalResidualCount: Int,
        timerCancelled: Bool,
        mainJobCleared: Bool
    ) {
        self.initialResidualDetected = initialResidualDetected
        self.diagnosticOnly = diagnosticOnly
        self.cleanupOnly = cleanupOnly
        self.eligibleWindowCount = eligibleWindowCount
        self.overlayWindowCount = overlayWindowCount
        self.unknownWindowCount = unknownWindowCount
        self.overlayUnmodified = overlayUnmodified
        self.applyAttempted = applyAttempted
        self.applyVerified = applyVerified
        self.cleanupVerified = cleanupVerified
        self.visualEffectApplied = visualEffectApplied
        self.blobContentVerified = blobContentVerified
        self.blobURLCreated = blobURLCreated
        self.blobFetchAllowed = blobFetchAllowed
        self.blobXHRAllowed = blobXHRAllowed
        self.blobFetchBlockedByPolicy = blobFetchBlockedByPolicy
        self.blobPolicyEventObserved = blobPolicyEventObserved
        self.blobPolicyDirectiveMatched = blobPolicyDirectiveMatched
        self.blobPolicyBlockedURIExact = blobPolicyBlockedURIExact
        self.blobPolicyBlockedURISchemeOnly =
            blobPolicyBlockedURISchemeOnly
        self.blobPolicyBlockedURIEmpty = blobPolicyBlockedURIEmpty
        self.blobPolicyBlockedURIOther = blobPolicyBlockedURIOther
        self.blobFetchRejectedTypeError = blobFetchRejectedTypeError
        self.blobFetchRejectedDOMException =
            blobFetchRejectedDOMException
        self.blobFetchRejectedOther = blobFetchRejectedOther
        self.blobXHRLoad = blobXHRLoad
        self.blobXHRError = blobXHRError
        self.blobXHRTimeout = blobXHRTimeout
        self.blobXHRAbort = blobXHRAbort
        self.blobXHRStatusZero = blobXHRStatusZero
        self.blobRevokeInvoked = blobRevokeInvoked
        self.postRevokeDereferenceRejected =
            postRevokeDereferenceRejected
        self.blobReferenceCleared = blobReferenceCleared
        self.policyListenerRemoved = policyListenerRemoved
        self.styleCountAfterApply = styleCountAfterApply
        self.rootClassCountAfterApply = rootClassCountAfterApply
        self.markerCountAfterApply = markerCountAfterApply
        self.stateCountAfterApply = stateCountAfterApply
        self.cssVariablePresentAfterApply = cssVariablePresentAfterApply
        self.finalResidualCount = finalResidualCount
        self.timerCancelled = timerCancelled
        self.mainJobCleared = mainJobCleared
    }
}

public struct Phase6Checkpoint: Codable, Equatable {
    public let schemaVersion: Int
    public let canaryVersion: Int
    public let toolVersion: String
    public let installationFingerprint: String
    public let processIdentityFingerprint: String
    public let startedAtFingerprint: String
    public let phase6ACompleted: Bool
    public let completedSessionCount: Int
    public let finalResidualCount: Int
    public let finalPortListenerCount: Int

    public init(
        schemaVersion: Int,
        canaryVersion: Int,
        toolVersion: String,
        installationFingerprint: String,
        processIdentityFingerprint: String,
        startedAtFingerprint: String,
        phase6ACompleted: Bool,
        completedSessionCount: Int,
        finalResidualCount: Int,
        finalPortListenerCount: Int
    ) {
        self.schemaVersion = schemaVersion
        self.canaryVersion = canaryVersion
        self.toolVersion = toolVersion
        self.installationFingerprint = installationFingerprint
        self.processIdentityFingerprint = processIdentityFingerprint
        self.startedAtFingerprint = startedAtFingerprint
        self.phase6ACompleted = phase6ACompleted
        self.completedSessionCount = completedSessionCount
        self.finalResidualCount = finalResidualCount
        self.finalPortListenerCount = finalPortListenerCount
    }
}

public enum CanarySessionMode: String {
    case applyCleanup
    case cleanupOnly
    case blobDiagnostic
}

public protocol CanarySessionRunning {
    func validateTools(nodePath: String, lifecycleHelperPath: String) throws
    func runSession(
        host: String,
        port: Int,
        processId: Int32,
        nodePath: String,
        lifecycleHelperPath: String,
        mode: CanarySessionMode
    ) throws -> CanarySessionFacts
    func closeInspector(
        host: String,
        port: Int,
        processId: Int32,
        nodePath: String,
        lifecycleHelperPath: String
    ) throws
}

public protocol CheckpointReservation: AnyObject {
    func commit(_ checkpoint: Phase6Checkpoint) throws
    func cancel()
}

public protocol CheckpointPersisting {
    func reserveForPhase6A() throws -> any CheckpointReservation
    func readPhase6A() throws -> Phase6Checkpoint
}

public final class CanaryNodeClient: CanarySessionRunning {
    private static let maximumOutputBytes = 64 * 1024
    private let helperPath: String

    public init(helperPath: String? = nil) throws {
        if let helperPath {
            self.helperPath = helperPath
            return
        }
        guard let url = Bundle.module.url(
            forResource: "css-canary-client",
            withExtension: "mjs")
        else {
            throw Self.failure(
                "helper.missing",
                "setup",
                "The bundled CSS canary helper is missing.",
                50)
        }
        self.helperPath = url.path
    }

    public func validateTools(
        nodePath: String,
        lifecycleHelperPath: String
    ) throws {
        let lifecycleClient = try NodeCDPClient(
            scriptPath: try requireRegularFile(lifecycleHelperPath))
        try lifecycleClient.validateNode(at: nodePath)
    }

    public func runSession(
        host: String,
        port: Int,
        processId: Int32,
        nodePath: String,
        lifecycleHelperPath: String,
        mode: CanarySessionMode
    ) throws -> CanarySessionFacts {
        let command = switch mode {
        case .applyCleanup: "apply-cleanup"
        case .cleanupOnly: "cleanup-only"
        case .blobDiagnostic: "diagnose-blob"
        }
        let result = try run(
            executable: try requireRegularFile(nodePath),
            arguments: [
                helperPath,
                command,
                "--lifecycle-helper",
                try requireRegularFile(lifecycleHelperPath),
                "--host", host,
                "--port", String(port),
                "--pid", String(processId),
            ],
            timeoutSeconds: 10)
        guard result.exitCode == 0 else {
            throw decodeFailure(result.standardOutput)
        }
        do {
            let facts = try JSONDecoder().decode(
                CanarySessionFacts.self,
                from: result.standardOutput)
            try validateFacts(facts, mode: mode)
            return facts
        } catch let failure as CSSCanaryFailure {
            throw failure
        } catch {
            throw Self.failure(
                "helper.response_invalid",
                "canary",
                "The canary helper returned an invalid response.",
                51)
        }
    }

    public func closeInspector(
        host: String,
        port: Int,
        processId: Int32,
        nodePath: String,
        lifecycleHelperPath: String
    ) throws {
        let lifecycleClient = try NodeCDPClient(
            scriptPath: try requireRegularFile(lifecycleHelperPath))
        try lifecycleClient.closeOnly(
            host: host,
            port: port,
            processId: processId,
            nodePath: nodePath)
    }

    private func validateFacts(
        _ facts: CanarySessionFacts,
        mode: CanarySessionMode
    ) throws {
        let integers = [
            facts.eligibleWindowCount,
            facts.overlayWindowCount,
            facts.unknownWindowCount,
            facts.styleCountAfterApply,
            facts.rootClassCountAfterApply,
            facts.markerCountAfterApply,
            facts.stateCountAfterApply,
            facts.finalResidualCount,
        ]
        guard integers.allSatisfy({ $0 >= 0 }),
              facts.mainJobCleared,
              facts.unknownWindowCount == 0,
              facts.eligibleWindowCount == 1,
              facts.overlayUnmodified,
              facts.cleanupVerified,
              facts.finalResidualCount == 0
        else {
            throw Self.failure(
                "canary.verification_failed",
                "canary",
                "Canary verification did not meet the fail-closed contract.",
                52)
        }
        if mode == .cleanupOnly || facts.initialResidualDetected {
            guard facts.cleanupOnly,
                  !facts.applyAttempted,
                  facts.blobRevokeInvoked,
                  facts.postRevokeDereferenceRejected,
                  facts.blobReferenceCleared,
                  facts.policyListenerRemoved,
                  !facts.visualEffectApplied,
                  facts.styleCountAfterApply == 0,
                  facts.rootClassCountAfterApply == 0,
                  facts.markerCountAfterApply == 0,
                  facts.stateCountAfterApply == 0,
                  !facts.cssVariablePresentAfterApply,
                  facts.timerCancelled
            else {
                throw Self.failure(
                    "canary.cleanup_only_invalid",
                    "canary",
                    "Cleanup-only result was invalid.",
                    52)
            }
            return
        }
        if mode == .blobDiagnostic {
            guard facts.diagnosticOnly,
                  !facts.cleanupOnly,
                  !facts.applyAttempted,
                  facts.blobContentVerified,
                  facts.blobURLCreated,
                  facts.blobRevokeInvoked,
                  facts.postRevokeDereferenceRejected,
                  facts.blobReferenceCleared,
                  facts.policyListenerRemoved
            else {
                throw Self.failure(
                    "canary.blob_diagnostic_invalid",
                    "canary",
                    "Blob diagnostic evidence was invalid.",
                    52)
            }
            return
        }
        guard facts.applyAttempted,
              facts.applyVerified,
              facts.visualEffectApplied,
              facts.blobContentVerified,
              facts.blobURLCreated,
              facts.blobFetchAllowed || facts.blobXHRAllowed ||
                facts.blobFetchBlockedByPolicy,
              facts.blobRevokeInvoked,
              facts.postRevokeDereferenceRejected,
              facts.blobReferenceCleared,
              facts.policyListenerRemoved,
              facts.timerCancelled,
              facts.styleCountAfterApply == 1,
              facts.rootClassCountAfterApply == 1,
              facts.markerCountAfterApply == 1,
              facts.stateCountAfterApply == 1,
              facts.cssVariablePresentAfterApply
        else {
            throw Self.failure(
                "canary.apply_verification_failed",
                "canary",
                "Canary application was not proven.",
                52)
        }
    }

    private func requireRegularFile(_ path: String) throws -> String {
        guard path.hasPrefix("/") else {
            throw Self.failure(
                "path.invalid",
                "setup",
                "Tool paths must be absolute.",
                2)
        }
        guard let pointer = Darwin.realpath(path, nil) else {
            throw Self.failure(
                "path.invalid",
                "setup",
                "A required tool path could not be resolved.",
                2)
        }
        defer { free(pointer) }
        let resolved = String(cString: pointer)
        var metadata = stat()
        guard lstat(path, &metadata) == 0,
              (metadata.st_mode & S_IFMT) == S_IFREG,
              path == resolved
        else {
            throw Self.failure(
                "path.invalid",
                "setup",
                "Tool paths must be non-symlink regular files.",
                2)
        }
        return resolved
    }

    private func run(
        executable: String,
        arguments: [String],
        timeoutSeconds: TimeInterval
    ) throws -> ProcessCapture {
        let process = Process()
        let output = Pipe()
        let errors = Pipe()
        process.executableURL = URL(fileURLWithPath: executable)
        process.arguments = arguments
        process.standardOutput = output
        process.standardError = errors
        do {
            try process.run()
        } catch {
            throw Self.failure(
                "helper.launch_failed",
                "helper",
                "The canary helper could not be launched.",
                53)
        }
        let deadline = Date().addingTimeInterval(timeoutSeconds)
        while process.isRunning && Date() < deadline {
            usleep(20_000)
        }
        if process.isRunning {
            process.terminate()
            process.waitUntilExit()
            throw Self.failure(
                "helper.timeout",
                "helper",
                "The canary helper exceeded its timeout.",
                54)
        }
        let standardOutput = output.fileHandleForReading.readDataToEndOfFile()
        let standardError = errors.fileHandleForReading.readDataToEndOfFile()
        guard standardOutput.count <= Self.maximumOutputBytes,
              standardError.count <= Self.maximumOutputBytes
        else {
            throw Self.failure(
                "helper.output_too_large",
                "helper",
                "The canary helper output exceeded its limit.",
                55)
        }
        return ProcessCapture(
            exitCode: process.terminationStatus,
            standardOutput: standardOutput)
    }

    private func decodeFailure(_ data: Data) -> CSSCanaryFailure {
        if let error = try? JSONDecoder().decode(HelperError.self, from: data) {
            return Self.failure(
                error.code,
                error.stage,
                error.message,
                56)
        }
        return Self.failure(
            "helper.failed",
            "helper",
            "The canary helper failed.",
            56)
    }

    private static func failure(
        _ code: String,
        _ stage: String,
        _ message: String,
        _ exitCode: Int32
    ) -> CSSCanaryFailure {
        CSSCanaryFailure(
            code: code,
            stage: stage,
            message: message,
            exitCode: exitCode)
    }
}

private struct ProcessCapture {
    let exitCode: Int32
    let standardOutput: Data
}

private struct HelperError: Codable {
    let code: String
    let stage: String
    let message: String
}

public final class AtomicCheckpointStore: CheckpointPersisting {
    private let homeDirectory: URL

    public init(
        homeDirectory: URL = FileManager.default.homeDirectoryForCurrentUser
    ) {
        self.homeDirectory = homeDirectory
    }

    public func reserveForPhase6A() throws -> any CheckpointReservation {
        let destination = try checkpointURL(requireExisting: false)
        var existing = stat()
        if lstat(destination.path, &existing) == 0 {
            throw checkpointFailure(
                "checkpoint.exists",
                "The phase 6A checkpoint already exists.")
        }
        guard errno == ENOENT else {
            throw checkpointFailure(
                "checkpoint.path_invalid",
                "The phase 6A checkpoint destination is unavailable.")
        }
        let entries: [String]
        do {
            entries = try FileManager.default.contentsOfDirectory(
                atPath: destination.deletingLastPathComponent().path)
        } catch {
            throw checkpointFailure(
                "checkpoint.path_invalid",
                "The approved checkpoint directory could not be inspected.")
        }
        guard entries.isEmpty else {
            throw checkpointFailure(
                "checkpoint.reservation_conflict",
                "The approved checkpoint directory is not empty.")
        }
        let temporary = destination.deletingLastPathComponent()
            .appendingPathComponent(".phase6a-\(UUID().uuidString).tmp")
        let descriptor = open(
            temporary.path,
            O_WRONLY | O_CREAT | O_EXCL | O_NOFOLLOW,
            S_IRUSR | S_IWUSR)
        guard descriptor >= 0 else {
            throw checkpointFailure(
                "checkpoint.write_failed",
                "The phase 6A checkpoint reservation could not be created.")
        }
        var metadata = stat()
        guard fstat(descriptor, &metadata) == 0,
              (metadata.st_mode & S_IFMT) == S_IFREG,
              (metadata.st_mode & 0o077) == 0,
              fsync(descriptor) == 0
        else {
            close(descriptor)
            unlink(temporary.path)
            throw checkpointFailure(
                "checkpoint.write_failed",
                "The phase 6A checkpoint reservation is not secure.")
        }
        return AtomicCheckpointReservation(
            descriptor: descriptor,
            temporary: temporary,
            destination: destination)
    }

    public func readPhase6A() throws -> Phase6Checkpoint {
        let url = try checkpointURL(requireExisting: true)
        var metadata = stat()
        guard lstat(url.path, &metadata) == 0,
              (metadata.st_mode & S_IFMT) == S_IFREG,
              (metadata.st_mode & 0o077) == 0
        else {
            throw checkpointFailure(
                "checkpoint.permissions_invalid",
                "The phase 6A checkpoint permissions are invalid.")
        }
        let checkpoint: Phase6Checkpoint
        do {
            checkpoint = try JSONDecoder().decode(
                Phase6Checkpoint.self,
                from: Data(contentsOf: url))
        } catch {
            throw checkpointFailure(
                "checkpoint.invalid",
                "The phase 6A checkpoint is invalid.")
        }
        guard checkpoint.schemaVersion == CSSCanaryConstants.schemaVersion,
              checkpoint.canaryVersion == CSSCanaryConstants.canaryVersion,
              checkpoint.toolVersion == CSSCanaryConstants.toolVersion,
              checkpoint.phase6ACompleted,
              checkpoint.completedSessionCount ==
                CSSCanaryConstants.plannedSessionCount,
              checkpoint.finalResidualCount == 0,
              checkpoint.finalPortListenerCount == 0
        else {
            throw checkpointFailure(
                "checkpoint.incompatible",
                "The phase 6A checkpoint is incompatible.")
        }
        return checkpoint
    }

    private func checkpointURL(
        requireExisting: Bool
    ) throws -> URL {
        let approvedRoot = homeDirectory
            .appendingPathComponent("Library/Application Support/CodexThemeStudio/devtools/state/macos-css-canary")
        var rootMetadata = stat()
        if lstat(approvedRoot.path, &rootMetadata) != 0 {
            guard errno == ENOENT else {
                throw checkpointFailure(
                    "checkpoint.path_invalid",
                    "The approved checkpoint directory is unavailable.")
            }
            guard !requireExisting else {
                throw checkpointFailure(
                    "checkpoint.not_found",
                    "The phase 6A checkpoint does not exist.")
            }
            do {
                try FileManager.default.createDirectory(
                    at: approvedRoot,
                    withIntermediateDirectories: true,
                    attributes: [.posixPermissions: 0o700])
            } catch {
                throw checkpointFailure(
                    "checkpoint.path_invalid",
                    "The approved checkpoint directory could not be created.")
            }
            guard lstat(approvedRoot.path, &rootMetadata) == 0 else {
                throw checkpointFailure(
                    "checkpoint.path_invalid",
                    "The approved checkpoint directory could not be created.")
            }
        }
        guard (rootMetadata.st_mode & S_IFMT) == S_IFDIR,
              (rootMetadata.st_mode & 0o777) == 0o700,
              rootMetadata.st_uid == geteuid(),
              let pointer = Darwin.realpath(approvedRoot.path, nil)
        else {
            throw checkpointFailure(
                "checkpoint.path_invalid",
                "The approved checkpoint directory could not be secured.")
        }
        defer { free(pointer) }
        let root = String(cString: pointer)
        return URL(fileURLWithPath: root)
            .appendingPathComponent("phase6a-checkpoint.json")
    }
}

private final class AtomicCheckpointReservation: CheckpointReservation {
    private var descriptor: Int32
    private let temporary: URL
    private let destination: URL
    private var active = true

    init(descriptor: Int32, temporary: URL, destination: URL) {
        self.descriptor = descriptor
        self.temporary = temporary
        self.destination = destination
    }

    deinit {
        cancel()
    }

    func commit(_ checkpoint: Phase6Checkpoint) throws {
        guard active else {
            throw checkpointFailure(
                "checkpoint.reservation_invalid",
                "The phase 6A checkpoint reservation is no longer active.")
        }
        let data: Data
        do {
            data = try JSONEncoder.sorted.encode(checkpoint)
        } catch {
            throw checkpointFailure(
                "checkpoint.write_failed",
                "The phase 6A checkpoint could not be encoded.")
        }
        let wroteAll = data.withUnsafeBytes { bytes -> Bool in
            guard let baseAddress = bytes.baseAddress else {
                return data.isEmpty
            }
            var offset = 0
            while offset < bytes.count {
                let result = Darwin.write(
                    descriptor,
                    baseAddress.advanced(by: offset),
                    bytes.count - offset)
                if result < 0 {
                    if errno == EINTR {
                        continue
                    }
                    return false
                }
                if result == 0 {
                    return false
                }
                offset += result
            }
            return true
        }
        guard wroteAll,
              fchmod(descriptor, S_IRUSR | S_IWUSR) == 0,
              fsync(descriptor) == 0,
              close(descriptor) == 0
        else {
            if descriptor >= 0 {
                close(descriptor)
                descriptor = -1
            }
            throw checkpointFailure(
                "checkpoint.write_failed",
                "The phase 6A checkpoint could not be written atomically.")
        }
        descriptor = -1
        guard renamex_np(
            temporary.path,
            destination.path,
            UInt32(RENAME_EXCL)) == 0
        else {
            throw checkpointFailure(
                "checkpoint.write_failed",
                "The phase 6A checkpoint could not be committed atomically.")
        }
        active = false
    }

    func cancel() {
        guard active else {
            return
        }
        if descriptor >= 0 {
            close(descriptor)
            descriptor = -1
        }
        unlink(temporary.path)
        active = false
    }
}

private func checkpointFailure(
    _ code: String,
    _ message: String
) -> CSSCanaryFailure {
    CSSCanaryFailure(
        code: code,
        stage: "checkpoint",
        message: message,
        exitCode: 70)
}

private extension JSONEncoder {
    static var sorted: JSONEncoder {
        let encoder = JSONEncoder()
        encoder.outputFormatting = [.sortedKeys, .withoutEscapingSlashes]
        return encoder
    }
}

public final class CSSCanaryCoordinator {
    private let discovery: TrustedDiscovering
    private let identity: IdentityRevalidating
    private let ports: PortQuerying
    private let signal: SignalSending
    private let sessions: CanarySessionRunning
    private let checkpoints: CheckpointPersisting
    private let sleeper: LifecycleSleeping

    public init(
        discovery: TrustedDiscovering,
        identity: IdentityRevalidating,
        ports: PortQuerying,
        signal: SignalSending,
        sessions: CanarySessionRunning,
        checkpoints: CheckpointPersisting,
        sleeper: LifecycleSleeping = SystemSleeper()
    ) {
        self.discovery = discovery
        self.identity = identity
        self.ports = ports
        self.signal = signal
        self.sessions = sessions
        self.checkpoints = checkpoints
        self.sleeper = sleeper
    }

    public static func live() throws -> CSSCanaryCoordinator {
        CSSCanaryCoordinator(
            discovery: ReadOnlyDiscoveryAdapter(),
            identity: NativeIdentityRevalidator(),
            ports: LsofPortQuery(),
            signal: DarwinSignalSender(),
            sessions: try CanaryNodeClient(),
            checkpoints: AtomicCheckpointStore())
    }

    public func run6A(
        bundlePath: String,
        nodePath: String,
        lifecycleHelperPath: String
    ) throws -> CSSCanaryResult {
        try sessions.validateTools(
            nodePath: nodePath,
            lifecycleHelperPath: lifecycleHelperPath)
        let reservation = try checkpoints.reserveForPhase6A()
        defer { reservation.cancel() }
        let snapshot = try discovery.discover(
            bundlePath: bundlePath,
            port: CSSCanaryConstants.inspectorPort)
        var state = RunState()
        do {
            for _ in 0..<CSSCanaryConstants.plannedSessionCount {
                let facts = try runSession(
                    snapshot: snapshot,
                    nodePath: nodePath,
                    lifecycleHelperPath: lifecycleHelperPath,
                    mode: .applyCleanup,
                    state: &state)
                state.record(facts)
                if facts.initialResidualDetected {
                    throw CSSCanaryFailure(
                        code: "canary.initial_residual_detected",
                        stage: "baseline",
                        message: "Initial canary residue was cleaned; Apply was not attempted.",
                        exitCode: 80,
                        cleanupAttempted: true,
                        cleanupVerified: facts.cleanupVerified)
                }
            }
            sleeper.sleep(
                milliseconds: CSSCanaryConstants.stabilityDelayMilliseconds)
            try identity.requireStable(snapshot)
            let finalListeners = try ports.listeners(
                port: CSSCanaryConstants.inspectorPort)
            guard finalListeners.isEmpty else {
                throw failure(
                    "inspector.residual_listener",
                    "finalize",
                    "Port 9229 has a residual listener.",
                    81)
            }
            let checkpoint = makeCheckpoint(snapshot: snapshot, state: state)
            try reservation.commit(checkpoint)
            return state.result(
                installationIdentityMatched: true,
                oldProcessAbsent: false,
                newProcessObserved: false,
                startedAtFingerprintChanged: false,
                checkpointWritten: true,
                requalificationPassed: false)
        } catch let original as CSSCanaryFailure {
            let cleanup = state.cleanupNeeded
                ? attemptEmergencyCleanup(
                    snapshot: snapshot,
                    nodePath: nodePath,
                    lifecycleHelperPath: lifecycleHelperPath,
                    state: &state)
                : (
                    attempted: false,
                    verified:
                        original.cleanupVerified ||
                        state.cleanupVerifiedCount > 0)
            throw progressedFailure(original, cleanup: cleanup, state: state)
        }
    }

    public func run6B(
        bundlePath: String,
        nodePath: String,
        lifecycleHelperPath: String
    ) throws -> CSSCanaryResult {
        let checkpoint = try checkpoints.readPhase6A()
        try sessions.validateTools(
            nodePath: nodePath,
            lifecycleHelperPath: lifecycleHelperPath)
        let snapshot = try discovery.discover(
            bundlePath: bundlePath,
            port: CSSCanaryConstants.inspectorPort)
        let fingerprints = Self.fingerprints(snapshot)
        guard fingerprints.installation == checkpoint.installationFingerprint else {
            throw failure(
                "qualification.installation_changed",
                "qualification",
                "The verified Codex installation identity changed.",
                90)
        }
        guard fingerprints.process != checkpoint.processIdentityFingerprint,
              fingerprints.startedAt != checkpoint.startedAtFingerprint
        else {
            throw failure(
                "qualification.old_process_present",
                "qualification",
                "A new Codex process and start time were not proven.",
                91)
        }

        var state = RunState()
        do {
            for _ in 0..<CSSCanaryConstants.plannedSessionCount {
                let facts = try runSession(
                    snapshot: snapshot,
                    nodePath: nodePath,
                    lifecycleHelperPath: lifecycleHelperPath,
                    mode: .applyCleanup,
                    state: &state)
                state.record(facts)
                if facts.initialResidualDetected {
                    throw CSSCanaryFailure(
                        code: "canary.initial_residual_detected",
                        stage: "baseline",
                        message: "Initial canary residue was cleaned; Apply was not attempted.",
                        exitCode: 80,
                        cleanupAttempted: true,
                        cleanupVerified: facts.cleanupVerified)
                }
            }
            sleeper.sleep(
                milliseconds: CSSCanaryConstants.stabilityDelayMilliseconds)
            try identity.requireStable(snapshot)
            let finalListeners = try ports.listeners(
                port: CSSCanaryConstants.inspectorPort)
            guard finalListeners.isEmpty else {
                throw failure(
                    "inspector.residual_listener",
                    "finalize",
                    "Port 9229 has a residual listener.",
                    81)
            }
            return state.result(
                installationIdentityMatched: true,
                oldProcessAbsent: true,
                newProcessObserved: true,
                startedAtFingerprintChanged: true,
                checkpointWritten: false,
                requalificationPassed: true)
        } catch let original as CSSCanaryFailure {
            let cleanup = state.cleanupNeeded
                ? attemptEmergencyCleanup(
                    snapshot: snapshot,
                    nodePath: nodePath,
                    lifecycleHelperPath: lifecycleHelperPath,
                    state: &state)
                : (
                    attempted: false,
                    verified:
                        original.cleanupVerified ||
                        state.cleanupVerifiedCount > 0)
            throw progressedFailure(original, cleanup: cleanup, state: state)
        }
    }

    public func runBlobDiagnostic(
        bundlePath: String,
        nodePath: String,
        lifecycleHelperPath: String
    ) throws -> CSSCanaryResult {
        try sessions.validateTools(
            nodePath: nodePath,
            lifecycleHelperPath: lifecycleHelperPath)
        let snapshot = try discovery.discover(
            bundlePath: bundlePath,
            port: CSSCanaryConstants.inspectorPort)
        var state = RunState()
        do {
            let facts = try runSession(
                snapshot: snapshot,
                nodePath: nodePath,
                lifecycleHelperPath: lifecycleHelperPath,
                mode: .blobDiagnostic,
                state: &state)
            state.record(facts)
            if facts.initialResidualDetected {
                throw CSSCanaryFailure(
                    code: "canary.initial_residual_detected",
                    stage: "baseline",
                    message: "Initial canary residue was cleaned; Blob diagnostics were not attempted.",
                    exitCode: 80,
                    cleanupAttempted: true,
                    cleanupVerified: facts.cleanupVerified)
            }
            sleeper.sleep(
                milliseconds: CSSCanaryConstants.stabilityDelayMilliseconds)
            try identity.requireStable(snapshot)
            guard try ports.listeners(
                port: CSSCanaryConstants.inspectorPort).isEmpty
            else {
                throw failure(
                    "inspector.residual_listener",
                    "finalize",
                    "Port 9229 has a residual listener.",
                    81)
            }
            return state.result(
                installationIdentityMatched: true,
                oldProcessAbsent: false,
                newProcessObserved: false,
                startedAtFingerprintChanged: false,
                checkpointWritten: false,
                requalificationPassed: false)
        } catch let original as CSSCanaryFailure {
            let cleanup = state.cleanupNeeded
                ? attemptEmergencyCleanup(
                    snapshot: snapshot,
                    nodePath: nodePath,
                    lifecycleHelperPath: lifecycleHelperPath,
                    state: &state)
                : (
                    attempted: false,
                    verified:
                        original.cleanupVerified ||
                        state.cleanupVerifiedCount > 0)
            throw progressedFailure(original, cleanup: cleanup, state: state)
        }
    }

    private func runSession(
        snapshot: TrustedSnapshot,
        nodePath: String,
        lifecycleHelperPath: String,
        mode: CanarySessionMode,
        state: inout RunState
    ) throws -> CanarySessionFacts {
        guard state.signalCount < CSSCanaryConstants.maximumSessionCount else {
            throw failure(
                "session.limit_exceeded",
                "session",
                "The session and signal limit was reached.",
                82)
        }
        try identity.requireStable(snapshot)
        guard try ports.listeners(
            port: CSSCanaryConstants.inspectorPort).isEmpty
        else {
            throw failure(
                "port.initially_busy",
                "session",
                "Port 9229 must be free before each session.",
                82)
        }
        try identity.requireStable(snapshot)
        try signal.sendUSR1(processId: snapshot.main.processId)
        state.signalCount += 1
        state.cleanupNeeded = true

        let listeners = try waitForOpen(processId: snapshot.main.processId)
        try identity.requireStable(snapshot)
        let host = preferredHost(listeners)
        var facts: CanarySessionFacts?
        var operationFailure: CSSCanaryFailure?
        do {
            facts = try sessions.runSession(
                host: host,
                port: CSSCanaryConstants.inspectorPort,
                processId: snapshot.main.processId,
                nodePath: nodePath,
                lifecycleHelperPath: lifecycleHelperPath,
                mode: mode)
        } catch let failure as CSSCanaryFailure {
            operationFailure = failure
        } catch {
            operationFailure = self.failure(
                "session.unexpected",
                "session",
                "The canary session failed unexpectedly.",
                83)
        }

        try ensureInspectorClosed(
            snapshot: snapshot,
            host: host,
            nodePath: nodePath,
            lifecycleHelperPath: lifecycleHelperPath)
        try identity.requireStable(snapshot)
        if let operationFailure {
            throw operationFailure
        }
        guard let facts,
              facts.cleanupVerified,
              facts.finalResidualCount == 0
        else {
            throw failure(
                "canary.cleanup_not_verified",
                "canary",
                "The session did not prove zero-residual cleanup.",
                83)
        }
        state.cleanupNeeded = false
        state.completedSessionCount += 1
        return facts
    }

    private func waitForOpen(processId: Int32) throws -> [LifecycleListener] {
        for _ in 0..<CSSCanaryConstants.openPollCount {
            let listeners = try ports.listeners(
                port: CSSCanaryConstants.inspectorPort)
            if !listeners.isEmpty {
                try validateListeners(listeners, processId: processId)
                return listeners
            }
            sleeper.sleep(
                milliseconds: CSSCanaryConstants.pollIntervalMilliseconds)
        }
        throw failure(
            "inspector.open_timeout",
            "inspector",
            "Inspector did not open within five seconds.",
            84)
    }

    private func ensureInspectorClosed(
        snapshot: TrustedSnapshot,
        host: String,
        nodePath: String,
        lifecycleHelperPath: String
    ) throws {
        for _ in 0..<CSSCanaryConstants.closePollCount {
            let listeners = try ports.listeners(
                port: CSSCanaryConstants.inspectorPort)
            if listeners.isEmpty {
                return
            }
            try validateListeners(
                listeners,
                processId: snapshot.main.processId)
            sleeper.sleep(
                milliseconds: CSSCanaryConstants.pollIntervalMilliseconds)
        }
        try identity.requireStable(snapshot)
        try sessions.closeInspector(
            host: host,
            port: CSSCanaryConstants.inspectorPort,
            processId: snapshot.main.processId,
            nodePath: nodePath,
            lifecycleHelperPath: lifecycleHelperPath)
        for _ in 0..<CSSCanaryConstants.closePollCount {
            if try ports.listeners(
                port: CSSCanaryConstants.inspectorPort).isEmpty
            {
                return
            }
            sleeper.sleep(
                milliseconds: CSSCanaryConstants.pollIntervalMilliseconds)
        }
        throw failure(
            "inspector.close_timeout",
            "inspector",
            "Inspector could not be closed.",
            85)
    }

    private func attemptEmergencyCleanup(
        snapshot: TrustedSnapshot,
        nodePath: String,
        lifecycleHelperPath: String,
        state: inout RunState
    ) -> (attempted: Bool, verified: Bool) {
        guard state.cleanupNeeded,
              state.signalCount < CSSCanaryConstants.maximumSessionCount
        else {
            return (false, false)
        }
        do {
            let facts = try runSession(
                snapshot: snapshot,
                nodePath: nodePath,
                lifecycleHelperPath: lifecycleHelperPath,
                mode: .cleanupOnly,
                state: &state)
            state.record(facts)
            return (
                true,
                facts.cleanupVerified && facts.finalResidualCount == 0)
        } catch {
            return (true, false)
        }
    }

    private func validateListeners(
        _ listeners: [LifecycleListener],
        processId: Int32
    ) throws {
        guard Set(listeners.map(\.processId)) == [processId] else {
            throw failure(
                "port.listener_owner_unknown",
                "port",
                "Port 9229 is not exclusively owned by the trusted process.",
                86)
        }
        guard listeners.allSatisfy({
            ($0.family == "IPv4" && $0.localAddress == "127.0.0.1") ||
            ($0.family == "IPv6" && $0.localAddress == "::1")
        }) else {
            throw failure(
                "port.listener_non_loopback",
                "port",
                "Port 9229 has a non-loopback listener.",
                86)
        }
    }

    private func preferredHost(_ listeners: [LifecycleListener]) -> String {
        listeners.contains {
            $0.family == "IPv4" && $0.localAddress == "127.0.0.1"
        } ? "127.0.0.1" : "::1"
    }

    private func makeCheckpoint(
        snapshot: TrustedSnapshot,
        state: RunState
    ) -> Phase6Checkpoint {
        let fingerprints = Self.fingerprints(snapshot)
        return Phase6Checkpoint(
            schemaVersion: CSSCanaryConstants.schemaVersion,
            canaryVersion: CSSCanaryConstants.canaryVersion,
            toolVersion: CSSCanaryConstants.toolVersion,
            installationFingerprint: fingerprints.installation,
            processIdentityFingerprint: fingerprints.process,
            startedAtFingerprint: fingerprints.startedAt,
            phase6ACompleted: true,
            completedSessionCount: state.completedSessionCount,
            finalResidualCount: state.finalResidualCount,
            finalPortListenerCount: 0)
    }

    public static func fingerprints(
        _ snapshot: TrustedSnapshot
    ) -> (installation: String, process: String, startedAt: String) {
        let installation = [
            snapshot.bundle.bundleIdentifier,
            snapshot.bundle.path,
            snapshot.bundle.mainExecutablePath,
            snapshot.bundle.mainExecutableSha256,
            snapshot.signature.signingIdentifier,
            snapshot.signature.teamIdentifier,
            String(snapshot.signature.valid),
            String(snapshot.signature.hardenedRuntime),
            String(snapshot.signature.gatekeeperAccepted),
        ].joined(separator: "\u{001f}")
        let process = [
            String(snapshot.main.processId),
            String(snapshot.main.parentProcessId),
            snapshot.main.startedAtUtc,
            snapshot.main.architecture.rawValue,
            snapshot.main.executablePath,
        ].joined(separator: "\u{001f}")
        return (
            sha256(installation),
            sha256(process),
            sha256(snapshot.main.startedAtUtc))
    }

    private static func sha256(_ value: String) -> String {
        SHA256.hash(data: Data(value.utf8))
            .map { String(format: "%02x", $0) }
            .joined()
    }

    private func failure(
        _ code: String,
        _ stage: String,
        _ message: String,
        _ exitCode: Int32
    ) -> CSSCanaryFailure {
        CSSCanaryFailure(
            code: code,
            stage: stage,
            message: message,
            exitCode: exitCode)
    }

    private func progressedFailure(
        _ original: CSSCanaryFailure,
        cleanup: (attempted: Bool, verified: Bool),
        state: RunState
    ) -> CSSCanaryFailure {
        CSSCanaryFailure(
            code: original.code,
            stage: original.stage,
            message: original.message,
            exitCode: original.exitCode,
            cleanupAttempted:
                original.cleanupAttempted || cleanup.attempted,
            cleanupVerified:
                original.cleanupVerified || cleanup.verified,
            signalCount: state.signalCount,
            completedSessionCount: state.completedSessionCount,
            applyVerifiedCount: state.applyVerifiedCount,
            cleanupVerifiedCount: state.cleanupVerifiedCount,
            finalResidualCount: state.finalResidualCount)
    }
}

private struct RunState {
    var eligibleWindowCount = 0
    var overlayWindowCount = 0
    var unknownWindowCount = 0
    var overlayUnmodified = true
    var applyVerifiedCount = 0
    var cleanupVerifiedCount = 0
    var visualEffectVerifiedCount = 0
    var blobContentVerifiedCount = 0
    var blobURLCreatedCount = 0
    var blobFetchAllowedCount = 0
    var blobXHRAllowedCount = 0
    var blobFetchBlockedByPolicyCount = 0
    var blobPolicyEventObservedCount = 0
    var blobPolicyDirectiveMatchedCount = 0
    var blobPolicyBlockedURIExactCount = 0
    var blobPolicyBlockedURISchemeOnlyCount = 0
    var blobPolicyBlockedURIEmptyCount = 0
    var blobPolicyBlockedURIOtherCount = 0
    var blobFetchRejectedTypeErrorCount = 0
    var blobFetchRejectedDOMExceptionCount = 0
    var blobFetchRejectedOtherCount = 0
    var blobXHRLoadCount = 0
    var blobXHRErrorCount = 0
    var blobXHRTimeoutCount = 0
    var blobXHRAbortCount = 0
    var blobXHRStatusZeroCount = 0
    var blobRevokeInvokedCount = 0
    var postRevokeDereferenceRejectedCount = 0
    var blobReferenceClearedCount = 0
    var policyListenerRemovedCount = 0
    var finalResidualCount = 0
    var completedSessionCount = 0
    var signalCount = 0
    var cleanupNeeded = false

    mutating func record(_ facts: CanarySessionFacts) {
        eligibleWindowCount = facts.eligibleWindowCount
        overlayWindowCount = facts.overlayWindowCount
        unknownWindowCount = facts.unknownWindowCount
        overlayUnmodified = overlayUnmodified && facts.overlayUnmodified
        applyVerifiedCount += facts.applyVerified ? 1 : 0
        cleanupVerifiedCount += facts.cleanupVerified ? 1 : 0
        visualEffectVerifiedCount += facts.visualEffectApplied ? 1 : 0
        blobContentVerifiedCount += facts.blobContentVerified ? 1 : 0
        blobURLCreatedCount += facts.blobURLCreated ? 1 : 0
        blobFetchAllowedCount += facts.blobFetchAllowed ? 1 : 0
        blobXHRAllowedCount += facts.blobXHRAllowed ? 1 : 0
        blobFetchBlockedByPolicyCount +=
            facts.blobFetchBlockedByPolicy ? 1 : 0
        blobPolicyEventObservedCount +=
            facts.blobPolicyEventObserved ? 1 : 0
        blobPolicyDirectiveMatchedCount +=
            facts.blobPolicyDirectiveMatched ? 1 : 0
        blobPolicyBlockedURIExactCount +=
            facts.blobPolicyBlockedURIExact ? 1 : 0
        blobPolicyBlockedURISchemeOnlyCount +=
            facts.blobPolicyBlockedURISchemeOnly ? 1 : 0
        blobPolicyBlockedURIEmptyCount +=
            facts.blobPolicyBlockedURIEmpty ? 1 : 0
        blobPolicyBlockedURIOtherCount +=
            facts.blobPolicyBlockedURIOther ? 1 : 0
        blobFetchRejectedTypeErrorCount +=
            facts.blobFetchRejectedTypeError ? 1 : 0
        blobFetchRejectedDOMExceptionCount +=
            facts.blobFetchRejectedDOMException ? 1 : 0
        blobFetchRejectedOtherCount +=
            facts.blobFetchRejectedOther ? 1 : 0
        blobXHRLoadCount += facts.blobXHRLoad ? 1 : 0
        blobXHRErrorCount += facts.blobXHRError ? 1 : 0
        blobXHRTimeoutCount += facts.blobXHRTimeout ? 1 : 0
        blobXHRAbortCount += facts.blobXHRAbort ? 1 : 0
        blobXHRStatusZeroCount += facts.blobXHRStatusZero ? 1 : 0
        blobRevokeInvokedCount += facts.blobRevokeInvoked ? 1 : 0
        postRevokeDereferenceRejectedCount +=
            facts.postRevokeDereferenceRejected ? 1 : 0
        blobReferenceClearedCount += facts.blobReferenceCleared ? 1 : 0
        policyListenerRemovedCount += facts.policyListenerRemoved ? 1 : 0
        finalResidualCount = facts.finalResidualCount
    }

    func result(
        installationIdentityMatched: Bool,
        oldProcessAbsent: Bool,
        newProcessObserved: Bool,
        startedAtFingerprintChanged: Bool,
        checkpointWritten: Bool,
        requalificationPassed: Bool
    ) -> CSSCanaryResult {
        CSSCanaryResult(
            installationIdentityMatched: installationIdentityMatched,
            processIdentityStable: true,
            oldProcessAbsent: oldProcessAbsent,
            newProcessObserved: newProcessObserved,
            startedAtFingerprintChanged: startedAtFingerprintChanged,
            eligibleWindowCount: eligibleWindowCount,
            overlayWindowCount: overlayWindowCount,
            unknownWindowCount: unknownWindowCount,
            overlayUnmodified: overlayUnmodified,
            applyVerifiedCount: applyVerifiedCount,
            cleanupVerifiedCount: cleanupVerifiedCount,
            visualEffectVerifiedCount: visualEffectVerifiedCount,
            blobContentVerifiedCount: blobContentVerifiedCount,
            blobURLCreatedCount: blobURLCreatedCount,
            blobFetchAllowedCount: blobFetchAllowedCount,
            blobXHRAllowedCount: blobXHRAllowedCount,
            blobFetchBlockedByPolicyCount:
                blobFetchBlockedByPolicyCount,
            blobPolicyEventObservedCount:
                blobPolicyEventObservedCount,
            blobPolicyDirectiveMatchedCount:
                blobPolicyDirectiveMatchedCount,
            blobPolicyBlockedURIExactCount:
                blobPolicyBlockedURIExactCount,
            blobPolicyBlockedURISchemeOnlyCount:
                blobPolicyBlockedURISchemeOnlyCount,
            blobPolicyBlockedURIEmptyCount:
                blobPolicyBlockedURIEmptyCount,
            blobPolicyBlockedURIOtherCount:
                blobPolicyBlockedURIOtherCount,
            blobFetchRejectedTypeErrorCount:
                blobFetchRejectedTypeErrorCount,
            blobFetchRejectedDOMExceptionCount:
                blobFetchRejectedDOMExceptionCount,
            blobFetchRejectedOtherCount:
                blobFetchRejectedOtherCount,
            blobXHRLoadCount: blobXHRLoadCount,
            blobXHRErrorCount: blobXHRErrorCount,
            blobXHRTimeoutCount: blobXHRTimeoutCount,
            blobXHRAbortCount: blobXHRAbortCount,
            blobXHRStatusZeroCount: blobXHRStatusZeroCount,
            blobRevokeInvokedCount: blobRevokeInvokedCount,
            postRevokeDereferenceRejectedCount:
                postRevokeDereferenceRejectedCount,
            blobReferenceClearedCount: blobReferenceClearedCount,
            policyListenerRemovedCount: policyListenerRemovedCount,
            finalResidualCount: finalResidualCount,
            completedSessionCount: completedSessionCount,
            signalCount: signalCount,
            finalPortListenerCount: 0,
            checkpointWritten: checkpointWritten,
            requalificationPassed: requalificationPassed)
    }
}

public enum CSSCanaryJSON {
    public static func encode<T: Encodable>(
        _ value: T,
        pretty: Bool = false
    ) throws -> Data {
        try LifecycleJSON.encode(value, pretty: pretty)
    }
}

public enum CSSCanarySchema {
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
      "$id": "https://codexthemestudio.local/schema/macos-css-canary-v4.json",
      "type": "object",
      "required": ["schemaVersion", "status", "result", "error"],
      "properties": {
        "schemaVersion": { "const": 4 },
        "status": { "enum": ["ok", "error"] },
        "result": { "type": ["object", "null"] },
        "error": { "type": ["object", "null"] }
      }
    }
    """
}

public struct CSSCanarySelfTest: Encodable {
    public let schemaVersion = CSSCanaryConstants.schemaVersion
    public let canaryVersion = CSSCanaryConstants.canaryVersion
    public let toolVersion = CSSCanaryConstants.toolVersion
    public let status = "ok"
    public let checks = [
        "two-session-normal-limit",
        "one-emergency-session-limit",
        "boolean-and-count-facts",
        "versioned-checkpoint",
        "pre-signal-checkpoint-reservation",
        "fixed-checkpoint-location",
        "cleanup-needed-state-machine",
        "structured-failure-progress",
        "lifecycle-helper-reuse",
        "blob-csp-policy-classification",
        "blob-xhr-diagnostic-fallback",
        "blob-diagnostic-no-apply",
        "privacy-safe-failure-classification",
        "bounded-policy-event-wait",
        "scheme-only-csp-proof-with-ambiguity-rejection",
        "blob-url-store-revocation",
        "zero-residual-contract",
    ]

    public init() {}
}
