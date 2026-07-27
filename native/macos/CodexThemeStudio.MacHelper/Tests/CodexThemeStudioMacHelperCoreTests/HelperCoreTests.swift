import Foundation
import Testing
@testable import CodexThemeStudioMacHelperCore

@Test
func protocolRejectsVersionThemeAndPathViolations() throws {
    let valid = makeRequest(command: .applyTemporary, theme: makeTheme())
    try ProtocolPolicy.validate(valid)
    #expect(throws: HelperFailure.self) {
        try ProtocolPolicy.validate(HelperRequest(
            schemaVersion: 2,
            protocolVersion: 1,
            requestId: UUID(),
            command: .discover,
            deadlineMilliseconds: 8_000,
            bundlePath: "/Applications/ChatGPT.app",
            theme: nil))
    }
    #expect(throws: HelperFailure.self) {
        try ProtocolPolicy.validate(HelperRequest(
            schemaVersion: 1,
            protocolVersion: 1,
            requestId: UUID(),
            command: .cleanup,
            deadlineMilliseconds: 20_000,
            bundlePath: "/Applications/ChatGPT.app",
            theme: makeTheme()))
    }
}

@Test
func protocolRejectsUnknownKeysAtEveryLevel() throws {
    let requestId = UUID().uuidString
    for json in [
        """
        {"schemaVersion":1,"protocolVersion":1,"requestId":"\(requestId)",
        "command":"discover","deadlineMilliseconds":8000,
        "bundlePath":"/Applications/ChatGPT.app","theme":null,"argv":[]}
        """,
        """
        {"schemaVersion":1,"protocolVersion":1,"requestId":"\(requestId)",
        "command":"apply-temporary","deadlineMilliseconds":20000,
        "bundlePath":"/Applications/ChatGPT.app",
        "theme":{"schemaVersion":1,
        "themeId":"5BE08C24-D21F-4DB0-BDB0-D6D4CC779D7B",
        "variant":"dark","palette":{"background":"#111111",
        "panel":"#181818","accent":"#2563EB","text":"#F5F5F5",
        "muted":"#A3A3A3","border":"#303030"},"css":"*{}"}}
        """,
        """
        {"schemaVersion":1,"protocolVersion":1,"requestId":"\(requestId)",
        "command":"apply-temporary","deadlineMilliseconds":20000,
        "bundlePath":"/Applications/ChatGPT.app",
        "theme":{"schemaVersion":1,
        "themeId":"5BE08C24-D21F-4DB0-BDB0-D6D4CC779D7B",
        "variant":"dark","palette":{"background":"#111111",
        "panel":"#181818","accent":"#2563EB","text":"#F5F5F5",
        "muted":"#A3A3A3","border":"#303030","url":"https://invalid"}}}
        """,
    ] {
        #expect(throws: HelperFailure.self) {
            try ProtocolPolicy.decodeRequest(Data(json.utf8))
        }
    }
}

@Test
func discoverDoesNotSignalOrRunNode() {
    let signal = FakeSignal()
    let node = FakeNode()
    let engine = HelperEngine(
        discovery: FakeDiscovery(),
        ports: FakePorts(sequence: [[]]),
        signal: signal,
        node: node,
        sleeper: FakeSleeper())
    let document = engine.execute(makeRequest(command: .discover, theme: nil))
    #expect(document.status == "ok")
    #expect(signal.count == 0)
    #expect(node.modes.isEmpty)
    #expect(document.result?.inspectorDisposition == .notOpened)
}

@Test
func qualificationUsesTwoSessionsAndProvesClosedPort() {
    let signal = FakeSignal()
    let node = FakeNode()
    let ports = FakePorts(sequence: [
        [],
        [PortListener(processId: 42, address: "127.0.0.1")],
        [],
        [PortListener(processId: 42, address: "[::1]")],
        [],
    ])
    let engine = HelperEngine(
        discovery: FakeDiscovery(),
        ports: ports,
        signal: signal,
        node: node,
        sleeper: FakeSleeper())
    let document = engine.execute(
        makeRequest(command: .qualifyAndApply, theme: makeTheme()))
    #expect(document.status == "ok")
    #expect(signal.count == 2)
    #expect(node.modes == ["apply-cleanup", "apply"])
    #expect(document.result?.portListenerCount == 0)
    #expect(document.result?.inspectorDisposition == .closed)
}

