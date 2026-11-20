import Foundation

// MARK: - Slots

/// The keys bound to one parameter in one scope.
public struct ParameterSlot: Codable, Sendable, Hashable {
    /// Hold + scroll / drag / arrows. Never a combo.
    public var dial: KeyChord?
    /// Tap to step down / up. Each may be a combo on its own ("Tab + [").
    public var nudgeDown: KeyChord?
    public var nudgeUp: KeyChord?
    /// Step overrides; nil uses the catalog default.
    public var normalStep: Double?
    public var coarseStep: Double?

    public init(
        dial: KeyChord? = nil,
        nudgeDown: KeyChord? = nil,
        nudgeUp: KeyChord? = nil,
        normalStep: Double? = nil,
        coarseStep: Double? = nil
    ) {
        self.dial = dial
        self.nudgeDown = nudgeDown
        self.nudgeUp = nudgeUp
        self.normalStep = normalStep
        self.coarseStep = coarseStep
    }

    public var isEmpty: Bool {
        dial == nil && nudgeDown == nil && nudgeUp == nil && normalStep == nil && coarseStep == nil
    }

    /// Every field a stored slot may have. `nudgeRequiresLayerKey` is read
    /// from older files only (true turns both Nudge Keys into combos) and is
    /// never written.
    fileprivate enum CodingKeys: String, CodingKey, CaseIterable {
        case dial, nudgeDown, nudgeUp, normalStep, coarseStep
        case nudgeRequiresLayerKey
    }

    public init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        dial = try c.decodeIfPresent(KeyChord.self, forKey: .dial)
        nudgeDown = try c.decodeIfPresent(KeyChord.self, forKey: .nudgeDown)
        nudgeUp = try c.decodeIfPresent(KeyChord.self, forKey: .nudgeUp)
        normalStep = try c.decodeIfPresent(Double.self, forKey: .normalStep)
        coarseStep = try c.decodeIfPresent(Double.self, forKey: .coarseStep)
        if try c.decodeIfPresent(Bool.self, forKey: .nudgeRequiresLayerKey) == true {
            nudgeDown = nudgeDown?.asCombo
            nudgeUp = nudgeUp?.asCombo
        }
    }

    public func encode(to encoder: Encoder) throws {
        var c = encoder.container(keyedBy: CodingKeys.self)
        try c.encodeIfPresent(dial, forKey: .dial)
        try c.encodeIfPresent(nudgeDown, forKey: .nudgeDown)
        try c.encodeIfPresent(nudgeUp, forKey: .nudgeUp)
        try c.encodeIfPresent(normalStep, forKey: .normalStep)
        try c.encodeIfPresent(coarseStep, forKey: .coarseStep)
    }
}

// MARK: - Command keys

public struct MacroStep: Codable, Sendable, Hashable {
    public static let delayRange = 0.0...10.0

    public var action: CommandKey.Action
    /// Pause after this step, in seconds.
    public var delay: Double

    public init(action: CommandKey.Action, delay: Double = 0) {
        self.action = action
        self.delay = delay
    }
}

/// A key that runs a Lightroom command, a preset, a shortcut, or a macro.
public struct CommandKey: Codable, Sendable, Hashable, Identifiable {
    public enum Scope: String, Codable, Sendable, CaseIterable {
        case develop, library, any

        public func includes(_ scope: ParameterScope) -> Bool {
            switch self {
            case .any: true
            case .develop: scope == .develop
            case .library: scope == .library
            }
        }
    }

    public enum Action: Codable, Sendable, Hashable {
        /// A protocol command name (see docs/protocol-v3.md) with an optional argument.
        case command(name: String, argument: String?)
        /// A Develop preset, by UUID; the name is for display.
        case preset(uuid: String, name: String)
        /// Lightroom's own keyboard shortcut, sent by the companion.
        case shortcut(KeyChord)
        case macro([MacroStep])
    }

    public static let maximumMacroSteps = 50
    public static let maximumTitleLength = 80

    public var id: UUID
    public var scope: Scope
    /// May be a combo: then it runs only while the Combo Key is held.
    public var chord: KeyChord
    public var group: String
    public var title: String
    public var action: Action

    public init(
        id: UUID = UUID(),
        scope: Scope,
        chord: KeyChord,
        group: String = "Lightroom",
        title: String,
        action: Action
    ) {
        self.id = id
        self.scope = scope
        self.chord = chord
        self.group = group
        self.title = title
        self.action = action
    }

    /// Every field a stored Command Key may have. `requiresLayerKey` is read
    /// from older files only (true turns the key into a combo) and is never
    /// written.
    fileprivate enum CodingKeys: String, CodingKey, CaseIterable {
        case id, scope, chord, group, title, action
        case requiresLayerKey
    }

