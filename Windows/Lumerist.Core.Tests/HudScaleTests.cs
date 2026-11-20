using Lumerist.Core;
using Xunit;

namespace Lumerist.Core.Tests;

/// <summary>
/// The Value HUD's scale (the Mac's OverlayContent.Scale and Bar in
/// ValueHUD.swift): positions, big and small marks, numbers, Kelvin.
/// </summary>
public class HudScaleTests
{
    private const double Tolerance = 1e-9;

    [Fact]
    public void ExposureHasABigNumberedMarkOnEveryWholeEv()
    {
        var ticks = new HudScale(-5, 5, "EV").Ticks();
        Assert.Equal(21, ticks.Count);
        Assert.Equal(new HudTick(0, true, "−5"), ticks[0]);
        Assert.Equal(new HudTick(0.05, false, null), ticks[1]);
        Assert.Equal(new HudTick(0.5, true, "0"), ticks[10]);
        Assert.Equal(new HudTick(0.6, true, "+1"), ticks[12]);
        Assert.Equal(new HudTick(1, true, "+5"), ticks[20]);
        Assert.Equal(11, ticks.Count(tick => tick.IsMajor));
        Assert.All(ticks.Where(tick => tick.IsMajor), tick => Assert.NotNull(tick.Label));
    }

    [Fact]
    public void AWideExposureRangeHasABigMarkEveryTwoEv()
    {
        var ticks = new HudScale(-10, 10, "EV").Ticks();
        Assert.Equal(21, ticks.Count);
        Assert.Equal(new HudTick(0, true, "−10"), ticks[0]);
        Assert.False(ticks[1].IsMajor); // −9
        Assert.Equal(new HudTick(0.1, true, "−8"), ticks[2]);
        Assert.Equal(new HudTick(1, true, "+10"), ticks[20]);
    }

    [Fact]
    public void PointsUseANiceStepWithHalfStepsBetween()
    {
        // ±100: a rough step of 25 is already nice; 25 splits in two.
        var ticks = new HudScale(-100, 100, "pt").Ticks();
        Assert.Equal(17, ticks.Count);
        Assert.Equal(
            ["−100", "−75", "−50", "−25", "0", "+25", "+50", "+75", "+100"],
            ticks.Where(tick => tick.IsMajor).Select(tick => tick.Label!));
        Assert.Equal(new HudTick(0.0625, false, null), ticks[1]);
    }

    [Fact]
    public void StepsThatStartWithOneOrFiveSplitInFive()
    {
        // 0…100: rough step 12.5 → 20, split in two (10s).
        var twenty = new HudScale(0, 100, "pt").Ticks();
        Assert.Equal(11, twenty.Count);
        Assert.Equal(["0", "+20", "+40", "+60", "+80", "+100"], twenty.Where(tick => tick.IsMajor).Select(tick => tick.Label!));
        // 0…40: rough step 5 → 5, split in five (1s).
        var five = new HudScale(0, 40, "pt").Ticks();
        Assert.Equal(41, five.Count);
        Assert.Equal(9, five.Count(tick => tick.IsMajor));
        Assert.Equal("+5", five[5].Label);
        Assert.False(five[1].IsMajor);
    }

    [Fact]
    public void FractionalNumbersRoundLikePrintf()
    {
        // ±1: a step of 0.25, split in two; %.1f rounds the exact ties to even.
        var ticks = new HudScale(-1, 1, "pt").Ticks();
        Assert.Equal(17, ticks.Count);
        Assert.Equal(
            ["−1", "−0.8", "−0.5", "−0.2", "0", "+0.2", "+0.5", "+0.8", "+1"],
            ticks.Where(tick => tick.IsMajor).Select(tick => tick.Label!));
    }

