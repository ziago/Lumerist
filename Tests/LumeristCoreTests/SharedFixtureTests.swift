import Foundation
import Testing
@testable import LumeristCore

// Runs the cases in Shared/fixtures (format: Shared/fixtures/README.md). The
// Windows app runs the same files in Windows/Lumerist.Core.Tests; together
// they keep the two apps behaving alike. Key names in the cases are logical
// ("Q", "LeftOf1", "Combo+K"); `MacKeys` turns them into macOS key codes.
// Each runner writes the ids it ran to build/parity/swift/, which
// Scripts/check-parity.py compares with the cases on disk.

// MARK: - Loading

private struct FixtureError: Error, CustomStringConvertible {
    let description: String
    init(_ description: String) { self.description = description }
}

private enum Fixtures {
    static let root = URL(fileURLWithPath: #filePath)
        .deletingLastPathComponent().deletingLastPathComponent().deletingLastPathComponent()

    static func object(at url: URL) throws -> [String: Any] {
        guard let object = try JSONSerialization.jsonObject(with: Data(contentsOf: url)) as? [String: Any] else {
            throw FixtureError("\(url.lastPathComponent) is not a JSON object")
        }
        return object
    }

    /// Every case of every file in Shared/fixtures/<directory>, in file order.
    static func cases(in directory: String) throws -> [[String: Any]] {
        let folder = root.appendingPathComponent("Shared/fixtures/\(directory)")
        let files = try FileManager.default.contentsOfDirectory(at: folder, includingPropertiesForKeys: nil)
            .filter { $0.pathExtension == "json" }
            .sorted { $0.lastPathComponent < $1.lastPathComponent }
        var all: [[String: Any]] = []
        for file in files {
            guard let cases = try object(at: file)["cases"] as? [[String: Any]] else {
                throw FixtureError("\(file.lastPathComponent) has no cases")
            }
            all += cases
        }
        return all
    }

    static func recordRun(_ group: String, ids: [String]) throws {
        let folder = root.appendingPathComponent("build/parity/swift")
        try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
        try (ids.sorted().joined(separator: "\n") + "\n")
            .write(to: folder.appendingPathComponent("\(group).txt"), atomically: true, encoding: .utf8)
    }

    /// Runs `body` for each case, recording failures under the case id.
    static func run(_ group: String, _ cases: [[String: Any]], _ body: ([String: Any], String) throws -> [String]) throws {
        var ids: [String] = []
        for testCase in cases {
            let id = testCase["id"] as? String ?? "<no id>"
            ids.append(id)
            do {
                for failure in try body(testCase, id) { Issue.record(Comment(rawValue: "\(id): \(failure)")) }
            } catch {
                Issue.record(Comment(rawValue: "\(id): \(error)"))
            }
        }
        #expect(Set(ids).count == ids.count, "duplicate case ids in \(group)")
        #expect(!ids.isEmpty, "no cases in \(group)")
        try recordRun(group, ids: ids)
    }
}

// MARK: - Logical keys → macOS

private enum MacKeys {
    static let codes: [String: Int64] = [
        "A": 0, "S": 1, "D": 2, "F": 3, "H": 4, "G": 5, "Z": 6, "X": 7, "C": 8, "V": 9, "B": 11,
        "Q": 12, "W": 13, "E": 14, "R": 15, "Y": 16, "T": 17, "O": 31, "U": 32, "I": 34, "P": 35,
        "L": 37, "J": 38, "K": 40, "N": 45, "M": 46,
        "1": 18, "2": 19, "3": 20, "4": 21, "5": 23, "6": 22, "7": 26, "8": 28, "9": 25, "0": 29,
        "[": 33, "]": 30, "=": 24, "-": 27, "'": 39, ";": 41, "\\": 42, ",": 43, "/": 44, ".": 47,
        "Return": 36, "Tab": 48, "Space": 49, "LeftOf1": 50, "Backspace": 51, "Esc": 53,
        "ForwardDelete": 117, "Home": 115, "End": 119, "PageUp": 116, "PageDown": 121,
        "ArrowLeft": 123, "ArrowRight": 124, "ArrowDown": 125, "ArrowUp": 126,
        "F1": 122, "F2": 120, "F3": 99, "F4": 118, "F5": 96, "F6": 97,
        "F7": 98, "F8": 100, "F9": 101, "F10": 109, "F11": 103, "F12": 111,
    ]
    static let modifiers: [String: KeyModifiers] = [
        "Shift": .shift, "Alt": .option, "Primary": .command, "Control": .control, "Combo": .combo,
    ]

    static func keyCode(_ name: String) throws -> Int64 {
        guard let code = codes[name] else { throw FixtureError("unknown key \(name)") }
        return code
    }

    static func modifiers(_ names: [String]) throws -> KeyModifiers {
        var result: KeyModifiers = []
        for name in names {
            guard let modifier = modifiers[name] else { throw FixtureError("unknown modifier \(name)") }
            result.insert(modifier)
        }
        return result
    }

    /// "Combo+Shift+[" → the chord; the last part is the key.
    static func chord(_ text: String) throws -> KeyChord {
        let parts = text.components(separatedBy: "+")
        guard let key = parts.last, !key.isEmpty else { throw FixtureError("bad chord \(text)") }
        return KeyChord(try keyCode(key), try modifiers(Array(parts.dropLast())))
    }

