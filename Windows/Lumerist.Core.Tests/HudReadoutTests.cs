using Lumerist.Core;
using Xunit;

namespace Lumerist.Core.Tests;

/// <summary>
/// What the Value HUD shows for an answer from Lightroom (the Mac's
/// OverlayContent and CommandStore.record in ValueHUD.swift and LumeristApp.swift).
/// </summary>
public class HudReadoutTests
{
    private static LightroomAck Accepted(string parameter, double value, double? minimum = null, double? maximum = null,
        string? unit = null, string? message = "accepted") =>
        new(7, true, message, parameter, ParameterScope.Develop, value, minimum, maximum, unit);

    [Fact]
    public void AnAcceptedChangeShowsItsValueAndScale()
    {
        var readout = HudReadout.FromAck(Accepted("Exposure", 0.35, -5, 5, "EV"));
        Assert.Equal("Exposure", readout.Title);
        Assert.Equal("Exposure", readout.DisplayTitle);
        Assert.False(readout.IsMask);
        Assert.Equal("+0.35", readout.ValueText);
        Assert.Equal("EV", readout.Unit);
        Assert.Null(readout.Detail);
        Assert.Equal(HudAccent.Teal, readout.Accent);
        Assert.Equal(HudBar.Create(0.35, -5, 5, "EV"), readout.Bar);
        Assert.Equal(("-5.00", "+5.00"), readout.RangeText);
        Assert.Null(readout.NoticeIcon);
        Assert.Null(readout.KeyLabel);
        Assert.Null(readout.StepLabel);
    }

    [Fact]
    public void PointsHaveNoUnitAndANoteStaysVisible()
    {
        var readout = HudReadout.FromAck(Accepted("Contrast", 100, -100, 100, message: "at the Contrast slider limit"));
        Assert.Equal("+100", readout.ValueText);
        Assert.Null(readout.Unit);
        Assert.Equal("at the Contrast slider limit", readout.Detail);
        Assert.Equal(("-100", "100"), readout.RangeText);
    }

    [Fact]
    public void AnEmptyMessageIsNoNote()
    {
        Assert.Null(HudReadout.FromAck(Accepted("Contrast", 1, message: "")).Detail);
        Assert.Null(HudReadout.FromAck(Accepted("Contrast", 1, message: null)).Detail);
    }

    [Fact]
    public void WithoutARangeThereIsNoBar()
    {
        var readout = HudReadout.FromAck(Accepted("Clarity", 12));
        Assert.Null(readout.Bar);
        Assert.Null(readout.RangeText);
        Assert.Equal("+12", readout.ValueText);
    }

    [Fact]
    public void MaskSlidersGetAMaskBadge()
    {
        var readout = HudReadout.FromAck(Accepted("local_Exposure", 0.5, -4, 4, "EV"));
        Assert.True(readout.IsMask);
        Assert.Equal("Mask exposure", readout.Title);
        Assert.Equal("Mask · Mask exposure", readout.DisplayTitle);
    }

    [Theory]
    [InlineData("Temperature", HudAccent.Magenta)]
    [InlineData("local_Tint", HudAccent.Magenta)]
    [InlineData("HueAdjustmentRed", HudAccent.Magenta)]
    [InlineData("SaturationAdjustmentBlue", HudAccent.Magenta)]
    [InlineData("Vibrance", HudAccent.Magenta)]
    [InlineData("SplitToningShadowHue", HudAccent.Magenta)]
    [InlineData("Exposure", HudAccent.Teal)]
    [InlineData("Clarity", HudAccent.Teal)]
    [InlineData("", HudAccent.Teal)]
    public void ColourSlidersAreMagenta(string id, HudAccent expected) => Assert.Equal(expected, HudReadout.AccentFor(id));

    [Fact]
    public void LabelsComeFromTheCatalogOrTheId()
    {
        Assert.Equal("Exposure", HudReadout.Label("Exposure"));
        Assert.Equal("Exposure", HudReadout.Label("Exposure", ParameterScope.Library));
        Assert.Equal("Luminance Noise Smoothing", HudReadout.Label("local_LuminanceNoiseSmoothing"));
        Assert.Equal("foo Bar", HudReadout.Label("fooBar"));
        Assert.Null(HudReadout.Label(null));
        Assert.Null(HudReadout.Label(""));
        Assert.Null(HudReadout.Label("local_"));
    }

    [Fact]
    public void AnAnswerWithoutParameterShowsNothing()
    {
        Assert.Null(HudReadout.ForAck(new LightroomAck(3, true, "accepted")));
        Assert.Null(HudReadout.ForAck(new LightroomAck(3, false, "not available")));
    }

    [Fact]
    public void ARefusedChangeShowsLightroomsReasonAsANotice()
    {
        var readout = HudReadout.ForAck(new LightroomAck(3, false, "No photo selected", "Exposure"))!;
        Assert.Equal(HudReadout.Notice("No photo selected"), readout);
        Assert.Equal("Lumerist", readout.Title);
        Assert.Equal(HudAccent.Neutral, readout.Accent);
        Assert.Equal(HudNoticeIcon.Info, readout.NoticeIcon);
        Assert.Equal("Lightroom rejected the change",
            HudReadout.ForAck(new LightroomAck(3, false, null, "Exposure"))!.Detail);
    }

    [Fact]
    public void AnAcceptedChangeIsShownThroughForAck()
    {
        var ack = Accepted("Exposure", 1, -5, 5, "EV");
        Assert.Equal(HudReadout.FromAck(ack), HudReadout.ForAck(ack));
    }

    [Fact]
    public void ARejectedAckReadsAsAnError()
    {
        var readout = HudReadout.FromAck(new LightroomAck(3, false, null, "Exposure"));
        Assert.Equal(HudAccent.Error, readout.Accent);
        Assert.Equal("Rejected by Lightroom", readout.Detail);
        Assert.Equal(HudNoticeIcon.Cross, readout.NoticeIcon);
        Assert.Null(readout.ValueText);
    }

    [Fact]
    public void AnAcceptedAckWithoutValueGetsACheckmark()
    {
        var readout = HudReadout.FromAck(new LightroomAck(3, true, null, "Exposure"));
        Assert.Equal(HudAccent.Teal, readout.Accent);
        Assert.Equal("Accepted", readout.Detail);
        Assert.Equal(HudNoticeIcon.Checkmark, readout.NoticeIcon);
    }

    [Fact]
    public void ANoticeIsNeutral()
    {
        var notice = HudReadout.Notice("Vibrance is not available in a mask");
        Assert.Equal("Lumerist", notice.DisplayTitle);
        Assert.Equal("Vibrance is not available in a mask", notice.Detail);
        Assert.Null(notice.ValueText);
        Assert.Null(notice.Bar);
    }

    [Fact]
    public void TheSampleIsExposureOnQ()
    {
        var sample = HudReadout.Sample;
        Assert.Equal("Exposure", sample.Title);
        Assert.Equal("+0.35", sample.ValueText);
        Assert.Equal("EV", sample.Unit);
        Assert.Equal(("-5.00", "+5.00"), sample.RangeText);
        Assert.Equal("Q", sample.KeyLabel);
        Assert.Equal("Normal", sample.StepLabel);
        Assert.Equal("Coarse", HudStepMode.Coarse.Label());
    }
}
