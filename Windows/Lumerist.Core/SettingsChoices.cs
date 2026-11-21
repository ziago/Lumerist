using System.Globalization;

namespace Lumerist.Core;

// The named choices of the settings screens (General, Mouse & Trackpad,
// Floating panels): each folds a stored value into the segments the user
// sees, as on the Mac.

/// <summary>
/// Three named update rates instead of a raw number (LightroomSection.swift).
/// A rate saved by an older version shows as the nearest choice until the
/// user picks one.
/// </summary>
public enum Smoothness
{
    Light,
    Balanced,
    Smooth,
}

/// <summary>Wheel: which way scrolling up moves the value.</summary>
public enum WheelDirection
{
    Raises,
    Lowers,
}

/// <summary>Dragging with or without the left button held.</summary>
public enum DragMode
{
    WithButton,
    Freely,
}

/// <summary>The two reset clicks folded into one choice.</summary>
public enum ResetClick
{
    DoubleClickOnly,
    MiddleClickOnly,
    Both,
    Off,
}

/// <summary>General → Startup: what Lumerist does when Lightroom quits.</summary>
public enum QuitChoice
{
    KeepRunning,
    QuitAfterPause,
}

public static class SettingsChoices
{
    public static IReadOnlyList<Smoothness> AllSmoothness { get; } = [Smoothness.Light, Smoothness.Balanced, Smoothness.Smooth];

    public static string Title(this Smoothness smoothness) => smoothness switch
    {
        Smoothness.Light => "Light",
        Smoothness.Balanced => "Balanced",
        _ => "Smooth",
    };

    /// <summary>Updates per second, inside the coalescer's range.</summary>
    public static double Hz(this Smoothness smoothness)
    {
        var wanted = smoothness switch
        {
            Smoothness.Light => 15,
            Smoothness.Balanced => IntentCoalescer.DefaultRate,
            _ => 50,
        };
        return Math.Clamp(wanted, IntentCoalescer.RateMinimum, IntentCoalescer.RateMaximum);
    }

    /// <summary>The choice closest to <paramref name="hz"/> (the first one on a tie).</summary>
    public static Smoothness Nearest(double hz) =>
        AllSmoothness.MinBy(choice => Math.Abs(choice.Hz() - hz));

    public static IReadOnlyList<WheelDirection> AllWheelDirections { get; } = [WheelDirection.Raises, WheelDirection.Lowers];

    public static string Title(this WheelDirection direction) =>
        direction == WheelDirection.Raises ? "Raises the value" : "Lowers the value";

    public static IReadOnlyList<DragMode> AllDragModes { get; } = [DragMode.WithButton, DragMode.Freely];

    public static string Title(this DragMode mode) => mode == DragMode.WithButton ? "With the button held" : "Freely";

    public static IReadOnlyList<ResetClick> AllResetClicks { get; } =
        [ResetClick.DoubleClickOnly, ResetClick.MiddleClickOnly, ResetClick.Both, ResetClick.Off];

    public static ResetClick ResetClickOf(InputOptions input) => (input.DoubleClickReset, input.MiddleClickReset) switch
    {
        (true, true) => ResetClick.Both,
        (true, false) => ResetClick.DoubleClickOnly,
        (false, true) => ResetClick.MiddleClickOnly,
        _ => ResetClick.Off,
    };

    public static string Title(this ResetClick choice) => choice switch
    {
        ResetClick.DoubleClickOnly => "Double-click",
        ResetClick.MiddleClickOnly => "Middle-click",
        ResetClick.Both => "Both",
        _ => "Off",
    };

    public static bool DoubleClick(this ResetClick choice) => choice is ResetClick.DoubleClickOnly or ResetClick.Both;

    public static bool MiddleClick(this ResetClick choice) => choice is ResetClick.MiddleClickOnly or ResetClick.Both;

    public static IReadOnlyList<QuitChoice> AllQuitChoices { get; } = [QuitChoice.KeepRunning, QuitChoice.QuitAfterPause];

    public static string Title(this QuitChoice choice) => choice == QuitChoice.KeepRunning ? "Keep running" : "Quit after a pause";

    /// <summary>A slider value rounded to its step and kept in range (NeonSlider).</summary>
    public static double Stepped(double value, double minimum, double maximum, double? step)
    {
        if (step is { } size && size > 0) value = Math.Round(value / size, MidpointRounding.AwayFromZero) * size;
        return Math.Clamp(value, minimum, maximum);
    }

    /// <summary>
    /// The Value HUD's "Hide after" slider: its far end, just past the longest
    /// duration, means Never (the HUD stays on screen).
    /// </summary>
    public static double HideAfterNeverValue(double longestDuration) => longestDuration + 0.1;

    /// <summary>True when a "Hide after" slider value means Never.</summary>
    public static bool HideAfterIsNever(double value, double longestDuration) =>
        value >= HideAfterNeverValue(longestDuration) - 0.05;

    /// <summary>"1.2 s".</summary>
    public static string Seconds(double value) =>
        value.ToString("0.0", CultureInfo.InvariantCulture) + " s";
}