    static func baseKey(_ text: String) -> String { text.components(separatedBy: "+").last ?? text }
}

// MARK: - Values

private enum Values {
    static func parameter(_ any: Any?) throws -> ParameterKey {
        guard let text = any as? String, let slash = text.firstIndex(of: "/"),
              let scope = ParameterScope(rawValue: String(text[..<slash])) else {
            throw FixtureError("bad parameter \(String(describing: any))")
        }
        return ParameterKey(scope: scope, id: String(text[text.index(after: slash)...]))
    }

    static func action(_ any: Any?) throws -> CommandKey.Action {
        guard let object = any as? [String: Any] else { throw FixtureError("bad action") }
        if let name = object["command"] as? String { return .command(name: name, argument: object["argument"] as? String) }
        if let chord = object["shortcut"] as? String { return .shortcut(try MacKeys.chord(chord)) }
        if let uuid = object["preset"] as? String { return .preset(uuid: uuid, name: object["name"] as? String ?? "") }
        if let steps = object["macro"] as? [[String: Any]] {
            return .macro(try steps.map { MacroStep(action: try action($0["action"]), delay: $0["delay"] as? Double ?? 0) })
        }
        throw FixtureError("bad action \(object)")
    }

    static func intent(_ any: Any) throws -> KeyIntent {
        if let name = any as? String {
            switch name {
            case "openPalette": return .openPalette
            case "closePalette": return .closePalette
            case "toggleKeyReference": return .toggleKeyReference
            case "restorePointer": return .restorePointer
            default: throw FixtureError("unknown intent \(name)")
            }
        }
        guard let object = any as? [String: Any] else { throw FixtureError("bad intent") }
        if let key = object["adjust"] {
            guard let delta = object["delta"] as? Double else { throw FixtureError("adjust without delta") }
            return .adjust(try parameter(key), delta: delta)
        }
        if let key = object["reset"] { return .reset(try parameter(key)) }
        if let key = object["gestureBegan"] { return .gestureBegan(try parameter(key)) }
        if let key = object["gestureEnded"] { return .gestureEnded(try parameter(key)) }
        if let key = object["revealPanel"] { return .revealPanel(try parameter(key)) }
        if let key = object["unavailableInMask"] { return .unavailableInMask(try parameter(key)) }
        if let action = object["action"] { return .action(try Values.action(action)) }
        if let paused = object["togglePause"] as? Bool { return .togglePause(isPaused: paused) }
        throw FixtureError("unknown intent \(object)")
    }

    static func same(_ a: KeyIntent, _ b: KeyIntent) -> Bool {
        if case .adjust(let keyA, let deltaA) = a, case .adjust(let keyB, let deltaB) = b {
            return keyA == keyB && abs(deltaA - deltaB) < 1e-9
        }
        return a == b
    }

    static func same(_ a: [KeyIntent], _ b: [KeyIntent]) -> Bool {
        a.count == b.count && zip(a, b).allSatisfy { same($0, $1) }
    }

    static func chordOrNil(_ any: Any?) throws -> KeyChord? {
        if any == nil || any is NSNull { return nil }
        guard let text = any as? String else { throw FixtureError("bad chord \(String(describing: any))") }
        return try MacKeys.chord(text)
    }

    static func number(_ any: Any?, _ name: String) throws -> Double {
        guard let value = any as? Double else { throw FixtureError("\(name) must be a number") }
        return value
    }

