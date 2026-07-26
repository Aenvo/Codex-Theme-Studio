@testable import CodexDiscoveryCore
import Foundation
import XCTest

final class DiscoveryCoreTests: XCTestCase {
    private var testRoot: URL!

    override func setUpWithError() throws {
        testRoot = FileManager.default.temporaryDirectory.appendingPathComponent(
            "CodexDiscoveryCoreTests-\(UUID().uuidString)",
            isDirectory: true)
        try FileManager.default.createDirectory(
            at: testRoot,
            withIntermediateDirectories: false)
    }

    override func tearDownWithError() throws {
        if let testRoot, FileManager.default.fileExists(atPath: testRoot.path) {
            try FileManager.default.removeItem(at: testRoot)
        }
    }

    func testBundleSelectionRejectsZeroAndMultipleCandidates() throws {
        XCTAssertThrowsError(try BundleSelectionPolicy.select([])) {
            XCTAssertEqual(($0 as? DiscoveryFailure)?.code, "bundle.not_found")
        }

        let first = testRoot.appendingPathComponent("One.app")
        let second = testRoot.appendingPathComponent("Two.app")
        try FileManager.default.createDirectory(at: first, withIntermediateDirectories: false)
        try FileManager.default.createDirectory(at: second, withIntermediateDirectories: false)
        XCTAssertEqual(
            try BundleSelectionPolicy.select([first.path]),
            try PathSecurity.realPath(first.path))
        XCTAssertThrowsError(
            try BundleSelectionPolicy.select([first.path, second.path]))
        {
            XCTAssertEqual(($0 as? DiscoveryFailure)?.code, "bundle.ambiguous")
        }
    }

    func testBundleInspectorReadsWhitelistedMetadata() throws {
        let bundle = try makeBundle()
        let result = try BundleInspector.inspect(bundlePath: bundle.path)
        XCTAssertEqual(result.identifier, DiscoveryConstants.bundleIdentifier)
        XCTAssertEqual(result.shortVersion, "1.2.3")
        XCTAssertEqual(result.bundleVersion, "42")
        XCTAssertEqual(
            result.executablePath,
            try PathSecurity.realPath(bundle
                .appendingPathComponent("Contents/MacOS/ChatGPT").path))
    }

    func testBundleInspectorRejectsWrongIdentifierAndMissingMetadata() throws {
        let wrong = try makeBundle(identifier: "example.untrusted")
        XCTAssertThrowsError(try BundleInspector.inspect(bundlePath: wrong.path)) {
            XCTAssertEqual(
                ($0 as? DiscoveryFailure)?.code,
                "bundle.identifier_mismatch")
        }

        let missing = try makeBundle(includeVersion: false)
        XCTAssertThrowsError(try BundleInspector.inspect(bundlePath: missing.path)) {
            XCTAssertEqual(
                ($0 as? DiscoveryFailure)?.code,
                "bundle.metadata_missing")
        }
    }

    func testBundleInspectorRejectsSymlinkAndPathEscape() throws {
        let symlinkBundle = try makeBundle(mainIsSymlink: true)
        XCTAssertThrowsError(
            try BundleInspector.inspect(bundlePath: symlinkBundle.path))
        {
            XCTAssertEqual(
                ($0 as? DiscoveryFailure)?.code,
                "executable.path_invalid")
        }

        let escaped = try makeBundle(executableName: "../outside")
        XCTAssertThrowsError(try BundleInspector.inspect(bundlePath: escaped.path)) {
            XCTAssertEqual(
                ($0 as? DiscoveryFailure)?.code,
                "executable.path_invalid")
        }
    }

    func testStableHasherDetectsFileChange() throws {
        let file = testRoot.appendingPathComponent("hash-target")
        try Data("before".utf8).write(to: file)
        XCTAssertEqual(try StableFileHasher.sha256(path: file.path).count, 64)

        XCTAssertThrowsError(try StableFileHasher.sha256(
            path: file.path,
            afterRead: {
                let handle = try FileHandle(forWritingTo: file)
                try handle.seekToEnd()
                try handle.write(contentsOf: Data("changed".utf8))
                try handle.close()
            }))
        {
            XCTAssertEqual(
                ($0 as? DiscoveryFailure)?.code,
                "executable.identity_changed")
        }
    }

