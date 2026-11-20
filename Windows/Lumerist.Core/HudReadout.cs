namespace Lumerist.Core;

/// <summary>The colour family of a Value HUD read-out.</summary>
public enum HudAccent
{
    /// <summary>Tone sliders.</summary>
    Teal,

    /// <summary>White balance and colour.</summary>
    Magenta,

    /// <summary>Notices.</summary>
    Neutral,

    /// <summary>Rejections.</summary>
    Error,
}

/// <summary>The small symbol before a notice's title.</summary>
public enum HudNoticeIcon
{
    Info,
    Checkmark,
    Cross,
}

/// <summary>The step a key drove: normal, or the larger one used with Shift.</summary>
public enum HudStepMode
{
    Normal,

    /// <summary>The larger step, used with Shift.</summary>
    Coarse,
}

public static class HudStepModes
{
    public static string Label(this HudStepMode mode) => mode == HudStepMode.Coarse ? "Coarse" : "Normal";
}

/// <summary>
/// What the Value HUD shows, derived from a plug-in acknowledgement or a
/// notice (the Mac's OverlayContent in ValueHUD.swift).
/// </summary>
public sealed record HudReadout
{
    private static readonly string[] ColourWords =
        ["Temperature", "Tint", "Vibrance", "Saturation", "Hue", "ColorGrade", "SplitToning", "GrayMixer"];

    public required string Title { get; init; }

    /// <summary>A mask's slider (<c>local_</c>): the title reads "Mask · …".</summary>
    public bool IsMask { get; init; }

    public string? ValueText { get; init; }

    public string? Unit { get; init; }

    public string? Detail { get; init; }

    public required HudAccent Accent { get; init; }

    public HudBar? Bar { get; init; }

    /// <summary>The range ends ("-5.00", "+5.00"), shown when the scale has no numbers.</summary>
    public (string Low, string High)? RangeText { get; init; }

    /// <summary>The key that drove the change ("Q"), shown as "[Q]".</summary>
    public string? KeyLabel { get; init; }

    /// <summary>"Normal" or "Coarse", beside the key.</summary>
    public string? StepLabel { get; init; }

    public HudNoticeIcon? NoticeIcon { get; init; }

    public string DisplayTitle => IsMask ? $"Mask · {Title}" : Title;

    /// <summary>A neutral note, e.g. "Vibrance is not available in a mask".</summary>
    public static HudReadout Notice(string text) => new()
    {
        Title = "Lumerist",
        Detail = text,
        Accent = HudAccent.Neutral,
        NoticeIcon = HudNoticeIcon.Info,
    };

    /// <summary>
    /// What to show for an answer from Lightroom, as the Mac decides it: an
    /// accepted change shows its value, a refused one a notice; answers
    /// without a parameter (commands, queries) show nothing.
    /// </summary>
    public static HudReadout? ForAck(LightroomAck ack)
    {
        ArgumentNullException.ThrowIfNull(ack);
        if (ack.Parameter is null) return null;
        return ack.Accepted ? FromAck(ack) : Notice(ack.Message ?? "Lightroom rejected the change");
    }

    /// <summary>The read-out of an acknowledgement. <c>local_</c> parameters get a "Mask ·" badge.</summary>
    public static HudReadout FromAck(LightroomAck ack)
    {
        ArgumentNullException.ThrowIfNull(ack);
        var id = ack.Parameter;
        var readout = new HudReadout
        {
            Title = Label(id, ack.Scope) ?? "Lightroom",
            IsMask = id?.StartsWith("local_", StringComparison.Ordinal) ?? false,
            Accent = ack.Accepted ? AccentFor(id ?? "") : HudAccent.Error,
        };
        if (ack.Accepted && ack.Value is { } value)
        {
            var unit = ack.Unit ?? "pt";
            readout = readout with
            {
                ValueText = HudFormatting.Number(value, unit),
                Unit = HudFormatting.UnitSuffix(unit),
                // A note such as "at the Exposure slider limit" stays visible.
                Detail = ack.Message is null or "accepted" or "" ? null : ack.Message,
            };
            if (ack.Minimum is { } minimum && ack.Maximum is { } maximum)
            {
                readout = readout with
                {
                    Bar = HudBar.Create(value, minimum, maximum, unit),
                    RangeText = (HudFormatting.RangeNumber(minimum, unit), HudFormatting.RangeNumber(maximum, unit)),
                };
            }
            return readout;
        }
        return readout with
        {
            Detail = ack.Message ?? (ack.Accepted ? "Accepted" : "Rejected by Lightroom"),
            NoticeIcon = ack.Accepted ? HudNoticeIcon.Checkmark : HudNoticeIcon.Cross,
        };
    }

    /// <summary>
    /// The sample shown while the HUD's style or place is chosen: Exposure at
    /// +0.35 EV on the Q key, normal step.
    /// </summary>
    public static HudReadout Sample { get; } = new()
    {
        Title = "Exposure",
        ValueText = "+0.35",
        Unit = "EV",
        Accent = HudAccent.Teal,
        Bar = HudBar.Create(0.35, -5, 5, "EV"),
        RangeText = ("-5.00", "+5.00"),
        KeyLabel = "Q",
        StepLabel = HudStepMode.Normal.Label(),
    };

    /// <summary>Catalog label when known; otherwise "local_LuminanceNoise" → "Luminance Noise".</summary>
    public static string? Label(string? id, ParameterScope? scope = null)
    {
        if (id is null) return null;
        var plain = id.StartsWith("local_", StringComparison.Ordinal) ? id["local_".Length..] : id;
        var descriptor = ParameterCatalog.Descriptor(scope ?? ParameterScope.Develop, id)
            ?? ParameterCatalog.Descriptor(ParameterScope.Develop, id)
            ?? ParameterCatalog.Descriptor(ParameterScope.Develop, plain);
        if (descriptor is not null) return descriptor.Label;
        var words = new System.Text.StringBuilder();
        foreach (var character in plain)
        {
            if (char.IsUpper(character) && words.Length > 0 && words[^1] != ' ') words.Append(' ');
            words.Append(character);
        }
        return words.Length == 0 ? null : words.ToString();
    }

    /// <summary>Magenta frame for white balance and colour, teal for tone.</summary>
    public static HudAccent AccentFor(string id)
    {
        var plain = id.StartsWith("local_", StringComparison.Ordinal) ? id["local_".Length..] : id;
        return ColourWords.Any(word => plain.Contains(word, StringComparison.OrdinalIgnoreCase)) ? HudAccent.Magenta : HudAccent.Teal;
    }
}