    static func flag(_ any: Any?, _ name: String) throws -> Bool {
        guard let value = any as? Bool else { throw FixtureError("\(name) must be true or false") }
        return value
    }
}

// MARK: - Keymap changes

private enum KeymapChanges {
    static func apply(_ any: Any?) throws -> Keymap {
        var keymap = Keymap.standardLayout
        guard let changes = any as? [String: Any] else {
            if any == nil { return keymap }
            throw FixtureError("keymap changes must be an object")
        }
        let known: Set<String> = ["input", "palette", "systemKeys", "importKeys", "slots", "commandKeys", "replaceCommandKeys"]
        if let unknown = changes.keys.first(where: { !known.contains($0) }) { throw FixtureError("unknown keymap change \(unknown)") }

        for (name, value) in changes["input"] as? [String: Any] ?? [:] {
            switch name {
            case "invertWheel": keymap.input.invertWheel = try Values.flag(value, name)
            case "swapNormalAndCoarse": keymap.input.swapNormalAndCoarse = try Values.flag(value, name)
            case "trackpadPointsPerStep": keymap.input.trackpadPointsPerStep = try Values.number(value, name)
            case "requireMouseDown": keymap.input.requireMouseDown = try Values.flag(value, name)
            case "dragSensitivity": keymap.input.dragSensitivity = try Values.number(value, name)
            case "returnCursorAfterRelease": keymap.input.returnCursorAfterRelease = try Values.flag(value, name)
            case "doubleClickReset": keymap.input.doubleClickReset = try Values.flag(value, name)
            case "middleClickReset": keymap.input.middleClickReset = try Values.flag(value, name)
            case "mouseBackForward": keymap.input.mouseBackForward = try Values.flag(value, name)
            case "followSelectedMask": keymap.input.followSelectedMask = try Values.flag(value, name)
            case "revealPanels": keymap.input.revealPanels = try Values.flag(value, name)
            case "arrowKeysAdjust": keymap.input.arrowKeysAdjust = try Values.flag(value, name)
            default: throw FixtureError("unknown input option \(name)")
            }
        }
        for (name, value) in changes["palette"] as? [String: Any] ?? [:] {
            guard name == "keepVisible" else { throw FixtureError("unknown palette option \(name)") }
            keymap.palette.keepVisible = try Values.flag(value, name)
        }
        for (name, value) in changes["systemKeys"] as? [String: Any] ?? [:] {
            let chord = try Values.chordOrNil(value)
            switch name {
            case "comboKey": keymap.systemKeys.comboKey = chord
            case "paletteKey": keymap.systemKeys.paletteKey = chord
            case "keyReferenceToggle": keymap.systemKeys.keyReferenceToggle = chord
            case "pauseToggle": keymap.systemKeys.pauseToggle = chord
            default: throw FixtureError("unknown system key \(name)")
            }
        }
        for (name, value) in changes["importKeys"] as? [String: Any] ?? [:] {
            if name == "advance" {
                keymap.importKeys.advance = try Values.flag(value, name)
                continue
            }
            guard let role = ImportKeys.Role(rawValue: name) else { throw FixtureError("unknown Import key \(name)") }
            keymap.importKeys[role] = try Values.chordOrNil(value)
        }
        for (name, value) in changes["slots"] as? [String: Any] ?? [:] {
            let key = try Values.parameter(name)
            guard let fields = value as? [String: Any] else { throw FixtureError("slot \(name) must be an object") }
            var failure: FixtureError?
            keymap.updateSlot(for: key) { slot in
                do {
                    for (field, value) in fields {
                        switch field {
                        case "dial": slot.dial = try Values.chordOrNil(value)
                        case "nudgeDown": slot.nudgeDown = try Values.chordOrNil(value)
                        case "nudgeUp": slot.nudgeUp = try Values.chordOrNil(value)
                        case "normalStep": slot.normalStep = value is NSNull ? nil : try Values.number(value, field)
                        case "coarseStep": slot.coarseStep = value is NSNull ? nil : try Values.number(value, field)
                        default: throw FixtureError("unknown slot field \(field)")
                        }
                    }
                } catch {
                    failure = error as? FixtureError ?? FixtureError("\(error)")
                }
            }
            if let failure { throw failure }
        }
        if let list = changes["commandKeys"] as? [[String: Any]] {
            let added = try list.map(commandKey)
            if changes["replaceCommandKeys"] as? Bool == true {
                keymap.commandKeys = added
            } else {
                keymap.commandKeys += added
            }
        }
        return keymap
    }

    static func commandKey(_ object: [String: Any]) throws -> CommandKey {
        guard let scopeName = object["scope"] as? String, let scope = CommandKey.Scope(rawValue: scopeName),
              let chord = object["chord"] as? String, let title = object["title"] as? String else {
            throw FixtureError("bad command key \(object)")
        }
        let id = (object["id"] as? String).flatMap(UUID.init(uuidString:)) ?? UUID()
        return CommandKey(id: id, scope: scope, chord: try MacKeys.chord(chord),
                          group: object["group"] as? String ?? "Lightroom", title: title,
                          action: try Values.action(object["action"]))
    }
}

// MARK: - Gestures

private typealias Engine = KeyGestureEngine<String>

private enum GestureRunner {
    static let contexts: [String: EngineContext] = [
        "develop": EngineContext(),
        "masking": EngineContext(tool: "masking"),
        "library": EngineContext(module: "library"),
        "map": EngineContext(module: "map"),
        "away": EngineContext(isLightroomFrontmost: false),
        "import": EngineContext(module: EngineContext.importModule),
    ]
    static let actions: Set<String> = ["down", "up", "wheel", "swipe", "click", "release", "move", "paletteDidClose", "setKeymap"]
    static let options: Set<String> = ["repeat", "at", "in", "textFocused", "modifiers", "momentum", "count"]
    static let expectations: Set<String> = ["decision", "intents", "lastIntent", "focusQueries", "state"]

    /// Lightroom's Import keys for the keyboard layout of a case: the US keys
    /// with the given changes (`"toggle": null` is a layout with no Toggle).
    static func importShortcuts(_ any: Any) throws -> ImportShortcuts {
        guard let changes = any as? [String: Any] else { throw FixtureError("lightroomImportKeys must be an object") }
        var keys = ImportShortcuts.standard
        for (name, value) in changes {
            switch name {
            case "include": keys.include = try MacKeys.chord(value as? String ?? "")
            case "exclude": keys.exclude = try MacKeys.chord(value as? String ?? "")
            case "toggle": keys.toggle = try Values.chordOrNil(value)
            case "nextPhoto": keys.nextPhoto = try MacKeys.chord(value as? String ?? "")
            default: throw FixtureError("unknown Lightroom Import key \(name)")
            }
        }
        return keys
    }

