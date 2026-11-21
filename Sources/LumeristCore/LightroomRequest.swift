import Foundation

/// Phase of a Dial Key gesture: the plug-in turns begin/end into Lightroom
/// tracking so one gesture becomes one history step.
public enum GesturePhase: String, Codable, Sendable, Hashable, CaseIterable {
    case begin
    case end
}

/// Read-only questions the companion can ask the plug-in.
public enum LightroomQuery: String, Codable, Sendable, Hashable, CaseIterable {
    /// Current module and Develop tool.
    case context
    /// Every develop preset (UUID, name, folder).
    case presets
    /// Catalog parameters this Lightroom does not support.
    case catalog
}

/// Allow-listed Lightroom commands (docs/protocol-v3.md).
public enum LightroomCommandName: String, Codable, Sendable, Hashable, CaseIterable {
    case undo, redo, autoTone, autoWhiteBalance, resetAll
    case nextPhoto, previousPhoto, flagPick, flagReject, flagRemove
    case rating, colorLabel, switchModule, selectTool, revealPanel
    case zoomToggle, showClipping
    case createMask, addToMask, subtractFromMask, intersectWithMask, invertMask, toggleMaskOverlay
    case applyPreset, copySettings, pasteSettings

    public static let colorLabels: Set<String> = ["red", "yellow", "green", "blue", "purple", "none"]
    public static let modules: Set<String> = ["library", "develop", "map", "book", "slideshow", "print", "web"]
    public static let tools: Set<String> = ["loupe", "crop", "dust", "redeye", "masking", "upright"]
    public static let maskTypes: Set<String> = ["brush", "gradient", "radialGradient", "rangeMask", "aiSelection"]
    public static let maskSubtypes: Set<String> = [
        "subject", "sky", "background", "objects", "people", "landscape", "color", "luminance", "depth",
    ]

    /// Whether `argument` is acceptable for this command (nil = none given).
    public func accepts(argument: String?) -> Bool {
        switch self {
        case .rating:
            guard let argument, let stars = Int(argument) else { return false }
            return (0...5).contains(stars) && argument == String(stars)
        case .colorLabel:
            return argument.map(Self.colorLabels.contains) ?? false
        case .switchModule:
            return argument.map(Self.modules.contains) ?? false
        case .selectTool:
            return argument.map(Self.tools.contains) ?? false
        case .revealPanel:
            return argument.flatMap { ParameterCatalog.descriptor(scope: .develop, id: $0) } != nil
        case .createMask, .addToMask, .subtractFromMask, .intersectWithMask:
            guard let argument else { return false }
            let parts = argument.split(separator: "/", omittingEmptySubsequences: false).map(String.init)
            guard parts.count <= 2, let type = parts.first, Self.maskTypes.contains(type) else { return false }
            return parts.count == 1 || Self.maskSubtypes.contains(parts[1])
        case .applyPreset:
            guard let argument, (1...64).contains(argument.count) else { return false }
            return argument.allSatisfy { $0.isASCII && ($0.isLetter || $0.isNumber || $0 == "-") }
        default:
            return argument == nil
        }
    }
}

/// One request from the companion to the plug-in (protocol 3). The client
/// assigns the wire `id` when it sends the request.
public struct LightroomRequest: Sendable, Hashable {
    public static let protocolVersion = 3

    public enum Kind: Sendable, Hashable {
        case ping
        case adjust(scope: ParameterScope, parameter: String, delta: Double)
        case reset(scope: ParameterScope, parameter: String)
        case gesture(phase: GesturePhase, parameter: String?)
        case command(LightroomCommandName, argument: String?)
        case query(LightroomQuery)
    }

    public let kind: Kind

    public init(_ kind: Kind) throws {
        self.kind = kind
        try validate()
    }

    public static let ping = try! LightroomRequest(.ping)

    public static func adjust(_ scope: ParameterScope, _ parameter: String, delta: Double) throws -> LightroomRequest {
        try LightroomRequest(.adjust(scope: scope, parameter: parameter, delta: delta))
    }

    public static func reset(_ scope: ParameterScope, _ parameter: String) throws -> LightroomRequest {
        try LightroomRequest(.reset(scope: scope, parameter: parameter))
    }

    public static func gesture(_ phase: GesturePhase, _ parameter: String?) throws -> LightroomRequest {
        try LightroomRequest(.gesture(phase: phase, parameter: parameter))
    }

    public static func command(_ name: LightroomCommandName, _ argument: String? = nil) throws -> LightroomRequest {
        try LightroomRequest(.command(name, argument: argument))
    }

    public static func query(_ query: LightroomQuery) -> LightroomRequest {
        try! LightroomRequest(.query(query))
    }

    /// The parameter a request touches, if any.
    var parameterKey: ParameterKey? {
        switch kind {
        case .adjust(let scope, let parameter, _), .reset(let scope, let parameter):
            ParameterKey(scope: scope, id: parameter)
        case .gesture(_, let parameter):
            parameter.map { ParameterKey(scope: .develop, id: $0) }
        default:
            nil
        }
    }