    public init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        id = try c.decode(UUID.self, forKey: .id)
        scope = try c.decode(Scope.self, forKey: .scope)
        chord = try c.decode(KeyChord.self, forKey: .chord)
        group = try c.decode(String.self, forKey: .group)
        title = try c.decode(String.self, forKey: .title)
        action = try c.decode(Action.self, forKey: .action)
        if try c.decodeIfPresent(Bool.self, forKey: .requiresLayerKey) == true {
            chord = chord.asCombo
        }
    }

    public func encode(to encoder: Encoder) throws {
        var c = encoder.container(keyedBy: CodingKeys.self)
        try c.encode(id, forKey: .id)
        try c.encode(scope, forKey: .scope)
        try c.encode(chord, forKey: .chord)
        try c.encode(group, forKey: .group)
        try c.encode(title, forKey: .title)
        try c.encode(action, forKey: .action)
    }
}

// MARK: - System keys

public struct SystemKeys: Codable, Sendable, Hashable {
    public enum Role: String, Codable, Sendable, CaseIterable {
        case comboKey, paletteKey, keyReferenceToggle, pauseToggle
    }

    /// Hold it and press another key for a combo ("Tab + K").
    public var comboKey: KeyChord?
    /// Hold to open the Palette (Develop).
    public var paletteKey: KeyChord?
    public var keyReferenceToggle: KeyChord?
    public var pauseToggle: KeyChord?

    /// Defaults: Tab is the Combo Key, the key left of 1 opens the Palette
    /// (key code 50 on ANSI keyboards; `Keymap.standardLayout(isoKeyboard:)`
    /// uses 10, the same spot on ISO keyboards, where 50 is Lightroom's
    /// Before/After key next to the left Shift), the combo
    /// Tab + H ("help") shows the Key Reference, and Control-Esc pauses. A
    /// quick tap of Tab still reaches Lightroom; the Palette key never does (a
    /// tap leaves the Palette open until the next press).
    public init(
        comboKey: KeyChord? = .tab,
        paletteKey: KeyChord? = .grave,
        keyReferenceToggle: KeyChord? = .comboH,
        pauseToggle: KeyChord? = .controlEscape
    ) {
        self.comboKey = comboKey
        self.paletteKey = paletteKey
        self.keyReferenceToggle = keyReferenceToggle
        self.pauseToggle = pauseToggle
    }

    public func chord(for role: Role) -> KeyChord? {
        switch role {
        case .comboKey: comboKey
        case .paletteKey: paletteKey
        case .keyReferenceToggle: keyReferenceToggle
        case .pauseToggle: pauseToggle
        }
    }

    /// Cleared keys are written as null so "disabled" survives a round trip;
    /// keys missing from older files take the defaults. `layerKey` is the
    /// Combo Key's name in older files: read when `comboKey` is missing,
    /// never written.
    fileprivate enum CodingKeys: String, CodingKey, CaseIterable {
        case comboKey, paletteKey, keyReferenceToggle, pauseToggle
        case layerKey
    }

    public init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        let defaults = SystemKeys()
        func read(_ key: CodingKeys, _ fallback: KeyChord?) throws -> KeyChord? {
            guard c.contains(key) else { return fallback }
            if try c.decodeNil(forKey: key) { return nil }
            return try c.decode(KeyChord.self, forKey: key)
        }
        comboKey = try read(.comboKey, read(.layerKey, defaults.comboKey))
        paletteKey = try read(.paletteKey, defaults.paletteKey)
        keyReferenceToggle = try read(.keyReferenceToggle, defaults.keyReferenceToggle)
        pauseToggle = try read(.pauseToggle, defaults.pauseToggle)
    }

    public func encode(to encoder: Encoder) throws {
        var c = encoder.container(keyedBy: CodingKeys.self)
        func write(_ value: KeyChord?, _ key: CodingKeys) throws {
            if let value { try c.encode(value, forKey: key) } else { try c.encodeNil(forKey: key) }
        }
        try write(comboKey, .comboKey)
        try write(paletteKey, .paletteKey)
        try write(keyReferenceToggle, .keyReferenceToggle)
        try write(pauseToggle, .pauseToggle)
    }
}

// MARK: - Palette

public struct PaletteEntry: Codable, Sendable, Hashable, Identifiable {
    public enum Kind: String, Codable, Sendable { case preset, tool }

    public var kind: Kind
    /// Preset UUID, or the protocol command name for a tool.
    public var id: String
    /// Optional command argument (e.g. "aiSelection/subject").
    public var argument: String?
    public var title: String

    public init(kind: Kind, id: String, argument: String? = nil, title: String) {
        self.kind = kind
        self.id = id
        self.argument = argument
        self.title = title
    }
}

public struct PaletteOptions: Codable, Sendable, Hashable {
    public var keepVisible: Bool
    /// Ordered favourites shown in the menu.
    public var favourites: [PaletteEntry]

    public init(keepVisible: Bool = false, favourites: [PaletteEntry] = []) {
        self.keepVisible = keepVisible
        self.favourites = favourites
    }