    [Fact]
    public void KelvinIsLogarithmicWithLabelsInThousands()
    {
        var scale = new HudScale(2_000, 50_000, "K");
        var ticks = scale.Ticks();
        Assert.Equal(10, ticks.Count);
        Assert.Equal(new HudTick(0, false, null), ticks[0]);
        Assert.Equal("3k", ticks[1].Label);
        Assert.True(ticks[1].IsMajor);
        Assert.False(ticks[2].IsMajor); // 4000
        Assert.Equal(["3k", "5k", "10k", "20k", "50k"], ticks.Where(tick => tick.Label is not null).Select(tick => tick.Label!));
        Assert.Equal(new HudTick(1, true, "50k"), ticks[9]);
        Assert.Equal(Math.Log(2.8) / Math.Log(25), scale.Fraction(5_600), Tolerance);
        // Below the range sits at the start.
        Assert.Equal(0, scale.Fraction(1_000));
    }

    [Fact]
    public void KelvinMarksOutsideTheRangeAreLeftOut()
    {
        var ticks = new HudScale(2_500, 12_000, "K").Ticks();
        Assert.Equal(5, ticks.Count); // 3000 4000 5000 7500 10000
    }

    [Fact]
    public void KelvinWithAZeroMinimumIsLinear()
    {
        var scale = new HudScale(0, 100, "K");
        Assert.Equal(0.5, scale.Fraction(50), Tolerance);
    }

    [Fact]
    public void FractionsAreClamped()
    {
        var scale = new HudScale(-5, 5, "EV");
        Assert.Equal(0, scale.Fraction(-9));
        Assert.Equal(1, scale.Fraction(9));
        Assert.Equal(0.535, scale.Fraction(0.35), Tolerance);
    }

    [Fact]
    public void AHugeRangeStopsAfterTwoHundredAndOneMarks()
    {
        Assert.Equal(201, new HudScale(-1_000, 1_000, "EV").Ticks().Count);
    }

    [Fact]
    public void NiceSteps()
    {
        Assert.Equal(0.5, HudScale.NiceStep(0.3), Tolerance);
        Assert.Equal(25, HudScale.NiceStep(25), Tolerance);
        Assert.Equal(10, HudScale.NiceStep(7), Tolerance);
        Assert.Equal(100, HudScale.NiceStep(100), Tolerance);
        Assert.Equal(2, HudScale.NiceStep(1.5), Tolerance);
        Assert.Equal(1, HudScale.NiceStep(0));
        Assert.Equal(1, HudScale.NiceStep(double.NaN));
        Assert.True(HudScale.IsFiveLike(10));
        Assert.True(HudScale.IsFiveLike(0.5));
        Assert.False(HudScale.IsFiveLike(2.5));
        Assert.False(HudScale.IsFiveLike(20));
    }

    [Fact]
    public void LabelsUseATypographicMinusAndAPlus()
    {
        Assert.Equal("0", HudScale.Label(0.00001));
        Assert.Equal("+3", HudScale.Label(3));
        Assert.Equal("−3", HudScale.Label(-3));
        Assert.Equal("+2.5", HudScale.Label(2.5));
        Assert.Equal("−2.5", HudScale.Label(-2.5));
        Assert.Equal("+0.3", HudScale.Label(0.35)); // 0.35 is just below in binary
        Assert.Equal("−0.2", HudScale.Label(-0.25));
        Assert.Equal("+1", HudScale.Label(1.00004));
    }

    [Fact]
    public void BarsMarkZeroOnlyWhenTheRangeSpansIt()
    {
        var exposure = HudBar.Create(0.35, -5, 5, "EV")!;
        Assert.Equal(0.535, exposure.Fraction, Tolerance);
        Assert.Equal(0.5, exposure.Zero!.Value, Tolerance);
        Assert.True(exposure.HasLabels);
        var amount = HudBar.Create(50, 0, 100)!;
        Assert.Null(amount.Zero);
        Assert.Equal(0.5, amount.Fraction, Tolerance);
        Assert.Equal(HudBar.Create(50, 0, 100), amount);
    }

    [Fact]
    public void NoBarForAnEmptyRangeOrABadNumber()
    {
        Assert.Null(HudBar.Create(1, 5, 5));
        Assert.Null(HudBar.Create(1, 5, -5));
        Assert.Null(HudBar.Create(double.NaN, 0, 1));
        Assert.Null(HudBar.Create(0, double.NegativeInfinity, 1));
    }
}
