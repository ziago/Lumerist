import SwiftUI
import LumeristCore

/// How the wheel, the trackpad, dragging and the mouse buttons move a slider
/// while a Dial Key is held.
struct MouseTrackpadScreen: View {
    let store: CommandStore

    var body: some View {
        let input = store.keymap.keymap.input
        VStack(alignment: .leading, spacing: Synth.Space.xl) {
            ScreenHeader(title: "Mouse & Trackpad", readout: "with a Dial Key held")

            SettingsSection(title: "Wheel", systemImage: "scroll", footer: "A wheel that clicks as it turns moves one step per click.") {
                SettingRow(title: "Scroll up",
                           detail: "Which way the value goes when you scroll up while holding a Dial Key.") {
                    picker("Scroll up", WheelDirection.allCases, input.invertWheel ? .lowers : .raises) {
                        $0.input.invertWheel = $1 == .lowers
                    }
                }
                toggleRow("Arrow keys adjust",
                          "With a Dial Key held, ← and → work like one click of the wheel.",
                          input.arrowKeysAdjust) { $0.input.arrowKeysAdjust = $1 }
            }

            SettingsSection(title: "Trackpad", systemImage: "rectangle.and.hand.point.up.left") {
                SettingRow(title: "Trackpad travel per step",
                           detail: "The length of swipe that counts as one step. Shorter means more sensitive.") {
                    NeonSlider(
                        value: Binding(get: { input.trackpadPointsPerStep },
                                       set: { value in edit { $0.input.trackpadPointsPerStep = value.rounded() } }),
                        range: InputOptions.trackpadPointsPerStepRange, step: 1,
                        readout: "\(Int(input.trackpadPointsPerStep)) pt", accessibilityLabel: "Trackpad travel per step",
                        lowLabel: "More sensitive", highLabel: "Less"
                    )
                }
            }

            SettingsSection(title: "Dragging", systemImage: "hand.draw") {
                SettingRow(title: "Drag",
                           detail: input.requireMouseDown
                               ? "Hold a Dial Key, press the left button and move the mouse."
                               : "Hold a Dial Key and move the mouse; no button needed.") {
                    picker("Drag", DragMode.allCases, input.requireMouseDown ? .withButton : .freely) {
                        $0.input.requireMouseDown = $1 == .withButton
                    }
                }
                SettingRow(title: "Drag speed", detail: "How far the mouse travels for each step. Faster needs less travel.") {
                    NeonSlider(
                        value: Binding(get: { input.dragSensitivity },
                                       set: { value in edit { $0.input.dragSensitivity = value.rounded() } }),
                        range: InputOptions.dragSensitivityRange, step: 1,
                        readout: "\(Int(input.dragSensitivity))", accessibilityLabel: "Drag speed",
                        lowLabel: "Slower", highLabel: "Faster"
                    )
                }
                toggleRow("Return the pointer after dragging",
                          "When the drag ends, the pointer jumps back to where it began, so it never wanders off the photo.",
                          input.returnCursorAfterRelease) { $0.input.returnCursorAfterRelease = $1 }
            }

            SettingsSection(title: "Mouse buttons", systemImage: "computermouse") {
                SettingRow(title: "Reset",
                           detail: "Puts the held Dial Key's slider back to its default, as double-clicking a slider does in Lightroom.") {
                    picker("Reset", ResetClick.allCases, ResetClick(input)) { keymap, choice in
                        keymap.input.doubleClickReset = choice.doubleClick
                        keymap.input.middleClickReset = choice.middleClick
                    }
                }
                toggleRow("Side buttons: previous / next photo",
                          "The back and forward buttons on the side of the mouse step through the filmstrip.",
                          input.mouseBackForward) { $0.input.mouseBackForward = $1 }
            }
        }
    }

    private func edit(_ change: (inout Keymap) -> Void) { store.editKeymap(change) }

    private func toggleRow(_ title: String, _ detail: String, _ value: Bool,
                           _ apply: @escaping (inout Keymap, Bool) -> Void) -> some View {
        SettingRow(title: title, detail: detail) {
            Toggle(title, isOn: Binding(get: { value }, set: { newValue in store.editKeymap { apply(&$0, newValue) } }))
                .labelsHidden()
                .toggleStyle(.neon)
        }
    }

    private func picker<Choice: SettingChoice>(_ label: String, _ choices: [Choice], _ current: Choice,
                                               _ apply: @escaping (inout Keymap, Choice) -> Void) -> some View {
        SynthSegmented(items: choices,
                       selection: Binding(get: { current },
                                          set: { choice in store.editKeymap { apply(&$0, choice) } }),
                       title: \.title)
            .fixedSize()
            .accessibilityElement(children: .contain)
            .accessibilityLabel(label)
    }
}

/// A choice shown in a segmented picker.
protocol SettingChoice: Hashable, CaseIterable {
    var title: String { get }
}

private enum WheelDirection: SettingChoice {
    case raises
    case lowers

    var title: String {
        switch self {
        case .raises: "Raises the value"
        case .lowers: "Lowers the value"
        }
    }
}

private enum DragMode: SettingChoice {
    case withButton
    case freely

    var title: String {
        switch self {
        case .withButton: "With the button held"
        case .freely: "Freely"
        }
    }
}

/// The two reset clicks folded into one choice.
private enum ResetClick: SettingChoice {
    case doubleClickOnly
    case middleClickOnly
    case both
    case off

    init(_ input: InputOptions) {
        switch (input.doubleClickReset, input.middleClickReset) {
        case (true, true): self = .both
        case (true, false): self = .doubleClickOnly
        case (false, true): self = .middleClickOnly
        case (false, false): self = .off
        }
    }

    var title: String {
        switch self {
        case .doubleClickOnly: "Double-click"
        case .middleClickOnly: "Middle-click"
        case .both: "Both"
        case .off: "Off"
        }
    }

    var doubleClick: Bool { self == .doubleClickOnly || self == .both }
    var middleClick: Bool { self == .middleClickOnly || self == .both }
}
