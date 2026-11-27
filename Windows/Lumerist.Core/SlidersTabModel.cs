using System.Globalization;

namespace Lumerist.Core;

// The Sliders (Quick Develop) tab without the drawing (Views/SlidersTab.swift):
// the steps a key moves by, the unit labels, the legend and the search.

/// <summary>The effective steps of one parameter, in the order the keys use them.</summary>
public sealed record StepPair(double Normal, double Coarse, double DefaultNormal, double DefaultCoarse, bool Swapped, bool IsCustom)
{
    public static StepPair For(ParameterDescriptor descriptor, ParameterSlot? slot, bool swapped) => new(
        slot?.NormalStep ?? descriptor.NormalStep,
        slot?.CoarseStep ?? descriptor.CoarseStep,
        descriptor.NormalStep,
        descriptor.CoarseStep,
        swapped,
        slot?.NormalStep is not null || slot?.CoarseStep is not null);

    /// <summary>The step of the key alone.</summary>
    public double Plain => Swapped ? Coarse : Normal;

    /// <summary>The step with Shift.</summary>
    public double Shifted => Swapped ? Normal : Coarse;

    public double DefaultPlain => Swapped ? DefaultCoarse : DefaultNormal;

    public double DefaultShifted => Swapped ? DefaultNormal : DefaultCoarse;

