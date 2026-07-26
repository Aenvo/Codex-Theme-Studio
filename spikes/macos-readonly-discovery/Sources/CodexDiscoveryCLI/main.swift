import CodexDiscoveryCore
import Darwin
import Foundation

private struct CLIOptions {
    let command: String
    let bundlePath: String?
    let port: Int
    let pretty: Bool

    static func parse(_ arguments: [String]) throws -> Self {
        guard let command = arguments.first,
              ["discover", "schema", "self-test"].contains(command)
        else {
            throw DiscoveryFailure(
                code: "usage.invalid",
                stage: "usage",
                message: "Usage: codex-macos-discovery discover [--bundle <absolute-app-path>] [--port 9229] [--pretty] | schema [--pretty] | self-test [--pretty]",
                exitCode: 2)
        }

        var bundlePath: String?
        var port = DiscoveryConstants.inspectorPort
        var pretty = false
        var index = 1
        while index < arguments.count {
            switch arguments[index] {
            case "--pretty":
                pretty = true
                index += 1
            case "--bundle" where command == "discover":
                guard index + 1 < arguments.count, bundlePath == nil else {
                    throw invalidUsage()
                }
                bundlePath = arguments[index + 1]
                index += 2
            case "--port" where command == "discover":
                guard index + 1 < arguments.count,
                      let parsed = Int(arguments[index + 1])
                else {
                    throw invalidUsage()
                }
                port = parsed
                index += 2
            default:
                throw invalidUsage()
            }
        }
        return Self(
            command: command,
            bundlePath: bundlePath,
            port: port,
            pretty: pretty)
    }

    private static func invalidUsage() -> DiscoveryFailure {
        DiscoveryFailure(
            code: "usage.invalid",
            stage: "usage",
            message: "Command arguments are invalid.",
            exitCode: 2)
    }
}

@inline(__always)
private func emit(_ data: Data, exitCode: Int32) -> Never {
    FileHandle.standardOutput.write(data)
    FileHandle.standardOutput.write(Data([0x0a]))
    Darwin.exit(exitCode)
}

private func emitFailure(_ failure: DiscoveryFailure, pretty: Bool) -> Never {
    if let data = try? JSONOutput.encode(
        DiscoveryDocument.failure(failure),
        pretty: pretty)
    {
        emit(data, exitCode: failure.exitCode)
    }
    let fallback = Data(
        #"{"error":{"code":"output.serialization_failed","message":"JSON output serialization failed.","stage":"output"},"observedAtUtc":"1970-01-01T00:00:00.000Z","result":null,"schemaVersion":1,"status":"error"}"#.utf8)
    emit(fallback, exitCode: 80)
}

do {
    guard #available(macOS 13.0, *) else {
        throw DiscoveryFailure(
            code: "platform.unsupported",
            stage: "platform",
            message: "macOS 13.0 or later is required.",
            exitCode: 3)
    }
    let options = try CLIOptions.parse(Array(CommandLine.arguments.dropFirst()))
    switch options.command {
    case "schema":
        emit(
            try DiscoverySchema.data(pretty: options.pretty),
            exitCode: 0)
    case "self-test":
        emit(
            try JSONOutput.encode(SelfTestDocument(), pretty: options.pretty),
            exitCode: 0)
    case "discover":
        let result = try DiscoveryCoordinator().discover(
            explicitBundlePath: options.bundlePath,
            port: options.port)
        emit(
            try JSONOutput.encode(
                DiscoveryDocument.success(result),
                pretty: options.pretty),
            exitCode: 0)
    default:
        throw DiscoveryFailure(
            code: "usage.invalid",
            stage: "usage",
            message: "Command is not supported.",
            exitCode: 2)
    }
} catch let failure as DiscoveryFailure {
    emitFailure(failure, pretty: false)
} catch {
    emitFailure(
        DiscoveryFailure(
            code: "internal.unexpected",
            stage: "internal",
            message: "An unexpected internal error occurred.",
            exitCode: 80),
        pretty: false)
}
