namespace Lumerist.Core;

/// <summary>
/// What a mask tool does when it is clicked in the Palette (the Mac's
/// PaletteMaskMode). With a mask selected in Lightroom, the modifiers held at
/// the click decide: none creates a new mask, Shift adds to the selected
/// mask, Alt subtracts from it, Shift+Alt intersects with it. Without a
/// selected mask a click always creates a new mask.
/// </summary>
public enum PaletteMaskMode
{
    New,
    Add,
    Subtract,
    Intersect,
}

public static class PaletteMaskModes
{
    /// <summary>The three modes the modifiers choose, in the order the hint lists them.</summary>
    public static IReadOnlyList<PaletteMaskMode> Modifying { get; } =
        [PaletteMaskMode.Add, PaletteMaskMode.Subtract, PaletteMaskMode.Intersect];

    public static PaletteMaskMode From(bool shift, bool alt, bool masking)
    {
        if (!masking) return PaletteMaskMode.New;
        return (shift, alt) switch
        {
            (true, true) => PaletteMaskMode.Intersect,
            (true, false) => PaletteMaskMode.Add,
            (false, true) => PaletteMaskMode.Subtract,
            _ => PaletteMaskMode.New,
        };
    }

    public static LightroomCommandName Command(this PaletteMaskMode mode) => mode switch
    {
        PaletteMaskMode.Add => LightroomCommandName.AddToMask,
        PaletteMaskMode.Subtract => LightroomCommandName.SubtractFromMask,
        PaletteMaskMode.Intersect => LightroomCommandName.IntersectWithMask,
        _ => LightroomCommandName.CreateMask,
    };

    /// <summary>Lightroom's own word for it.</summary>
    public static string Title(this PaletteMaskMode mode) => mode switch
    {
        PaletteMaskMode.Add => "Add",
        PaletteMaskMode.Subtract => "Subtract",
        PaletteMaskMode.Intersect => "Intersect",
        _ => "New",
    };

    /// <summary>The modifiers that choose it.</summary>
    public static string Keys(this PaletteMaskMode mode) => mode switch
    {
        PaletteMaskMode.Add => "Shift",
        PaletteMaskMode.Subtract => "Alt",
        PaletteMaskMode.Intersect => "Shift+Alt",
        _ => "",
    };

    /// <summary>Marks the mask tools while its modifiers are held.</summary>
    public static string? Badge(this PaletteMaskMode mode) => mode switch
    {
        PaletteMaskMode.Add => "+",
        PaletteMaskMode.Subtract => "−",
        PaletteMaskMode.Intersect => "∩",
        _ => null,
    };

    /// <summary>"Shift Add", "Alt Subtract", "Shift+Alt Intersect".</summary>
    public static string HintText(this PaletteMaskMode mode) => $"{mode.Keys()} {mode.Title()}";
}

/// <summary>A mask tool offered by the Palette (the Mac's MaskTool).</summary>
/// <param name="Id">The mask argument, e.g. "aiSelection/subject".</param>
/// <param name="Symbol">The symbol's name; the app draws its own glyph for it.</param>
public sealed record PaletteMaskTool(string Id, string Title, string Symbol);

/// <summary>
/// The Palette's content and texts (the Mac's PaletteView): Lightroom's mask
/// tools under "Create New Mask", then the user's favourite presets and
/// tools, and the line that names the Palette Key.
/// </summary>
public static class PaletteMenu
{
    /// <summary>Every mask tool the app knows, as on the Mac.</summary>
    public static IReadOnlyList<PaletteMaskTool> MaskTools { get; } =
    [
        new("aiSelection/subject", "Subject", "person.crop.rectangle"),
        new("aiSelection/sky", "Sky", "cloud.sun"),
        new("aiSelection/background", "Background", "rectangle.dashed"),
        new("aiSelection/people", "People", "person.2"),
        new("aiSelection/objects", "Objects", "cube"),
        new("aiSelection/landscape", "Landscape", "mountain.2"),
        new("brush", "Brush", "paintbrush.pointed"),
        new("gradient", "Linear", "square.bottomhalf.filled"),
        new("radialGradient", "Radial", "circle.dotted"),
        new("rangeMask/color", "Color range", "eyedropper"),
        new("rangeMask/luminance", "Luminance range", "sun.max"),
        new("rangeMask/depth", "Depth range", "cube.transparent"),
    ];