    static func button(_ name: String) throws -> MouseButton {
        switch name {
        case "left": .left
        case "right": .right
        case "middle": .middle
        case "back": .back
        case "forward": .forward
        default: throw FixtureError("unknown button \(name)")
        }
    }

    static func run(_ testCase: [String: Any]) throws -> [String] {
        guard let steps = testCase["steps"] as? [[String: Any]] else { throw FixtureError("no steps") }
        var engine = Engine(keymap: try KeymapChanges.apply(testCase["keymap"]))
        if let keys = testCase["lightroomImportKeys"] { engine.update(importShortcuts: try importShortcuts(keys)) }
        var failures: [String] = []

        for (index, step) in steps.enumerated() {
            let label = "step \(index + 1)"
            let actionKeys = step.keys.filter(actions.contains)
            guard actionKeys.count == 1, let action = actionKeys.first else {
                throw FixtureError("\(label) must do exactly one thing")
            }
            if let unknown = step.keys.first(where: { !actions.contains($0) && !options.contains($0) && !expectations.contains($0) }) {
                throw FixtureError("\(label): unknown field \(unknown)")
            }
            let contextName = step["in"] as? String ?? "develop"
            guard let context = contexts[contextName] else { throw FixtureError("\(label): unknown context \(contextName)") }
            let modifiers = try MacKeys.modifiers(step["modifiers"] as? [String] ?? [])
            let time = step["at"] as? Double ?? 0
            var focusQueries = 0

            let outcome: Engine.Outcome?
            switch action {
            case "down":
                let text = step["down"] as? String ?? ""
                let focused = step["textFocused"] as? Bool ?? false
                outcome = engine.keyDown(chord: try MacKeys.chord(text), event: MacKeys.baseKey(text) + "↓",
                                         isAutoRepeat: step["repeat"] as? Bool ?? false, timestamp: time,
                                         context: context, isTextInputFocused: { focusQueries += 1; return focused })
            case "up":
                let key = step["up"] as? String ?? ""
                outcome = engine.keyUp(keyCode: try MacKeys.keyCode(key), event: key + "↑", timestamp: time, context: context)
            case "wheel":
                let lines = try Values.number(step["wheel"], "wheel")
                outcome = engine.scroll(lineDelta: lines, pointDelta: lines * 10, isContinuous: false, isMomentum: false,
                                        modifiers: modifiers, context: context)
            case "swipe":
                outcome = engine.scroll(lineDelta: 0, pointDelta: try Values.number(step["swipe"], "swipe"), isContinuous: true,
                                        isMomentum: step["momentum"] as? Bool ?? false, modifiers: modifiers, context: context)
            case "click":
                outcome = engine.mouseDown(try button(step["click"] as? String ?? ""), clickCount: step["count"] as? Int ?? 1,
                                           event: "click", context: context)
            case "release":
                outcome = engine.mouseUp(try button(step["release"] as? String ?? ""), event: "release", context: context)
            case "move":
                outcome = engine.mouseMoved(dx: try Values.number(step["move"], "move"), dy: 0, modifiers: modifiers,
                                            event: "move", context: context)
            case "paletteDidClose":
                engine.paletteDidClose()
                outcome = nil
            default: // setKeymap
                engine.update(keymap: try KeymapChanges.apply(step["setKeymap"]))
                outcome = nil
            }

            if let expected = step["decision"] {
                guard let outcome else { throw FixtureError("\(label): no decision to check") }
                let actual: String = switch outcome.decision {
                case .pass: "pass"
                case .consume: "consume"
                case .replace(let events): events.joined(separator: " ")
                }
                let wanted = (expected as? [String])?.joined(separator: " ") ?? (expected as? String ?? "?")
                if actual != wanted { failures.append("\(label) decision: expected \(wanted), got \(actual)") }
            }
            if let expected = step["intents"] as? [Any] {
                let wanted = try expected.map(Values.intent)
                let got = outcome?.intents ?? []
                if !Values.same(got, wanted) { failures.append("\(label) intents: expected \(wanted), got \(got)") }
            }
            if let expected = step["lastIntent"] {
                let wanted = try Values.intent(expected)
                let got = outcome?.intents.last
                if got.map({ Values.same($0, wanted) }) != true {
                    failures.append("\(label) last intent: expected \(wanted), got \(String(describing: got))")
                }
            }
            if let expected = step["focusQueries"] as? Int, expected != focusQueries {
                failures.append("\(label) focus queries: expected \(expected), got \(focusQueries)")
            }
            for (name, value) in step["state"] as? [String: Any] ?? [:] {
                let matches: Bool
                switch name {
                case "engaged": matches = engine.isEngaged == (value as? Bool)
                case "paused": matches = engine.isPaused == (value as? Bool)
                case "paletteOpen": matches = engine.isPaletteOpen == (value as? Bool)
                case "needsScrollEvents": matches = engine.needsScrollEvents == (value as? Bool)
                case "activeParameter":
                    matches = value is NSNull ? engine.activeParameter == nil : engine.activeParameter == (try Values.parameter(value))
                case "heldEvents": matches = engine.heldEvents == (value as? [String])
                default: throw FixtureError("\(label): unknown state \(name)")
                }
                if !matches { failures.append("\(label) state \(name): expected \(value)") }
            }
        }
        return failures
    }
}

// MARK: - Keymap rules

private enum RuleRunner {
    static func run(_ testCase: [String: Any]) throws -> [String] {
        let keymap = try KeymapChanges.apply(testCase["keymap"])
        var failures: [String] = []

        if let expected = testCase["validate"] {
            do {
                try keymap.validate()
                if expected as? String != "ok" { failures.append("validate passed, expected \(expected)") }
            } catch let error as KeymapError {
                if let mismatch = try mismatch(error, expected) { failures.append(mismatch) }
            }
        }
        if let expected = testCase["conflicts"] as? [String: Any] {
            // Lightroom's language, for its own keys: absent or null is English.
            let language = testCase["lightroomLanguage"] as? String
            let conflicts = keymap.conflicts(lightroomLanguage: language)
            let errors = conflicts.filter { $0.severity == .error }
            let warnings = conflicts.filter { $0.severity == .warning }
            if let count = expected["errors"] as? Int, count != errors.count {
                failures.append("expected \(count) errors, got \(errors.map(\.message))")
            }
            if let count = expected["warnings"] as? Int, count != warnings.count {
                failures.append("expected \(count) warnings, got \(warnings.map(\.message))")
            }
            for matcher in expected["matches"] as? [[String: Any]] ?? [] {
                if try !conflicts.contains(where: { try matches($0, matcher) }) {
                    failures.append("no conflict matches \(matcher); got \(conflicts.map(\.message))")
                }
            }
        }
        return failures
    }

