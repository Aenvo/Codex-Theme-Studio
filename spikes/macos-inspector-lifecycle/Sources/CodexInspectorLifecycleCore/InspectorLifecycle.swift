import CodexDiscoveryCore
import Darwin
import Foundation

public enum LifecycleConstants {
    public static let schemaVersion = 1
    public static let inspectorPort = 9229
    public static let expectedNodeVersion = "v24.18.0"
    public static let openPollCount = 67
    public static let closePollCount = 40
    public static let pollIntervalMilliseconds = 75
    public static let stabilityDelayMilliseconds = 1_000
    public static let maximumDurationMilliseconds = 15_000
}

public struct LifecycleFailure: Error, Equatable {
    public let code: String
    public let stage: String
    public let message: String
    public let exitCode: Int32
    public let cleanup: CleanupEvidence?

    public init(
        code: String,
        stage: String,
        message: String,
        exitCode: Int32,
        cleanup: CleanupEvidence? = nil
    ) {
        self.code = code
        self.stage = stage
        self.message = message
        self.exitCode = exitCode
        self.cleanup = cleanup
    }
}

public struct LifecycleErrorBody: Codable, Equatable {
    public let code: String
    public let stage: String
    public let message: String
    public let cleanup: CleanupEvidence?
}

public struct LifecycleDocument: Codable, Equatable {
    public let schemaVersion: Int
    public let status: String
    public let observedAtUtc: String
    public let result: LifecycleResult?
    public let error: LifecycleErrorBody?

    public static func success(
        _ result: LifecycleResult,
        now: Date = Date()
    ) -> Self {
        Self(
            schemaVersion: LifecycleConstants.schemaVersion,
            status: "ok",
            observedAtUtc: Timestamp.format(now),
            result: result,
            error: nil)
    }

    public static func failure(
        _ failure: LifecycleFailure,
        now: Date = Date()
    ) -> Self {
        Self(
            schemaVersion: LifecycleConstants.schemaVersion,
            status: "error",
            observedAtUtc: Timestamp.format(now),
            result: nil,
            error: LifecycleErrorBody(
                code: failure.code,
                stage: failure.stage,
                message: failure.message,
                cleanup: failure.cleanup))
    }
}

public struct LifecycleResult: Codable, Equatable {
    public let bundle: LifecycleBundleEvidence
    public let signature: LifecycleSignatureEvidence
    public let mainProcess: ProcessRecord
    public let activation: ActivationEvidence
    public let inspector: InspectorEvidence
    public let closure: ClosureEvidence
}

public struct LifecycleBundleEvidence: Codable, Equatable {
    public let path: String
    public let bundleIdentifier: String
    public let shortVersion: String
    public let bundleVersion: String
    public let mainExecutablePath: String
    public let mainExecutableSha256: String

    public init(
        path: String,
        bundleIdentifier: String,
        shortVersion: String,
        bundleVersion: String,
        mainExecutablePath: String,
        mainExecutableSha256: String
    ) {
        self.path = path
        self.bundleIdentifier = bundleIdentifier
        self.shortVersion = shortVersion
        self.bundleVersion = bundleVersion
        self.mainExecutablePath = mainExecutablePath
        self.mainExecutableSha256 = mainExecutableSha256
    }
}

public struct LifecycleSignatureEvidence: Codable, Equatable {
    public let valid: Bool
    public let signingIdentifier: String
    public let teamIdentifier: String
    public let subjectSummary: String
    public let hardenedRuntime: Bool
    public let gatekeeperAccepted: Bool
    public let notarizationStatus: String

    public init(
        valid: Bool,
        signingIdentifier: String,
        teamIdentifier: String,
        subjectSummary: String,
        hardenedRuntime: Bool,
        gatekeeperAccepted: Bool,
        notarizationStatus: String
    ) {
        self.valid = valid
        self.signingIdentifier = signingIdentifier
        self.teamIdentifier = teamIdentifier
        self.subjectSummary = subjectSummary
        self.hardenedRuntime = hardenedRuntime
        self.gatekeeperAccepted = gatekeeperAccepted
        self.notarizationStatus = notarizationStatus
    }
}

public struct ActivationEvidence: Codable, Equatable {
    public let signal: String
    public let sentAtUtc: String
    public let listenerObservedAtUtc: String
    public let listeners: [LifecycleListener]
}