    func testSignaturePolicyRequiresIdentityTeamSubjectAndHardenedRuntime() throws {
        let valid = NativeSignatureFacts(
            valid: true,
            signingIdentifier: DiscoveryConstants.bundleIdentifier,
            teamIdentifier: DiscoveryConstants.teamIdentifier,
            subjectSummary: "Developer ID Application: OpenAI",
            hardenedRuntime: true)
        XCTAssertNoThrow(try SignaturePolicy.validate(valid))

        let cases: [(NativeSignatureFacts, String)] = [
            (.init(
                valid: false,
                signingIdentifier: DiscoveryConstants.bundleIdentifier,
                teamIdentifier: DiscoveryConstants.teamIdentifier,
                subjectSummary: "OpenAI",
                hardenedRuntime: true), "signature.invalid"),
            (.init(
                valid: true,
                signingIdentifier: DiscoveryConstants.bundleIdentifier,
                teamIdentifier: nil,
                subjectSummary: "OpenAI",
                hardenedRuntime: true), "signature.team_mismatch"),
            (.init(
                valid: true,
                signingIdentifier: DiscoveryConstants.bundleIdentifier,
                teamIdentifier: DiscoveryConstants.teamIdentifier,
                subjectSummary: nil,
                hardenedRuntime: true), "signature.subject_missing"),
            (.init(
                valid: true,
                signingIdentifier: DiscoveryConstants.bundleIdentifier,
                teamIdentifier: DiscoveryConstants.teamIdentifier,
                subjectSummary: "OpenAI",
                hardenedRuntime: false), "signature.hardened_runtime_missing"),
        ]
        for (facts, code) in cases {
            XCTAssertThrowsError(try SignaturePolicy.validate(facts)) {
                XCTAssertEqual(($0 as? DiscoveryFailure)?.code, code)
            }
        }
    }

    func testGatekeeperParserAcceptsOnlyNotarizedVerdict() throws {
        let accepted = gatekeeperPlist(
            verdict: true,
            source: "Notarized Developer ID")
        XCTAssertEqual(
            try GatekeeperParser.parse(accepted, exitCode: 0),
            GatekeeperFacts(accepted: true, notarized: true))

        XCTAssertThrowsError(
            try GatekeeperParser.parse(accepted, exitCode: 3))
        {
            XCTAssertEqual(($0 as? DiscoveryFailure)?.code, "gatekeeper.denied")
        }

        let unknown = Data(
            #"<?xml version="1.0"?><plist version="1.0"><dict><key>new-key</key><true/></dict></plist>"#
                .utf8)
        XCTAssertThrowsError(
            try GatekeeperParser.parse(unknown, exitCode: 0))
        {
            XCTAssertEqual(
                ($0 as? DiscoveryFailure)?.code,
                "gatekeeper.assessment_failed")
        }
    }

    func testProcessPolicyHandlesZeroOneAndMultipleMainProcesses() throws {
        let bundle = "/Applications/ChatGPT.app"
        let main = bundle + "/Contents/MacOS/ChatGPT"
        XCTAssertEqual(
            try ProcessPolicy.evaluate(
                identities: [],
                bundlePath: bundle,
                mainExecutablePath: main).state,
            "notRunning")

        let one = rawProcess(1, path: main)
        let result = try ProcessPolicy.evaluate(
            identities: [one],
            bundlePath: bundle,
            mainExecutablePath: main)
        XCTAssertEqual(result.main?.processId, 1)

        XCTAssertThrowsError(try ProcessPolicy.evaluate(
            identities: [one, rawProcess(2, path: main)],
            bundlePath: bundle,
            mainExecutablePath: main))
        {
            XCTAssertEqual(
                ($0 as? DiscoveryFailure)?.code,
                "process.main_ambiguous")
        }
    }

