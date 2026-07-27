import CodexCSSCanaryCore
import Foundation

@main
struct CodexCSSCanaryCommand {
    static func main() {
        do {
            let arguments = Array(CommandLine.arguments.dropFirst())
            guard let command = arguments.first else {
                throw usageFailure()
            }
            switch command {
            case "schema":
                emit(try CSSCanarySchema.data(pretty: false), exitCode: 0)
            case "self-test":
                emit(
                    try CSSCanaryJSON.encode(CSSCanarySelfTest()),
                    exitCode: 0)
            case "six-a", "six-b", "diagnose-blob":
                let options = try parseOptions(
                    Array(arguments.dropFirst()))
                let coordinator = try CSSCanaryCoordinator.live()
                let result: CSSCanaryResult
                if command == "six-a" {
                    result = try coordinator.run6A(
                        bundlePath: options.bundlePath,
                        nodePath: options.nodePath,
                        lifecycleHelperPath: options.lifecycleHelperPath)
                } else if command == "six-b" {
                    result = try coordinator.run6B(
                        bundlePath: options.bundlePath,
                        nodePath: options.nodePath,
                        lifecycleHelperPath: options.lifecycleHelperPath)
                } else {
                    result = try coordinator.runBlobDiagnostic(
                        bundlePath: options.bundlePath,
                        nodePath: options.nodePath,
                        lifecycleHelperPath: options.lifecycleHelperPath)
                }
                emit(
                    try CSSCanaryJSON.encode(CSSCanaryDocument.success(result)),
                    exitCode: 0)
            default:
                throw usageFailure()
            }
        } catch let failure as CSSCanaryFailure {
            let data = (try? CSSCanaryJSON.encode(
                CSSCanaryDocument.failure(failure)))
                ?? Data(#"{"schemaVersion":4,"status":"error"}"#.utf8)
            emit(data, exitCode: failure.exitCode)
        } catch {
            let failure = CSSCanaryFailure(
                code: "cli.unexpected",
                stage: "cli",
                message: "The CSS canary command failed.",
                exitCode: 1)
            let data = (try? CSSCanaryJSON.encode(
                CSSCanaryDocument.failure(failure)))
                ?? Data(#"{"schemaVersion":4,"status":"error"}"#.utf8)
            emit(data, exitCode: 1)
        }
    }

    private struct Options {
        let bundlePath: String
        let nodePath: String
        let lifecycleHelperPath: String
    }

    private static func parseOptions(
        _ arguments: [String]
    ) throws -> Options {
        var values: [String: String] = [:]
        var index = 0
        let allowed = Set([
            "--bundle",
            "--node",
            "--lifecycle-helper",
            "--port",
        ])
        while index < arguments.count {
            let name = arguments[index]
            guard allowed.contains(name),
                  values[name] == nil,
                  index + 1 < arguments.count
            else {
                throw usageFailure()
            }
            values[name] = arguments[index + 1]
            index += 2
        }
        guard values["--port"] == String(CSSCanaryConstants.inspectorPort),
              let bundlePath = values["--bundle"],
              let nodePath = values["--node"],
              let lifecycleHelperPath = values["--lifecycle-helper"],
              [bundlePath, nodePath, lifecycleHelperPath]
                .allSatisfy({ $0.hasPrefix("/") })
        else {
            throw usageFailure()
        }
        return Options(
            bundlePath: bundlePath,
            nodePath: nodePath,
            lifecycleHelperPath: lifecycleHelperPath)
    }

    private static func usageFailure() -> CSSCanaryFailure {
        CSSCanaryFailure(
            code: "usage.invalid",
            stage: "usage",
            message: "Use six-a, six-b, diagnose-blob, schema, or self-test with exact tool paths and port 9229; checkpoint storage is fixed.",
            exitCode: 2)
    }

    private static func emit(_ data: Data, exitCode: Int32) -> Never {
        FileHandle.standardOutput.write(data)
        FileHandle.standardOutput.write(Data([0x0a]))
        Foundation.exit(exitCode)
    }
}
