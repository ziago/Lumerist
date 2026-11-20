using System.Globalization;
using System.Numerics;

namespace Lumerist.Core;

/// <summary>
/// How the Value HUD writes numbers (the Mac's OverlayFormatting in
/// ValueHUD.swift): EV with a sign and two decimals, whole kelvin, one decimal
/// for degrees and pixels, points as a signed whole number or one decimal.
/// </summary>
/// <remarks>
/// The Mac formats with the user's locale; the Windows app runs with the
/// invariant culture (InvariantGlobalization), so "." and "," always.
/// </remarks>
public static class HudFormatting
{
    /// <summary>
    /// EV: sign and two decimals; K: whole kelvin; deg: one decimal; pt: sign
    /// and a whole number, or one decimal when fractional.
    /// </summary>
    public static string Number(double value, string unit) => unit switch
    {
        "EV" => HudNumberText.Format(value, 2, 2, grouping: true, HudNumberText.Sign.ExceptZero),
        "K" => HudNumberText.Format(HudNumberText.RoundAwayFromZero(value), 0, 0, grouping: false, HudNumberText.Sign.Automatic),
        "deg" or "°" or "px" => HudNumberText.Format(value, 1, 1, grouping: true, HudNumberText.Sign.ExceptZero),
        _ => Math.Abs(value - HudNumberText.RoundAwayFromZero(value)) < 0.05
            ? HudNumberText.Format(value, 0, 0, grouping: true, HudNumberText.Sign.ExceptZero)
            : HudNumberText.Format(value, 1, 1, grouping: true, HudNumberText.Sign.ExceptZero),
    };

    /// <summary>The ends of the slider range, under the scale when it has no numbers of its own.</summary>
    public static string RangeNumber(double value, string unit) => unit switch
    {
        "EV" => HudNumberText.Format(value, 2, 2, grouping: true, HudNumberText.Sign.ExceptZero),
        "K" => HudNumberText.Format(HudNumberText.RoundAwayFromZero(value), 0, 0, grouping: false, HudNumberText.Sign.Automatic),
        _ => HudNumberText.Format(value, 0, 1, grouping: true, HudNumberText.Sign.Automatic),
    };

    /// <summary>The unit written after the value, or null for plain points.</summary>
    public static string? UnitSuffix(string unit) => unit switch
    {
        "EV" or "K" or "px" or "%" => unit,
        "deg" => "°",
        _ => null,
    };

    /// <summary>The value and its unit in one string ("+0.35 EV").</summary>
    public static string ValueText(double value, string unit) =>
        string.Join(' ', new[] { Number(value, unit), UnitSuffix(unit) }.OfType<string>());
}

/// <summary>
/// Number text that matches the Mac exactly: its number formatter (shortest
/// decimal form of the double, rounded half to even) and C's <c>%.1f</c>
/// (the exact binary value, rounded half to even).
/// </summary>
internal static class HudNumberText
{
    public enum Sign
    {
        /// <summary>A minus on negative numbers (also when they round to zero), nothing on positive ones.</summary>
        Automatic,

        /// <summary>A plus on positive numbers, a minus on negative ones, nothing on zero or what rounds to it.</summary>
        ExceptZero,
    }

    /// <summary>Swift's <c>rounded()</c>: to nearest, halves away from zero.</summary>
    public static double RoundAwayFromZero(double value) => Math.Round(value, MidpointRounding.AwayFromZero);

    /// <summary>
    /// Formats with <paramref name="minimumFraction"/>…<paramref name="maximumFraction"/>
    /// decimals: the shortest decimal that reads back as <paramref name="value"/>,
    /// rounded half to even (so 0.125 → "0.12" and 0.165 → "0.16", as on the Mac).
    /// </summary>
    public static string Format(double value, int minimumFraction, int maximumFraction, bool grouping, Sign sign)
    {
        if (!double.IsFinite(value) ||
            !decimal.TryParse(value.ToString("R", CultureInfo.InvariantCulture), NumberStyles.Float,
                CultureInfo.InvariantCulture, out var exact))
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }
        var rounded = decimal.Round(exact, maximumFraction, MidpointRounding.ToEven);
        var pattern = (grouping ? "#,##0" : "0")
            + (maximumFraction > 0 ? "." + new string('0', minimumFraction) + new string('#', maximumFraction - minimumFraction) : "");
        var digits = Math.Abs(rounded).ToString(pattern, CultureInfo.InvariantCulture);
        var prefix = sign switch
        {
            Sign.ExceptZero => rounded > 0 ? "+" : rounded < 0 ? "-" : "",
            _ => double.IsNegative(value) ? "-" : "",
        };
        return prefix + digits;
    }

    /// <summary>
    /// C's <c>%.Nf</c>: the exact binary value of the double, rounded half to
    /// even to <paramref name="digits"/> decimals (0.25 → "0.2", 0.35 → "0.3").
    /// </summary>
    public static string Printf(double value, int digits)
    {
        if (!double.IsFinite(value)) return value.ToString(CultureInfo.InvariantCulture);
        var bits = BitConverter.DoubleToInt64Bits(value);
        var negative = bits < 0;
        var biased = (int)((bits >> 52) & 0x7FF);
        var fraction = bits & 0xF_FFFF_FFFF_FFFFL;
        // value = mantissa × 2^exponent, exactly.
        BigInteger mantissa = biased == 0 ? fraction : fraction | (1L << 52);
        var exponent = biased == 0 ? -1074 : biased - 1075;
        var scaled = mantissa * BigInteger.Pow(10, digits);
        BigInteger whole;
        if (exponent >= 0)
        {
            whole = scaled << exponent;
        }
        else
        {
            var denominator = BigInteger.One << -exponent;
            whole = BigInteger.DivRem(scaled, denominator, out var remainder);
            var comparison = (remainder * 2).CompareTo(denominator);
            if (comparison > 0 || (comparison == 0 && !whole.IsEven)) whole += 1;
        }
        var text = whole.ToString(CultureInfo.InvariantCulture).PadLeft(digits + 1, '0');
        if (digits > 0) text = text[..^digits] + "." + text[^digits..];
        return negative ? "-" + text : text;
    }
}
