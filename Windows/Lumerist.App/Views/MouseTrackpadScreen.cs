using System.Windows;
using System.Windows.Controls;
using Lumerist.App.Design;
using Lumerist.App.Design.Controls;
using Lumerist.Core;

namespace Lumerist.App.Views;

/// <summary>
/// How the wheel, the trackpad, dragging and the mouse buttons move a slider
/// while a Dial Key is held (MouseTrackpadScreen.swift).
/// </summary>
internal sealed class MouseTrackpadScreen(ScreenContext context) : Screen(context)
{
    protected override FrameworkElement Build()
    {
        var input = Controller.Keymap.Input;
        return Layout.VStack(Theme.Space.XL,
            Surfaces.ScreenHeader("Mouse & Trackpad", "with a Dial Key held"),

            Surfaces.SettingsSection("Wheel", Icons.Wheel, "A wheel that clicks as it turns moves one step per click.",
                Surfaces.SettingRow("Scroll up", "Which way the value goes when you scroll up while holding a Dial Key.",
                    Picker("Scroll up", SettingsChoices.AllWheelDirections, input.InvertWheel ? WheelDirection.Lowers : WheelDirection.Raises,
                        SettingsChoices.Title, (keymap, choice) => keymap.Input.InvertWheel = choice == WheelDirection.Lowers)),
                ToggleRow("Arrow keys adjust", "With a Dial Key held, ← and → work like one click of the wheel.",
                    input.ArrowKeysAdjust, (keymap, value) => keymap.Input.ArrowKeysAdjust = value)),

            Surfaces.SettingsSection("Trackpad", Icons.Touchpad, null,
                Surfaces.SettingRow("Trackpad travel per step", "The length of swipe that counts as one step. Shorter means more sensitive.",
                    SynthControls.Slider(input.TrackpadPointsPerStep, InputOptions.TrackpadPointsPerStepMinimum, InputOptions.TrackpadPointsPerStepMaximum, 1,
                        value => $"{(int)value} pt",
                        value => Edit(keymap => keymap.Input.TrackpadPointsPerStep = Math.Round(value, MidpointRounding.AwayFromZero)),
                        "Trackpad travel per step", "More sensitive", "Less"))),

            Surfaces.SettingsSection("Dragging", Icons.Drag, null,
                Surfaces.SettingRow("Drag",
                    input.RequireMouseDown
                        ? "Hold a Dial Key, press the left button and move the mouse."
                        : "Hold a Dial Key and move the mouse; no button needed.",
                    Picker("Drag", SettingsChoices.AllDragModes, input.RequireMouseDown ? DragMode.WithButton : DragMode.Freely,
                        SettingsChoices.Title, (keymap, choice) => keymap.Input.RequireMouseDown = choice == DragMode.WithButton)),
                Surfaces.SettingRow("Drag speed", "How far the mouse travels for each step. Faster needs less travel.",
                    SynthControls.Slider(input.DragSensitivity, InputOptions.DragSensitivityMinimum, InputOptions.DragSensitivityMaximum, 1,
                        value => $"{(int)value}",
                        value => Edit(keymap => keymap.Input.DragSensitivity = Math.Round(value, MidpointRounding.AwayFromZero)),
                        "Drag speed", "Slower", "Faster")),
                ToggleRow("Return the pointer after dragging",
                    "When the drag ends, the pointer jumps back to where it began, so it never wanders off the photo.",
                    input.ReturnCursorAfterRelease, (keymap, value) => keymap.Input.ReturnCursorAfterRelease = value)),

            Surfaces.SettingsSection("Mouse buttons", Icons.Mouse, null,
                Surfaces.SettingRow("Reset",
                    "Puts the held Dial Key's slider back to its default, as double-clicking a slider does in Lightroom.",
                    Picker("Reset", SettingsChoices.AllResetClicks, SettingsChoices.ResetClickOf(input), SettingsChoices.Title,
                        (keymap, choice) =>
                        {
                            keymap.Input.DoubleClickReset = choice.DoubleClick();
                            keymap.Input.MiddleClickReset = choice.MiddleClick();
                        })),
                ToggleRow("Side buttons: previous / next photo",
                    "The back and forward buttons on the side of the mouse step through the filmstrip.",
                    input.MouseBackForward, (keymap, value) => keymap.Input.MouseBackForward = value)));
    }

    private void Edit(Action<Keymap> change)
    {
        // Slider values that did not change still arrive (a click on the thumb): nothing to save then.
        var proposed = Controller.Keymap.Clone();
        change(proposed);
        if (proposed.Input == Controller.Keymap.Input) return;
        KeymapEditor.Edit(Controller, change);
    }

    private Border ToggleRow(string title, string detail, bool value, Action<Keymap, bool> apply) =>
        Surfaces.SettingRow(title, detail, SynthControls.Toggle(value, newValue => Edit(keymap => apply(keymap, newValue)), title));

    private FrameworkElement Picker<T>(string label, IReadOnlyList<T> choices, T current, Func<T, string> title, Action<Keymap, T> apply) =>
        SynthControls.Segmented(choices, current, title, choice => Edit(keymap => apply(keymap, choice)), name: label);
}