    /// Older files may also carry `enabled`, `compactRows` and `presetSource`:
    /// all ignored. The Palette is on whenever it has a key, and presets
    /// always follow Lightroom's own preset storage.
    private enum CodingKeys: String, CodingKey {
        case keepVisible, favourites
    }

    public init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        let d = PaletteOptions()
        keepVisible = try c.decodeIfPresent(Bool.self, forKey: .keepVisible) ?? d.keepVisible
        favourites = try c.decodeIfPresent([PaletteEntry].self, forKey: .favourites) ?? d.favourites
    }
}

// MARK: - Input options

public struct InputOptions: Codable, Sendable, Hashable {
    public static let dragSensitivityRange = 1.0...30.0
    public static let trackpadPointsPerStepRange = ScrollStepAccumulator.pointsPerStepRange

    public var invertWheel: Bool
    /// Swap the normal and coarse (Shift) steps.
    public var swapNormalAndCoarse: Bool
    public var trackpadPointsPerStep: Double
    /// Drag adjusts only while the left button is down.
    public var requireMouseDown: Bool
    /// Higher means less mouse travel per step: points per step = 31 − sensitivity.
    public var dragSensitivity: Double
    public var returnCursorAfterRelease: Bool
    public var doubleClickReset: Bool
    public var middleClickReset: Bool
    /// Mouse back / forward buttons go to the previous / next photo.
    public var mouseBackForward: Bool
    /// With a mask selected, Dial Keys and Nudge Keys move the mask's sliders.
    public var followSelectedMask: Bool
    /// Reveal the matching Lightroom panel when a gesture starts.
    public var revealPanels: Bool
    /// ← / → move the held Dial Key's slider one step.
    public var arrowKeysAdjust: Bool

    public init(
        invertWheel: Bool = false,
        swapNormalAndCoarse: Bool = false,
        trackpadPointsPerStep: Double = ScrollStepAccumulator.pointsPerStep,
        requireMouseDown: Bool = true,
        dragSensitivity: Double = 25,
        returnCursorAfterRelease: Bool = true,
        doubleClickReset: Bool = true,
        middleClickReset: Bool = true,
        mouseBackForward: Bool = true,
        followSelectedMask: Bool = true,
        revealPanels: Bool = false,
        arrowKeysAdjust: Bool = true
    ) {
        self.invertWheel = invertWheel
        self.swapNormalAndCoarse = swapNormalAndCoarse
        self.trackpadPointsPerStep = trackpadPointsPerStep
        self.requireMouseDown = requireMouseDown
        self.dragSensitivity = dragSensitivity
        self.returnCursorAfterRelease = returnCursorAfterRelease
        self.doubleClickReset = doubleClickReset
        self.middleClickReset = middleClickReset
        self.mouseBackForward = mouseBackForward
        self.followSelectedMask = followSelectedMask
        self.revealPanels = revealPanels
        self.arrowKeysAdjust = arrowKeysAdjust
    }

    /// Horizontal mouse travel, in points, for one drag step.
    public var dragPointsPerStep: Double { 31 - dragSensitivity }

    private enum CodingKeys: String, CodingKey {
        case invertWheel, swapNormalAndCoarse, trackpadPointsPerStep, requireMouseDown, dragSensitivity,
             returnCursorAfterRelease, doubleClickReset, middleClickReset, mouseBackForward,
             followSelectedMask, revealPanels, arrowKeysAdjust
    }

    public init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        let d = InputOptions()
        func bool(_ key: CodingKeys, _ fallback: Bool) throws -> Bool {
            try c.decodeIfPresent(Bool.self, forKey: key) ?? fallback
        }
        invertWheel = try bool(.invertWheel, d.invertWheel)
        swapNormalAndCoarse = try bool(.swapNormalAndCoarse, d.swapNormalAndCoarse)
        trackpadPointsPerStep = try c.decodeIfPresent(Double.self, forKey: .trackpadPointsPerStep) ?? d.trackpadPointsPerStep
        requireMouseDown = try bool(.requireMouseDown, d.requireMouseDown)
        dragSensitivity = try c.decodeIfPresent(Double.self, forKey: .dragSensitivity) ?? d.dragSensitivity
        returnCursorAfterRelease = try bool(.returnCursorAfterRelease, d.returnCursorAfterRelease)
        doubleClickReset = try bool(.doubleClickReset, d.doubleClickReset)
        middleClickReset = try bool(.middleClickReset, d.middleClickReset)
        mouseBackForward = try bool(.mouseBackForward, d.mouseBackForward)
        followSelectedMask = try bool(.followSelectedMask, d.followSelectedMask)
        revealPanels = try bool(.revealPanels, d.revealPanels)
        arrowKeysAdjust = try bool(.arrowKeysAdjust, d.arrowKeysAdjust)
    }
}

// MARK: - Keymap

/// Everything the user assigns: per-scope parameter keys, command keys,
/// system keys, Palette and input options. Persisted as format 3.
public struct Keymap: Codable, Sendable, Hashable {
    public static let currentFormat = 3

