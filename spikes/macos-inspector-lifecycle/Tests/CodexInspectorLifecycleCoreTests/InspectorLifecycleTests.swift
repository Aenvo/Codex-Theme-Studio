@testable import CodexInspectorLifecycleCore
import CodexDiscoveryCore
import Foundation
import XCTest

final class InspectorLifecycleTests: XCTestCase {
    func testSuccessfulLifecycleSendsOneSignalClosesAndRevalidates() throws {
        let snapshot = makeSnapshot()
        let discovery = FakeDiscovery(snapshot: snapshot)
        let identity = FakeIdentity()
        let ports = FakePorts([
            [],
            [loopback(snapshot.main.processId)],
            [],
            [],
        ])
        let signal = FakeSignal()
        let cdp = FakeCDP()
        let coordinator = makeCoordinator(
            discovery: discovery,
            identity: identity,
            ports: ports,
            signal: signal,
            cdp: cdp)

        let result = try coordinator.run(
            bundlePath: snapshot.bundle.path,
            port: LifecycleConstants.inspectorPort,
            nodePath: "/fixed/node")

        XCTAssertEqual(signal.processIds, [snapshot.main.processId])
        XCTAssertEqual(cdp.inspectCalls, 1)
        XCTAssertEqual(cdp.closeCalls, 0)
        XCTAssertEqual(result.inspector.routeTypes, ["app:index.html", "avatar-overlay"])
        XCTAssertTrue(result.closure.processStable)
        XCTAssertTrue(result.closure.portFreeIPv4)
        XCTAssertTrue(result.closure.portFreeIPv6)
        XCTAssertGreaterThanOrEqual(identity.callCount, 4)
    }

    func testInitialBusyPortStopsBeforeSignal() {
        let snapshot = makeSnapshot()
        let signal = FakeSignal()
        let coordinator = makeCoordinator(
            discovery: FakeDiscovery(snapshot: snapshot),
            ports: FakePorts([[loopback(snapshot.main.processId)]]),
            signal: signal)

        XCTAssertThrowsError(try coordinator.run(
            bundlePath: snapshot.bundle.path,
            port: LifecycleConstants.inspectorPort,
            nodePath: "/fixed/node"))
        {
            XCTAssertEqual(($0 as? LifecycleFailure)?.code, "port.initially_busy")
        }
        XCTAssertTrue(signal.processIds.isEmpty)
    }

    func testDiscoveryAmbiguityStopsBeforeSignal() {
        let signal = FakeSignal()
        let coordinator = makeCoordinator(
            discovery: FakeDiscovery(error: LifecycleFailure(
                code: "process.main_ambiguous",
                stage: "preflight",
                message: "Multiple trusted main processes were found.",
                exitCode: 21)),
            signal: signal)

        XCTAssertThrowsError(try coordinator.run(
            bundlePath: "/Applications/ChatGPT.app",
            port: LifecycleConstants.inspectorPort,
            nodePath: "/fixed/node"))
        {
            XCTAssertEqual(
                ($0 as? LifecycleFailure)?.code,
                "process.main_ambiguous")
        }
        XCTAssertTrue(signal.processIds.isEmpty)
    }

    func testSignalFailureDoesNotAttemptCDP() {
        let snapshot = makeSnapshot()
        let cdp = FakeCDP()
        let signal = FakeSignal(error: LifecycleFailure(
            code: "signal.failed",
            stage: "activation",
            message: "Signal failed.",
            exitCode: 40))
        let coordinator = makeCoordinator(
            discovery: FakeDiscovery(snapshot: snapshot),
            ports: FakePorts([[]]),
            signal: signal,
            cdp: cdp)

        XCTAssertThrowsError(try coordinator.run(
            bundlePath: snapshot.bundle.path,
            port: LifecycleConstants.inspectorPort,
            nodePath: "/fixed/node"))
        {
            XCTAssertEqual(($0 as? LifecycleFailure)?.code, "signal.failed")
        }
        XCTAssertEqual(cdp.inspectCalls, 0)
        XCTAssertEqual(cdp.closeCalls, 0)
    }

    func testOpenTimeoutReportsClosedCleanupWithoutConnecting() {
        let snapshot = makeSnapshot()
        let cdp = FakeCDP()
        let coordinator = makeCoordinator(
            discovery: FakeDiscovery(snapshot: snapshot),
            ports: FakePorts(Array(
                repeating: [],
                count: LifecycleConstants.openPollCount + 2)),
            cdp: cdp)

        XCTAssertThrowsError(try coordinator.run(
            bundlePath: snapshot.bundle.path,
            port: LifecycleConstants.inspectorPort,
            nodePath: "/fixed/node"))
        {
            let failure = $0 as? LifecycleFailure
            XCTAssertEqual(failure?.code, "inspector.open_timeout")
            XCTAssertEqual(failure?.cleanup?.portClosed, true)
        }
        XCTAssertEqual(cdp.inspectCalls, 0)
        XCTAssertEqual(cdp.closeCalls, 0)
    }