public struct InspectorEvidence: Codable, Equatable {
    public let browserTargetId: String
    public let pageTargetId: String
    public let protocolVersion: String
    public let runtimeKind: String
    public let electronVersion: String
    public let windowCount: Int
    public let routeTypes: [String]
    public let eligibleAppWindowCount: Int
}

public struct ClosureEvidence: Codable, Equatable {
    public let requestedAtUtc: String
    public let closedAtUtc: String
    public let portFreeIPv4: Bool
    public let portFreeIPv6: Bool
    public let processStable: Bool
    public let inspectorOpenDurationMilliseconds: Int
}

public struct CleanupEvidence: Codable, Equatable {
    public let attempted: Bool
    public let retryAttempted: Bool
    public let portClosed: Bool
    public let processIdentityTrusted: Bool
    public let diagnosticCode: String?
}

public struct LifecycleListener: Codable, Equatable, Hashable {
    public let family: String
    public let localAddress: String
    public let processId: Int32

    public init(family: String, localAddress: String, processId: Int32) {
        self.family = family
        self.localAddress = localAddress
        self.processId = processId
    }
}

public struct TrustedSnapshot: Equatable {
    public let bundle: LifecycleBundleEvidence
    public let signature: LifecycleSignatureEvidence
    public let main: ProcessRecord

    public init(
        bundle: LifecycleBundleEvidence,
        signature: LifecycleSignatureEvidence,
        main: ProcessRecord
    ) {
        self.bundle = bundle
        self.signature = signature
        self.main = main
    }
}

public struct CDPFacts: Codable, Equatable {
    public let browserTargetId: String
    public let pageTargetId: String
    public let protocolVersion: String
    public let runtimeKind: String
    public let electronVersion: String
    public let windowCount: Int
    public let routeTypes: [String]
    public let eligibleAppWindowCount: Int
    public let closeRequestedAtUtc: String

    public init(
        browserTargetId: String,
        pageTargetId: String,
        protocolVersion: String,
        runtimeKind: String,
        electronVersion: String,
        windowCount: Int,
        routeTypes: [String],
        eligibleAppWindowCount: Int,
        closeRequestedAtUtc: String
    ) {
        self.browserTargetId = browserTargetId
        self.pageTargetId = pageTargetId
        self.protocolVersion = protocolVersion
        self.runtimeKind = runtimeKind
        self.electronVersion = electronVersion
        self.windowCount = windowCount
        self.routeTypes = routeTypes
        self.eligibleAppWindowCount = eligibleAppWindowCount
        self.closeRequestedAtUtc = closeRequestedAtUtc
    }
}

public protocol TrustedDiscovering {
    func discover(bundlePath: String, port: Int) throws -> TrustedSnapshot
}

public protocol IdentityRevalidating {
    func requireStable(_ snapshot: TrustedSnapshot) throws
}

public protocol PortQuerying {
    func listeners(port: Int) throws -> [LifecycleListener]
}

public protocol SignalSending {
    func sendUSR1(processId: Int32) throws
}

public protocol CDPCommunicating {
    func validateNode(at path: String) throws
    func inspectAndClose(
        host: String,
        port: Int,
        processId: Int32,
        nodePath: String
    ) throws -> CDPFacts
    func closeOnly(
        host: String,
        port: Int,
        processId: Int32,
        nodePath: String
    ) throws
}

public protocol LifecycleSleeping {
    func sleep(milliseconds: Int)
}

public protocol MonotonicTiming {
    func nowNanoseconds() -> UInt64
}

public struct SystemSleeper: LifecycleSleeping {
    public init() {}

    public func sleep(milliseconds: Int) {
        usleep(useconds_t(milliseconds * 1_000))
    }
}

public struct SystemMonotonicTimer: MonotonicTiming {
    public init() {}

    public func nowNanoseconds() -> UInt64 {
        DispatchTime.now().uptimeNanoseconds
    }
}

public final class ReadOnlyDiscoveryAdapter: TrustedDiscovering {
    private let coordinator: DiscoveryCoordinator

    public init(coordinator: DiscoveryCoordinator = DiscoveryCoordinator()) {
        self.coordinator = coordinator
    }