    public var format: Int
    /// Develop slots keyed by parameter id.
    public var develop: [String: ParameterSlot]
    /// Library Quick Develop slots keyed by parameter id.
    public var library: [String: ParameterSlot]
    public var commandKeys: [CommandKey]
    public var systemKeys: SystemKeys
    public var palette: PaletteOptions
    public var input: InputOptions

    public init(
        develop: [String: ParameterSlot] = [:],
        library: [String: ParameterSlot] = [:],
        commandKeys: [CommandKey] = [],
        systemKeys: SystemKeys = SystemKeys(),
        palette: PaletteOptions = PaletteOptions(),
        input: InputOptions = InputOptions()
    ) {
        self.format = Self.currentFormat
        self.develop = develop
        self.library = library
        self.commandKeys = commandKeys
        self.systemKeys = systemKeys
        self.palette = palette
        self.input = input
    }

    fileprivate enum CodingKeys: String, CodingKey, CaseIterable {
        case format, develop, library, commandKeys, systemKeys, palette, input
    }

    public init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        format = try c.decode(Int.self, forKey: .format)
        develop = try c.decodeIfPresent([String: ParameterSlot].self, forKey: .develop) ?? [:]
        library = try c.decodeIfPresent([String: ParameterSlot].self, forKey: .library) ?? [:]
        commandKeys = try c.decodeIfPresent([CommandKey].self, forKey: .commandKeys) ?? []
        systemKeys = try c.decodeIfPresent(SystemKeys.self, forKey: .systemKeys) ?? SystemKeys()
        palette = try c.decodeIfPresent(PaletteOptions.self, forKey: .palette) ?? PaletteOptions()
        input = try c.decodeIfPresent(InputOptions.self, forKey: .input) ?? InputOptions()
    }

    // MARK: Slots

    public func slots(for scope: ParameterScope) -> [String: ParameterSlot] {
        switch scope {
        case .develop: develop
        case .library: library
        }
    }

    public func slot(for key: ParameterKey) -> ParameterSlot? {
        slots(for: key.scope)[key.id]
    }

    /// Stores a slot; an empty slot is removed.
    public mutating func setSlot(_ slot: ParameterSlot?, for key: ParameterKey) {
        let value = (slot?.isEmpty ?? true) ? nil : slot
        switch key.scope {
        case .develop: develop[key.id] = value
        case .library: library[key.id] = value
        }
    }

    public mutating func updateSlot(for key: ParameterKey, _ change: (inout ParameterSlot) -> Void) {
        var slot = self.slot(for: key) ?? ParameterSlot()
        change(&slot)
        setSlot(slot, for: key)
    }

    /// Effective steps: user override or catalog default.
    public func steps(for key: ParameterKey) -> (normal: Double, coarse: Double)? {
        guard let descriptor = ParameterCatalog.descriptor(for: key) else { return nil }
        let slot = self.slot(for: key)
        return (slot?.normalStep ?? descriptor.normalStep, slot?.coarseStep ?? descriptor.coarseStep)
    }

    public mutating func clearDialKeys(in scope: ParameterScope) {
        for id in slots(for: scope).keys { updateSlot(for: ParameterKey(scope: scope, id: id)) { $0.dial = nil } }
    }

    public mutating func clearNudgeKeys(in scope: ParameterScope) {
        for id in slots(for: scope).keys {
            updateSlot(for: ParameterKey(scope: scope, id: id)) {
                $0.nudgeDown = nil
                $0.nudgeUp = nil
            }
        }
    }

    /// Command keys that apply in a scope.
    public func commandKeys(in scope: ParameterScope) -> [CommandKey] {
        commandKeys.filter { $0.scope.includes(scope) }
    }

    // MARK: Masks

    /// Develop parameter → the selected mask's equivalent (Lightroom 16 masking panel).
    public static let localParameterMap: [String: String] = [
        "Exposure": "local_Exposure", "Contrast": "local_Contrast",
        "Highlights": "local_Highlights", "Shadows": "local_Shadows",
        "Whites": "local_Whites", "Blacks": "local_Blacks",
        "Temperature": "local_Temperature", "Tint": "local_Tint",
        "Texture": "local_Texture", "Clarity": "local_Clarity",
        "Dehaze": "local_Dehaze", "Saturation": "local_Saturation",
        "Sharpness": "local_Sharpness", "LuminanceSmoothing": "local_LuminanceNoise",
        "GrainAmount": "local_Grain", "DefringePurpleAmount": "local_Defringe",
        "PresetAmount": "local_Amount", "local_Amount": "local_Amount",
    ]

    /// The mask parameter a develop key controls while a mask is selected,
    /// or nil when the parameter has no mask equivalent.
    public static func localKey(for key: ParameterKey) -> ParameterKey? {
        guard key.scope == .develop else { return nil }
        if key.id.hasPrefix("local_") { return key }
        guard let local = localParameterMap[key.id] else { return nil }
        return ParameterKey(scope: .develop, id: local)
    }

    // MARK: Persistence

    public func encoded() throws -> Data {
        let encoder = JSONEncoder()
        encoder.outputFormatting = [.sortedKeys]
        return try encoder.encode(self)
    }

    /// Decodes a stored keymap. Strict on purpose: an unknown format, or a
    /// top-level, slot, Command Key or system key field this build does not
    /// know, throws instead of silently loading empty key slots (callers then
    /// fall back to the standard layout). Fields of older files that this
    /// build still reads (the Layer Key flags, now combos) are accepted.
    public static func decodeStored(_ data: Data) throws -> Keymap {
        let decoder = JSONDecoder()
        guard let format = (try? decoder.decode(FormatProbe.self, from: data))?.format else {
            throw KeymapError.unsupportedFormat(0)
        }
        guard format == currentFormat else { throw KeymapError.unsupportedFormat(format) }
        try checkKnownFields(data)
        let keymap = try decoder.decode(Keymap.self, from: data)
        try keymap.validate()
        return keymap
    }

    private static func checkKnownFields(_ data: Data) throws {
        guard let root = try JSONSerialization.jsonObject(with: data) as? [String: Any] else {
            throw KeymapError.unknownFields(["<root>"])
        }
        var unknown = Set(root.keys).subtracting(CodingKeys.allCases.map(\.stringValue))
        for scope in [CodingKeys.develop, .library] {
            guard let slots = root[scope.stringValue] as? [String: Any] else { continue }
            for case let slot as [String: Any] in slots.values {
                unknown.formUnion(Set(slot.keys).subtracting(ParameterSlot.CodingKeys.allCases.map(\.stringValue)))
            }
        }
        if let commandKeys = root[CodingKeys.commandKeys.stringValue] as? [Any] {
            for case let commandKey as [String: Any] in commandKeys {
                unknown.formUnion(Set(commandKey.keys).subtracting(CommandKey.CodingKeys.allCases.map(\.stringValue)))
            }
        }
        if let systemKeys = root[CodingKeys.systemKeys.stringValue] as? [String: Any] {
            unknown.formUnion(Set(systemKeys.keys).subtracting(SystemKeys.CodingKeys.allCases.map(\.stringValue)))
        }
        guard unknown.isEmpty else { throw KeymapError.unknownFields(unknown.sorted()) }
    }

    /// How a chord reads with this keymap's Combo Key: "Q", "Tab + K".
    public func displayText(for chord: KeyChord) -> String {
        chord.displayText(comboKey: systemKeys.comboKey)
    }

    private struct FormatProbe: Decodable { let format: Int? }
}