    func testWrongOwnerAndNonLoopbackFailClosed() {
        let snapshot = makeSnapshot()
        let invalidListeners = [
            LifecycleListener(
                family: "IPv4",
                localAddress: "127.0.0.1",
                processId: snapshot.main.processId + 1),
            LifecycleListener(
                family: "IPv4",
                localAddress: "0.0.0.0",
                processId: snapshot.main.processId),
        ]
        let expectedCodes = [
            "port.listener_owner_unknown",
            "port.listener_non_loopback",
        ]
        for (listener, expectedCode) in zip(invalidListeners, expectedCodes) {
            let cdp = FakeCDP()
            let coordinator = makeCoordinator(
                discovery: FakeDiscovery(snapshot: snapshot),
                ports: FakePorts([[], [listener], [listener]]),
                cdp: cdp)
            XCTAssertThrowsError(try coordinator.run(
                bundlePath: snapshot.bundle.path,
                port: LifecycleConstants.inspectorPort,
                nodePath: "/fixed/node"))
            {
                XCTAssertEqual(($0 as? LifecycleFailure)?.code, expectedCode)
                XCTAssertEqual(
                    ($0 as? LifecycleFailure)?.cleanup?.portClosed,
                    false)
            }
            XCTAssertEqual(cdp.inspectCalls, 0)
            XCTAssertEqual(cdp.closeCalls, 0)
        }
    }

    func testIdentityChangePreventsCleanupConnection() {
        let snapshot = makeSnapshot()
        let identity = FakeIdentity(failAtCall: 3)
        let cdp = FakeCDP()
        let coordinator = makeCoordinator(
            discovery: FakeDiscovery(snapshot: snapshot),
            identity: identity,
            ports: FakePorts([
                [],
                [loopback(snapshot.main.processId)],
            ]),
            cdp: cdp)

        XCTAssertThrowsError(try coordinator.run(
            bundlePath: snapshot.bundle.path,
            port: LifecycleConstants.inspectorPort,
            nodePath: "/fixed/node"))
        {
            let failure = $0 as? LifecycleFailure
            XCTAssertEqual(failure?.code, "process.identity_changed")
            XCTAssertEqual(failure?.cleanup?.processIdentityTrusted, false)
        }
        XCTAssertEqual(cdp.inspectCalls, 0)
        XCTAssertEqual(cdp.closeCalls, 0)
    }

    func testCloseTimeoutPerformsOneBoundedRetry() {
        let snapshot = makeSnapshot()
        let listener = loopback(snapshot.main.processId)
        var sequence: [[LifecycleListener]] = [[], [listener]]
        sequence.append(contentsOf: Array(
            repeating: [listener],
            count: LifecycleConstants.closePollCount))
        sequence.append([listener])
        sequence.append([])
        let cdp = FakeCDP()
        let coordinator = makeCoordinator(
            discovery: FakeDiscovery(snapshot: snapshot),
            ports: FakePorts(sequence),
            cdp: cdp)

        XCTAssertThrowsError(try coordinator.run(
            bundlePath: snapshot.bundle.path,
            port: LifecycleConstants.inspectorPort,
            nodePath: "/fixed/node"))
        {
            let failure = $0 as? LifecycleFailure
            XCTAssertEqual(failure?.code, "inspector.close_timeout")
            XCTAssertEqual(failure?.cleanup?.retryAttempted, true)
            XCTAssertEqual(failure?.cleanup?.portClosed, true)
        }
        XCTAssertEqual(cdp.inspectCalls, 1)
        XCTAssertEqual(cdp.closeCalls, 1)
    }

    func testPrivacyPolicyRejectsForbiddenFields() {
        XCTAssertThrowsError(try LifecycleJSON.encode(["token": "not-allowed"]))
        XCTAssertThrowsError(try LifecycleJSON.encode([
            "nested": ["environment": "not-allowed"],
        ]))
    }

    func testSchemaAndSelfTestAreSinglePrivacySafeJSONDocuments() throws {
        let documents = [
            try LifecycleSchema.data(pretty: false),
            try LifecycleJSON.encode(LifecycleSelfTest()),
        ]
        for data in documents {
            let object = try JSONSerialization.jsonObject(with: data)
            XCTAssertNoThrow(try JSONOutput.validatePrivacyKeys(object))
            XCTAssertNotNil(object as? [String: Any])
        }
    }