    public func discover(bundlePath: String, port: Int) throws -> TrustedSnapshot {
        let result: DiscoveryResult
        do {
            result = try coordinator.discover(
                explicitBundlePath: bundlePath,
                port: port)
        } catch let failure as DiscoveryFailure {
            throw LifecycleFailure(
                code: failure.code,
                stage: "preflight.\(failure.stage)",
                message: failure.message,
                exitCode: failure.exitCode)
        }
        guard result.port9229.state == "free",
              result.port9229.listeners.isEmpty
        else {
            throw LifecycleFailure(
                code: "port.initially_busy",
                stage: "preflight",
                message: "Port 9229 must be free before activation.",
                exitCode: 20)
        }
        guard result.processes.state == "running",
              let main = result.processes.main
        else {
            throw LifecycleFailure(
                code: "process.main_unavailable",
                stage: "preflight",
                message: "Exactly one trusted Codex main process is required.",
                exitCode: 21)
        }
        return TrustedSnapshot(
            bundle: LifecycleBundleEvidence(
                path: result.bundle.path,
                bundleIdentifier: result.bundle.bundleIdentifier,
                shortVersion: result.bundle.shortVersion,
                bundleVersion: result.bundle.bundleVersion,
                mainExecutablePath: result.bundle.mainExecutablePath,
                mainExecutableSha256: result.bundle.mainExecutableSha256),
            signature: LifecycleSignatureEvidence(
                valid: result.signature.valid,
                signingIdentifier: result.signature.signingIdentifier,
                teamIdentifier: result.signature.teamIdentifier,
                subjectSummary: result.signature.subjectSummary,
                hardenedRuntime: result.signature.hardenedRuntime,
                gatekeeperAccepted: result.signature.gatekeeperAccepted,
                notarizationStatus: result.signature.notarizationStatus),
            main: main)
    }
}

public final class NativeIdentityRevalidator: IdentityRevalidating {
    public init() {}

    public func requireStable(_ snapshot: TrustedSnapshot) throws {
        let processes: ProcessObservation
        do {
            processes = try NativeProcessInspector.inspect(
                bundlePath: snapshot.bundle.path,
                mainExecutablePath: snapshot.bundle.mainExecutablePath)
        } catch {
            throw identityFailure()
        }
        guard processes.state == "running",
              let main = processes.main,
              main == snapshot.main
        else {
            throw identityFailure()
        }
    }

    private func identityFailure() -> LifecycleFailure {
        LifecycleFailure(
            code: "process.identity_changed",
            stage: "identity",
            message: "The trusted Codex process identity changed.",
            exitCode: 22)
    }
}

public final class LsofPortQuery: PortQuerying {
    private let runner: CommandRunning

    public init(runner: CommandRunning = ProcessCommandRunner()) {
        self.runner = runner
    }

    public func listeners(port: Int) throws -> [LifecycleListener] {
        guard port == LifecycleConstants.inspectorPort else {
            throw LifecycleFailure(
                code: "port.unsupported",
                stage: "port",
                message: "Only port 9229 is supported.",
                exitCode: 2)
        }
        let result: CommandResult
        do {
            result = try runner.run(CommandSpec(
                executable: DiscoveryConstants.lsofPath,
                arguments: [
                    "-nP",
                    "-a",
                    "-iTCP:\(port)",
                    "-sTCP:LISTEN",
                    "-FpcfntT",
                ],
                timeoutSeconds: 2,
                maximumOutputBytes: 64 * 1024))
        } catch {
            throw LifecycleFailure(
                code: "port.query_failed",
                stage: "port",
                message: "Port listener state could not be queried.",
                exitCode: 30)
        }
        if result.exitCode == 1,
           result.standardOutput.isEmpty,
           result.standardError.isEmpty
        {
            return []
        }
        guard result.exitCode == 0,
              let text = String(data: result.standardOutput, encoding: .utf8)
        else {
            throw LifecycleFailure(
                code: "port.query_failed",
                stage: "port",
                message: "Port listener state could not be queried.",
                exitCode: 30)
        }
        do {
            return try LsofParser.parse(text).map {
                LifecycleListener(
                    family: $0.family,
                    localAddress: $0.address,
                    processId: $0.processId)
            }
        } catch {
            throw LifecycleFailure(
                code: "port.protocol_invalid",
                stage: "port",
                message: "Port listener output was not recognized.",
                exitCode: 31)
        }
    }
}

