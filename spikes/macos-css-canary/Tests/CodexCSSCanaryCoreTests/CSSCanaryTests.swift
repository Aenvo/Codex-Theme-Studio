import CodexCSSCanaryCore
import CodexInspectorLifecycleCore
import CodexDiscoveryCore
import Foundation
import Testing

@Test
func twoNormalSessionsProduceCheckpoint() throws {
    let fixture = Fixture()
    let coordinator = fixture.coordinator(
        facts: [goodFacts(), goodFacts()])
    let result = try coordinator.run6A(
        bundlePath: "/fixture/Codex.app",
        nodePath: "/fixture/node",
        lifecycleHelperPath: "/fixture/helper.mjs")
    #expect(result.completedSessionCount == 2)
    #expect(result.signalCount == 2)
    #expect(result.applyVerifiedCount == 2)
    #expect(result.cleanupVerifiedCount == 2)
    #expect(result.finalResidualCount == 0)
    #expect(result.checkpointWritten)
    #expect(fixture.signal.count == 2)
    #expect(fixture.checkpoint.written?.phase6ACompleted == true)
}

@Test
func initialResidueCleansAndStopsWithoutEmergencySession() {
    let fixture = Fixture()
    let coordinator = fixture.coordinator(facts: [cleanupFacts(residue: true)])
    do {
        _ = try coordinator.run6A(
            bundlePath: "/fixture/Codex.app",
            nodePath: "/fixture/node",
            lifecycleHelperPath: "/fixture/helper.mjs")
        Issue.record("Expected initial residue to stop phase 6A.")
    } catch let failure as CSSCanaryFailure {
        #expect(failure.code == "canary.initial_residual_detected")
        #expect(failure.cleanupAttempted)
        #expect(failure.cleanupVerified)
        #expect(fixture.signal.count == 1)
    } catch {
        Issue.record("Unexpected error type.")
    }
}

@Test
func failedSecondSessionUsesOnlyOneEmergencyCleanup() {
    let fixture = Fixture()
    let coordinator = fixture.coordinator(
        facts: [goodFacts(), nil, cleanupFacts(residue: false)])
    do {
        _ = try coordinator.run6A(
            bundlePath: "/fixture/Codex.app",
            nodePath: "/fixture/node",
            lifecycleHelperPath: "/fixture/helper.mjs")
        Issue.record("Expected session failure.")
    } catch let failure as CSSCanaryFailure {
        #expect(failure.cleanupAttempted)
        #expect(failure.cleanupVerified)
        #expect(failure.signalCount == 3)
        #expect(failure.completedSessionCount == 2)
        #expect(failure.applyVerifiedCount == 1)
        #expect(failure.cleanupVerifiedCount == 2)
        #expect(fixture.signal.count == 3)
    } catch {
        Issue.record("Unexpected error type.")
    }
}

@Test
func checkpointReservationFailureOccursBeforeDiscoveryOrSignal() {
    let fixture = Fixture()
    fixture.checkpoint.reserveFailure = CSSCanaryFailure(
        code: "checkpoint.write_failed",
        stage: "checkpoint",
        message: "Fixture reservation failed.",
        exitCode: 70)
    do {
        _ = try fixture.coordinator(facts: []).run6A(
            bundlePath: "/fixture/Codex.app",
            nodePath: "/fixture/node",
            lifecycleHelperPath: "/fixture/helper.mjs")
        Issue.record("Expected checkpoint reservation failure.")
    } catch let failure as CSSCanaryFailure {
        #expect(failure.code == "checkpoint.write_failed")
        #expect(failure.signalCount == 0)
        #expect(fixture.discovery.count == 0)
        #expect(fixture.signal.count == 0)
    } catch {
        Issue.record("Unexpected error type.")
    }
}

