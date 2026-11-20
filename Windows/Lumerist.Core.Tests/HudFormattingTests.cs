using Lumerist.Core;
using Xunit;

namespace Lumerist.Core.Tests;

/// <summary>
/// The Value HUD's numbers (the Mac's OverlayFormatting in ValueHUD.swift):
/// signs, decimals per unit, and the Mac's rounding (half to even).
/// </summary>
public class HudFormattingTests
{
    [Theory]
    [InlineData(0.35, "+0.35")]
    [InlineData(-1.5, "-1.50")]
    [InlineData(0, "0.00")]
    [InlineData(-0.001, "0.00")] // rounds to zero: no sign
    [InlineData(0.125, "+0.12")] // half to even
    [InlineData(0.165, "+0.16")] // the shortest decimal, 0.165, rounded half to even
    [InlineData(0.135, "+0.14")]
    [InlineData(4.999, "+5.00")]
    public void ExposureHasASignAndTwoDecimals(double value, string expected) =>
        Assert.Equal(expected, HudFormatting.Number(value, "EV"));

    [Theory]
    [InlineData(5_600.4, "5600")]
    [InlineData(5_600.5, "5601")] // rounded first, halves away from zero
    [InlineData(12_500, "12500")] // no thousands separator
    public void KelvinIsWholeWithoutGrouping(double value, string expected) =>
        Assert.Equal(expected, HudFormatting.Number(value, "K"));

    [Theory]
    [InlineData(12, "+12")]
    [InlineData(-12, "-12")]
    [InlineData(12.04, "+12")] // close enough to whole
    [InlineData(12.96, "+13")]
    [InlineData(12.3, "+12.3")]
    [InlineData(-0.5, "-0.5")]
    [InlineData(0, "0")]
    [InlineData(1_234, "+1,234")]
    public void PointsAreWholeOrOneDecimal(double value, string expected) =>
        Assert.Equal(expected, HudFormatting.Number(value, "pt"));

    [Theory]
    [InlineData(1.25, "deg", "+1.2")]
    [InlineData(-45, "deg", "-45.0")]
    [InlineData(3, "px", "+3.0")]
    [InlineData(0.04, "°", "0.0")]
    public void DegreesAndPixelsHaveOneDecimal(double value, string unit, string expected) =>
        Assert.Equal(expected, HudFormatting.Number(value, unit));

    [Theory]
    [InlineData(-5, "EV", "-5.00")]
    [InlineData(5, "EV", "+5.00")]
    [InlineData(-100, "pt", "-100")]
    [InlineData(100, "pt", "100")]
    [InlineData(0.5, "pt", "0.5")]
    [InlineData(2.25, "pt", "2.2")]
    [InlineData(1_500, "%", "1,500")]
    [InlineData(2_000, "K", "2000")]
    [InlineData(50_000, "K", "50000")]
    public void RangeEnds(double value, string unit, string expected) =>
        Assert.Equal(expected, HudFormatting.RangeNumber(value, unit));

    [Theory]
    [InlineData("EV", "EV")]
    [InlineData("K", "K")]
    [InlineData("px", "px")]
    [InlineData("%", "%")]
    [InlineData("deg", "°")]
    [InlineData("pt", null)]
    [InlineData("temp", null)]
    public void UnitSuffixes(string unit, string? expected) => Assert.Equal(expected, HudFormatting.UnitSuffix(unit));

    [Fact]
    public void ValueTextJoinsNumberAndUnit()
    {
        Assert.Equal("+0.35 EV", HudFormatting.ValueText(0.35, "EV"));
        Assert.Equal("+12", HudFormatting.ValueText(12, "pt"));
        Assert.Equal("-3.5 °", HudFormatting.ValueText(-3.5, "deg"));
    }

    [Theory]
    [InlineData(0.25, 1, "0.2")]
    [InlineData(0.75, 1, "0.8")]
    [InlineData(0.35, 1, "0.3")]
    [InlineData(-0.25, 1, "-0.2")]
    [InlineData(2.5, 1, "2.5")]
    [InlineData(2.5, 0, "2")]
    [InlineData(3.5, 0, "4")]
    [InlineData(-0.04, 1, "-0.0")]
    [InlineData(1e20, 1, "100000000000000000000.0")]
    public void PrintfRoundsTheExactValueHalfToEven(double value, int digits, string expected) =>
        Assert.Equal(expected, HudNumberText.Printf(value, digits));
}