@Test
func unknownPortOwnerFailsClosedBeforeNode() {
    let node = FakeNode()
    let engine = HelperEngine(
        discovery: FakeDiscovery(),
        ports: FakePorts(sequence: [
            [],
            [PortListener(processId: 999, address: "127.0.0.1")],
        ]),
        signal: FakeSignal(),
        node: node,
        sleeper: FakeSleeper())
    let document = engine.execute(
        makeRequest(command: .applyTemporary, theme: makeTheme()))
    #expect(document.status == "error")
    #expect(document.error?.code == "port.owner_mismatch")
    #expect(node.modes.isEmpty)
}

@Test
func nodeFailureUsesOneCleanupAttemptWithoutAnotherSignal() {
    let signal = FakeSignal()
    let node = FakeNode(failingModes: ["apply"])
    let engine = HelperEngine(
        discovery: FakeDiscovery(),
        ports: FakePorts(sequence: [
            [],
            [PortListener(processId: 42, address: "127.0.0.1")],
            [PortListener(processId: 42, address: "127.0.0.1")],
            [],
        ]),
        signal: signal,
        node: node,
        sleeper: FakeSleeper())
    let document = engine.execute(
        makeRequest(command: .applyTemporary, theme: makeTheme()))
    #expect(document.status == "error")
    #expect(document.error?.code == "renderer.apply_failed")
    #expect(signal.count == 1)
    #expect(node.modes == ["apply", "cleanup"])
}

@Test
func emergencyCleanupPreservesTheExactCloseFailure() {
    let signal = FakeSignal()
    let node = FakeNode(failures: [
        "apply": HelperFailure("renderer.proof_invalid", stage: "renderer"),
        "cleanup": HelperFailure(
            "inspector.close_request_failed",
            stage: "close"),
    ])
    let engine = HelperEngine(
        discovery: FakeDiscovery(),
        ports: FakePorts(sequence: [
            [],
            [PortListener(processId: 42, address: "127.0.0.1")],
            [PortListener(processId: 42, address: "127.0.0.1")],
        ]),
        signal: signal,
        node: node,
        sleeper: FakeSleeper())

    let document = engine.execute(
        makeRequest(command: .applyTemporary, theme: makeTheme()))

    #expect(document.status == "error")
    #expect(document.error?.code == "inspector.close_request_failed")
    #expect(document.error?.stage == "close")
    #expect(signal.count == 1)
    #expect(node.modes == ["apply", "cleanup"])
}

@Test
func closedPortTimeoutIsNotMisreportedAsRendererCleanupFailure() {
    let signal = FakeSignal()
    let listener = PortListener(
        processId: 42,
        address: "127.0.0.1")
    let ports = FakePorts(sequence:
        [[]] +
        [[listener]] +
        Array(repeating: [listener], count: 41) +
        [[listener], []])
    let node = FakeNode()
    let engine = HelperEngine(
        discovery: FakeDiscovery(),
        ports: ports,
        signal: signal,
        node: node,
        sleeper: FakeSleeper())

    let document = engine.execute(
        makeRequest(command: .applyTemporary, theme: makeTheme()))

    #expect(document.status == "error")
    #expect(document.error?.code == "inspector.close_failed")
    #expect(document.error?.stage == "port")
    #expect(signal.count == 1)
    #expect(node.modes == ["apply", "cleanup"])
}

@Test
func emergencyCleanupRefusesUnknownPortOwner() {
    let signal = FakeSignal()
    let node = FakeNode(failingModes: ["apply"])
    let engine = HelperEngine(
        discovery: FakeDiscovery(),
        ports: FakePorts(sequence: [
            [],
            [PortListener(processId: 42, address: "127.0.0.1")],
            [PortListener(processId: 999, address: "127.0.0.1")],
        ]),
        signal: signal,
        node: node,
        sleeper: FakeSleeper())
    let document = engine.execute(
        makeRequest(command: .applyTemporary, theme: makeTheme()))
    #expect(document.status == "error")
    #expect(document.error?.code == "port.owner_mismatch")
    #expect(signal.count == 1)
    #expect(node.modes == ["apply"])
}