// MARK: - Defaults

extension Keymap {
    private static func dialSlots(_ pairs: [(String, Int64)]) -> [String: ParameterSlot] {
        Dictionary(uniqueKeysWithValues: pairs.map { ($0.0, ParameterSlot(dial: KeyChord($0.1))) })
    }

    /// Lumerist's standard layout, built on Capture One's Speed Edit keys and
    /// translated to Lightroom Classic. The left hand stays on the keyboard,
    /// the right hand on the mouse:
    ///
    ///     1 Temp     2 Tint      3 Vibrance  4 Texture   5 Clarity  6 Dehaze
    ///     Q Exposure W Contrast  E Saturation R Straighten T Crop zoom
    ///     A Highl.   S Shadows   D Whites    F Blacks
    ///                X Shadows   C Darks     V Lights    B Highlights  (tone curve)
    ///
    /// 1 2, Q W and A S D F are Capture One's own keys. R straightens because
    /// a tap on R opens Lightroom's Crop tool, and T, next to it, zooms the
    /// crop. Vibrance, Texture, Clarity and Dehaze run along the number row,
    /// with Saturation on E. Lightroom has no Levels, so the parametric tone
    /// curve sits where Capture One keeps them, dark to light from X to B.
    /// P and M are the preset and mask amounts. Library uses the same keys for
    /// its Quick Develop controls.
    ///
    /// Four combos are preset in Develop (hold Tab): Tab + A Auto Tone,
    /// Tab + W Auto White Balance, Tab + R Reset all, and Tab + Q Lightroom's
    /// own ⌥⌘V, Paste Settings from Previous (the SDK has no command for it).
    /// They have fixed ids, so they stay the same keys across launches.
    public static let standardLayout: Keymap = {
        let develop = dialSlots([
            ("Temperature", 18), ("Tint", 19), ("Vibrance", 20), ("Texture", 21), ("Clarity", 23), ("Dehaze", 22),
            ("Exposure", 12), ("Contrast", 13), ("Saturation", 14), ("straightenAngle", 15), ("lumerist.cropZoom", 17),
            ("Highlights", 0), ("Shadows", 1), ("Whites", 2), ("Blacks", 3),
            ("ParametricShadows", 7), ("ParametricDarks", 8), ("ParametricLights", 9), ("ParametricHighlights", 11),
            ("PresetAmount", 35), ("local_Amount", 46),
        ])
        let library = dialSlots([
            ("Temperature", 18), ("Tint", 19), ("Vibrance", 20), ("Texture", 21), ("Clarity", 23),
            ("Exposure", 12), ("Contrast", 13), ("Saturation", 14),
            ("Highlights", 0), ("Shadows", 1), ("Whites", 2), ("Blacks", 3),
        ])
        func combo(_ id: String, _ keyCode: Int64, _ title: String, _ action: CommandKey.Action) -> CommandKey {
            CommandKey(id: UUID(uuidString: id)!, scope: .develop, chord: KeyChord(keyCode, [.combo]),
                       title: title, action: action)
        }
        let combos = [
            combo("8022E100-5E15-408A-A02C-9796E4B60CBA", 0, "Auto Tone",
                  .command(name: "autoTone", argument: nil)),
            combo("84C5337F-EA7E-44F3-97CE-6772C2231EB4", 13, "Auto White Balance",
                  .command(name: "autoWhiteBalance", argument: nil)),
            combo("F0851B74-86AE-4988-BE39-01DF8EFAFE25", 15, "Reset all",
                  .command(name: "resetAll", argument: nil)),
            combo("787E1A28-ED89-40D3-9855-6049A5F3F639", 12, "Paste settings from previous",
                  .shortcut(KeyChord(9, [.option, .command]))),
        ]
        return Keymap(develop: develop, library: library, commandKeys: combos)
    }()