    static func matches(_ conflict: KeyConflict, _ matcher: [String: Any]) throws -> Bool {
        for (field, value) in matcher {
            switch field {
            case "severity": if conflict.severity.rawValue != value as? String { return false }
            case "chord": if conflict.chord != (try MacKeys.chord(value as? String ?? "")) { return false }
            case "scope": if conflict.scope?.rawValue != value as? String { return false }
            case "message": if conflict.message != value as? String { return false }
            case "lightroomAction": if conflict.lightroomAction != value as? String { return false }
            case "usesSystem":
                guard let role = SystemKeys.Role(rawValue: value as? String ?? "") else { throw FixtureError("unknown role \(value)") }
                if !conflict.usages.contains(.system(role)) { return false }
            default: throw FixtureError("unknown matcher field \(field)")
            }
        }
        return true
    }

    /// nil when `error` is the expected one.
    static func mismatch(_ error: KeymapError, _ expected: Any) throws -> String? {
        guard let object = expected as? [String: Any], let name = object["error"] as? String else {
            return "validate threw \(error), expected \(expected)"
        }
        let parameter = object["parameter"] == nil ? nil : try Values.parameter(object["parameter"])
        let same: Bool
        switch error {
        case .invalidStep(let key): same = name == "invalidStep" && key == parameter
        case .invalidOption(let option): same = name == "invalidOption" && option == object["option"] as? String
        case .invalidCommandKey(let reason): same = name == "invalidCommandKey" && reason == object["reason"] as? String
        case .invalidCombo(let misuse):
            switch misuse {
            case .dialKey(let key):
                same = name == "invalidCombo" && object["misuse"] as? String == "dialKey" && key == parameter
            case .lightroomShortcut:
                same = name == "invalidCombo" && object["misuse"] as? String == "lightroomShortcut"
            case .systemKey(let role):
                same = name == "invalidCombo" && object["misuse"] as? String == "systemKey"
                    && role.rawValue == object["role"] as? String
            case .importKey(let role):
                same = name == "invalidCombo" && object["misuse"] as? String == "importKey"
                    && role.rawValue == object["role"] as? String
            }
        case .conflicts: same = name == "conflicts"
        default: same = false
        }
        return same ? nil : "validate threw \(error), expected \(object)"
    }
}

// MARK: - Protocol

private enum ProtocolRunner {
    static func request(_ object: [String: Any]) throws -> LightroomRequest.Kind {
        func scope() throws -> ParameterScope {
            guard let scope = (object["scope"] as? String).flatMap(ParameterScope.init(rawValue:)) else {
                throw FixtureError("bad scope")
            }
            return scope
        }
        switch object["kind"] as? String {
        case "ping": return .ping
        case "adjust":
            return .adjust(scope: try scope(), parameter: object["parameter"] as? String ?? "",
                           delta: try Values.number(object["delta"], "delta"))
        case "reset": return .reset(scope: try scope(), parameter: object["parameter"] as? String ?? "")
        case "gesture":
            guard let phase = (object["phase"] as? String).flatMap(GesturePhase.init(rawValue:)) else { throw FixtureError("bad phase") }
            return .gesture(phase: phase, parameter: object["parameter"] as? String)
        case "command":
            guard let name = (object["name"] as? String).flatMap(LightroomCommandName.init(rawValue:)) else {
                throw FixtureError("unknown command")
            }
            return .command(name, argument: object["argument"] as? String)
        case "query":
            guard let query = (object["name"] as? String).flatMap(LightroomQuery.init(rawValue:)) else { throw FixtureError("unknown query") }
            return .query(query)
        default: throw FixtureError("unknown request kind")
        }
    }