@Test
func processIdentityChangeStopsBeforeSignal() {
    let signal = FakeSignal()
    let original = makeSnapshot()
    let changed = TrustedSnapshot(
        installation: original.installation,
        process: ProcessEvidence(
            processId: 42,
            parentProcessId: 1,
            startedAtUtc: Date(timeIntervalSince1970: 2),
            executablePath: original.process.executablePath,
            architecture: "arm64"))
    let engine = HelperEngine(
        discovery: SequenceDiscovery(sequence: [original, changed]),
        ports: FakePorts(sequence: [[]]),
        signal: signal,
        node: FakeNode(),
        sleeper: FakeSleeper())
    let document = engine.execute(
        makeRequest(command: .applyTemporary, theme: makeTheme()))
    #expect(document.status == "error")
    #expect(document.error?.code == "process.identity_changed")
    #expect(signal.count == 0)
}

@Test
func publicJsonDoesNotContainPrivateFields() throws {
    let document = HelperEngine(
        discovery: FakeDiscovery(),
        ports: FakePorts(sequence: [[]]),
        signal: FakeSignal(),
        node: FakeNode(),
        sleeper: FakeSleeper())
        .execute(makeRequest(command: .discover, theme: nil))
    let json = String(decoding: try ProtocolPolicy.encode(document), as: UTF8.self)
    for denied in [
        "argv", "commandLine", "environment", "url", "dom",
        "token", "credential", "conversation", "targetId",
    ] {
        #expect(!json.lowercased().contains("\"\(denied.lowercased())\""))
    }
}

@Test
func runtimeManifestVerifiesEveryExecutableInputAndDetectsReplacement() throws {
    let fixture = try makeRuntimeFixture()
    defer { try? FileManager.default.removeItem(at: fixture.root) }
    let manifest = try RuntimeManifest.generate(contentsURL: fixture.contents)
    let manifestURL = fixture.contents
        .appendingPathComponent("Resources/runtime-manifest.json")
    try RuntimeManifest.canonicalData(manifest).write(to: manifestURL)
    let expectedManifestHash = try StableHasher.sha256(manifestURL)
    let loaded = try RuntimeManifest.load(
        manifestURL,
        expectedSha256: expectedManifestHash)

    try loaded.verify(contentsURL: fixture.contents)
    try Data("replacement".utf8).write(to: fixture.renderer)
    #expect(throws: HelperFailure.self) {
        try loaded.verify(contentsURL: fixture.contents)
    }
    try Data("tampered".utf8).write(to: manifestURL)
    #expect(throws: HelperFailure.self) {
        try RuntimeManifest.load(
            manifestURL,
            expectedSha256: expectedManifestHash)
    }
}

@Test
func unconfiguredRuntimeIdentityFailsClosed() throws {
    let directory = FileManager.default.temporaryDirectory
        .appendingPathComponent(UUID().uuidString)
    try Data("{}".utf8).write(to: directory)
    defer { try? FileManager.default.removeItem(at: directory) }

    #expect(throws: HelperFailure.self) {
        try RuntimeManifest.load(directory, expectedSha256: "")
    }
}

@Test
func runtimeManifestRejectsUnknownFieldsAndHigherSchema() throws {
    let fixture = try makeRuntimeFixture()
    defer { try? FileManager.default.removeItem(at: fixture.root) }
    let manifest = try RuntimeManifest.generate(contentsURL: fixture.contents)
    let valid = try RuntimeManifest.canonicalData(manifest)
    let manifestURL = fixture.contents
        .appendingPathComponent("Resources/runtime-manifest.json")

    var unknown = try JSONSerialization.jsonObject(with: valid) as! [String: Any]
    unknown["privateData"] = true
    try JSONSerialization.data(withJSONObject: unknown, options: [.sortedKeys])
        .write(to: manifestURL)
    #expect(throws: HelperFailure.self) {
        try RuntimeManifest.load(
            manifestURL,
            expectedSha256: try StableHasher.sha256(manifestURL))
    }

    var higher = try JSONSerialization.jsonObject(with: valid) as! [String: Any]
    higher["schemaVersion"] = 2
    try JSONSerialization.data(withJSONObject: higher, options: [.sortedKeys])
        .write(to: manifestURL)
    #expect(throws: HelperFailure.self) {
        try RuntimeManifest.load(
            manifestURL,
            expectedSha256: try StableHasher.sha256(manifestURL))
    }
}