@Test
func checkpointCommitFailureDoesNotOpenEmergencySessionAfterCleanSessions() {
    let fixture = Fixture()
    fixture.checkpoint.commitFailure = CSSCanaryFailure(
        code: "checkpoint.write_failed",
        stage: "checkpoint",
        message: "Fixture commit failed.",
        exitCode: 70)
    do {
        _ = try fixture.coordinator(
            facts: [goodFacts(), goodFacts()]).run6A(
                bundlePath: "/fixture/Codex.app",
                nodePath: "/fixture/node",
                lifecycleHelperPath: "/fixture/helper.mjs")
        Issue.record("Expected checkpoint commit failure.")
    } catch let failure as CSSCanaryFailure {
        #expect(failure.code == "checkpoint.write_failed")
        #expect(!failure.cleanupAttempted)
        #expect(failure.cleanupVerified)
        #expect(failure.signalCount == 2)
        #expect(failure.completedSessionCount == 2)
        #expect(failure.applyVerifiedCount == 2)
        #expect(failure.cleanupVerifiedCount == 2)
        #expect(failure.finalResidualCount == 0)
        #expect(fixture.signal.count == 2)
    } catch {
        Issue.record("Unexpected error type.")
    }
}

@Test
func sixBRequiresChangedProcessAndStartFingerprints() {
    let fixture = Fixture()
    let checkpoint = Phase6Checkpoint(
        schemaVersion: CSSCanaryConstants.schemaVersion,
        canaryVersion: 1,
        toolVersion: CSSCanaryConstants.toolVersion,
        installationFingerprint: CSSCanaryCoordinator
            .fingerprints(fixture.snapshot).installation,
        processIdentityFingerprint: CSSCanaryCoordinator
            .fingerprints(fixture.snapshot).process,
        startedAtFingerprint: CSSCanaryCoordinator
            .fingerprints(fixture.snapshot).startedAt,
        phase6ACompleted: true,
        completedSessionCount: 2,
        finalResidualCount: 0,
        finalPortListenerCount: 0)
    fixture.checkpoint.readValue = checkpoint
    do {
        _ = try fixture.coordinator(facts: []).run6B(
            bundlePath: "/fixture/Codex.app",
            nodePath: "/fixture/node",
            lifecycleHelperPath: "/fixture/helper.mjs")
        Issue.record("Expected the unchanged process to be rejected.")
    } catch let failure as CSSCanaryFailure {
        #expect(failure.code == "qualification.old_process_present")
        #expect(fixture.signal.count == 0)
    } catch {
        Issue.record("Unexpected error type.")
    }
}

@Test
func publicJsonDoesNotExposePrivateFields() throws {
    let data = try CSSCanaryJSON.encode(CSSCanaryDocument.success(
        RunStateFixture.result))
    let text = String(decoding: data, as: UTF8.self).lowercased()
    for forbidden in [
        "\"argv\"", "\"commandline\"", "\"environment\"", "\"url\"",
        "\"dom\"", "\"token\"", "\"credential\"", "\"conversation\"",
        "\"pid\"", "\"targetid\"",
    ] {
        #expect(!text.contains(forbidden))
    }
}

@Test
func failureJsonContainsOnlySafeProgressCounts() throws {
    let failure = CSSCanaryFailure(
        code: "fixture.failure",
        stage: "fixture",
        message: "Safe fixture failure.",
        exitCode: 1,
        cleanupAttempted: true,
        cleanupVerified: true,
        signalCount: 2,
        completedSessionCount: 2,
        applyVerifiedCount: 2,
        cleanupVerifiedCount: 2,
        finalResidualCount: 0)
    let data = try CSSCanaryJSON.encode(
        CSSCanaryDocument.failure(failure))
    let text = String(decoding: data, as: UTF8.self).lowercased()
    #expect(text.contains("\"signalcount\":2"))
    #expect(text.contains("\"completedsessioncount\":2"))
    for forbidden in [
        "\"argv\"", "\"commandline\"", "\"environment\"", "\"url\"",
        "\"dom\"", "\"token\"", "\"credential\"", "\"conversation\"",
        "\"pid\"", "\"targetid\"",
    ] {
        #expect(!text.contains(forbidden))
    }
}

