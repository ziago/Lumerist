import Foundation

/// The plug-in's answer to one request (protocol 3). For an adjustment it may
/// carry the parameter's resulting value, range and unit for the overlay.
public struct LightroomAck: Sendable, Hashable {
    static let maximumMessageBytes = 1_024
    static let maximumUnitLength = 8

    /// The request id (nil when the plug-in could not read the request).
    public let id: Int?
    public let accepted: Bool
    public let message: String?
    public let parameter: String?
    public let scope: ParameterScope?
    public let value: Double?
    public let minimum: Double?
    public let maximum: Double?
    /// "EV", "K", "deg", "px", "%", "pt" or "choice".
    public let unit: String?

    public init(
        id: Int?,
        accepted: Bool,
        message: String? = nil,
        parameter: String? = nil,
        scope: ParameterScope? = nil,
        value: Double? = nil,
        minimum: Double? = nil,
        maximum: Double? = nil,
        unit: String? = nil
    ) throws {
        self.id = id
        self.accepted = accepted
        self.message = message
        self.parameter = parameter
        self.scope = scope
        self.value = value
        self.minimum = minimum
        self.maximum = maximum
        self.unit = unit
        try validate()
    }

    /// The catalog entry the ack refers to, when it names one.
    var parameterKey: ParameterKey? {
        guard let parameter else { return nil }
        return ParameterKey(scope: scope ?? .develop, id: parameter)
    }

    private func validate() throws {
        guard message?.utf8.count ?? 0 <= Self.maximumMessageBytes else {
            throw MessageCodec.ValidationError.messageTooLong
        }
        for number in [value, minimum, maximum] {
            if let number, !number.isFinite {
                throw MessageCodec.ValidationError.invalidNumber
            }
        }
        guard unit?.utf8.count ?? 0 <= Self.maximumUnitLength else {
            throw MessageCodec.ValidationError.unitTooLong
        }
    }
}

/// Where the user is in Lightroom: the module and, in Develop, the tool
/// ("masking" while the Masking panel is open).
public struct LightroomContext: Sendable, Hashable {
    public let module: String
    public let tool: String

    public init(module: String, tool: String) {
        self.module = module
        self.tool = tool
    }

    public var isMasking: Bool { module == "develop" && tool == "masking" }
}

/// One develop preset as listed by the plug-in.
public struct PresetItem: Codable, Sendable, Hashable, Identifiable {
    /// The preset's UUID (the `applyPreset` argument).
    public let id: String
    public let name: String
    /// The preset folder.
    public let group: String

    public init(id: String, name: String, group: String) {
        self.id = id
        self.name = name
        self.group = group
    }
}

/// The answer to a query.
public enum LightroomResult: Sendable, Hashable {
    case context(id: Int?, LightroomContext)
    case presets(id: Int?, items: [PresetItem], truncated: Bool)
    /// Develop parameters whose range Lightroom could not report, and the
    /// mask parameters that could not be checked (no mask selected).
    case catalog(id: Int?, unsupported: [String], skipped: [String])

    public var id: Int? {
        switch self {
        case .context(let id, _), .presets(let id, _, _), .catalog(let id, _, _): id
        }
    }
}

/// Everything the plug-in sends on the response channel.
public enum LightroomMessage: Sendable, Hashable {
    case ack(LightroomAck)
    /// Pushed after connecting and whenever the module or tool changes.
    case context(LightroomContext)
    case result(LightroomResult)
}

/// JSON-lines codec for plug-in messages (protocol 3).
enum MessageCodec {
    /// Lines may be large (preset lists).
    static let maximumLineBytes = 1_048_576

    struct Wire: Codable {
        var version: Int
        /// Optional so that a line from an older plug-in (no `kind`) still
        /// reaches the version check and gets a useful error.
        var kind: String?
        var id: Int?
        var accepted: Bool?
        var message: String?
        var parameter: String?
        var scope: String?
        var value: Double?
        var minimum: Double?
        var maximum: Double?
        var unit: String?
        var name: String?
        var module: String?
        var tool: String?
        var items: [PresetItem]?
        var truncated: Bool?
        var unsupported: [String]?
        var skipped: [String]?
    }