@Test
func runtimeManifestRejectsSymlinkMissingAndExtraRuntimeFiles() throws {
    let fixture = try makeRuntimeFixture()
    defer { try? FileManager.default.removeItem(at: fixture.root) }
    let manifest = try RuntimeManifest.generate(contentsURL: fixture.contents)
    let manifestURL = fixture.contents
        .appendingPathComponent("Resources/runtime-manifest.json")
    try RuntimeManifest.canonicalData(manifest).write(to: manifestURL)
    let loaded = try RuntimeManifest.load(
        manifestURL,
        expectedSha256: try StableHasher.sha256(manifestURL))

    try FileManager.default.removeItem(at: fixture.renderer)
    try FileManager.default.createSymbolicLink(
        at: fixture.renderer,
        withDestinationURL: fixture.cdp)
    #expect(throws: HelperFailure.self) {
        try loaded.verify(contentsURL: fixture.contents)
    }

    try FileManager.default.removeItem(at: fixture.renderer)
    try Data("renderer".utf8).write(to: fixture.renderer)
    let extra = fixture.renderer.deletingLastPathComponent()
        .appendingPathComponent("extra.mjs")
    try Data("extra".utf8).write(to: extra)
    #expect(throws: HelperFailure.self) {
        try RuntimeManifest.generate(contentsURL: fixture.contents)
            .verify(contentsURL: fixture.contents)
    }
}

@Test
func runtimeManifestRejectsEscapeDuplicateCaseConflictAndWrongRole() throws {
    let fixture = try makeRuntimeFixture()
    defer { try? FileManager.default.removeItem(at: fixture.root) }
    let valid = try RuntimeManifest.generate(contentsURL: fixture.contents)
    let first = valid.files[0]
    let invalidEntries: [[RuntimeManifest.FileEntry]] = [
        [
            .init(
                path: "../node",
                role: first.role,
                size: first.size,
                sha256: first.sha256),
            valid.files[1],
            valid.files[2],
        ],
        [first, first, valid.files[2]],
        [
            .init(
                path: first.path.uppercased(),
                role: first.role,
                size: first.size,
                sha256: first.sha256),
            valid.files[1],
            valid.files[2],
        ],
        [
            .init(
                path: first.path,
                role: "renderer-runtime",
                size: first.size,
                sha256: first.sha256),
            valid.files[1],
            valid.files[2],
        ],
    ]
    for entries in invalidEntries {
        #expect(throws: HelperFailure.self) {
            try RuntimeManifest.canonicalData(RuntimeManifest(files: entries))
        }
    }
}

@Test
func runtimeManifestRejectsUndeclaredExecutable() throws {
    let fixture = try makeRuntimeFixture()
    defer { try? FileManager.default.removeItem(at: fixture.root) }
    let macOS = fixture.contents.appendingPathComponent("MacOS")
    try FileManager.default.createDirectory(
        at: macOS,
        withIntermediateDirectories: true)
    let unexpected = macOS.appendingPathComponent("unexpected-tool")
    try Data("tool".utf8).write(to: unexpected)
    try FileManager.default.setAttributes(
        [.posixPermissions: 0o755],
        ofItemAtPath: unexpected.path)
    let manifest = try RuntimeManifest.generate(contentsURL: fixture.contents)
    #expect(throws: HelperFailure.self) {
        try manifest.verify(contentsURL: fixture.contents)
    }
}

private struct RuntimeFixture {
    let root: URL
    let contents: URL
    let cdp: URL
    let renderer: URL
}

private func makeRuntimeFixture() throws -> RuntimeFixture {
    let root = FileManager.default.temporaryDirectory
        .appendingPathComponent("codex-theme-studio-helper-tests")
        .appendingPathComponent(UUID().uuidString)
    let contents = root.appendingPathComponent("Contents")
    let helpers = contents.appendingPathComponent("Helpers")
    let runtime = contents.appendingPathComponent("Resources/runtime/macos")
    try FileManager.default.createDirectory(
        at: helpers,
        withIntermediateDirectories: true)
    try FileManager.default.createDirectory(
        at: runtime,
        withIntermediateDirectories: true)
    let node = helpers.appendingPathComponent("node")
    let helper = helpers.appendingPathComponent("CodexThemeStudio.MacHelper")
    let cdp = runtime.appendingPathComponent("cdp-client.mjs")
    let renderer = runtime.appendingPathComponent("renderer-runtime.mjs")
    try Data("node".utf8).write(to: node)
    try Data("helper".utf8).write(to: helper)
    try Data("script".utf8).write(to: cdp)
    try Data("renderer".utf8).write(to: renderer)
    return RuntimeFixture(
        root: root,
        contents: contents,
        cdp: cdp,
        renderer: renderer)
}

