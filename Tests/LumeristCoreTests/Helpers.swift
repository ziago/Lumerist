@testable import LumeristCore

/// The bridge token the tests' clients send (protocol 4).
let testToken = String(repeating: "0123456789abcdef", count: 4)

/// A Develop parameter: `dev("Exposure")`.
func dev(_ id: String) -> ParameterKey { ParameterKey(scope: .develop, id: id) }

/// A Library Quick Develop parameter: `lib("Exposure")`.
func lib(_ id: String) -> ParameterKey { ParameterKey(scope: .library, id: id) }