    /// Decodes one line. Returns nil for kinds, events or results this build
    /// does not know (a newer plug-in may add them).
    static func decodeLine(_ data: Data) throws -> LightroomMessage? {
        let trimmed = data.last == 0x0A ? data.dropLast() : data[...]
        guard trimmed.count <= maximumLineBytes else {
            throw ValidationError.lineTooLong
        }
        let wire = try JSONDecoder().decode(Wire.self, from: Data(trimmed))
        guard wire.version == LightroomRequest.protocolVersion else {
            throw ValidationError.unsupportedVersion(wire.version)
        }
        guard let kind = wire.kind else {
            throw ValidationError.malformed
        }
        switch kind {
        case "ack":
            guard let accepted = wire.accepted else { throw ValidationError.malformed }
            return .ack(try LightroomAck(
                id: wire.id,
                accepted: accepted,
                message: wire.message,
                parameter: wire.parameter,
                scope: wire.scope.flatMap(ParameterScope.init(rawValue:)),
                value: wire.value,
                minimum: wire.minimum,
                maximum: wire.maximum,
                unit: wire.unit
            ))
        case "event":
            guard wire.name == "context" else { return nil }
            return .context(LightroomContext(module: wire.module ?? "", tool: wire.tool ?? ""))
        case "result":
            switch wire.name {
            case "context":
                return .result(.context(id: wire.id, LightroomContext(module: wire.module ?? "", tool: wire.tool ?? "")))
            case "presets":
                return .result(.presets(id: wire.id, items: wire.items ?? [], truncated: wire.truncated ?? false))
            case "catalog":
                return .result(.catalog(id: wire.id, unsupported: wire.unsupported ?? [], skipped: wire.skipped ?? []))
            default:
                return nil
            }
        default:
            return nil
        }
    }

    /// Encodes a message as the plug-in would (used by the tests).
    static func encodeLine(_ message: LightroomMessage) throws -> Data {
        var wire = Wire(version: LightroomRequest.protocolVersion, kind: "ack")
        switch message {
        case .ack(let ack):
            wire.id = ack.id
            wire.accepted = ack.accepted
            wire.message = ack.message
            wire.parameter = ack.parameter
            wire.scope = ack.scope?.rawValue
            wire.value = ack.value
            wire.minimum = ack.minimum
            wire.maximum = ack.maximum
            wire.unit = ack.unit
        case .context(let context):
            wire.kind = "event"
            wire.name = "context"
            wire.module = context.module
            wire.tool = context.tool
        case .result(let result):
            wire.kind = "result"
            wire.id = result.id
            switch result {
            case .context(_, let context):
                wire.name = "context"
                wire.module = context.module
                wire.tool = context.tool
            case .presets(_, let items, let truncated):
                wire.name = "presets"
                wire.items = items
                wire.truncated = truncated ? true : nil
            case .catalog(_, let unsupported, let skipped):
                wire.name = "catalog"
                wire.unsupported = unsupported
                wire.skipped = skipped
            }
        }
        let encoder = JSONEncoder()
        encoder.outputFormatting = [.sortedKeys, .withoutEscapingSlashes]
        return try encoder.encode(wire) + Data([0x0A])
    }

    enum ValidationError: Error, Equatable, LocalizedError, Sendable {
        case unsupportedVersion(Int)
        case malformed
        case lineTooLong
        case messageTooLong
        case invalidNumber
        case unitTooLong

        var errorDescription: String? {
            switch self {
            case .unsupportedVersion(let version):
                "The plug-in speaks protocol \(version); this companion needs protocol \(LightroomRequest.protocolVersion). Update the plug-in or the app."
            case .malformed:
                "The plug-in sent a malformed message."
            case .lineTooLong:
                "The plug-in sent a message longer than 1 MB."
            case .messageTooLong:
                "A bridge message must not exceed \(LightroomAck.maximumMessageBytes) UTF-8 bytes."
            case .invalidNumber:
                "A bridge value must be a finite number."
            case .unitTooLong:
                "A bridge unit must not exceed \(LightroomAck.maximumUnitLength) UTF-8 bytes."
            }
        }
    }
}
