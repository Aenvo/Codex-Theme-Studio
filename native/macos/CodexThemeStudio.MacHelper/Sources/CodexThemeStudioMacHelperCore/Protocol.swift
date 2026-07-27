import Foundation

public enum HelperConstants {
    public static let schemaVersion = 1
    public static let protocolVersion = 1
    public static let toolVersion = "0.1.0"
    public static let maximumRequestBytes = 64 * 1024
    public static let maximumResponseBytes = 256 * 1024
    public static let inspectorPort = 9229
    public static let bundleIdentifier = "com.openai.codex"
    public static let teamIdentifier = "2DC432GLL2"
}

public enum HelperCommand: String, Codable, CaseIterable {
    case discover
    case inspectRuntime = "inspect-runtime"
    case applyTemporary = "apply-temporary"
    case qualifyAndApply = "qualify-and-apply"
    case cleanup
}

public enum CleanupDisposition: String, Codable {
    case notNeeded
    case verified
    case unverified
}

public enum InspectorDisposition: String, Codable {
    case notOpened
    case closed
    case residual
    case unknown
}

public struct ThemePaletteRequest: Codable, Equatable {
    public let background: String
    public let panel: String
    public let accent: String
    public let text: String
    public let muted: String
    public let border: String

    public init(
        background: String,
        panel: String,
        accent: String,
        text: String,
        muted: String,
        border: String)
    {
        self.background = background
        self.panel = panel
        self.accent = accent
        self.text = text
        self.muted = muted
        self.border = border
    }
}

public struct ThemeRequest: Codable, Equatable {
    public let schemaVersion: Int
    public let themeId: UUID
    public let variant: String
    public let palette: ThemePaletteRequest

    public init(
        schemaVersion: Int,
        themeId: UUID,
        variant: String,
        palette: ThemePaletteRequest)
    {
        self.schemaVersion = schemaVersion
        self.themeId = themeId
        self.variant = variant
        self.palette = palette
    }
}

public struct HelperRequest: Codable, Equatable {
    public let schemaVersion: Int
    public let protocolVersion: Int
    public let requestId: UUID
    public let command: HelperCommand
    public let deadlineMilliseconds: Int
    public let bundlePath: String
    public let theme: ThemeRequest?

    public init(
        schemaVersion: Int,
        protocolVersion: Int,
        requestId: UUID,
        command: HelperCommand,
        deadlineMilliseconds: Int,
        bundlePath: String,
        theme: ThemeRequest?)
    {
        self.schemaVersion = schemaVersion
        self.protocolVersion = protocolVersion
        self.requestId = requestId
        self.command = command
        self.deadlineMilliseconds = deadlineMilliseconds
        self.bundlePath = bundlePath
        self.theme = theme
    }
}

public struct InstallationEvidence: Codable, Equatable {
    public let platform: String
    public let productIdentifier: String
    public let publisherIdentifier: String
    public let version: String
    public let buildVersion: String
    public let executablePath: String
    public let executableSha256: String
    public let signatureValid: Bool
    public let hardenedRuntime: Bool
    public let gatekeeperAccepted: Bool
}

public struct ProcessEvidence: Codable, Equatable {
    public let processId: Int32
    public let parentProcessId: Int32
    public let startedAtUtc: Date
    public let executablePath: String
    public let architecture: String
}

public struct CapabilityEvidence: Codable, Equatable {
    public let pureCssPalette: Bool
    public let backgroundImage: Bool
    public let temporaryApply: Bool
    public let cleanup: Bool
    public let requalification: Bool

    public init(
        pureCssPalette: Bool = true,
        backgroundImage: Bool = false,
        temporaryApply: Bool = true,
        cleanup: Bool = true,
        requalification: Bool = true)
    {
        self.pureCssPalette = pureCssPalette
        self.backgroundImage = backgroundImage
        self.temporaryApply = temporaryApply
        self.cleanup = cleanup
        self.requalification = requalification
    }
}

public struct HelperResult: Codable, Equatable {
    public let installation: InstallationEvidence?
    public let process: ProcessEvidence?
    public let capabilities: CapabilityEvidence?
    public let cleanupDisposition: CleanupDisposition
    public let inspectorDisposition: InspectorDisposition
    public let eligibleWindowCount: Int
    public let appliedWindowCount: Int
    public let residualCount: Int
    public let portListenerCount: Int
}

public struct HelperErrorBody: Codable, Equatable {
    public let code: String
    public let stage: String
}

public struct HelperDocument: Codable, Equatable {
    public let schemaVersion: Int
    public let protocolVersion: Int
    public let toolVersion: String
    public let requestId: UUID
    public let status: String
    public let result: HelperResult?
    public let error: HelperErrorBody?
}