    /// The standard layout for the keyboard in use: the Palette Key is the
    /// key left of 1, key code 50 on ANSI keyboards and 10 on ISO ones.
    public static func standardLayout(isoKeyboard: Bool) -> Keymap {
        var keymap = standardLayout
        if isoKeyboard { keymap.systemKeys.paletteKey = .isoSection }
        return keymap
    }
}

// MARK: - Validation and conflicts

public enum KeymapError: Error, Equatable, LocalizedError, Sendable {
    case unsupportedFormat(Int)
    case unknownFields([String])
    case unknownParameter(ParameterKey)
    case invalidStep(ParameterKey)
    case invalidOption(String)
    case invalidCommandKey(String)
    case invalidCombo(ComboMisuse)
    case conflicts([KeyConflict])

    public var errorDescription: String? {
        switch self {
        case .unsupportedFormat(let format): "Unsupported keymap format: \(format)."
        case .unknownFields(let fields): "Unknown keymap fields: \(fields.joined(separator: ", "))."
        case .unknownParameter(let key): "Unknown parameter \(key)."
        case .invalidStep(let key): "Steps for \(key) are outside the allowed range."
        case .invalidOption(let name): "The \(name) setting is out of range."
        case .invalidCommandKey(let reason): "Invalid command key: \(reason)."
        case .invalidCombo(let misuse): misuse.message
        case .conflicts(let conflicts): conflicts.first?.message ?? "Conflicting keys."
        }
    }
}

/// Places where a combo (a chord with `.combo`) is not allowed.
public enum ComboMisuse: Sendable, Hashable {
    /// A Dial Key is held on its own while you scroll.
    case dialKey(ParameterKey)
    /// Lumerist sends a Lightroom shortcut as a plain key press.
    case lightroomShortcut
    case systemKey(SystemKeys.Role)

    public var message: String {
        switch self {
        case .dialKey:
            "A Dial Key can't be a combo: you hold it on its own while you scroll. Combos work for Nudge Keys and Command Keys."
        case .lightroomShortcut:
            "A Lightroom shortcut can't be a combo: Lumerist sends it to Lightroom as a plain key press."
        case .systemKey:
            "Only the Key Reference toggle can be a combo. Press a single key, with modifiers if you like."
        }
    }
}

/// Where a chord is used, for conflict reports and the key overview.
public enum KeyUsage: Sendable, Hashable {
    case dial(ParameterKey)
    case nudgeDown(ParameterKey)
    case nudgeUp(ParameterKey)
    case action(UUID, title: String)
    case system(SystemKeys.Role)

    var isNudge: Bool {
        switch self {
        case .nudgeDown, .nudgeUp: true
        default: false
        }
    }
}

public struct KeyConflict: Sendable, Hashable {
    public enum Severity: String, Sendable { case error, warning }

    public let severity: Severity
    /// The chord in conflict; combos keep their `.combo` modifier.
    public let chord: KeyChord
    /// nil for conflicts among system keys.
    public let scope: ParameterScope?
    public let usages: [KeyUsage]
    public let message: String
}