    private func validate() throws {
        switch kind {
        case .ping, .query:
            return
        case .adjust(let scope, let parameter, let delta):
            guard let descriptor = ParameterCatalog.descriptor(scope: scope, id: parameter) else {
                throw ValidationError.unknownParameter(ParameterKey(scope: scope, id: parameter))
            }
            guard delta.isFinite, abs(delta) <= descriptor.maximumDelta else {
                throw ValidationError.invalidDelta(descriptor.key, limit: descriptor.maximumDelta)
            }
        case .reset(let scope, let parameter):
            guard ParameterCatalog.descriptor(scope: scope, id: parameter) != nil else {
                throw ValidationError.unknownParameter(ParameterKey(scope: scope, id: parameter))
            }
            guard scope == .develop else {
                throw ValidationError.resetNotAvailable(scope)
            }
        case .gesture(let phase, let parameter):
            if let parameter {
                guard ParameterCatalog.descriptor(scope: .develop, id: parameter) != nil else {
                    throw ValidationError.unknownParameter(ParameterKey(scope: .develop, id: parameter))
                }
            } else if phase == .begin {
                throw ValidationError.missingGestureParameter
            }
        case .command(let name, let argument):
            guard name.accepts(argument: argument) else {
                throw ValidationError.invalidArgument(name, argument)
            }
        }
    }

    public enum ValidationError: Error, Equatable, LocalizedError, Sendable {
        case unknownParameter(ParameterKey)
        case invalidDelta(ParameterKey, limit: Double)
        case resetNotAvailable(ParameterScope)
        case missingGestureParameter
        case invalidArgument(LightroomCommandName, String?)

        public var errorDescription: String? {
            switch self {
            case .unknownParameter(let key):
                "Unknown parameter \(key)."
            case .invalidDelta(let key, let limit):
                "\(key.id) needs a finite change between -\(limit) and \(limit)."
            case .resetNotAvailable(let scope):
                "Reset is not available in \(scope.rawValue)."
            case .missingGestureParameter:
                "A gesture begin needs a parameter."
            case .invalidArgument(let name, let argument):
                "Invalid argument for \(name.rawValue): \(argument ?? "none")."
            }
        }
    }
}

/// The JSON-lines wire form of a request (sorted keys, newline-terminated).
enum RequestCodec {
    struct Wire: Codable, Equatable {
        var version: Int
        var id: Int
        var kind: String
        var scope: String?
        var parameter: String?
        var delta: Double?
        var phase: String?
        var name: String?
        var argument: String?
    }

    static func encodeLine(_ request: LightroomRequest, id: Int) throws -> Data {
        var wire = Wire(version: LightroomRequest.protocolVersion, id: id, kind: "ping")
        switch request.kind {
        case .ping:
            break
        case .adjust(let scope, let parameter, let delta):
            wire.kind = "adjust"
            wire.scope = scope.rawValue
            wire.parameter = parameter
            wire.delta = delta
        case .reset(let scope, let parameter):
            wire.kind = "reset"
            wire.scope = scope.rawValue
            wire.parameter = parameter
        case .gesture(let phase, let parameter):
            wire.kind = "gesture"
            wire.phase = phase.rawValue
            wire.parameter = parameter
        case .command(let name, let argument):
            wire.kind = "command"
            wire.name = name.rawValue
            wire.argument = argument
        case .query(let query):
            wire.kind = "query"
            wire.name = query.rawValue
        }
        let encoder = JSONEncoder()
        encoder.outputFormatting = [.sortedKeys, .withoutEscapingSlashes]
        return try encoder.encode(wire) + Data([0x0A])
    }

    /// Decodes a request line (used by the tests).
    static func decodeLine(_ data: Data) throws -> (id: Int, request: LightroomRequest) {
        let trimmed = data.last == 0x0A ? data.dropLast() : data[...]
        let wire = try JSONDecoder().decode(Wire.self, from: Data(trimmed))
        guard wire.version == LightroomRequest.protocolVersion else {
            throw DecodingError.unsupportedVersion(wire.version)
        }
        func scope() throws -> ParameterScope {
            guard let raw = wire.scope, let scope = ParameterScope(rawValue: raw) else { throw DecodingError.malformed }
            return scope
        }
        let kind: LightroomRequest.Kind
        switch wire.kind {
        case "ping":
            kind = .ping
        case "adjust":
            guard let parameter = wire.parameter, let delta = wire.delta else { throw DecodingError.malformed }
            kind = .adjust(scope: try scope(), parameter: parameter, delta: delta)
        case "reset":
            guard let parameter = wire.parameter else { throw DecodingError.malformed }
            kind = .reset(scope: try scope(), parameter: parameter)
        case "gesture":
            guard let raw = wire.phase, let phase = GesturePhase(rawValue: raw) else { throw DecodingError.malformed }
            kind = .gesture(phase: phase, parameter: wire.parameter)
        case "command":
            guard let raw = wire.name, let name = LightroomCommandName(rawValue: raw) else { throw DecodingError.malformed }
            kind = .command(name, argument: wire.argument)
        case "query":
            guard let raw = wire.name, let query = LightroomQuery(rawValue: raw) else { throw DecodingError.malformed }
            kind = .query(query)
        default:
            throw DecodingError.malformed
        }
        return (wire.id, try LightroomRequest(kind))
    }

    enum DecodingError: Error, Equatable, Sendable {
        case unsupportedVersion(Int)
        case malformed
    }
}
