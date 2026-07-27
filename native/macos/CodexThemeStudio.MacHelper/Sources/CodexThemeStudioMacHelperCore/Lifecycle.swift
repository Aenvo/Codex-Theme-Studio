import Darwin
import Foundation

public protocol SignalSending {
    func activateInspector(processId: Int32) throws
}

public protocol NodeRunning {
    func run(
        mode: String,
        snapshot: TrustedSnapshot,
        theme: ThemeRequest?) throws -> NodeFacts
}

public protocol LifecycleSleeping {
    func sleep(milliseconds: Int)
}

public struct NodeFacts: Codable, Equatable {
    public let eligibleWindowCount: Int
    public let appliedWindowCount: Int
    public let residualCount: Int
    public let cleanupVerified: Bool
    public let visualEffectApplied: Bool
}

public struct DarwinSignalSender: SignalSending {
    public init() {}

    public func activateInspector(processId: Int32) throws {
        guard kill(processId, SIGUSR1) == 0 else {
            throw HelperFailure("inspector.activation_failed", stage: "signal")
        }
    }
}

public struct ThreadSleeper: LifecycleSleeping {
    public init() {}

    public func sleep(milliseconds: Int) {
        Thread.sleep(forTimeInterval: Double(milliseconds) / 1_000)
    }
}

public final class HelperEngine {
    private let discovery: TrustedDiscovering
    private let ports: PortQuerying
    private let signal: SignalSending
    private let node: NodeRunning
    private let sleeper: LifecycleSleeping

    public init(
        discovery: TrustedDiscovering,
        ports: PortQuerying,
        signal: SignalSending,
        node: NodeRunning,
        sleeper: LifecycleSleeping)
    {
        self.discovery = discovery
        self.ports = ports
        self.signal = signal
        self.node = node
        self.sleeper = sleeper
    }

    public func execute(_ request: HelperRequest) -> HelperDocument {
        do {
            let snapshot = try discovery.discover(bundlePath: request.bundlePath)
            let initial = try ports.listeners(port: HelperConstants.inspectorPort)
            guard initial.isEmpty else {
                throw HelperFailure("port.in_use", stage: "port")
            }
            if request.command == .discover {
                return success(
                    request,
                    snapshot: snapshot,
                    cleanup: .notNeeded,
                    inspector: .notOpened,
                    facts: nil)
            }
            let modes: [String]
            switch request.command {
            case .inspectRuntime:
                modes = ["inspect"]
            case .applyTemporary:
                modes = ["apply"]
            case .qualifyAndApply:
                modes = ["apply-cleanup", "apply"]
            case .cleanup:
                modes = ["cleanup"]
            case .discover:
                modes = []
            }
            var lastFacts: NodeFacts?
            for mode in modes {
                let current = try discovery.discover(bundlePath: request.bundlePath)
                guard current == snapshot else {
                    throw HelperFailure("process.identity_changed", stage: "process")
                }
                try signal.activateInspector(processId: snapshot.process.processId)
                try waitForOwnedListener(processId: snapshot.process.processId)
                do {
                    lastFacts = try node.run(
                        mode: mode,
                        snapshot: snapshot,
                        theme: request.theme)
                    try waitForClosedPort()
                } catch {
                    try emergencyCloseIfSafe(
                        snapshot: snapshot,
                        bundlePath: request.bundlePath)
                    throw error
                }
            }
            let final = try discovery.discover(bundlePath: request.bundlePath)
            guard final == snapshot else {
                throw HelperFailure("process.identity_changed", stage: "process")
            }
            let cleanup: CleanupDisposition =
                request.command == .cleanup ||
                request.command == .qualifyAndApply
                    ? .verified
                    : .notNeeded
            return success(
                request,
                snapshot: snapshot,
                cleanup: cleanup,
                inspector: .closed,
                facts: lastFacts)
        } catch let failure as HelperFailure {
            return HelperDocument(
                schemaVersion: HelperConstants.schemaVersion,
                protocolVersion: HelperConstants.protocolVersion,
                toolVersion: HelperConstants.toolVersion,
                requestId: request.requestId,
                status: "error",
                result: nil,
                error: HelperErrorBody(
                    code: failure.code,
                    stage: failure.stage))
        } catch {
            return HelperDocument(
                schemaVersion: HelperConstants.schemaVersion,
                protocolVersion: HelperConstants.protocolVersion,
                toolVersion: HelperConstants.toolVersion,
                requestId: request.requestId,
                status: "error",
                result: nil,
                error: HelperErrorBody(
                    code: "helper.unexpected",
                    stage: "helper"))
        }
    }