extension Keymap {
    public func validate() throws {
        guard format == Self.currentFormat else { throw KeymapError.unsupportedFormat(format) }
        for scope in ParameterScope.allCases {
            for (id, slot) in slots(for: scope) {
                let key = ParameterKey(scope: scope, id: id)
                guard let descriptor = ParameterCatalog.descriptor(for: key) else {
                    throw KeymapError.unknownParameter(key)
                }
                if slot.dial?.isCombo == true { throw KeymapError.invalidCombo(.dialKey(key)) }
                for step in [slot.normalStep, slot.coarseStep].compactMap({ $0 }) {
                    guard step.isFinite, step > 0, descriptor.stepRange.contains(step) else {
                        throw KeymapError.invalidStep(key)
                    }
                }
            }
        }
        guard InputOptions.trackpadPointsPerStepRange.contains(input.trackpadPointsPerStep) else {
            throw KeymapError.invalidOption("trackpad sensitivity")
        }
        guard InputOptions.dragSensitivityRange.contains(input.dragSensitivity) else {
            throw KeymapError.invalidOption("drag sensitivity")
        }
        // Only the Key Reference toggle may be a combo: the other system keys
        // are held on their own or must work everywhere.
        for role in SystemKeys.Role.allCases where role != .keyReferenceToggle
            && systemKeys.chord(for: role)?.isCombo == true {
            throw KeymapError.invalidCombo(.systemKey(role))
        }
        for action in commandKeys {
            let title = action.title.trimmingCharacters(in: .whitespacesAndNewlines)
            guard !title.isEmpty, title.count <= CommandKey.maximumTitleLength else {
                throw KeymapError.invalidCommandKey("a title of 1–\(CommandKey.maximumTitleLength) characters is required")
            }
            try Self.validate(action.action, nested: false)
        }
        let errors = conflicts().filter { $0.severity == .error }
        if !errors.isEmpty { throw KeymapError.conflicts(errors) }
    }

    private static func validate(_ action: CommandKey.Action, nested: Bool) throws {
        switch action {
        case .command(let name, _):
            guard !name.isEmpty else { throw KeymapError.invalidCommandKey("empty command") }
        case .preset(let uuid, _):
            guard !uuid.isEmpty else { throw KeymapError.invalidCommandKey("missing preset") }
        case .shortcut(let chord):
            guard !chord.isCombo else { throw KeymapError.invalidCombo(.lightroomShortcut) }
        case .macro(let steps):
            guard !nested else { throw KeymapError.invalidCommandKey("macros cannot contain macros") }
            guard !steps.isEmpty, steps.count <= CommandKey.maximumMacroSteps else {
                throw KeymapError.invalidCommandKey("a macro needs 1–\(CommandKey.maximumMacroSteps) steps")
            }
            for step in steps {
                guard step.delay.isFinite, MacroStep.delayRange.contains(step.delay) else {
                    throw KeymapError.invalidCommandKey("macro delays must be 0–10 s")
                }
                try validate(step.action, nested: true)
            }
        }
    }

    /// Every problem, errors first. Allowed on purpose: one Dial Key and
    /// one tap Command Key on the same chord (hold adjusts, tap runs the action).
    /// A combo is simply another chord: "Tab + K" and "K" never conflict.
    public func conflicts() -> [KeyConflict] {
        var result: [KeyConflict] = []
        let reservedTypingKeys: Set<Int64> = [36, 49, 51, 53] // Return, Space, Delete, Esc
        func name(_ chord: KeyChord) -> String { displayText(for: chord) }

        // System keys among themselves.
        var systemUsages: [KeyChord: [KeyUsage]] = [:]
        for role in SystemKeys.Role.allCases {
            if let chord = systemKeys.chord(for: role) { systemUsages[chord, default: []].append(.system(role)) }
        }
        for (chord, usages) in systemUsages where usages.count > 1 {
            result.append(KeyConflict(severity: .error, chord: chord, scope: nil, usages: usages,
                                      message: "\(name(chord)) is used by more than one system key."))
        }
        if let toggle = systemKeys.keyReferenceToggle, toggle.isCombo {
            if systemKeys.comboKey == nil {
                result.append(KeyConflict(severity: .warning, chord: toggle, scope: nil, usages: [.system(.keyReferenceToggle)],
                                          message: "\(name(toggle)) does nothing until you set a Combo Key."))
            } else if toggle.keyCode == systemKeys.comboKey?.keyCode {
                result.append(KeyConflict(severity: .error, chord: toggle, scope: nil,
                                          usages: [.system(.keyReferenceToggle), .system(.comboKey)],
                                          message: "\(name(toggle)) can't work: a combo can't use the Combo Key itself."))
            }
        }

        for scope in ParameterScope.allCases {
            var byChord: [KeyChord: [KeyUsage]] = [:]
            for (id, slot) in slots(for: scope).sorted(by: { $0.key < $1.key }) {
                let key = ParameterKey(scope: scope, id: id)
                if let chord = slot.dial { byChord[chord, default: []].append(.dial(key)) }
                if let chord = slot.nudgeDown { byChord[chord, default: []].append(.nudgeDown(key)) }
                if let chord = slot.nudgeUp { byChord[chord, default: []].append(.nudgeUp(key)) }
            }
            for action in commandKeys(in: scope) {
                byChord[action.chord, default: []].append(.action(action.id, title: action.title))
            }

            for (chord, usages) in byChord.sorted(by: { name($0.key) < name($1.key) }) {
                let text = name(chord)
                let dials = usages.filter { if case .dial = $0 { true } else { false } }.count
                let nudges = usages.filter(\.isNudge).count
                let actions = usages.filter { if case .action = $0 { true } else { false } }.count
                var problem: String?
                if dials > 1 { problem = "\(text) is the Dial Key of more than one slider." }
                else if nudges > 1 { problem = "\(text) is used by more than one Nudge Key." }
                else if nudges > 0 && (dials > 0 || actions > 0) { problem = "\(text) is both a Nudge Key and another key." }
                else if actions > 1 { problem = "\(text) runs more than one action." }
                if let problem {
                    result.append(KeyConflict(severity: .error, chord: chord, scope: scope, usages: usages, message: problem))
                }
                if let roles = systemUsages[chord] {
                    result.append(KeyConflict(severity: .error, chord: chord, scope: scope, usages: roles + usages,
                                              message: "\(text) is a system key and cannot be assigned here."))
                }
                if chord.isCombo {
                    result += comboProblems(chord, text: text, scope: scope, usages: usages)
                } else if reservedTypingKeys.contains(chord.keyCode) || chord.usesReservedModifiers {
                    result.append(KeyConflict(severity: .warning, chord: chord, scope: scope, usages: usages,
                                              message: "\(text) is also used for typing or by Lightroom and macOS."))
                }
            }
        }
        return result.sorted { ($0.severity == .error ? 0 : 1) < ($1.severity == .error ? 0 : 1) }
    }

