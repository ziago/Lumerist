namespace Lumerist.Core;

/// <summary>
/// What the user asked for with a key, wheel or mouse gesture. The app maps
/// intents to bridge requests (docs/protocol-v3.md) or to its own UI.
/// </summary>
public abstract record KeyIntent
{
    private KeyIntent() { }

    /// <summary>Change a parameter by Delta (slider units; see the catalog unit).</summary>
    public sealed record Adjust(ParameterKey Key, double Delta) : KeyIntent;

    public sealed record Reset(ParameterKey Key) : KeyIntent;

    /// <summary>A Dial Key gesture started (history grouping, overlay).</summary>
    public sealed record GestureBegan(ParameterKey Key) : KeyIntent;

    public sealed record GestureEnded(ParameterKey Key) : KeyIntent;

    public sealed record RunAction(CommandAction Action) : KeyIntent;

    public sealed record OpenPalette() : KeyIntent;

    public sealed record ClosePalette() : KeyIntent;

    public sealed record ToggleKeyReference() : KeyIntent;

    public sealed record TogglePause(bool IsPaused) : KeyIntent;

    /// <summary>Put the pointer back where the drag started.</summary>
    public sealed record RestorePointer() : KeyIntent;

    public sealed record RevealPanel(ParameterKey Key) : KeyIntent;

    /// <summary>
    /// A key was pressed while a mask is selected, but its slider has no mask
    /// equivalent; nothing was changed (the global photo is left alone).
    /// </summary>
    public sealed record UnavailableInMask(ParameterKey Key) : KeyIntent;

    /// <summary>Intents that reach Lightroom without waiting for the next update tick (all but slider changes).</summary>
    public bool IsImmediate => this is not Adjust;
}