    /// <summary>Up to two decimals: "1", "0.05", "0.33".</summary>
    public static string Text(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    /// <summary>A step field's text: up to three decimals.</summary>
    public static string FieldText(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    /// <summary>Reads a step typed by the user ("0.5" or "0,5"); null when it is not a number.</summary>
    public static double? Parse(string text)
    {
        var normalized = text.Trim().Replace(',', '.');
        return double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && double.IsFinite(value)
            ? value
            : null;
    }

    /// <summary>
    /// What to store for a typed step: clamped into the allowed range, and
    /// null (no override) when it equals the default.
    /// </summary>
    public static double? Stored(ParameterDescriptor descriptor, double value, bool normal)
    {
        var fallback = normal ? descriptor.NormalStep : descriptor.CoarseStep;
        var clamped = Math.Clamp(value, descriptor.StepMinimum, descriptor.StepMaximum);
        return Math.Abs(clamped - fallback) < 1e-9 ? null : clamped;
    }
}

public static class SlidersTabText
{
    /// <summary>The unit beside a slider's name: "pt · 50 K", "°", "EV"…</summary>
    public static string UnitLabel(string unit) => unit switch
    {
        "temp" => "pt · 50 K",
        "deg" => "°",
        "choice" => "choice",
        _ => unit,
    };

    /// <summary>The unit in the step editor: "pt (50 K each)", "°", "EV"…</summary>
    public static string EditorUnit(string unit) => unit switch
    {
        "temp" => "pt (50 K each)",
        "deg" => "°",
        _ => unit,
    };

    /// <summary>Choice parameters move one option at a time: no step to edit.</summary>
    public static bool IsChoice(ParameterDescriptor descriptor) => descriptor.StepMinimum == descriptor.StepMaximum;

    /// <summary>"Click a box, then press a key · hold Tab as well for a combo".</summary>
    public static string Legend(KeyChord? comboKey)
    {
        var combo = comboKey is { } key ? $"hold {key.DisplayText} as well for a combo" : "combos need a Combo Key";
        return $"Click a box, then press a key · {combo}";
    }

    /// <summary>How many sliders of a section have at least one key.</summary>
    public static int AssignedCount(IEnumerable<ParameterDescriptor> parameters, Keymap keymap) =>
        parameters.Count(descriptor => keymap.Slot(descriptor.Key) is { } slot
            && (slot.Dial is not null || slot.NudgeDown is not null || slot.NudgeUp is not null));

    /// <summary>
    /// The sections of a scope, keeping the sliders whose name or section
    /// contains <paramref name="search"/>, or whose keys read like it.
    /// </summary>
    public static IReadOnlyList<(string Section, IReadOnlyList<ParameterDescriptor> Parameters)> FilteredSections(
        Keymap keymap, ParameterScope scope, string search)
    {
        var all = ParameterCatalog.Sections(scope);
        var query = search.Trim();
        if (query.Length == 0) return all;
        var result = new List<(string, IReadOnlyList<ParameterDescriptor>)>();
        foreach (var (section, parameters) in all)
        {
            var matches = parameters.Where(descriptor =>
            {
                if (CommandTitles.Contains(descriptor.Label, query) || CommandTitles.Contains(section, query)) return true;
                var slot = keymap.Slot(descriptor.Key);
                return new[] { slot?.Dial, slot?.NudgeDown, slot?.NudgeUp }.OfType<KeyChord>()
                    .Any(chord => CommandTitles.Contains(keymap.DisplayText(chord), query));
            }).ToList();
            if (matches.Count > 0) result.Add((section, matches));
        }
        return result;
    }

    /// <summary>Sections open by default: Basic in Develop, Quick Develop in Library.</summary>
    public static IReadOnlySet<string> DefaultExpanded { get; } = new HashSet<string>(StringComparer.Ordinal) { "Basic", "Quick Develop" };
}

/// <summary>
/// Tooltips for the names of key kinds wherever they label something: the
/// Sliders and Quick Develop column headers and the Keyboard tab (the Mac's
/// KeyKindHelp.swift). The same texts as the Mac's, with "Shift" for ⇧ and
/// Windows for macOS. Library's Quick Develop has no sliders: a step there is
/// added to every selected photo, so the Library texts say so.
/// </summary>
public static class KeyTypeHelp
{
    /// <summary>How long the pointer rests before a tooltip shows, in milliseconds: every tooltip of the app.</summary>
    public const int InitialShowDelayMilliseconds = 1000;

    public static string Dial(ParameterScope scope) => scope == ParameterScope.Develop
        ? "Hold it and turn the mouse wheel or swipe on the trackpad (or drag, or press ← →): the slider follows until you let go. A quick tap still reaches Lightroom, unless the same key is also a Command Key."
        : "Hold it and turn the mouse wheel or swipe on the trackpad (or drag, or press ← →): each step is added to every selected photo, as with Lightroom's Quick Develop buttons, until you let go. A quick tap still reaches Lightroom, unless the same key is also a Command Key.";

    public static string NudgeDown(ParameterScope scope) => scope == ParameterScope.Develop
        ? "Each press moves the slider one step down; hold the key to repeat. Add Shift for the other step."
        : "Each press moves every selected photo one step down; hold the key to repeat. Add Shift for the other step.";

    public static string NudgeUp(ParameterScope scope) => scope == ParameterScope.Develop
        ? "Each press moves the slider one step up; hold the key to repeat. Add Shift for the other step."
        : "Each press moves every selected photo one step up; hold the key to repeat. Add Shift for the other step.";

    /// <summary>The Keyboard tab's legend names both Nudge Keys at once ("Nudge − / +").</summary>
    public static string NudgeKeys(ParameterScope scope) => scope == ParameterScope.Develop
        ? "Each press moves the slider one step down (Nudge −) or up (Nudge +); hold the key to repeat. Add Shift for the other step."
        : "Each press moves every selected photo one step down (Nudge −) or up (Nudge +); hold the key to repeat. Add Shift for the other step.";

    /// <summary>The same step serves the Nudge Keys and each step of a Dial Key.</summary>
    public static string Step(ParameterScope scope) => scope == ParameterScope.Develop
        ? "How far the slider moves for one Nudge press, and for each click of the mouse wheel, stretch of trackpad swipe or ← → press with the Dial Key held. The Shift value is the step when you add Shift. Click a step to change it."
        : "How much is added to every selected photo for one Nudge press, and for each click of the mouse wheel, stretch of trackpad swipe or ← → press with the Dial Key held. The Shift value is the step when you add Shift. Click a step to change it.";

    public const string CommandKey = "A tap runs a Lightroom command, a preset, a Lightroom shortcut or a macro.";

    public const string SystemKey =
        "Works in every module: the Combo Key, the Palette Key, the Key Reference toggle and the pause toggle. Change them in General → System Keys.";

    public const string Conflict =
        "A key that clashes with another of your keys, or that Lightroom or Windows also uses. Point at the key to read why.";

    /// <summary>The Keyboard tab's Plain keys / Combos picker, with the current Combo Key.</summary>
    public static string Combos(KeyChord? comboKey)
    {
        if (comboKey is not { } combo)
            return "A combo is a key of its own: hold the Combo Key, then press another key. Set a Combo Key in General → System Keys.";
        var example = new KeyChord(0x25, KeyModifiers.Combo).DisplayTextWith(combo); // "Tab + K" (the Mac's key code 40)
        return $"A combo is a key of its own: hold {combo.DisplayText}, then press another key ({example}). Plain keys work alone.";
    }

    /// <summary>The help of a slot's key kind, as its column header reads it.</summary>
    public static string For(SlotRole role, ParameterScope scope) => role switch
    {
        SlotRole.Dial => Dial(scope),
        SlotRole.NudgeDown => NudgeDown(scope),
        _ => NudgeUp(scope),
    };
}