@Test
func atomicCheckpointReservationCommitsWithPrivatePermissions() throws {
    let home = temporaryHome()
    defer { try? FileManager.default.removeItem(at: home) }
    let store = AtomicCheckpointStore(homeDirectory: home)
    let reservation = try store.reserveForPhase6A()
    let root = checkpointRoot(home)
    #expect(fileMode(root.path) == 0o700)
    try reservation.commit(checkpointFixture())
    let destination = root.appendingPathComponent(
        "phase6a-checkpoint.json")
    #expect(fileMode(destination.path) == 0o600)
    #expect(try store.readPhase6A() == checkpointFixture())
}

@Test
func atomicCheckpointCancellationRemovesOnlyReservation() throws {
    let home = temporaryHome()
    defer { try? FileManager.default.removeItem(at: home) }
    let store = AtomicCheckpointStore(homeDirectory: home)
    let reservation = try store.reserveForPhase6A()
    let root = checkpointRoot(home)
    #expect(try FileManager.default.contentsOfDirectory(
        atPath: root.path).count == 1)
    reservation.cancel()
    #expect(try FileManager.default.contentsOfDirectory(
        atPath: root.path).isEmpty)
}

@Test
func atomicCheckpointRejectsExistingDestinationAndSymlinkRoot() throws {
    let existingHome = temporaryHome()
    defer { try? FileManager.default.removeItem(at: existingHome) }
    let existingStore = AtomicCheckpointStore(homeDirectory: existingHome)
    let reservation = try existingStore.reserveForPhase6A()
    try reservation.commit(checkpointFixture())
    #expect(throws: CSSCanaryFailure.self) {
        _ = try existingStore.reserveForPhase6A()
    }

    let symlinkHome = temporaryHome()
    defer { try? FileManager.default.removeItem(at: symlinkHome) }
    let root = checkpointRoot(symlinkHome)
    try FileManager.default.createDirectory(
        at: root.deletingLastPathComponent(),
        withIntermediateDirectories: true)
    let target = symlinkHome.appendingPathComponent("target")
    try FileManager.default.createDirectory(
        at: target,
        withIntermediateDirectories: true)
    try FileManager.default.createSymbolicLink(
        at: root,
        withDestinationURL: target)
    let symlinkStore = AtomicCheckpointStore(homeDirectory: symlinkHome)
    #expect(throws: CSSCanaryFailure.self) {
        _ = try symlinkStore.reserveForPhase6A()
    }
}

@Test
func atomicCheckpointRejectsUnwritableApprovedDirectory() throws {
    let home = temporaryHome()
    defer {
        chmod(checkpointRoot(home).path, 0o700)
        try? FileManager.default.removeItem(at: home)
    }
    let root = checkpointRoot(home)
    try FileManager.default.createDirectory(
        at: root,
        withIntermediateDirectories: true,
        attributes: [.posixPermissions: 0o700])
    #expect(chmod(root.path, 0o500) == 0)
    let store = AtomicCheckpointStore(homeDirectory: home)
    #expect(throws: CSSCanaryFailure.self) {
        _ = try store.reserveForPhase6A()
    }
}

@Test
func atomicCheckpointRejectsStaleReservationWithoutDeletingIt() throws {
    let home = temporaryHome()
    defer { try? FileManager.default.removeItem(at: home) }
    let root = checkpointRoot(home)
    try FileManager.default.createDirectory(
        at: root,
        withIntermediateDirectories: true,
        attributes: [.posixPermissions: 0o700])
    let stale = root.appendingPathComponent(".phase6a-stale.tmp")
    #expect(FileManager.default.createFile(
        atPath: stale.path,
        contents: Data(),
        attributes: [.posixPermissions: 0o600]))
    let store = AtomicCheckpointStore(homeDirectory: home)
    #expect(throws: CSSCanaryFailure.self) {
        _ = try store.reserveForPhase6A()
    }
    #expect(FileManager.default.fileExists(atPath: stale.path))
}