    static func errorName(_ error: LightroomRequest.ValidationError) -> String {
        switch error {
        case .unknownParameter: "unknownParameter"
        case .invalidDelta: "invalidDelta"
        case .resetNotAvailable: "resetNotAvailable"
        case .missingGestureParameter: "missingGestureParameter"
        case .invalidArgument: "invalidArgument"
        }
    }

    static func runRequest(_ testCase: [String: Any]) throws -> [String] {
        guard let object = testCase["request"] as? [String: Any] else { throw FixtureError("no request") }
        let kind = try request(object)
        if let expected = testCase["error"] as? String {
            do {
                _ = try LightroomRequest(kind)
                return ["request accepted, expected \(expected)"]
            } catch let error as LightroomRequest.ValidationError {
                return errorName(error) == expected ? [] : ["expected \(expected), got \(error)"]
            }
        }
        guard let id = testCase["wireId"] as? Int, let line = testCase["line"] as? [String: Any] else {
            throw FixtureError("needs wireId and line")
        }
        // The line names the token the client was given (protocol 4).
        let data = try RequestCodec.encodeLine(try LightroomRequest(kind), id: id, token: line["token"] as? String ?? "")
        guard data.last == 0x0A, data.dropLast().firstIndex(of: 0x0A) == nil else { return ["a line must end with one newline"] }
        let decoded = try JSONSerialization.jsonObject(with: Data(data.dropLast())) as? NSDictionary
        return decoded?.isEqual(to: line) == true ? [] : ["expected \(line), got \(String(decoding: data, as: UTF8.self))"]
    }

    static func errorName(_ error: MessageCodec.ValidationError) -> String {
        switch error {
        case .unsupportedVersion: "unsupportedVersion"
        case .malformed: "malformed"
        case .lineTooLong: "lineTooLong"
        case .messageTooLong: "messageTooLong"
        case .invalidNumber: "invalidNumber"
        case .unitTooLong: "unitTooLong"
        }
    }

    static func context(_ object: [String: Any]) -> LightroomContext {
        LightroomContext(module: object["module"] as? String ?? "", tool: object["tool"] as? String ?? "",
                         language: object["language"] as? String)
    }

    static func message(_ object: [String: Any]) throws -> LightroomMessage {
        if let ack = object["ack"] as? [String: Any] {
            return .ack(try LightroomAck(
                id: ack["id"] as? Int, accepted: try Values.flag(ack["accepted"], "accepted"),
                message: ack["message"] as? String, parameter: ack["parameter"] as? String,
                scope: (ack["scope"] as? String).flatMap(ParameterScope.init(rawValue:)),
                value: ack["value"] as? Double, minimum: ack["minimum"] as? Double, maximum: ack["maximum"] as? Double,
                unit: ack["unit"] as? String))
        }
        if let event = object["context"] as? [String: Any] { return .context(context(event)) }
        if let result = object["result"] as? [String: Any] {
            if let value = result["context"] as? [String: Any] {
                return .result(.context(id: value["id"] as? Int, context(value)))
            }
            if let value = result["presets"] as? [String: Any] {
                let items = (value["items"] as? [[String: Any]] ?? []).map {
                    PresetItem(id: $0["id"] as? String ?? "", name: $0["name"] as? String ?? "", group: $0["group"] as? String ?? "")
                }
                return .result(.presets(id: value["id"] as? Int, items: items, truncated: value["truncated"] as? Bool ?? false))
            }
            if let value = result["catalog"] as? [String: Any] {
                return .result(.catalog(id: value["id"] as? Int, unsupported: value["unsupported"] as? [String] ?? [],
                                        skipped: value["skipped"] as? [String] ?? []))
            }
        }
        throw FixtureError("bad expected message \(object)")
    }

    static func runMessage(_ testCase: [String: Any]) throws -> [String] {
        guard let line = testCase["line"] else { throw FixtureError("no line") }
        let data = try JSONSerialization.data(withJSONObject: line) + Data([0x0A])
        if let expected = testCase["error"] as? String {
            do {
                let decoded = try MessageCodec.decodeLine(data)
                return ["decoded \(String(describing: decoded)), expected \(expected)"]
            } catch let error as MessageCodec.ValidationError {
                return errorName(error) == expected ? [] : ["expected \(expected), got \(error)"]
            }
        }
        guard let expected = testCase["message"] else { throw FixtureError("needs message or error") }
        let decoded = try MessageCodec.decodeLine(data)
        if expected is NSNull {
            return decoded == nil ? [] : ["expected the line to be ignored, got \(String(describing: decoded))"]
        }
        guard let object = expected as? [String: Any] else { throw FixtureError("bad message") }
        let wanted = try message(object)
        return decoded == wanted ? [] : ["expected \(wanted), got \(String(describing: decoded))"]
    }
}

// MARK: - The keys Lumerist presses

private enum LightroomKeysRunner {
    /// The US layout, by logical key: what each key types alone.
    static let usLayout: [String: String] = {
        var layout: [String: String] = ["LeftOf1": "`"]
        for name in MacKeys.codes.keys where name.count == 1 { layout[name] = name.lowercased() }
        return layout
    }()

