import LumeristCore

/// Tooltips for the names of key kinds wherever they label something: the
/// Sliders and Quick Develop column headers and the Keyboard tab. The
/// Windows app shows the same texts, with "Shift" for ⇧.
///
/// Library's Quick Develop has no sliders: a step there is added to every
/// selected photo, so the Library texts say so.
enum KeyKindHelp {
    static func dialKey(_ scope: ParameterScope) -> String {
        switch scope {
        case .develop:
            "Hold it and turn the mouse wheel or swipe on the trackpad (or drag, or press ← →): the slider follows until you let go. A quick tap still reaches Lightroom, unless the same key is also a Command Key."
        case .library:
            "Hold it and turn the mouse wheel or swipe on the trackpad (or drag, or press ← →): each step is added to every selected photo, as with Lightroom's Quick Develop buttons, until you let go. A quick tap still reaches Lightroom, unless the same key is also a Command Key."
        }
    }

    static func nudgeDown(_ scope: ParameterScope) -> String {
        switch scope {
        case .develop: "Each press moves the slider one step down; hold the key to repeat. Add ⇧ for the other step."
        case .library: "Each press moves every selected photo one step down; hold the key to repeat. Add ⇧ for the other step."
        }
    }

    static func nudgeUp(_ scope: ParameterScope) -> String {
        switch scope {
        case .develop: "Each press moves the slider one step up; hold the key to repeat. Add ⇧ for the other step."
        case .library: "Each press moves every selected photo one step up; hold the key to repeat. Add ⇧ for the other step."
        }
    }

    /// The Keyboard tab's legend names both Nudge Keys at once.
    static func nudgeKeys(_ scope: ParameterScope) -> String {
        switch scope {
        case .develop: "Each press moves the slider one step down (Nudge −) or up (Nudge +); hold the key to repeat. Add ⇧ for the other step."
        case .library: "Each press moves every selected photo one step down (Nudge −) or up (Nudge +); hold the key to repeat. Add ⇧ for the other step."
        }
    }

    /// The same step serves the Nudge Keys and each step of a Dial Key.
    static func step(_ scope: ParameterScope) -> String {
        switch scope {
        case .develop:
            "How far the slider moves for one Nudge press, and for each click of the mouse wheel, stretch of trackpad swipe or ← → press with the Dial Key held. The ⇧ value is the step when you add Shift. Click a step to change it."
        case .library:
            "How much is added to every selected photo for one Nudge press, and for each click of the mouse wheel, stretch of trackpad swipe or ← → press with the Dial Key held. The ⇧ value is the step when you add Shift. Click a step to change it."
        }
    }

    static let commandKey = "A tap runs a Lightroom command, a preset, a Lightroom shortcut or a macro."

    static let systemKey = "Works in every module: the Combo Key, the Palette Key, the Key Reference toggle and the pause toggle. Change them in General → System Keys."

    static let conflict = "A key that clashes with another of your keys, or that Lightroom or macOS also uses. Point at the key to read why."

    static func combos(comboKey: KeyChord?) -> String {
        guard let comboKey else {
            return "A combo is a key of its own: hold the Combo Key, then press another key. Set a Combo Key in General → System Keys."
        }
        let example = KeyChord(40, .combo).displayText(comboKey: comboKey) // "Tab + K"
        return "A combo is a key of its own: hold \(comboKey.displayText), then press another key (\(example)). Plain keys work alone."
    }
}