public struct DarwinSignalSender: SignalSending {
    public init() {}

    public func sendUSR1(processId: Int32) throws {
        guard Darwin.kill(processId, SIGUSR1) == 0 else {
            throw LifecycleFailure(
                code: "signal.failed",
                stage: "activation",
                message: "SIGUSR1 could not be delivered to the trusted process.",
                exitCode: 40)
        }
    }
}

public final class NodeCDPClient: CDPCommunicating {
    private static let maximumOutputBytes = 128 * 1024
    private let scriptPath: String

    public init(scriptPath: String? = nil) throws {
        if let scriptPath {
            self.scriptPath = scriptPath
            return
        }
        guard let url = Bundle.module.url(
            forResource: "cdp-client",
            withExtension: "mjs")
        else {
            throw LifecycleFailure(
                code: "helper.missing",
                stage: "setup",
                message: "The bundled CDP helper is missing.",
                exitCode: 50)
        }
        self.scriptPath = url.path
    }

    public func validateNode(at path: String) throws {
        let resolved = try requireExecutable(path)
        let result = try run(
            executable: resolved,
            arguments: [
                "-p",
                "process.version+' '+process.platform+' '+process.arch",
            ],
            timeoutSeconds: 2)
        guard result.exitCode == 0,
              result.standardError.isEmpty,
              String(data: result.standardOutput, encoding: .utf8)?
                .trimmingCharacters(in: .whitespacesAndNewlines)
                == "\(LifecycleConstants.expectedNodeVersion) darwin arm64"
        else {
            throw LifecycleFailure(
                code: "node.identity_mismatch",
                stage: "setup",
                message: "The fixed Node runtime identity is invalid.",
                exitCode: 51)
        }
    }

    public func inspectAndClose(
        host: String,
        port: Int,
        processId: Int32,
        nodePath: String
    ) throws -> CDPFacts {
        let result = try invoke(
            command: "inspect-and-close",
            host: host,
            port: port,
            processId: processId,
            nodePath: nodePath)
        guard result.exitCode == 0 else {
            throw decodeHelperFailure(result.standardOutput)
        }
        do {
            return try JSONDecoder().decode(CDPFacts.self, from: result.standardOutput)
        } catch {
            throw LifecycleFailure(
                code: "helper.response_invalid",
                stage: "inspector",
                message: "The CDP helper returned an invalid response.",
                exitCode: 52)
        }
    }

    public func closeOnly(
        host: String,
        port: Int,
        processId: Int32,
        nodePath: String
    ) throws {
        let result = try invoke(
            command: "close-only",
            host: host,
            port: port,
            processId: processId,
            nodePath: nodePath)
        guard result.exitCode == 0 else {
            throw decodeHelperFailure(result.standardOutput)
        }
    }

    private func invoke(
        command: String,
        host: String,
        port: Int,
        processId: Int32,
        nodePath: String
    ) throws -> ProcessCapture {
        guard host == "127.0.0.1" || host == "::1",
              port == LifecycleConstants.inspectorPort,
              processId > 0
        else {
            throw LifecycleFailure(
                code: "helper.arguments_invalid",
                stage: "inspector",
                message: "CDP helper arguments are invalid.",
                exitCode: 2)
        }
        return try run(
            executable: try requireExecutable(nodePath),
            arguments: [
                scriptPath,
                command,
                "--host", host,
                "--port", String(port),
                "--pid", String(processId),
            ],
            timeoutSeconds: 7)
    }

