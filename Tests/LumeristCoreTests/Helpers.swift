@testable import LumeristCore

/// A Develop parameter: `dev("Exposure")`.
func dev(_ id: String) -> ParameterKey { ParameterKey(scope: .develop, id: id) }

/// A Library Quick Develop parameter: `lib("Exposure")`.
func lib(_ id: String) -> ParameterKey { ParameterKey(scope: .library, id: id) }
