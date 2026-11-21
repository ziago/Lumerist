/// Where a parameter is adjusted: the Develop module (LrDevelopController,
/// active photo) or Library Quick Develop (relative, every selected photo).
public enum ParameterScope: String, Codable, Sendable, CaseIterable, Hashable {
    case develop
    case library
}

/// One adjustable Lightroom parameter, generated from Shared/parameters.json.
public struct ParameterDescriptor: Sendable, Hashable, Identifiable {
    /// The LrDevelopController parameter name ("lumerist." ids are plug-in specials).
    public let id: String
    public let label: String
    public let section: String
    public let scope: ParameterScope
    /// "EV", "temp" (points; 50 K each on a Kelvin scale), "deg", "px", "%", "choice", or "pt".
    public let unit: String
    public let normalStep: Double
    public let coarseStep: Double
    /// Largest change one request may carry.
    public let maximumDelta: Double
    /// Range offered for user-defined steps.
    public let stepRange: ClosedRange<Double>
    /// Hue-like parameters wrap around instead of stopping at the end.
    public let wraps: Bool
    /// Choice parameters (e.g. vignette style) step through options.
    public let isEnumerated: Bool
    /// Name still to be confirmed in Lightroom (the plug-in reports unsupported ones).
    public let needsVerification: Bool

    /// Unique across scopes ("develop:Exposure", "library:Exposure").
    public var key: ParameterKey { ParameterKey(scope: scope, id: id) }
}

/// Identifies a parameter in one scope.
public struct ParameterKey: Codable, Sendable, Hashable, CustomStringConvertible {
    public let scope: ParameterScope
    public let id: String

    public init(scope: ParameterScope, id: String) {
        self.scope = scope
        self.id = id
    }

    public var description: String { "\(scope.rawValue):\(id)" }
}

public enum ParameterCatalog {
    public static var all: [ParameterDescriptor] { generatedEntries }

    private static let byKey: [ParameterKey: ParameterDescriptor] =
        Dictionary(generatedEntries.map { ($0.key, $0) }, uniquingKeysWith: { first, _ in first })

    public static func descriptor(for key: ParameterKey) -> ParameterDescriptor? { byKey[key] }

    public static func descriptor(scope: ParameterScope, id: String) -> ParameterDescriptor? {
        byKey[ParameterKey(scope: scope, id: id)]
    }

    /// Parameters of one scope grouped by section, in catalog order.
    public static func sections(for scope: ParameterScope) -> [(section: String, parameters: [ParameterDescriptor])] {
        var order: [String] = []
        var groups: [String: [ParameterDescriptor]] = [:]
        for entry in generatedEntries where entry.scope == scope {
            if groups[entry.section] == nil { order.append(entry.section) }
            groups[entry.section, default: []].append(entry)
        }
        return order.map { ($0, groups[$0] ?? []) }
    }
}