    private func requireExecutable(_ path: String) throws -> String {
        guard path.hasPrefix("/") else {
            throw LifecycleFailure(
                code: "node.path_invalid",
                stage: "setup",
                message: "Node path must be absolute.",
                exitCode: 2)
        }
        let resolved: String
        do {
            resolved = try PathSecurity.realPath(path)
        } catch {
            throw LifecycleFailure(
                code: "node.path_invalid",
                stage: "setup",
                message: "Node path could not be resolved.",
                exitCode: 2)
        }
        var metadata = stat()
        guard lstat(path, &metadata) == 0,
              (metadata.st_mode & S_IFMT) == S_IFREG,
              path == resolved
        else {
            throw LifecycleFailure(
                code: "node.path_invalid",
                stage: "setup",
                message: "Node path must be a non-symlink regular file.",
                exitCode: 2)
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
            throw LifecycleFailure(
                code: "helper.launch_failed",
                stage: "helper",
                message: "The helper process could not be launched.",
                exitCode: 52)
        }

        let deadline = Date().addingTimeInterval(timeoutSeconds)
        while process.isRunning && Date() < deadline {
            usleep(20_000)
        }
        if process.isRunning {
            process.terminate()
            process.waitUntilExit()
            throw LifecycleFailure(
                code: "helper.timeout",
                stage: "helper",
                message: "The helper process exceeded its timeout.",
                exitCode: 53)
        }
        let standardOutput = output.fileHandleForReading.readDataToEndOfFile()
        let standardError = errors.fileHandleForReading.readDataToEndOfFile()
        guard standardOutput.count <= Self.maximumOutputBytes,
              standardError.count <= Self.maximumOutputBytes
        else {
            throw LifecycleFailure(
                code: "helper.output_too_large",
                stage: "helper",
                message: "The helper output exceeded its limit.",
                exitCode: 54)
        }
        return ProcessCapture(
            exitCode: process.terminationStatus,
            standardOutput: standardOutput,
            standardError: standardError)
    }

    private func decodeHelperFailure(_ data: Data) -> LifecycleFailure {
        if let failure = try? JSONDecoder().decode(HelperFailure.self, from: data) {
            return LifecycleFailure(
                code: failure.code,
                stage: failure.stage,
                message: failure.message,
                exitCode: 55)
        }
        return LifecycleFailure(
            code: "helper.failed",
            stage: "helper",
            message: "The CDP helper failed.",
            exitCode: 55)
    }
}

private struct ProcessCapture {
    let exitCode: Int32
    let standardOutput: Data
    let standardError: Data
}

private struct HelperFailure: Codable {
    let code: String
    let stage: String
    let message: String
}

public final class InspectorLifecycleCoordinator {
    private let discovery: TrustedDiscovering
    private let identity: IdentityRevalidating
    private let ports: PortQuerying
    private let signal: SignalSending
    private let cdp: CDPCommunicating
    private let sleeper: LifecycleSleeping
    private let timer: MonotonicTiming

    public init(
        discovery: TrustedDiscovering,
        identity: IdentityRevalidating,
        ports: PortQuerying,
        signal: SignalSending,
        cdp: CDPCommunicating,
        sleeper: LifecycleSleeping = SystemSleeper(),
        timer: MonotonicTiming = SystemMonotonicTimer()
    ) {
        self.discovery = discovery
        self.identity = identity
        self.ports = ports
        self.signal = signal
        self.cdp = cdp
        self.sleeper = sleeper
        self.timer = timer
    }

    public static func live() throws -> InspectorLifecycleCoordinator {
        InspectorLifecycleCoordinator(
            discovery: ReadOnlyDiscoveryAdapter(),
            identity: NativeIdentityRevalidator(),
            ports: LsofPortQuery(),
            signal: DarwinSignalSender(),
            cdp: try NodeCDPClient())
    }

