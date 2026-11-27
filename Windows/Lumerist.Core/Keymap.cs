namespace Lumerist.Core;

/// <summary>The keys bound to one parameter in one scope.</summary>
/// <param name="Dial">Hold + scroll / drag / arrows. Never a combo.</param>
/// <param name="NudgeDown">Tap to step down. May be a combo on its own ("Tab + [").</param>
/// <param name="NudgeUp">Tap to step up. May be a combo on its own.</param>
/// <param name="NormalStep">Step override; null uses the catalog default.</param>
/// <param name="CoarseStep">Large-step override; null uses the catalog default.</param>
public sealed record ParameterSlot(
    KeyChord? Dial = null,
    KeyChord? NudgeDown = null,
    KeyChord? NudgeUp = null,
    double? NormalStep = null,
    double? CoarseStep = null)
{
    public bool IsEmpty => Dial is null && NudgeDown is null && NudgeUp is null && NormalStep is null && CoarseStep is null;
}

public enum SystemKeyRole
{
    ComboKey,
    PaletteKey,
    KeyReferenceToggle,
    PauseToggle,
}

public static class SystemKeyRoles
{
    public static IReadOnlyList<SystemKeyRole> All { get; } =
        [SystemKeyRole.ComboKey, SystemKeyRole.PaletteKey, SystemKeyRole.KeyReferenceToggle, SystemKeyRole.PauseToggle];

    /// <summary>"comboKey", "paletteKey", "keyReferenceToggle", "pauseToggle".</summary>
    public static string WireName(this SystemKeyRole role) => role switch
    {
        SystemKeyRole.ComboKey => "comboKey",
        SystemKeyRole.PaletteKey => "paletteKey",
        SystemKeyRole.KeyReferenceToggle => "keyReferenceToggle",
        _ => "pauseToggle",
    };

    public static SystemKeyRole? FromWireName(string? name) => All.Cast<SystemKeyRole?>().FirstOrDefault(role => role!.Value.WireName() == name);
}

/// <summary>
/// Defaults: Tab is the Combo Key, the key left of 1 opens the Palette, the
/// combo Tab + H ("help") shows the Key Reference, and Ctrl+Esc pauses. A
/// quick tap of Tab still reaches Lightroom; the Palette Key never does.
/// A null key is cleared (that system key is off).
/// </summary>
public sealed record SystemKeys
{
    /// <summary>Hold it and press another key for a combo ("Tab + K").</summary>
    public KeyChord? ComboKey { get; set; } = KeyChord.Tab;

    /// <summary>Hold to open the Palette (Develop).</summary>
    public KeyChord? PaletteKey { get; set; } = KeyChord.LeftOf1;

    public KeyChord? KeyReferenceToggle { get; set; } = KeyChord.ComboH;

    public KeyChord? PauseToggle { get; set; } = KeyChord.ControlEscape;

    public KeyChord? ChordFor(SystemKeyRole role) => role switch
    {
        SystemKeyRole.ComboKey => ComboKey,
        SystemKeyRole.PaletteKey => PaletteKey,
        SystemKeyRole.KeyReferenceToggle => KeyReferenceToggle,
        _ => PauseToggle,
    };
}

public enum PaletteEntryKind
{
    Preset,
    Tool,
}

/// <param name="Id">Preset UUID, or the protocol command name for a tool.</param>
/// <param name="Argument">Optional command argument (e.g. "aiSelection/subject").</param>
public sealed record PaletteEntry(PaletteEntryKind Kind, string Id, string? Argument, string Title);

public sealed record PaletteOptions
{
    public bool KeepVisible { get; set; }

    /// <summary>Ordered favourites shown in the Palette.</summary>
    public List<PaletteEntry> Favourites { get; set; } = [];
}

public sealed record InputOptions
{
    public const double DragSensitivityMinimum = 1, DragSensitivityMaximum = 30;
    public const double TrackpadPointsPerStepMinimum = 4, TrackpadPointsPerStepMaximum = 40;
    public const double DefaultTrackpadPointsPerStep = 12;

    public bool InvertWheel { get; set; }

    /// <summary>Swap the normal and large (Shift) steps.</summary>
    public bool SwapNormalAndCoarse { get; set; }

    /// <summary>Touchpad travel per step (precision touchpads and high-resolution wheels).</summary>
    public double TrackpadPointsPerStep { get; set; } = DefaultTrackpadPointsPerStep;

    /// <summary>Drag adjusts only while the left button is down.</summary>
    public bool RequireMouseDown { get; set; } = true;

    /// <summary>Higher means less mouse travel per step: points per step = 31 − sensitivity.</summary>
    public double DragSensitivity { get; set; } = 25;

    public bool ReturnCursorAfterRelease { get; set; } = true;
    public bool DoubleClickReset { get; set; } = true;
    public bool MiddleClickReset { get; set; } = true;

    /// <summary>Mouse back / forward buttons go to the previous / next photo.</summary>
    public bool MouseBackForward { get; set; } = true;

    /// <summary>With a mask selected, Dial Keys and Nudge Keys move the mask's sliders.</summary>
    public bool FollowSelectedMask { get; set; } = true;

