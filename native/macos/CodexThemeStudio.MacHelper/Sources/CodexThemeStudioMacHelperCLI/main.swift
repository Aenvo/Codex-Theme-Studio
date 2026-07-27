import CodexThemeStudioMacHelperCore
import Foundation

func emit(_ object: Any, exitCode: Int32) -> Never {
    let data = try! JSONSerialization.data(
        withJSONObject: object,
        options: [.sortedKeys])
    FileHandle.standardOutput.write(data)
    FileHandle.standardOutput.write(Data([0x0A]))
    exit(exitCode)
}

let arguments = Array(CommandLine.arguments.dropFirst())
if arguments == ["schema"] {
    emit(ProtocolPolicy.schemaDocument(), exitCode: 0)
}
if arguments == ["self-test"] {
    emit([
        "schemaVersion": HelperConstants.schemaVersion,
        "protocolVersion": HelperConstants.protocolVersion,
        "toolVersion": HelperConstants.toolVersion,
        "status": "ok",
        "packagedRuntimeIdentityConfigured":
            GeneratedRuntimeIdentity.isConfigured,
        "checks": [
            "single-json-stdin",
            "single-json-stdout",
            "palette-only-theme",
            "trusted-bundle",
            "owned-loopback-port",
            "finally-inspector-close",
            "runtime-hash-verification",
        ],
    ], exitCode: 0)
}
guard arguments.isEmpty || arguments == ["serve"] else {
    emit([
        "status": "error",
        "error": ["code": "usage.invalid", "stage": "usage"],
    ], exitCode: 2)
}

do {
    let data = FileHandle.standardInput.readDataToEndOfFile()
    let request = try ProtocolPolicy.decodeRequest(data)
    let engine = HelperEngine(
        discovery: SystemDiscovery(),
        ports: LsofPortQuery(),
        signal: DarwinSignalSender(),
        node: SystemNodeRunner(),
        sleeper: ThreadSleeper())
    let document = engine.execute(request)
    let output = try ProtocolPolicy.encode(document)
    FileHandle.standardOutput.write(output)
    FileHandle.standardOutput.write(Data([0x0A]))
    exit(document.status == "ok" ? 0 : 1)
} catch let failure as HelperFailure {
    emit([
        "schemaVersion": HelperConstants.schemaVersion,
        "protocolVersion": HelperConstants.protocolVersion,
        "toolVersion": HelperConstants.toolVersion,
        "status": "error",
        "error": ["code": failure.code, "stage": failure.stage],
    ], exitCode: 1)
} catch {
    emit([
        "schemaVersion": HelperConstants.schemaVersion,
        "protocolVersion": HelperConstants.protocolVersion,
        "toolVersion": HelperConstants.toolVersion,
        "status": "error",
        "error": ["code": "helper.unexpected", "stage": "helper"],
    ], exitCode: 1)
}