    /// Logical key → macOS key code on this keyboard: on ISO keyboards the
    /// key left of 1 is 10 and IsoLeftOfZ is 50.
    static func keyCode(_ name: String, iso: Bool) throws -> Int64 {
        switch (name, iso) {
        case ("LeftOf1", true): return 10
        case ("IsoLeftOfZ", true): return 50
        case ("IsoLeftOfZ", false): throw FixtureError("IsoLeftOfZ needs \"keyboard\": \"iso\"")
        default: return try MacKeys.keyCode(name)
        }
    }

    static func chord(_ text: String, iso: Bool) throws -> KeyChord {
        let parts = text.components(separatedBy: "+")
        guard let key = parts.last, !key.isEmpty else { throw FixtureError("bad chord \(text)") }
        return KeyChord(try keyCode(key, iso: iso), try MacKeys.modifiers(Array(parts.dropLast())))
    }

    static func run(_ testCase: [String: Any]) throws -> [String] {
        let known: Set<String> = ["id", "about", "lightroomLanguage", "keyboard", "layout", "deadKeys", "importKeys", "fallbacks"]
        if let unknown = testCase.keys.first(where: { !known.contains($0) }) { throw FixtureError("unknown field \(unknown)") }
        let iso: Bool
        switch testCase["keyboard"] as? String ?? "ansi" {
        case "ansi": iso = false
        case "iso": iso = true
        case let other: throw FixtureError("unknown keyboard \(other)")
        }
        let language = testCase["lightroomLanguage"] as? String
        var typed = usLayout
        for (name, value) in testCase["layout"] as? [String: Any] ?? [:] {
            typed[name] = value as? String // null: the key types nothing
            if !(value is String || value is NSNull) { throw FixtureError("layout values are characters or null") }
        }
        let dead = Set(testCase["deadKeys"] as? [String] ?? [])
        var plain: [Int64: String] = [:]
        var display: [Int64: String] = [:]
        for (name, text) in typed {
            let code = try keyCode(name, iso: iso)
            display[code] = text
            if !dead.contains(name) { plain[code] = text }
        }
        if Set(plain.values).count != plain.count { throw FixtureError("two keys type the same character") }
        let layout = LayoutCharacters(plain: plain, command: plain, display: display)

        var failures: [String] = []
        let sends = ImportShortcuts.forLayout(layout, lightroomLanguage: language)
        for (name, value) in testCase["importKeys"] as? [String: Any] ?? [:] {
            guard let role = ImportKeys.Role(rawValue: name) else { throw FixtureError("unknown Import key \(name)") }
            let expected = value is NSNull ? nil : try chord(value as? String ?? "", iso: iso)
            if sends.chord(for: role) != expected { failures.append("\(name): \(String(describing: sends.chord(for: role)))") }
        }
        for (name, value) in testCase["fallbacks"] as? [String: Any] ?? [:] {
            let expected = try chord(value as? String ?? "", iso: iso)
            let actual: KeyChord?
            if let command = AppCommand(rawValue: name) {
                actual = command.press(lightroomLanguage: language).chord(layout: layout, asciiCapable: nil)
            } else if let command = LightroomCommandName(rawValue: name) {
                actual = FallbackShortcuts.press(for: command, lightroomLanguage: language)?.chord(layout: layout, asciiCapable: nil)
            } else {
                throw FixtureError("unknown command \(name)")
            }
            if actual != expected { failures.append("\(name): \(String(describing: actual))") }
        }
        return failures
    }
}

// MARK: - Standard layout

private enum LayoutRunner {
    static func run(_ fixture: [String: Any]) throws -> [String] {
        let keymap = Keymap.standardLayout
        var failures: [String] = []

        for scope in ParameterScope.allCases {
            let expected = (fixture[scope.rawValue] as? [String: Any])?["dial"] as? [String: String] ?? [:]
            let slots = keymap.slots(for: scope)
            if Set(slots.keys) != Set(expected.keys) { failures.append("\(scope) parameters differ: \(slots.keys.sorted())") }
            for (id, key) in expected {
                let slot = slots[id]
                if slot?.dial != (try MacKeys.chord(key)) { failures.append("\(scope)/\(id) should be on \(key)") }
                if slot?.nudgeDown != nil || slot?.nudgeUp != nil || slot?.normalStep != nil || slot?.coarseStep != nil {
                    failures.append("\(scope)/\(id) should only have a Dial Key")
                }
            }
        }

        let expectedKeys = try (fixture["commandKeys"] as? [[String: Any]] ?? []).map(KeymapChanges.commandKey)
        if keymap.commandKeys != expectedKeys {
            failures.append("command keys differ: \(keymap.commandKeys.map { keymap.displayText(for: $0.chord) + " " + $0.title })")
        }

        let system = fixture["systemKeys"] as? [String: Any] ?? [:]
        for role in SystemKeys.Role.allCases where keymap.systemKeys.chord(for: role) != (try Values.chordOrNil(system[role.rawValue])) {
            failures.append("system key \(role.rawValue) differs")
        }

        let importKeys = fixture["importKeys"] as? [String: Any] ?? [:]
        if Set(importKeys.keys) != Set(ImportKeys.CodingKeys.allCases.map(\.stringValue)) {
            failures.append("Import key fields differ: \(importKeys.keys.sorted())")
        }
        for role in ImportKeys.Role.allCases where keymap.importKeys[role] != (try Values.chordOrNil(importKeys[role.rawValue])) {
            failures.append("Import key \(role.rawValue) differs")
        }
        if keymap.importKeys.advance != importKeys["advance"] as? Bool { failures.append("Import advance differs") }

        let input = fixture["input"] as? [String: Any] ?? [:]
        let options = keymap.input
        let actual: [String: Any] = [
            "invertWheel": options.invertWheel, "swapNormalAndCoarse": options.swapNormalAndCoarse,
            "trackpadPointsPerStep": options.trackpadPointsPerStep, "requireMouseDown": options.requireMouseDown,
            "dragSensitivity": options.dragSensitivity, "returnCursorAfterRelease": options.returnCursorAfterRelease,
            "doubleClickReset": options.doubleClickReset, "middleClickReset": options.middleClickReset,
            "mouseBackForward": options.mouseBackForward, "followSelectedMask": options.followSelectedMask,
            "revealPanels": options.revealPanels, "arrowKeysAdjust": options.arrowKeysAdjust,
        ]
        if Set(input.keys) != Set(actual.keys) { failures.append("input options differ: \(input.keys.sorted())") }
        for (name, value) in actual where !(NSDictionary(dictionary: [name: value]).isEqual(to: [name: input[name] ?? NSNull()])) {
            failures.append("input \(name) should be \(input[name] ?? "missing")")
        }

        let palette = fixture["palette"] as? [String: Any] ?? [:]
        if keymap.palette.keepVisible != palette["keepVisible"] as? Bool || !keymap.palette.favourites.isEmpty
            || (palette["favourites"] as? [Any])?.isEmpty != true {
            failures.append("palette defaults differ")
        }
        let conflicts = keymap.conflicts()
        if !conflicts.isEmpty { failures.append("conflicts: \(conflicts.map(\.message))") }
        return failures
    }
}

// MARK: - Tests

struct SharedFixtureTests {
    @Test func gestures() throws {
        try Fixtures.run("gestures", try Fixtures.cases(in: "gestures")) { testCase, _ in try GestureRunner.run(testCase) }
    }

