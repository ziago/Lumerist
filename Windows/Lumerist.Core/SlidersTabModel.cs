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