@Test
func blobDiagnosticUsesOneSessionWithoutApplyingOrWritingCheckpoint() throws {
    let fixture = Fixture()
    let coordinator = fixture.coordinator(facts: [diagnosticFacts()])
    let result = try coordinator.runBlobDiagnostic(
        bundlePath: "/fixture/Codex.app",
        nodePath: "/fixture/node",
        lifecycleHelperPath: "/fixture/helper.mjs")
    #expect(result.completedSessionCount == 1)
    #expect(result.signalCount == 1)
    #expect(result.applyVerifiedCount == 0)
    #expect(result.cleanupVerifiedCount == 1)
    #expect(result.blobFetchAllowedCount == 0)
    #expect(result.blobXHRAllowedCount == 0)
    #expect(result.blobFetchBlockedByPolicyCount == 0)
    #expect(result.blobFetchRejectedTypeErrorCount == 1)
    #expect(result.blobXHRErrorCount == 1)
    #expect(result.blobXHRStatusZeroCount == 1)
    #expect(!result.checkpointWritten)
    #expect(fixture.signal.count == 1)
    #expect(fixture.checkpoint.written == nil)
}

private final class Fixture {
    let snapshot: TrustedSnapshot
    let discovery: FakeDiscovery
    let identity = FakeIdentity()
    let ports: FakePorts
    let signal = FakeSignal()
    let sessions = FakeSessions()
    let checkpoint = FakeCheckpoint()

    init() {
        let bundle = LifecycleBundleEvidence(
            path: "/fixture/Codex.app",
            bundleIdentifier: "com.openai.codex",
            shortVersion: "1",
            bundleVersion: "1",
            mainExecutablePath: "/fixture/Codex.app/Contents/MacOS/Codex",
            mainExecutableSha256: String(repeating: "a", count: 64))
        let signature = LifecycleSignatureEvidence(
            valid: true,
            signingIdentifier: "com.openai.codex",
            teamIdentifier: "2DC432GLL2",
            subjectSummary: "Developer ID Application",
            hardenedRuntime: true,
            gatekeeperAccepted: true,
            notarizationStatus: "accepted")
        let process = ProcessRecord(
            kind: .main,
            processId: 400,
            parentProcessId: 1,
            startedAtUtc: "2026-01-01T00:00:00.000Z",
            architecture: .arm64,
            executablePath: bundle.mainExecutablePath)
        snapshot = TrustedSnapshot(
            bundle: bundle,
            signature: signature,
            main: process)
        discovery = FakeDiscovery(snapshot: snapshot)
        ports = FakePorts(processId: process.processId)
    }

    func coordinator(facts: [CanarySessionFacts?]) -> CSSCanaryCoordinator {
        sessions.queue = facts
        return CSSCanaryCoordinator(
            discovery: discovery,
            identity: identity,
            ports: ports,
            signal: signal,
            sessions: sessions,
            checkpoints: checkpoint,
            sleeper: NoSleep())
    }
}

private final class FakeDiscovery: TrustedDiscovering {
    let snapshot: TrustedSnapshot
    var count = 0
    init(snapshot: TrustedSnapshot) { self.snapshot = snapshot }
    func discover(bundlePath: String, port: Int) throws -> TrustedSnapshot {
        count += 1
        return snapshot
    }
}

private final class FakeIdentity: IdentityRevalidating {
    func requireStable(_ snapshot: TrustedSnapshot) throws {}
}

private final class FakePorts: PortQuerying {
    private let processId: Int32
    private var calls = 0
    init(processId: Int32) { self.processId = processId }
    func listeners(port: Int) throws -> [LifecycleListener] {
        calls += 1
        return calls % 3 == 2
            ? [LifecycleListener(
                family: "IPv4",
                localAddress: "127.0.0.1",
                processId: processId)]
            : []
    }
}

private final class FakeSignal: SignalSending {
    var count = 0
    func sendUSR1(processId: Int32) throws { count += 1 }
}