    private func waitForOwnedListener(processId: Int32) throws {
        for _ in 0..<67 {
            let listeners = try ports.listeners(port: HelperConstants.inspectorPort)
            if !listeners.isEmpty {
                guard listeners.allSatisfy({
                    $0.processId == processId &&
                    ($0.address == "127.0.0.1" || $0.address == "[::1]")
                }) else {
                    throw HelperFailure("port.owner_mismatch", stage: "port")
                }
                return
            }
            sleeper.sleep(milliseconds: 75)
        }
        throw HelperFailure("inspector.activation_failed", stage: "port")
    }

    private func waitForClosedPort() throws {
        for _ in 0..<41 {
            if try ports.listeners(port: HelperConstants.inspectorPort).isEmpty {
                return
            }
            sleeper.sleep(milliseconds: 75)
        }
        throw HelperFailure("inspector.close_failed", stage: "port")
    }

    private func emergencyCloseIfSafe(
        snapshot: TrustedSnapshot,
        bundlePath: String) throws
    {
        let current = try discovery.discover(bundlePath: bundlePath)
        guard current == snapshot else {
            throw HelperFailure("process.identity_changed", stage: "process")
        }
        let listeners = try ports.listeners(port: HelperConstants.inspectorPort)
        if listeners.isEmpty {
            return
        }
        guard listeners.allSatisfy({
            $0.processId == snapshot.process.processId &&
            ($0.address == "127.0.0.1" || $0.address == "[::1]")
        }) else {
            throw HelperFailure("port.owner_mismatch", stage: "port")
        }
        do {
            _ = try node.run(
                mode: "cleanup",
                snapshot: snapshot,
                theme: nil)
            try waitForClosedPort()
        } catch {
            throw HelperFailure("renderer.cleanup_failed", stage: "cleanup")
        }
    }

    private func success(
        _ request: HelperRequest,
        snapshot: TrustedSnapshot,
        cleanup: CleanupDisposition,
        inspector: InspectorDisposition,
        facts: NodeFacts?) -> HelperDocument
    {
        HelperDocument(
            schemaVersion: HelperConstants.schemaVersion,
            protocolVersion: HelperConstants.protocolVersion,
            toolVersion: HelperConstants.toolVersion,
            requestId: request.requestId,
            status: "ok",
            result: HelperResult(
                installation: snapshot.installation,
                process: snapshot.process,
                capabilities: CapabilityEvidence(),
                cleanupDisposition: cleanup,
                inspectorDisposition: inspector,
                eligibleWindowCount: facts?.eligibleWindowCount ?? 0,
                appliedWindowCount: facts?.appliedWindowCount ?? 0,
                residualCount: facts?.residualCount ?? 0,
                portListenerCount: 0),
            error: nil)
    }
}

public struct SystemNodeRunner: NodeRunning {
    public init() {}