    private func makeCoordinator(
        discovery: TrustedDiscovering? = nil,
        identity: IdentityRevalidating = FakeIdentity(),
        ports: PortQuerying = FakePorts([[]]),
        signal: SignalSending = FakeSignal(),
        cdp: FakeCDP = FakeCDP()
    ) -> InspectorLifecycleCoordinator {
        InspectorLifecycleCoordinator(
            discovery: discovery ?? FakeDiscovery(snapshot: makeSnapshot()),
            identity: identity,
            ports: ports,
            signal: signal,
            cdp: cdp,
            sleeper: FakeSleeper(),
            timer: FakeTimer())
    }

    private func makeSnapshot() -> TrustedSnapshot {
        let bundlePath = "/Applications/ChatGPT.app"
        let executablePath = bundlePath + "/Contents/MacOS/ChatGPT"
        return TrustedSnapshot(
            bundle: LifecycleBundleEvidence(
                path: bundlePath,
                bundleIdentifier: "com.openai.codex",
                shortVersion: "26.721.41059",
                bundleVersion: "5848",
                mainExecutablePath: executablePath,
                mainExecutableSha256: String(repeating: "a", count: 64)),
            signature: LifecycleSignatureEvidence(
                valid: true,
                signingIdentifier: "com.openai.codex",
                teamIdentifier: "2DC432GLL2",
                subjectSummary: "Developer ID Application: OpenAI",
                hardenedRuntime: true,
                gatekeeperAccepted: true,
                notarizationStatus: "accepted"),
            main: ProcessRecord(
                kind: .main,
                processId: 42,
                parentProcessId: 1,
                startedAtUtc: "2026-07-26T12:42:19.083Z",
                architecture: .arm64,
                executablePath: executablePath))
    }

    private func loopback(_ processId: Int32) -> LifecycleListener {
        LifecycleListener(
            family: "IPv4",
            localAddress: "127.0.0.1",
            processId: processId)
    }
}

private final class FakeDiscovery: TrustedDiscovering {
    private let snapshot: TrustedSnapshot?
    private let error: LifecycleFailure?

    init(snapshot: TrustedSnapshot) {
        self.snapshot = snapshot
        error = nil
    }

    init(error: LifecycleFailure) {
        snapshot = nil
        self.error = error
    }

    func discover(bundlePath: String, port: Int) throws -> TrustedSnapshot {
        if let error { throw error }
        return snapshot!
    }
}

private final class FakeIdentity: IdentityRevalidating {
    private let failAtCall: Int?
    private(set) var callCount = 0

    init(failAtCall: Int? = nil) {
        self.failAtCall = failAtCall
    }

    func requireStable(_ snapshot: TrustedSnapshot) throws {
        callCount += 1
        if let failAtCall, callCount >= failAtCall {
            throw LifecycleFailure(
                code: "process.identity_changed",
                stage: "identity",
                message: "Identity changed.",
                exitCode: 22)
        }
    }
}

private final class FakePorts: PortQuerying {
    private var values: [[LifecycleListener]]
    private var index = 0

    init(_ values: [[LifecycleListener]]) {
        self.values = values
    }

    func listeners(port: Int) throws -> [LifecycleListener] {
        guard !values.isEmpty else { return [] }
        let value = values[min(index, values.count - 1)]
        index += 1
        return value
    }
}

private final class FakeSignal: SignalSending {
    private let error: LifecycleFailure?
    private(set) var processIds: [Int32] = []

    init(error: LifecycleFailure? = nil) {
        self.error = error
    }

    func sendUSR1(processId: Int32) throws {
        processIds.append(processId)
        if let error { throw error }
    }
}

private final class FakeCDP: CDPCommunicating {
    private(set) var inspectCalls = 0
    private(set) var closeCalls = 0

    func validateNode(at path: String) throws {}

    func inspectAndClose(
        host: String,
        port: Int,
        processId: Int32,
        nodePath: String
    ) throws -> CDPFacts {
        inspectCalls += 1
        return CDPFacts(
            browserTargetId: "11111111-2222-4333-8444-555555555555",
            pageTargetId: "11111111-2222-4333-8444-555555555555",
            protocolVersion: "1.1",
            runtimeKind: "node",
            electronVersion: "150.0.0",
            windowCount: 2,
            routeTypes: ["app:index.html", "avatar-overlay"],
            eligibleAppWindowCount: 1,
            closeRequestedAtUtc: "2026-07-27T00:00:01.000Z")
    }

    func closeOnly(
        host: String,
        port: Int,
        processId: Int32,
        nodePath: String
    ) throws {
        closeCalls += 1
    }
}

private struct FakeSleeper: LifecycleSleeping {
    func sleep(milliseconds: Int) {}
}

private final class FakeTimer: MonotonicTiming {
    private var value: UInt64 = 0

    func nowNanoseconds() -> UInt64 {
        defer { value += 1_000_000_000 }
        return value
    }
}