    /// <summary>Reveal the matching Lightroom panel when a gesture starts.</summary>
    public bool RevealPanels { get; set; }

    /// <summary>← / → move the held Dial Key's slider one step.</summary>
    public bool ArrowKeysAdjust { get; set; } = true;

    /// <summary>Horizontal mouse travel, in pixels, for one drag step.</summary>
    public double DragPointsPerStep => 31 - DragSensitivity;
}

/// <summary>
/// Everything the user assigns: per-scope parameter keys, Command Keys,
/// System Keys, Import keys, Palette and input options. A mutable value: take a
/// <see cref="Clone"/> before handing it to another owner.
/// Validation and conflicts: KeymapRules.cs. Storage: KeymapStore.cs.
/// </summary>
public sealed partial class Keymap
{
    /// <summary>Develop slots keyed by parameter id.</summary>
    public Dictionary<string, ParameterSlot> Develop { get; set; } = [];

    /// <summary>Library Quick Develop slots keyed by parameter id.</summary>
    public Dictionary<string, ParameterSlot> Library { get; set; } = [];

    public List<CommandKey> CommandKeys { get; set; } = [];
    public SystemKeys SystemKeys { get; set; } = new();

    /// <summary>Culling keys for Lightroom's Import window (none in the standard layout).</summary>
    public ImportKeys ImportKeys { get; set; } = new();

    public PaletteOptions Palette { get; set; } = new();
    public InputOptions Input { get; set; } = new();

    /// <summary>A deep copy (slots, keys and options are immutable records or copied).</summary>
    public Keymap Clone() => new()
    {
        Develop = new Dictionary<string, ParameterSlot>(Develop),
        Library = new Dictionary<string, ParameterSlot>(Library),
        CommandKeys = [.. CommandKeys],
        SystemKeys = SystemKeys with { },
        ImportKeys = ImportKeys with { },
        Palette = Palette with { Favourites = [.. Palette.Favourites] },
        Input = Input with { },
    };

    // Slots ----------------------------------------------------------------------

    public Dictionary<string, ParameterSlot> Slots(ParameterScope scope) =>
        scope == ParameterScope.Develop ? Develop : Library;

    public ParameterSlot? Slot(ParameterKey key) => Slots(key.Scope).GetValueOrDefault(key.Id);

    /// <summary>Stores a slot; an empty slot is removed.</summary>
    public void SetSlot(ParameterSlot? slot, ParameterKey key)
    {
        if (slot is null || slot.IsEmpty) Slots(key.Scope).Remove(key.Id);
        else Slots(key.Scope)[key.Id] = slot;
    }

    public void UpdateSlot(ParameterKey key, Func<ParameterSlot, ParameterSlot> change) =>
        SetSlot(change(Slot(key) ?? new ParameterSlot()), key);

    /// <summary>Effective steps: user override or catalog default.</summary>
    public (double Normal, double Coarse)? Steps(ParameterKey key)
    {
        if (ParameterCatalog.Descriptor(key) is not { } descriptor) return null;
        var slot = Slot(key);
        return (slot?.NormalStep ?? descriptor.NormalStep, slot?.CoarseStep ?? descriptor.CoarseStep);
    }

    public void ClearDialKeys(ParameterScope scope)
    {
        foreach (var id in Slots(scope).Keys.ToList())
            UpdateSlot(new ParameterKey(scope, id), slot => slot with { Dial = null });
    }

    public void ClearNudgeKeys(ParameterScope scope)
    {
        foreach (var id in Slots(scope).Keys.ToList())
            UpdateSlot(new ParameterKey(scope, id), slot => slot with { NudgeDown = null, NudgeUp = null });
    }

    /// <summary>Command Keys that apply in a scope.</summary>
    public IEnumerable<CommandKey> CommandKeysIn(ParameterScope scope) => CommandKeys.Where(key => key.Scope.Includes(scope));

    /// <summary>How a chord reads with this keymap's Combo Key: "Q", "Tab + K".</summary>
    public string DisplayText(KeyChord chord) => chord.DisplayTextWith(SystemKeys.ComboKey);

    // Masks ----------------------------------------------------------------------

    /// <summary>Develop parameter → the selected mask's equivalent (Lightroom's masking panel).</summary>
    public static IReadOnlyDictionary<string, string> LocalParameterMap { get; } = new Dictionary<string, string>
    {
        ["Exposure"] = "local_Exposure", ["Contrast"] = "local_Contrast",
        ["Highlights"] = "local_Highlights", ["Shadows"] = "local_Shadows",
        ["Whites"] = "local_Whites", ["Blacks"] = "local_Blacks",
        ["Temperature"] = "local_Temperature", ["Tint"] = "local_Tint",
        ["Texture"] = "local_Texture", ["Clarity"] = "local_Clarity",
        ["Dehaze"] = "local_Dehaze", ["Saturation"] = "local_Saturation",
        ["Sharpness"] = "local_Sharpness", ["LuminanceSmoothing"] = "local_LuminanceNoise",
        ["GrainAmount"] = "local_Grain", ["DefringePurpleAmount"] = "local_Defringe",
        ["PresetAmount"] = "local_Amount", ["local_Amount"] = "local_Amount",
    };