    func testProcessIdentityChangeFailsClosed() throws {
        let before = rawProcess(1, path: "/Applications/ChatGPT.app/Contents/MacOS/ChatGPT")
        XCTAssertNoThrow(try ProcessPolicy.requireStable(before: before, after: before))
        let after = RawProcessIdentity(
            processId: before.processId,
            parentProcessId: before.parentProcessId,
            startSeconds: before.startSeconds + 1,
            startMicroseconds: before.startMicroseconds,
            executablePath: before.executablePath,
            architecture: before.architecture)
        XCTAssertThrowsError(
            try ProcessPolicy.requireStable(before: before, after: after))
        {
            XCTAssertEqual(
                ($0 as? DiscoveryFailure)?.code,
                "process.identity_changed")
        }
    }

    func testProcessPathClassification() {
        let bundle = "/Applications/ChatGPT.app"
        let main = bundle + "/Contents/MacOS/ChatGPT"
        XCTAssertEqual(
            ProcessPolicy.classify(
                executablePath: main,
                bundlePath: bundle,
                mainExecutablePath: main),
            .main)
        XCTAssertEqual(
            ProcessPolicy.classify(
                executablePath: bundle + "/Contents/Frameworks/Codex Framework.framework/Helpers/Codex (Renderer).app/Contents/MacOS/Codex (Renderer)",
                bundlePath: bundle,
                mainExecutablePath: main),
            .renderer)
        XCTAssertEqual(
            ProcessPolicy.classify(
                executablePath: bundle + "/Contents/Frameworks/Codex Framework.framework/Helpers/Codex (Service).app/Contents/MacOS/Codex (Service)",
                bundlePath: bundle,
                mainExecutablePath: main),
            .service)
        XCTAssertEqual(
            ProcessPolicy.classify(
                executablePath: bundle + "/Contents/Resources/codex",
                bundlePath: bundle,
                mainExecutablePath: main),
            .resourcesCodex)
    }

    func testLsofAndPortPolicyCoverFreeIPv4IPv6UnknownAndNonLoopback() throws {
        XCTAssertEqual(
            try PortPolicy.evaluate(listeners: [], trustedProcessIds: []),
            PortObservation(state: "free", listeners: []))

        let ipv4 = try LsofParser.parse(
            "p123\ncChatGPT\nf10\ntIPv4\nn127.0.0.1:9229\nTST=LISTEN\n")
        let ipv6 = try LsofParser.parse(
            "p123\ncChatGPT\nf11\ntIPv6\nn[::1]:9229\nTST=LISTEN\n")
        let accepted = try PortPolicy.evaluate(
            listeners: ipv4 + ipv6,
            trustedProcessIds: [123])
        XCTAssertEqual(accepted.state, "knownCodexListener")
        XCTAssertEqual(accepted.listeners.count, 2)

        XCTAssertThrowsError(try PortPolicy.evaluate(
            listeners: ipv4,
            trustedProcessIds: []))
        {
            XCTAssertEqual(
                ($0 as? DiscoveryFailure)?.code,
                "port.listener_owner_unknown")
        }

        let wildcard = try LsofParser.parse(
            "p123\ncChatGPT\nf12\ntIPv4\nn*:9229\nTST=LISTEN\n")
        XCTAssertThrowsError(try PortPolicy.evaluate(
            listeners: wildcard,
            trustedProcessIds: [123]))
        {
            XCTAssertEqual(
                ($0 as? DiscoveryFailure)?.code,
                "port.listener_non_loopback")
        }
    }

    func testPortPolicyRejectsMultipleOwners() throws {
        let listeners = [
            ParsedPortListener(processId: 1, family: "IPv4", address: "127.0.0.1"),
            ParsedPortListener(processId: 2, family: "IPv6", address: "::1"),
        ]
        XCTAssertThrowsError(try PortPolicy.evaluate(
            listeners: listeners,
            trustedProcessIds: [1, 2]))
        {
            XCTAssertEqual(
                ($0 as? DiscoveryFailure)?.code,
                "port.listener_ambiguous")
        }
    }