    /// A combo can't use the Combo Key itself (that key is already held) or
    /// the pause key (which always pauses); without a Combo Key it does nothing.
    private func comboProblems(_ chord: KeyChord, text: String, scope: ParameterScope,
                               usages: [KeyUsage]) -> [KeyConflict] {
        guard let comboKey = systemKeys.comboKey else {
            return [KeyConflict(severity: .warning, chord: chord, scope: scope, usages: usages,
                                message: "\(text) does nothing until you set a Combo Key.")]
        }
        if chord.keyCode == comboKey.keyCode {
            return [KeyConflict(severity: .error, chord: chord, scope: scope, usages: usages + [.system(.comboKey)],
                                message: "\(text) can't work: a combo can't use the Combo Key itself.")]
        }
        if let pause = systemKeys.pauseToggle, chord.withoutCombo == pause {
            return [KeyConflict(severity: .error, chord: chord, scope: scope, usages: usages + [.system(.pauseToggle)],
                                message: "\(text) can't work: \(pause.displayText) always pauses Lumerist.")]
        }
        return []
    }
}

// MARK: - Key overview

public struct AssignmentSummary: Sendable {
    public enum Sort: String, Sendable, CaseIterable { case parameter, key, section }

    public struct Entry: Sendable, Hashable {
        public let descriptor: ParameterDescriptor
        public let slot: ParameterSlot
    }

    public let scope: ParameterScope
    public let dialKeys: [Entry]
    public let nudgeKeys: [Entry]

    public var dialKeyCount: Int { dialKeys.count }
    /// Individual nudge key bindings (decrease and increase count separately).
    public var nudgeKeyCount: Int {
        nudgeKeys.reduce(0) { $0 + ($1.slot.nudgeDown == nil ? 0 : 1) + ($1.slot.nudgeUp == nil ? 0 : 1) }
    }
}

extension Keymap {
    public func summary(for scope: ParameterScope, sortedBy sort: AssignmentSummary.Sort = .parameter) -> AssignmentSummary {
        let order = Dictionary(uniqueKeysWithValues: ParameterCatalog.all.enumerated().map { ($0.element.key, $0.offset) })
        var entries: [AssignmentSummary.Entry] = []
        for (id, slot) in slots(for: scope) {
            guard let descriptor = ParameterCatalog.descriptor(scope: scope, id: id) else { continue }
            entries.append(.init(descriptor: descriptor, slot: slot))
        }
        func sorted(_ list: [AssignmentSummary.Entry], key: (AssignmentSummary.Entry) -> KeyChord?) -> [AssignmentSummary.Entry] {
            switch sort {
            case .parameter:
                list.sorted { $0.descriptor.label.localizedCaseInsensitiveCompare($1.descriptor.label) == .orderedAscending }
            case .key:
                list.sorted { (key($0)?.displayText ?? "") < (key($1)?.displayText ?? "") }
            case .section:
                list.sorted { (order[$0.descriptor.key] ?? 0) < (order[$1.descriptor.key] ?? 0) }
            }
        }
        return AssignmentSummary(
            scope: scope,
            dialKeys: sorted(entries.filter { $0.slot.dial != nil }, key: { $0.slot.dial }),
            nudgeKeys: sorted(entries.filter { $0.slot.nudgeDown != nil || $0.slot.nudgeUp != nil },
                            key: { $0.slot.nudgeDown ?? $0.slot.nudgeUp })
        )
    }
}