private final class FakeSessions: CanarySessionRunning {
    var queue: [CanarySessionFacts?] = []
    func validateTools(nodePath: String, lifecycleHelperPath: String) throws {}
    func runSession(
        host: String,
        port: Int,
        processId: Int32,
        nodePath: String,
        lifecycleHelperPath: String,
        mode: CanarySessionMode
    ) throws -> CanarySessionFacts {
        guard !queue.isEmpty else {
            throw CSSCanaryFailure(
                code: "fixture.empty",
                stage: "fixture",
                message: "No fixture result.",
                exitCode: 1)
        }
        let next = queue.removeFirst()
        guard let next else {
            throw CSSCanaryFailure(
                code: "fixture.failed",
                stage: "fixture",
                message: "Fixture failure.",
                exitCode: 1)
        }
        return next
    }
    func closeInspector(
        host: String,
        port: Int,
        processId: Int32,
        nodePath: String,
        lifecycleHelperPath: String
    ) throws {}
}

private final class FakeCheckpoint: CheckpointPersisting {
    var written: Phase6Checkpoint?
    var readValue: Phase6Checkpoint?
    var reserveFailure: CSSCanaryFailure?
    var commitFailure: CSSCanaryFailure?
    var reservationCount = 0
    var cancelCount = 0

    func reserveForPhase6A() throws -> any CheckpointReservation {
        if let reserveFailure {
            throw reserveFailure
        }
        reservationCount += 1
        return FakeCheckpointReservation(owner: self)
    }

    func readPhase6A() throws -> Phase6Checkpoint {
        guard let readValue else {
            throw CSSCanaryFailure(
                code: "fixture.missing",
                stage: "fixture",
                message: "Fixture checkpoint missing.",
                exitCode: 1)
        }
        return readValue
    }
}

private final class FakeCheckpointReservation: CheckpointReservation {
    private weak var owner: FakeCheckpoint?
    private var active = true

    init(owner: FakeCheckpoint) {
        self.owner = owner
    }

    func commit(_ checkpoint: Phase6Checkpoint) throws {
        guard let owner, active else {
            throw CSSCanaryFailure(
                code: "fixture.reservation_invalid",
                stage: "fixture",
                message: "Fixture reservation is inactive.",
                exitCode: 1)
        }
        if let commitFailure = owner.commitFailure {
            throw commitFailure
        }
        owner.written = checkpoint
        active = false
    }

    func cancel() {
        guard active else {
            return
        }
        owner?.cancelCount += 1
        active = false
    }
}

private struct NoSleep: LifecycleSleeping {
    func sleep(milliseconds: Int) {}
}

private func temporaryHome() -> URL {
    FileManager.default.temporaryDirectory
        .appendingPathComponent("cts-css-canary-\(UUID().uuidString)")
}

private func checkpointRoot(_ home: URL) -> URL {
    home.appendingPathComponent(
        "Library/Application Support/CodexThemeStudio/devtools/state/macos-css-canary")
}

private func checkpointFixture() -> Phase6Checkpoint {
    Phase6Checkpoint(
        schemaVersion: CSSCanaryConstants.schemaVersion,
        canaryVersion: CSSCanaryConstants.canaryVersion,
        toolVersion: CSSCanaryConstants.toolVersion,
        installationFingerprint: String(repeating: "a", count: 64),
        processIdentityFingerprint: String(repeating: "b", count: 64),
        startedAtFingerprint: String(repeating: "c", count: 64),
        phase6ACompleted: true,
        completedSessionCount: 2,
        finalResidualCount: 0,
        finalPortListenerCount: 0)
}

private func fileMode(_ path: String) -> mode_t? {
    var metadata = stat()
    guard lstat(path, &metadata) == 0 else {
        return nil
    }
    return metadata.st_mode & 0o777
}