    /// <summary>The eight tools under "Create New Mask", four per row.</summary>
    public static IReadOnlyList<string> MenuToolIds { get; } =
    [
        "aiSelection/subject", "aiSelection/sky", "aiSelection/background", "aiSelection/people",
        "aiSelection/objects", "brush", "gradient", "radialGradient",
    ];

    public const string Title = "Palette";
    public const string CreateNewMask = "Create New Mask";
    public const string Favourites = "Favourites";
    public const string NoFavourites = "No favourites yet. Add presets and mask tools in Floating panels → Palette.";
    public const string PinnedHelp = "Pinned: the Palette stays open after you release its key";
    public const string PinHelp = "Pin to keep the Palette open after you release its key";
    public const string PinLabel = "Pin the Palette";
    public const string CloseLabel = "Close the Palette";
    public const string ModeHintLabel = "Hold Shift to add to the selected mask, Alt to subtract, both to intersect";
    public const string MaskingHint =
        "Creates a new mask. Hold Shift to add to the selected mask, Alt to subtract, both to intersect.";

    public static PaletteMaskTool? MaskTool(string? id) => id is null ? null : MaskTools.FirstOrDefault(tool => tool.Id == id);

    /// <summary>A mask tool favourite: New / Add / Subtract / Intersect come from the click.</summary>
    public static bool IsMaskTool(PaletteEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return entry.Kind == PaletteEntryKind.Tool && LightroomCommandNames.FromWireName(entry.Id) is
            LightroomCommandName.CreateMask or LightroomCommandName.AddToMask or
            LightroomCommandName.SubtractFromMask or LightroomCommandName.IntersectWithMask;
    }

    /// <summary>A favourite row's symbol: a mask tool shows its own, a preset the sliders.</summary>
    public static string Symbol(PaletteEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return entry.Kind == PaletteEntryKind.Preset ? "slider.horizontal.3" : MaskTool(entry.Argument)?.Symbol ?? "circle.dashed";
    }

    /// <summary>A mask tool clicked in the given mode.</summary>
    public static CommandAction MaskAction(string? argument, PaletteMaskMode mode) =>
        new CommandAction.Command(mode.Command().WireName(), argument);

    /// <summary>What a favourite runs: a preset, a mask tool in the clicked mode, or its own command.</summary>
    public static CommandAction Action(PaletteEntry entry, PaletteMaskMode mode)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (entry.Kind == PaletteEntryKind.Preset) return new CommandAction.Preset(entry.Id, entry.Title);
        // Mask tools follow the modifiers; other tools keep their command.
        return IsMaskTool(entry) ? MaskAction(entry.Argument, mode) : new CommandAction.Command(entry.Id, entry.Argument);
    }

    /// <summary>The screen-reader name of a favourite row.</summary>
    public static string EntryLabel(PaletteEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return entry.Kind == PaletteEntryKind.Preset ? $"Apply preset {entry.Title}" : entry.Title;
    }

    /// <summary>The screen-reader name of a mask tool cell.</summary>
    public static string ToolLabel(string title, bool masking) => masking ? $"{title} mask" : $"New {title} mask";

    /// <summary>"Mask selected", Lightroom's module ("Develop"), or nothing.</summary>
    public static string? Subtitle(LightroomContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.IsMasking) return "Mask selected";
        return context.Module.Length == 0 ? null : CommandTitles.Capitalized(context.Module);
    }

    /// <summary>The panel's last line names its own key, e.g. "Hold ` for a look · tap it to keep it open".</summary>
    public static string FooterText(Keymap keymap, bool keepVisible)
    {
        ArgumentNullException.ThrowIfNull(keymap);
        var key = keymap.SystemKeys.PaletteKey is { } chord ? keymap.DisplayText(chord) : "its key";
        return keepVisible
            ? $"Pinned · press {key} or × to close"
            : $"Hold {key} for a look · tap it to keep it open";
    }
}