    func testJSONPrivacyKeyPolicyRejectsForbiddenFields() throws {
        for key in JSONOutput.forbiddenKeys {
            XCTAssertThrowsError(
                try JSONOutput.validatePrivacyKeys([key: "redacted"]))
        }
        let data = try JSONOutput.encode(SelfTestDocument())
        let text = try XCTUnwrap(String(data: data, encoding: .utf8))
        for key in JSONOutput.forbiddenKeys {
            XCTAssertFalse(text.lowercased().contains("\"\(key)\""))
        }
    }

    func testExternalCommandPolicyRequiresAbsoluteAllowlistAndLimits() throws {
        let spec = CommandSpec(
            executable: DiscoveryConstants.codesignPath,
            arguments: ["--verify", "/path with spaces/App.app"],
            timeoutSeconds: 10,
            maximumOutputBytes: 4096)
        XCTAssertNoThrow(try ExternalCommandPolicy.validate(spec))
        XCTAssertEqual(spec.arguments.count, 2)

        XCTAssertThrowsError(try ExternalCommandPolicy.validate(CommandSpec(
            executable: "codesign",
            arguments: [])))
        XCTAssertThrowsError(try ExternalCommandPolicy.validate(CommandSpec(
            executable: DiscoveryConstants.codesignPath,
            arguments: [],
            timeoutSeconds: 31,
            maximumOutputBytes: 4096)))
        XCTAssertThrowsError(try ExternalCommandPolicy.validate(CommandSpec(
            executable: DiscoveryConstants.codesignPath,
            arguments: [],
            timeoutSeconds: 1,
            maximumOutputBytes: 300 * 1024)))
    }

    func testSchemaIsSingleValidJSONDocumentWithoutForbiddenKeys() throws {
        let data = try DiscoverySchema.data(pretty: false)
        let object = try JSONSerialization.jsonObject(with: data)
        XCTAssertTrue(object is [String: Any])
        try JSONOutput.validatePrivacyKeys(object)
    }

    private func makeBundle(
        identifier: String = DiscoveryConstants.bundleIdentifier,
        includeVersion: Bool = true,
        executableName: String = "ChatGPT",
        mainIsSymlink: Bool = false
    ) throws -> URL {
        let bundle = testRoot.appendingPathComponent(
            UUID().uuidString + ".app",
            isDirectory: true)
        let contents = bundle.appendingPathComponent("Contents", isDirectory: true)
        let macOS = contents.appendingPathComponent("MacOS", isDirectory: true)
        try FileManager.default.createDirectory(
            at: macOS,
            withIntermediateDirectories: true)

        var values: [String: Any] = [
            "CFBundleIdentifier": identifier,
            "CFBundleVersion": "42",
            "CFBundleExecutable": executableName,
        ]
        if includeVersion {
            values["CFBundleShortVersionString"] = "1.2.3"
        }
        let plist = try PropertyListSerialization.data(
            fromPropertyList: values,
            format: .binary,
            options: 0)
        try plist.write(to: contents.appendingPathComponent("Info.plist"))

        if executableName == "ChatGPT" {
            let executable = macOS.appendingPathComponent(executableName)
            if mainIsSymlink {
                let target = testRoot.appendingPathComponent("outside-executable")
                try Data("outside".utf8).write(to: target)
                try FileManager.default.createSymbolicLink(
                    at: executable,
                    withDestinationURL: target)
            } else {
                try Data("mach-o-fixture".utf8).write(to: executable)
            }
        }
        return bundle
    }

    private func gatekeeperPlist(verdict: Bool, source: String) -> Data {
        let value: [String: Any] = [
            "assessment:verdict": verdict,
            "assessment:authority": [
                "assessment:authority:source": source,
            ],
        ]
        return try! PropertyListSerialization.data(
            fromPropertyList: value,
            format: .xml,
            options: 0)
    }

    private func rawProcess(_ processId: Int32, path: String) -> RawProcessIdentity {
        RawProcessIdentity(
            processId: processId,
            parentProcessId: 0,
            startSeconds: 1_700_000_000,
            startMicroseconds: 123_456,
            executablePath: path,
            architecture: .arm64)
    }
}