private func goodFacts() -> CanarySessionFacts {
    CanarySessionFacts(
        initialResidualDetected: false,
        diagnosticOnly: false,
        cleanupOnly: false,
        eligibleWindowCount: 1,
        overlayWindowCount: 1,
        unknownWindowCount: 0,
        overlayUnmodified: true,
        applyAttempted: true,
        applyVerified: true,
        cleanupVerified: true,
        visualEffectApplied: true,
        blobContentVerified: true,
        blobURLCreated: true,
        blobFetchAllowed: false,
        blobXHRAllowed: false,
        blobFetchBlockedByPolicy: true,
        blobRevokeInvoked: true,
        postRevokeDereferenceRejected: true,
        blobReferenceCleared: true,
        policyListenerRemoved: true,
        styleCountAfterApply: 1,
        rootClassCountAfterApply: 1,
        markerCountAfterApply: 1,
        stateCountAfterApply: 1,
        cssVariablePresentAfterApply: true,
        finalResidualCount: 0,
        timerCancelled: true,
        mainJobCleared: true)
}

private func cleanupFacts(residue: Bool) -> CanarySessionFacts {
    CanarySessionFacts(
        initialResidualDetected: residue,
        diagnosticOnly: false,
        cleanupOnly: true,
        eligibleWindowCount: 1,
        overlayWindowCount: 1,
        unknownWindowCount: 0,
        overlayUnmodified: true,
        applyAttempted: false,
        applyVerified: false,
        cleanupVerified: true,
        visualEffectApplied: false,
        blobContentVerified: false,
        blobURLCreated: false,
        blobFetchAllowed: false,
        blobXHRAllowed: false,
        blobFetchBlockedByPolicy: false,
        blobRevokeInvoked: true,
        postRevokeDereferenceRejected: true,
        blobReferenceCleared: true,
        policyListenerRemoved: true,
        styleCountAfterApply: 0,
        rootClassCountAfterApply: 0,
        markerCountAfterApply: 0,
        stateCountAfterApply: 0,
        cssVariablePresentAfterApply: false,
        finalResidualCount: 0,
        timerCancelled: true,
        mainJobCleared: true)
}

private func diagnosticFacts() -> CanarySessionFacts {
    CanarySessionFacts(
        initialResidualDetected: false,
        diagnosticOnly: true,
        cleanupOnly: false,
        eligibleWindowCount: 1,
        overlayWindowCount: 1,
        unknownWindowCount: 0,
        overlayUnmodified: true,
        applyAttempted: false,
        applyVerified: false,
        cleanupVerified: true,
        visualEffectApplied: false,
        blobContentVerified: true,
        blobURLCreated: true,
        blobFetchAllowed: false,
        blobXHRAllowed: false,
        blobFetchBlockedByPolicy: false,
        blobFetchRejectedTypeError: true,
        blobXHRError: true,
        blobXHRStatusZero: true,
        blobRevokeInvoked: true,
        postRevokeDereferenceRejected: true,
        blobReferenceCleared: true,
        policyListenerRemoved: true,
        styleCountAfterApply: 0,
        rootClassCountAfterApply: 0,
        markerCountAfterApply: 0,
        stateCountAfterApply: 0,
        cssVariablePresentAfterApply: false,
        finalResidualCount: 0,
        timerCancelled: true,
        mainJobCleared: true)
}

private enum RunStateFixture {
    static let result = CSSCanaryResult(
        installationIdentityMatched: true,
        processIdentityStable: true,
        oldProcessAbsent: true,
        newProcessObserved: false,
        startedAtFingerprintChanged: false,
        eligibleWindowCount: 1,
        overlayWindowCount: 1,
        unknownWindowCount: 0,
        overlayUnmodified: true,
        applyVerifiedCount: 2,
        cleanupVerifiedCount: 2,
        visualEffectVerifiedCount: 2,
        blobContentVerifiedCount: 2,
        blobURLCreatedCount: 2,
        blobFetchAllowedCount: 0,
        blobXHRAllowedCount: 0,
        blobFetchBlockedByPolicyCount: 2,
        blobRevokeInvokedCount: 2,
        postRevokeDereferenceRejectedCount: 2,
        blobReferenceClearedCount: 2,
        policyListenerRemovedCount: 2,
        finalResidualCount: 0,
        completedSessionCount: 2,
        signalCount: 2,
        finalPortListenerCount: 0,
        checkpointWritten: true,
        requalificationPassed: false)
}