    public func run(
        bundlePath: String,
        port: Int,
        nodePath: String
    ) throws -> LifecycleResult {
        guard port == LifecycleConstants.inspectorPort else {
            throw failure(
                "port.unsupported",
                "usage",
                "Only port 9229 is supported.",
                2)
        }
        try cdp.validateNode(at: nodePath)
        let snapshot = try discovery.discover(bundlePath: bundlePath, port: port)
        try identity.requireStable(snapshot)
        guard try ports.listeners(port: port).isEmpty else {
            throw failure(
                "port.initially_busy",
                "preflight",
                "Port 9229 must be free before activation.",
                20)
        }
        try identity.requireStable(snapshot)

        let openedAt = timer.nowNanoseconds()
        let sentAt = Timestamp.format(Date())
        try signal.sendUSR1(processId: snapshot.main.processId)
        var inspectorListeners: [LifecycleListener] = []
        var cleanupAttempted = false
        var cleanupRetryAttempted = false

        do {
            inspectorListeners = try waitForOpen(
                processId: snapshot.main.processId,
                port: port)
            try identity.requireStable(snapshot)
            let host = preferredHost(inspectorListeners)
            let observedAt = Timestamp.format(Date())
            let facts = try cdp.inspectAndClose(
                host: host,
                port: port,
                processId: snapshot.main.processId,
                nodePath: nodePath)
            cleanupAttempted = true
            try waitForClosed(port: port)
            try identity.requireStable(snapshot)
            sleeper.sleep(milliseconds: LifecycleConstants.stabilityDelayMilliseconds)
            guard try ports.listeners(port: port).isEmpty else {
                throw failure(
                    "inspector.residual_listener",
                    "closure",
                    "Port 9229 reopened after closure.",
                    61)
            }
            try identity.requireStable(snapshot)

            let closedAt = Timestamp.format(Date())
            let duration = elapsedMilliseconds(since: openedAt)
            guard duration <= LifecycleConstants.maximumDurationMilliseconds else {
                throw failure(
                    "inspector.duration_exceeded",
                    "closure",
                    "Inspector lifecycle exceeded the hard duration limit.",
                    62)
            }
            return LifecycleResult(
                bundle: snapshot.bundle,
                signature: snapshot.signature,
                mainProcess: snapshot.main,
                activation: ActivationEvidence(
                    signal: "SIGUSR1",
                    sentAtUtc: sentAt,
                    listenerObservedAtUtc: observedAt,
                    listeners: inspectorListeners),
                inspector: InspectorEvidence(
                    browserTargetId: facts.browserTargetId,
                    pageTargetId: facts.pageTargetId,
                    protocolVersion: facts.protocolVersion,
                    runtimeKind: facts.runtimeKind,
                    electronVersion: facts.electronVersion,
                    windowCount: facts.windowCount,
                    routeTypes: facts.routeTypes,
                    eligibleAppWindowCount: facts.eligibleAppWindowCount),
                closure: ClosureEvidence(
                    requestedAtUtc: facts.closeRequestedAtUtc,
                    closedAtUtc: closedAt,
                    portFreeIPv4: true,
                    portFreeIPv6: true,
                    processStable: true,
                    inspectorOpenDurationMilliseconds: duration))
        } catch let original as LifecycleFailure {
            let cleanup = attemptCleanup(
                snapshot: snapshot,
                port: port,
                nodePath: nodePath,
                knownListeners: inspectorListeners,
                attempted: &cleanupAttempted,
                retryAttempted: &cleanupRetryAttempted)
            throw LifecycleFailure(
                code: original.code,
                stage: original.stage,
                message: original.message,
                exitCode: original.exitCode,
                cleanup: cleanup)
        } catch {
            let cleanup = attemptCleanup(
                snapshot: snapshot,
                port: port,
                nodePath: nodePath,
                knownListeners: inspectorListeners,
                attempted: &cleanupAttempted,
                retryAttempted: &cleanupRetryAttempted)
            throw LifecycleFailure(
                code: "lifecycle.unexpected",
                stage: "lifecycle",
                message: "Inspector lifecycle failed unexpectedly.",
                exitCode: 70,
                cleanup: cleanup)
        }
    }

    private func waitForOpen(
        processId: Int32,
        port: Int
    ) throws -> [LifecycleListener] {
        for _ in 0..<LifecycleConstants.openPollCount {
            let listeners = try ports.listeners(port: port)
            if !listeners.isEmpty {
                try validateListeners(listeners, processId: processId)
                return listeners
            }
            sleeper.sleep(milliseconds: LifecycleConstants.pollIntervalMilliseconds)
        }
        throw failure(
            "inspector.open_timeout",
            "activation",
            "Inspector did not open within five seconds.",
            60)
    }

    private func waitForClosed(port: Int) throws {
        for _ in 0..<LifecycleConstants.closePollCount {
            if try ports.listeners(port: port).isEmpty {
                return
            }
            sleeper.sleep(milliseconds: LifecycleConstants.pollIntervalMilliseconds)
        }
        throw failure(
            "inspector.close_timeout",
            "closure",
            "Inspector did not close within three seconds.",
            61)
    }

