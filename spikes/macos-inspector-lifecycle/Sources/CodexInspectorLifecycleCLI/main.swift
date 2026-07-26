import CodexInspectorLifecycleCore
import Darwin
import Foundation

private struct Options {
    let command: String
    let bundlePath: String?
    let port: Int
    let nodePath: String?
    let pretty: Bool

    static func parse(_ arguments: [String]) throws -> Self {
        guard let command = arguments.first,
              ["lifecycle", "schema", "self-test"].contains(command)
        else {
            throw usageFailure()
        }
        var bundlePath: String?
        var nodePath: String?
        var port = LifecycleConstants.inspectorPort
        var pretty = false
        var index = 1
        while index < arguments.count {
            switch arguments[index] {
            case "--pretty":
                pretty = true
                index += 1
            case "--bundle" where command == "lifecycle":
                guard index + 1 < arguments.count, bundlePath == nil else {
                    throw usageFailure()
                }
                bundlePath = arguments[index + 1]
                index += 2
            case "--node" where command == "lifecycle":
                guard index + 1 < arguments.count, nodePath == nil else {
                    throw usageFailure()
                }
                nodePath = arguments[index + 1]
                index += 2
            case "--port" where command == "lifecycle":
                guard index + 1 < arguments.count,
                      let parsed = Int(arguments[index + 1])
                else {
                    throw usageFailure()
                }
                port = parsed
                index += 2
            default:
                throw usageFailure()
            }
        }
        if command == "lifecycle" {
            guard let bundlePath,
                  bundlePath.hasPrefix("/"),
                  let nodePath,
                  nodePath.hasPrefix("/")
            else {
                throw usageFailure()
            }
        }
        return Self(
            command: command,
            bundlePath: bundlePath,
            port: port,
            nodePath: nodePath,
            pretty: pretty)
    }

    private static func usageFailure() -> LifecycleFailure {
        LifecycleFailure(
            code: "usage.invalid",
            stage: "usage",
            message: "Usage: codex-macos-inspector-lifecycle lifecycle --bundle <absolute-app-path> --node <absolute-node-path> [--port 9229] [--pretty] | schema [--pretty] | self-test [--pretty]",
            exitCode: 2)
    }
}

@inline(__always)
private func emit(_ data: Data, exitCode: Int32) -> Never {
    FileHandle.standardOutput.write(data)
    FileHandle.standardOutput.write(Data([0x0a]))
    Darwin.exit(exitCode)
}

private func emitFailure(_ failure: LifecycleFailure) -> Never {
    if let data = try? LifecycleJSON.encode(
        LifecycleDocument.failure(failure),
        pretty: false)
    {
        emit(data, exitCode: failure.exitCode)
    }
    let fallback = Data(
        #"{"error":{"cleanup":null,"code":"output.serialization_failed","message":"JSON output serialization failed.","stage":"output"},"observedAtUtc":"1970-01-01T00:00:00.000Z","result":null,"schemaVersion":1,"status":"error"}"#.utf8)
    emit(fallback, exitCode: 80)
}

do {
    guard #available(macOS 13.0, *) else {
        throw LifecycleFailure(
            code: "platform.unsupported",
            stage: "platform",
            message: "macOS 13.0 or later is required.",
            exitCode: 3)
    }
    let options = try Options.parse(Array(CommandLine.arguments.dropFirst()))
    switch options.command {
    case "schema":
        emit(
            try LifecycleSchema.data(pretty: options.pretty),
            exitCode: 0)
    case "self-test":
        emit(
            try LifecycleJSON.encode(
                LifecycleSelfTest(),
                pretty: options.pretty),
            exitCode: 0)
    case "lifecycle":
        let result = try InspectorLifecycleCoordinator.live().run(
            bundlePath: options.bundlePath!,
            port: options.port,
            nodePath: options.nodePath!)
        emit(
            try LifecycleJSON.encode(
                LifecycleDocument.success(result),
                pretty: options.pretty),
            exitCode: 0)
    default:
        throw LifecycleFailure(
            code: "usage.invalid",
            stage: "usage",
            message: "Command is not supported.",
            exitCode: 2)
    }
} catch let failure as LifecycleFailure {
    emitFailure(failure)
} catch {
    emitFailure(LifecycleFailure(
        code: "internal.unexpected",
        stage: "internal",
        message: "An unexpected internal error occurred.",
        exitCode: 80))
}