public struct HelperFailure: Error, Equatable {
    public let code: String
    public let stage: String

    public init(_ code: String, stage: String) {
        self.code = code
        self.stage = stage
    }
}

public enum ProtocolPolicy {
    private static let colorPattern =
        try! NSRegularExpression(pattern: "^#[0-9A-F]{6}(?:[0-9A-F]{2})?$")

    public static func decodeRequest(_ data: Data) throws -> HelperRequest {
        guard !data.isEmpty, data.count <= HelperConstants.maximumRequestBytes else {
            throw HelperFailure("protocol.request_too_large", stage: "request")
        }
        try rejectUnknownKeys(data)
        let decoder = JSONDecoder()
        decoder.keyDecodingStrategy = .useDefaultKeys
        let request: HelperRequest
        do {
            request = try decoder.decode(HelperRequest.self, from: data)
        } catch {
            throw HelperFailure("protocol.request_invalid", stage: "request")
        }
        try validate(request)
        return request
    }

    private static func rejectUnknownKeys(_ data: Data) throws {
        let rootKeys: Set<String> = [
            "schemaVersion", "protocolVersion", "requestId", "command",
            "deadlineMilliseconds", "bundlePath", "theme",
        ]
        let themeKeys: Set<String> = [
            "schemaVersion", "themeId", "variant", "palette",
        ]
        let paletteKeys: Set<String> = [
            "background", "panel", "accent", "text", "muted", "border",
        ]
        guard let root = try? JSONSerialization.jsonObject(with: data)
                as? [String: Any],
              Set(root.keys).isSubset(of: rootKeys)
        else {
            throw HelperFailure("protocol.request_invalid", stage: "request")
        }
        if let theme = root["theme"] {
            if theme is NSNull {
                return
            }
            guard let object = theme as? [String: Any],
                  Set(object.keys).isSubset(of: themeKeys),
                  let palette = object["palette"] as? [String: Any],
                  Set(palette.keys).isSubset(of: paletteKeys)
            else {
                throw HelperFailure("protocol.request_invalid", stage: "request")
            }
        }
    }

    public static func validate(_ request: HelperRequest) throws {
        guard request.schemaVersion == HelperConstants.schemaVersion,
              request.protocolVersion == HelperConstants.protocolVersion
        else {
            throw HelperFailure("protocol.version_mismatch", stage: "request")
        }
        let allowedDeadline = request.command == .discover
            ? 1...8_000
            : request.command == .qualifyAndApply
                ? 1...35_000
                : 1...20_000
        guard allowedDeadline.contains(request.deadlineMilliseconds),
              request.bundlePath == "/Applications/ChatGPT.app"
        else {
            throw HelperFailure("protocol.request_invalid", stage: "request")
        }
        let needsTheme =
            request.command == .applyTemporary ||
            request.command == .qualifyAndApply
        guard needsTheme == (request.theme != nil) else {
            throw HelperFailure("protocol.request_invalid", stage: "request")
        }
        if let theme = request.theme {
            try validate(theme)
        }
    }

    public static func validate(_ theme: ThemeRequest) throws {
        let colors = [
            theme.palette.background,
            theme.palette.panel,
            theme.palette.accent,
            theme.palette.text,
            theme.palette.muted,
            theme.palette.border,
        ]
        guard theme.schemaVersion == 1,
              ["auto", "light", "dark"].contains(theme.variant),
              colors.allSatisfy({ color in
                  let range = NSRange(color.startIndex..., in: color)
                  return colorPattern.firstMatch(
                      in: color,
                      range: range)?.range == range
              })
        else {
            throw HelperFailure("protocol.theme_invalid", stage: "request")
        }
    }

    public static func encode(_ document: HelperDocument) throws -> Data {
        let encoder = JSONEncoder()
        encoder.dateEncodingStrategy = .iso8601
        encoder.outputFormatting = [.sortedKeys]
        let data = try encoder.encode(document)
        guard data.count <= HelperConstants.maximumResponseBytes else {
            throw HelperFailure("protocol.response_too_large", stage: "response")
        }
        return data
    }

    public static func schemaDocument() -> [String: Any] {
        [
            "$schema": "https://json-schema.org/draft/2020-12/schema",
            "$id": "https://codexthemestudio.local/schema/macos-helper-v1.json",
            "schemaVersion": HelperConstants.schemaVersion,
            "protocolVersion": HelperConstants.protocolVersion,
            "toolVersion": HelperConstants.toolVersion,
            "commands": HelperCommand.allCases.map(\.rawValue),
            "maximumRequestBytes": HelperConstants.maximumRequestBytes,
            "maximumResponseBytes": HelperConstants.maximumResponseBytes,
        ]
    }
}