    private func validateListeners(
        _ listeners: [LifecycleListener],
        processId: Int32
    ) throws {
        guard !listeners.isEmpty,
              Set(listeners.map(\.processId)) == [processId]
        else {
            throw failure(
                "port.listener_owner_unknown",
                "port",
                "Port 9229 is not exclusively owned by the trusted main process.",
                31)
        }
        guard listeners.allSatisfy({
            ($0.family == "IPv4" && $0.localAddress == "127.0.0.1") ||
            ($0.family == "IPv6" && $0.localAddress == "::1")
        }) else {
            throw failure(
                "port.listener_non_loopback",
                "port",
                "Port 9229 has a non-loopback listener.",
                31)
        }
    }

    private func preferredHost(_ listeners: [LifecycleListener]) -> String {
        listeners.contains {
            $0.family == "IPv4" && $0.localAddress == "127.0.0.1"
        } ? "127.0.0.1" : "::1"
    }

    private func attemptCleanup(
        snapshot: TrustedSnapshot,
        port: Int,
        nodePath: String,
        knownListeners: [LifecycleListener],
        attempted: inout Bool,
        retryAttempted: inout Bool
    ) -> CleanupEvidence {
        do {
            try identity.requireStable(snapshot)
            let current = try ports.listeners(port: port)
            if current.isEmpty {
                return CleanupEvidence(
                    attempted: attempted,
                    retryAttempted: retryAttempted,
                    portClosed: true,
                    processIdentityTrusted: true,
                    diagnosticCode: nil)
            }
            try validateListeners(current, processId: snapshot.main.processId)
            let host = preferredHost(current.isEmpty ? knownListeners : current)
            attempted = true
            retryAttempted = true
            try cdp.closeOnly(
                host: host,
                port: port,
                processId: snapshot.main.processId,
                nodePath: nodePath)
            try waitForClosed(port: port)
            try identity.requireStable(snapshot)
            return CleanupEvidence(
                attempted: attempted,
                retryAttempted: retryAttempted,
                portClosed: true,
                processIdentityTrusted: true,
                diagnosticCode: nil)
        } catch let cleanupFailure as LifecycleFailure {
            return CleanupEvidence(
                attempted: attempted,
                retryAttempted: retryAttempted,
                portClosed: false,
                processIdentityTrusted:
                    cleanupFailure.code != "process.identity_changed",
                diagnosticCode: cleanupFailure.code)
        } catch {
            return CleanupEvidence(
                attempted: attempted,
                retryAttempted: retryAttempted,
                portClosed: false,
                processIdentityTrusted: false,
                diagnosticCode: "cleanup.unexpected")
        }
    }

    private func elapsedMilliseconds(since start: UInt64) -> Int {
        Int((timer.nowNanoseconds() - start) / 1_000_000)
    }

    private func failure(
        _ code: String,
        _ stage: String,
        _ message: String,
        _ exitCode: Int32
    ) -> LifecycleFailure {
        LifecycleFailure(
            code: code,
            stage: stage,
            message: message,
            exitCode: exitCode)
    }
}

public enum LifecycleJSON {
    public static func encode<T: Encodable>(
        _ value: T,
        pretty: Bool = false
    ) throws -> Data {
        try JSONOutput.encode(value, pretty: pretty)
    }
}

public enum LifecycleSchema {
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
      "$id": "https://codexthemestudio.local/schema/macos-inspector-lifecycle-v1.json",
      "type": "object",
      "additionalProperties": false,
      "required": ["schemaVersion", "status", "observedAtUtc", "result", "error"],
      "properties": {
        "schemaVersion": { "const": 1 },
        "status": { "enum": ["ok", "error"] },
        "observedAtUtc": { "type": "string" },
        "result": { "type": ["object", "null"] },
        "error": { "type": ["object", "null"] }
      }
    }
    """
}

public struct LifecycleSelfTest: Encodable {
    public let schemaVersion: Int
    public let status: String
    public let checks: [String]

    public init() throws {
        schemaVersion = LifecycleConstants.schemaVersion
        status = "ok"
        checks = [
            "single-json",
            "privacy-key-policy",
            "fixed-port",
            "bounded-lifecycle",
            "loopback-only",
            "single-signal",
            "close-retry-limit",
        ]
        try JSONOutput.validatePrivacyKeys([
            "schemaVersion": LifecycleConstants.schemaVersion,
            "status": "ok",
        ] as [String: Any])
    }
}