private func makeRequest(
    command: HelperCommand,
    theme: ThemeRequest?) -> HelperRequest
{
    HelperRequest(
        schemaVersion: 1,
        protocolVersion: 1,
        requestId: UUID(),
        command: command,
        deadlineMilliseconds:
            command == .discover ? 8_000 :
            command == .qualifyAndApply ? 35_000 : 20_000,
        bundlePath: "/Applications/ChatGPT.app",
        theme: theme)
}

private func makeTheme() -> ThemeRequest {
    ThemeRequest(
        schemaVersion: 1,
        themeId: UUID(uuidString: "5BE08C24-D21F-4DB0-BDB0-D6D4CC779D7B")!,
        variant: "dark",
        palette: ThemePaletteRequest(
            background: "#111111",
            panel: "#181818",
            accent: "#2563EB",
            text: "#F5F5F5",
            muted: "#A3A3A3",
            border: "#303030"))
}

private func makeSnapshot() -> TrustedSnapshot {
    TrustedSnapshot(
        installation: InstallationEvidence(
            platform: "macOS",
            productIdentifier: "com.openai.codex",
            publisherIdentifier: "2DC432GLL2",
            version: "1",
            buildVersion: "1",
            executablePath: "/Applications/ChatGPT.app/Contents/MacOS/ChatGPT",
            executableSha256: String(repeating: "A", count: 64),
            signatureValid: true,
            hardenedRuntime: true,
            gatekeeperAccepted: true),
        process: ProcessEvidence(
            processId: 42,
            parentProcessId: 1,
            startedAtUtc: Date(timeIntervalSince1970: 1),
            executablePath: "/Applications/ChatGPT.app/Contents/MacOS/ChatGPT",
            architecture: "arm64"))
}

private struct FakeDiscovery: TrustedDiscovering {
    func discover(bundlePath: String) throws -> TrustedSnapshot {
        makeSnapshot()
    }
}

private final class SequenceDiscovery: TrustedDiscovering {
    private var sequence: [TrustedSnapshot]

    init(sequence: [TrustedSnapshot]) {
        self.sequence = sequence
    }

    func discover(bundlePath: String) throws -> TrustedSnapshot {
        if sequence.count > 1 {
            return sequence.removeFirst()
        }
        return sequence[0]
    }
}

private final class FakePorts: PortQuerying {
    private var sequence: [[PortListener]]
    init(sequence: [[PortListener]]) {
        self.sequence = sequence
    }
    func listeners(port: Int) throws -> [PortListener] {
        sequence.isEmpty ? [] : sequence.removeFirst()
    }
}

private final class FakeSignal: SignalSending {
    var count = 0
    func activateInspector(processId: Int32) throws {
        count += 1
    }
}

private final class FakeNode: NodeRunning {
    var modes: [String] = []
    private let failingModes: Set<String>
    private let failures: [String: HelperFailure]

    init(
        failingModes: Set<String> = [],
        failures: [String: HelperFailure] = [:])
    {
        self.failingModes = failingModes
        self.failures = failures
    }

    func run(
        mode: String,
        snapshot: TrustedSnapshot,
        theme: ThemeRequest?) throws -> NodeFacts
    {
        modes.append(mode)
        if let failure = failures[mode] {
            throw failure
        }
        if failingModes.contains(mode) {
            throw HelperFailure(
                "renderer.\(mode)_failed",
                stage: "renderer")
        }
        return NodeFacts(
            eligibleWindowCount: 1,
            appliedWindowCount: mode == "cleanup" ? 0 : 1,
            residualCount: mode == "apply" ? 10 : 0,
            cleanupVerified: mode != "apply",
            visualEffectApplied: mode != "cleanup")
    }
}

private struct FakeSleeper: LifecycleSleeping {
    func sleep(milliseconds: Int) {}
}