    public func run(
        mode: String,
        snapshot: TrustedSnapshot,
        theme: ThemeRequest?) throws -> NodeFacts
    {
        let helperURL = URL(fileURLWithPath: CommandLine.arguments[0])
            .standardizedFileURL
        let contents = helperURL
            .deletingLastPathComponent()
            .deletingLastPathComponent()
        let nodeURL = contents.appendingPathComponent("Helpers/node")
        let scriptURL = contents
            .appendingPathComponent("Resources/runtime/macos/cdp-client.mjs")
        let rendererURL = contents
            .appendingPathComponent("Resources/runtime/macos/renderer-runtime.mjs")
        let manifestURL = contents
            .appendingPathComponent("Resources/runtime-manifest.json")
        let manifest = try RuntimeManifest.load(
            manifestURL,
            expectedSha256: GeneratedRuntimeIdentity.runtimeManifestSha256)
        try manifest.verify(
            nodeURL: nodeURL,
            scriptURL: scriptURL,
            rendererURL: rendererURL)

        let request = NodeRequest(
            command: mode,
            host: "127.0.0.1",
            port: HelperConstants.inspectorPort,
            processId: snapshot.process.processId,
            theme: theme)
        let process = Process()
        let input = Pipe()
        let output = Pipe()
        let error = Pipe()
        process.executableURL = nodeURL
        process.arguments = [scriptURL.path]
        process.standardInput = input
        process.standardOutput = output
        process.standardError = error
        try process.run()
        try input.fileHandleForWriting.write(
            JSONEncoder().encode(request))
        try input.fileHandleForWriting.close()
        let deadline = Date().addingTimeInterval(8)
        while process.isRunning && Date() < deadline {
            Thread.sleep(forTimeInterval: 0.01)
        }
        guard !process.isRunning else {
            throw HelperFailure("renderer.timeout", stage: "node")
        }
        let outputData = output.fileHandleForReading.readDataToEndOfFile()
        let errorData = error.fileHandleForReading.readDataToEndOfFile()
        guard outputData.count <= HelperConstants.maximumResponseBytes,
              errorData.count <= 32 * 1024
        else {
            throw HelperFailure("renderer.response_too_large", stage: "node")
        }
        let document = try JSONDecoder().decode(NodeDocument.self, from: outputData)
        guard process.terminationStatus == 0,
              document.status == "ok",
              let facts = document.result
        else {
            throw HelperFailure(
                document.error?.code ?? "renderer.failed",
                stage: document.error?.stage ?? "node")
        }
        return facts
    }
}

private struct NodeRequest: Codable {
    let command: String
    let host: String
    let port: Int
    let processId: Int32
    let theme: ThemeRequest?
}

private struct NodeDocument: Codable {
    let status: String
    let result: NodeFacts?
    let error: HelperErrorBody?
}

struct RuntimeManifest: Codable {
    let schemaVersion: Int
    let nodeSha256: String
    let scriptSha256: String
    let rendererScriptSha256: String

    static func load(
        _ url: URL,
        expectedSha256: String) throws -> RuntimeManifest
    {
        guard expectedSha256.count == 64,
              expectedSha256.allSatisfy(\.isHexDigit)
        else {
            throw HelperFailure(
                "helper.runtime_identity_unconfigured",
                stage: "runtime")
        }
        let file = try PathPolicy.requireRegularFile(url)
        guard try StableHasher.sha256(file) == expectedSha256.uppercased() else {
            throw HelperFailure(
                "helper.runtime_manifest_hash_mismatch",
                stage: "runtime")
        }
        let manifest = try JSONDecoder().decode(
            RuntimeManifest.self,
            from: Data(contentsOf: file))
        guard manifest.schemaVersion == 1 else {
            throw HelperFailure("helper.runtime_manifest_invalid", stage: "runtime")
        }
        return manifest
    }

    func verify(
        nodeURL: URL,
        scriptURL: URL,
        rendererURL: URL) throws
    {
        let node = try PathPolicy.requireRegularFile(nodeURL)
        let script = try PathPolicy.requireRegularFile(scriptURL)
        let renderer = try PathPolicy.requireRegularFile(rendererURL)
        guard try StableHasher.sha256(node) == nodeSha256,
              try StableHasher.sha256(script) == scriptSha256,
              try StableHasher.sha256(renderer) == rendererScriptSha256
        else {
            throw HelperFailure("helper.runtime_hash_mismatch", stage: "runtime")
        }
    }
}
