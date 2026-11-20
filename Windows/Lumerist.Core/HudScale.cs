namespace Lumerist.Core;

/// <summary>One scale mark of the Value HUD.</summary>
/// <param name="Fraction">Position along the scale, 0…1.</param>
/// <param name="IsMajor">A big mark on a round value; the small ones sit in between.</param>
/// <param name="Label">The number under a big mark, or null.</param>
public readonly record struct HudTick(double Fraction, bool IsMajor, string? Label);

/// <summary>
/// The Value HUD's bar: where the value sits in the slider range, where zero
/// is, and the scale marks (the Mac's OverlayContent.Bar).
/// </summary>
/// <param name="Fraction">Position of the value inside the slider range, 0…1.</param>
/// <param name="Zero">Zero's position when the range spans it, else null.</param>
/// <param name="Ticks">
/// Scale marks: big labelled ones on round values (every EV for exposure),
/// small ones in between.
/// </param>
public sealed record HudBar(double Fraction, double? Zero, IReadOnlyList<HudTick> Ticks)
{
    /// <summary>Null when a number is not finite or the range is empty.</summary>
    public static HudBar? Create(double value, double minimum, double maximum, string? unit = null)
    {
        if (!double.IsFinite(value) || !double.IsFinite(minimum) || !double.IsFinite(maximum) || maximum <= minimum) return null;
        var scale = new HudScale(minimum, maximum, unit);
        return new HudBar(
            scale.Fraction(value),
            minimum < 0 && maximum > 0 ? scale.Fraction(0) : null,
            scale.Ticks());
    }

    /// <summary>True when any mark carries a number (the range ends are then not written).</summary>
    public bool HasLabels => Ticks.Any(tick => tick.Label is not null);

    public bool Equals(HudBar? other) =>
        other is not null && Fraction.Equals(other.Fraction) && Zero.Equals(other.Zero) && Ticks.SequenceEqual(other.Ticks);

    public override int GetHashCode() => HashCode.Combine(Fraction, Zero, Ticks.Count);
}

/// <summary>
/// Maps slider values to 0…1 and chooses the scale marks (the Mac's
/// OverlayContent.Scale). Kelvin uses a logarithmic scale, like Lightroom's
/// white-balance slider.
/// </summary>
public sealed class HudScale(double minimum, double maximum, string? unit)
{
    private static readonly double[] KelvinMajors = [2_000, 3_000, 4_000, 5_000, 7_500, 10_000, 15_000, 20_000, 30_000, 50_000];
    private static readonly HashSet<double> KelvinLabelled = [3_000, 5_000, 10_000, 20_000, 50_000];

    public double Minimum { get; } = minimum;

    public double Maximum { get; } = maximum;

    public string? Unit { get; } = unit;

    private bool IsKelvin => Unit == "K" && Minimum > 0;

    /// <summary>Where <paramref name="value"/> sits, clamped to 0…1.</summary>
    public double Fraction(double value)
    {
        var raw = IsKelvin
            ? (Math.Log(Math.Max(value, Minimum)) - Math.Log(Minimum)) / (Math.Log(Maximum) - Math.Log(Minimum))
            : (value - Minimum) / (Maximum - Minimum);
        return Math.Min(Math.Max(raw, 0), 1);
    }

    /// <summary>
    /// Big marks on round values (every whole EV for exposure, every other one
    /// on a wide range), labelled, or every other one labelled when more than
    /// nine would crowd; small marks in between.
    /// </summary>
    public IReadOnlyList<HudTick> Ticks()
    {
        if (IsKelvin)
        {
            return KelvinMajors
                .Where(value => value >= Minimum && value <= Maximum)
                .Select(value => new HudTick(Fraction(value), KelvinLabelled.Contains(value),
                    KelvinLabelled.Contains(value) ? $"{(int)(value / 1_000)}k" : null))
                .ToList();
        }
        var span = Maximum - Minimum;
        // Exposure: every whole EV is a big, labelled mark.
        var major = Unit == "EV" ? (span <= 12 ? 1 : 2) : NiceStep(span / 8);
        var minor = Unit == "EV" ? major / 2 : major / (IsFiveLike(major) ? 5 : 2);
        // Label every major mark, or every other one when they would crowd.
        var majorCount = (int)Math.Floor(span / major) + 1;
        var labelStride = Unit == "EV" || majorCount <= 9 ? 1 : 2;

        var result = new List<HudTick>();
        var index = 0;
        var value = Math.Ceiling(Minimum / minor) * minor;
        while (value <= Maximum + minor * 0.001)
        {
            var isMajor = Math.Abs(HudNumberText.RoundAwayFromZero(value / major) - value / major) < 0.0001;
            string? label = null;
            if (isMajor)
            {
                var majorIndex = (long)HudNumberText.RoundAwayFromZero(value / major);
                if (majorIndex % labelStride == 0) label = Label(value);
            }
            result.Add(new HudTick(Fraction(value), isMajor, label));
            value += minor;
            index++;
            if (index > 200) break;
        }
        return result;
    }

    /// <summary>1, 2, 2.5 or 5 times a power of ten, at least <paramref name="rough"/>.</summary>
    internal static double NiceStep(double rough)
    {
        if (!(rough > 0) || !double.IsFinite(rough)) return 1;
        var exponent = Math.Floor(Math.Log10(rough));
        var power = Math.Pow(10, exponent);
        foreach (var multiplier in new[] { 1.0, 2.0, 2.5, 5.0, 10.0 })
        {
            if (rough <= multiplier * power) return multiplier * power;
        }
        return 10 * power;
    }

    /// <summary>Steps that start with 1 or 5 split into five small marks; the others into two.</summary>
    internal static bool IsFiveLike(double step)
    {
        var leading = step / Math.Pow(10, Math.Floor(Math.Log10(step)));
        return Math.Abs(leading - 1) < 0.001 || Math.Abs(leading - 5) < 0.001;
    }

    /// <summary>"0", "+2", "−2", "+2.5": a typographic minus, a plus on positive numbers.</summary>
    internal static string Label(double value)
    {
        if (Math.Abs(value) < 0.0001) return "0";
        var whole = Math.Abs(value - HudNumberText.RoundAwayFromZero(value)) < 0.0001;
        var number = whole
            ? ((long)HudNumberText.RoundAwayFromZero(value)).ToString(System.Globalization.CultureInfo.InvariantCulture)
            : HudNumberText.Printf(value, 1);
        return value > 0 ? "+" + number : number.Replace("-", "−", StringComparison.Ordinal);
    }
}