    @Test func lightroomKeys() throws {
        try Fixtures.run("lightroom-keys", try Fixtures.cases(in: "lightroom-keys")) { testCase, _ in try LightroomKeysRunner.run(testCase) }
    }

    @Test func keymapRules() throws {
        try Fixtures.run("keymap-rules", try Fixtures.cases(in: "keymap-rules")) { testCase, _ in try RuleRunner.run(testCase) }
    }

    @Test func protocolLines() throws {
        let requests = try Fixtures.object(at: Fixtures.root.appendingPathComponent("Shared/fixtures/protocol/requests.json"))
        let messages = try Fixtures.object(at: Fixtures.root.appendingPathComponent("Shared/fixtures/protocol/messages.json"))
        let requestCases = requests["cases"] as? [[String: Any]] ?? []
        let messageCases = messages["cases"] as? [[String: Any]] ?? []
        try Fixtures.run("protocol", requestCases + messageCases) { testCase, id in
            id.hasPrefix("protocol-requests/") ? try ProtocolRunner.runRequest(testCase) : try ProtocolRunner.runMessage(testCase)
        }
    }

    @Test func standardLayout() throws {
        let fixture = try Fixtures.object(at: Fixtures.root.appendingPathComponent("Shared/fixtures/standard-layout.json"))
        try Fixtures.run("standard-layout", [fixture.merging(["id": "standard-layout"]) { a, _ in a }]) { testCase, _ in
            try LayoutRunner.run(testCase)
        }
    }

    /// "Report a problem": the prefilled GitHub form, the same address on both systems.
    @Test func bugReportLinks() throws {
        try Fixtures.run("bug-report", try Fixtures.cases(in: "bug-report")) { testCase, _ in
            if let computers = testCase["computers"] as? [[String: Any]] {
                return computers.compactMap { row in
                    let isMac = row["isMac"] as? Bool ?? false, isArm = row["isArm"] as? Bool ?? false
                    let name = BugReport.computer(isMac: isMac, isArm: isArm)
                    return name == row["name"] as? String ? nil : "computer(isMac: \(isMac), isArm: \(isArm)) = \(name)"
                }
            }
            guard let details = testCase["details"] as? [String: String], let expected = testCase["url"] as? String else {
                throw FixtureError("a bug-report case needs details and url, or computers")
            }
            let url = BugReport.url(for: .init(lumerist: details["lumerist"] ?? "", system: details["system"] ?? "",
                                               computer: details["computer"] ?? "", keyboard: details["keyboard"] ?? ""))
            return url.absoluteString == expected ? [] : ["url \(url.absoluteString)"]
        }
    }

    /// The Mac's own translation of the logical key names must stay complete.
    @Test func everyLogicalKeyHasAMacKeyCode() {
        #expect(Set(MacKeys.codes.values).count == MacKeys.codes.count)
        #expect(MacKeys.codes["LeftOf1"] == KeyChord.grave.keyCode)
        #expect(MacKeys.codes["Tab"] == KeyChord.tab.keyCode)
    }
}