    /// <summary>The mask parameter a Develop key controls while a mask is selected, or null.</summary>
    public static ParameterKey? LocalKey(ParameterKey key)
    {
        if (key.Scope != ParameterScope.Develop) return null;
        if (key.Id.StartsWith("local_", StringComparison.Ordinal)) return key;
        return LocalParameterMap.TryGetValue(key.Id, out var local) ? new ParameterKey(ParameterScope.Develop, local) : null;
    }

    // Standard layout --------------------------------------------------------------

    private static Dictionary<string, ParameterSlot> DialSlots(params (string Id, int ScanCode)[] pairs) =>
        pairs.ToDictionary(pair => pair.Id, pair => new ParameterSlot(Dial: new KeyChord(pair.ScanCode)));

    /// <summary>
    /// Lumerist's standard layout (Shared/fixtures/standard-layout.json), the
    /// same keys as on the Mac. The left hand stays on the keyboard, the right
    /// hand on the mouse:
    /// <code>
    ///     1 Temp     2 Tint      3 Vibrance  4 Texture   5 Clarity  6 Dehaze
    ///     Q Exposure W Contrast  E Saturation R Straighten T Crop zoom
    ///     A Highl.   S Shadows   D Whites    F Blacks
    ///                X Shadows   C Darks     V Lights    B Highlights  (tone curve)
    /// </code>
    /// P and M are the preset and mask amounts; Library uses the same keys for
    /// Quick Develop. Four combos in Develop: Tab + A Auto Tone, Tab + W Auto
    /// White Balance, Tab + R Reset all, Tab + Q Paste Settings from Previous
    /// (<see cref="AppCommands.PasteSettingsFromPrevious"/>, Lightroom's Ctrl+Alt+V
    /// with the key that types v). Fixed ids keep them the same keys across launches.
    /// No Import key is assigned.
    /// </summary>
    public static Keymap StandardLayout() => StandardLayout(KeyChord.LeftOf1);

    /// <summary>
    /// The standard layout with the Palette Key the keyboard allows
    /// (<see cref="PaletteKeyDefault"/>): the key left of 1, or none where that
    /// key switches the input method or types a letter or digit.
    /// </summary>
    public static Keymap StandardLayout(KeyChord? paletteKey)
    {
        var develop = DialSlots(
            ("Temperature", ScanCodes.Digit1), ("Tint", ScanCodes.Digit2), ("Vibrance", ScanCodes.Digit3),
            ("Texture", ScanCodes.Digit4), ("Clarity", ScanCodes.Digit5), ("Dehaze", ScanCodes.Digit6),
            ("Exposure", ScanCodes.Q), ("Contrast", ScanCodes.W), ("Saturation", ScanCodes.E),
            ("straightenAngle", ScanCodes.R), ("lumerist.cropZoom", ScanCodes.T),
            ("Highlights", ScanCodes.A), ("Shadows", ScanCodes.S), ("Whites", ScanCodes.D), ("Blacks", ScanCodes.F),
            ("ParametricShadows", ScanCodes.X), ("ParametricDarks", ScanCodes.C),
            ("ParametricLights", ScanCodes.V), ("ParametricHighlights", ScanCodes.B),
            ("PresetAmount", ScanCodes.P), ("local_Amount", ScanCodes.M));
        var library = DialSlots(
            ("Temperature", ScanCodes.Digit1), ("Tint", ScanCodes.Digit2), ("Vibrance", ScanCodes.Digit3),
            ("Texture", ScanCodes.Digit4), ("Clarity", ScanCodes.Digit5),
            ("Exposure", ScanCodes.Q), ("Contrast", ScanCodes.W), ("Saturation", ScanCodes.E),
            ("Highlights", ScanCodes.A), ("Shadows", ScanCodes.S), ("Whites", ScanCodes.D), ("Blacks", ScanCodes.F));

        static CommandKey Combo(string id, int scanCode, string title, CommandAction action) =>
            new(Guid.Parse(id), CommandScope.Develop, new KeyChord(scanCode, KeyModifiers.Combo), "Lightroom", title, action);

        return new Keymap
        {
            Develop = develop,
            Library = library,
            CommandKeys =
            [
                Combo("8022E100-5E15-408A-A02C-9796E4B60CBA", ScanCodes.A, "Auto Tone", new CommandAction.Command("autoTone")),
                Combo("84C5337F-EA7E-44F3-97CE-6772C2231EB4", ScanCodes.W, "Auto White Balance",
                    new CommandAction.Command("autoWhiteBalance")),
                Combo("F0851B74-86AE-4988-BE39-01DF8EFAFE25", ScanCodes.R, "Reset all", new CommandAction.Command("resetAll")),
                Combo("787E1A28-ED89-40D3-9855-6049A5F3F639", ScanCodes.Q, "Paste settings from previous",
                    new CommandAction.Command(AppCommands.PasteSettingsFromPrevious)),
            ],
            SystemKeys = new SystemKeys { PaletteKey = paletteKey },
        };
    }
}
