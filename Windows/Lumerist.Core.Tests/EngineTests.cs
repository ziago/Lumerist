using Xunit;
using static Lumerist.Core.Tests.KeymapTestSupport;

namespace Lumerist.Core.Tests;

// Engine behaviour lives in Shared/fixtures/gestures (GestureFixtureTests).
// These are the checks a case cannot express: the scroll accumulator, the
// coalescer, mouse button numbers, keymap ownership and a few queries.
// Swift counterparts: ScrollStepTests.swift and the IntentCoalescer tests in
// KeymapTests.swift.

public class ScrollStepAccumulatorTests
{
    [Fact]
    public void NotchedWheelLinesMapOneToOne()
    {
        var accumulator = new ScrollStepAccumulator();
        Assert.Equal(1, accumulator.Steps(1, 10, isContinuous: false, isMomentum: false));
        Assert.Equal(-1, accumulator.Steps(-1, -10, isContinuous: false, isMomentum: false));
        Assert.Equal(3, accumulator.Steps(3, 30, isContinuous: false, isMomentum: false));
        Assert.Equal(0, accumulator.Steps(0, 0, isContinuous: false, isMomentum: false));
    }

    [Fact]
    public void NotchedWheelClampsAtTenStepsPerEvent()
    {
        var accumulator = new ScrollStepAccumulator();
        Assert.Equal(10, accumulator.Steps(25, 250, isContinuous: false, isMomentum: false));
        Assert.Equal(-10, accumulator.Steps(-25, -250, isContinuous: false, isMomentum: false));
        Assert.Equal(10, accumulator.Steps(1e300, 0, isContinuous: false, isMomentum: false));
    }

    [Fact]
    public void FractionalNotchNeverLosesAStep()
    {
        var accumulator = new ScrollStepAccumulator();
        Assert.Equal(1, accumulator.Steps(0.4, 4, isContinuous: false, isMomentum: false));
        Assert.Equal(-1, accumulator.Steps(-0.4, -4, isContinuous: false, isMomentum: false));
        Assert.Equal(0, accumulator.Steps(double.NaN, 4, isContinuous: false, isMomentum: false));
    }

    [Fact]
    public void ContinuousScrollAccumulatesTwelvePointsPerStep()
    {
        Assert.Equal(12, InputOptions.DefaultTrackpadPointsPerStep);
        var accumulator = new ScrollStepAccumulator();
        Assert.Equal(12, accumulator.PointsPerStep);
        Assert.Equal(0, accumulator.Steps(0, 5, isContinuous: true, isMomentum: false));
        Assert.Equal(0, accumulator.Steps(0, 5, isContinuous: true, isMomentum: false));
        // 15 points: one step, 3 points kept.
        Assert.Equal(1, accumulator.Steps(0, 5, isContinuous: true, isMomentum: false));
        // 3 + 9 = 12: exactly one more step.
        Assert.Equal(1, accumulator.Steps(0, 9, isContinuous: true, isMomentum: false));
        Assert.Equal(0, accumulator.Steps(0, 11, isContinuous: true, isMomentum: false));
    }

    [Fact]
    public void ContinuousScrollClampsLargeSwipes()
    {
        var accumulator = new ScrollStepAccumulator();
        Assert.Equal(10, accumulator.Steps(0, 600, isContinuous: true, isMomentum: false));
    }

    [Fact]
    public void DirectionReversalDiscardsTheResidual()
    {
        var accumulator = new ScrollStepAccumulator();
        Assert.Equal(0, accumulator.Steps(0, 10, isContinuous: true, isMomentum: false));
        // Without a reset, 10 - 5 would leave +5; reversal starts again from -5.
        Assert.Equal(0, accumulator.Steps(0, -5, isContinuous: true, isMomentum: false));
        Assert.Equal(-1, accumulator.Steps(0, -7, isContinuous: true, isMomentum: false));
    }

    [Fact]
    public void MomentumIsIgnoredAndKeepsTheResidual()
    {
        var accumulator = new ScrollStepAccumulator();
        Assert.Equal(0, accumulator.Steps(0, 10, isContinuous: true, isMomentum: false));
        Assert.Equal(0, accumulator.Steps(0, 500, isContinuous: true, isMomentum: true));
        Assert.Equal(0, accumulator.Steps(5, 50, isContinuous: false, isMomentum: true));
        Assert.Equal(1, accumulator.Steps(0, 2, isContinuous: true, isMomentum: false));
    }

    [Fact]
    public void ResetDiscardsTheResidual()
    {
        var accumulator = new ScrollStepAccumulator();
        Assert.Equal(0, accumulator.Steps(0, 10, isContinuous: true, isMomentum: false));
        accumulator.Reset();
        Assert.Equal(0, accumulator.Steps(0, 2, isContinuous: true, isMomentum: false));
    }

    [Fact]
    public void TrackpadSensitivityChangesTheStepSizeWithinItsRange()
    {
        var accumulator = new ScrollStepAccumulator { PointsPerStep = 6 };
        Assert.Equal(2, accumulator.Steps(0, 12, isContinuous: true, isMomentum: false));
        accumulator.PointsPerStep = 1; // clamped to the minimum of 4
        Assert.Equal(InputOptions.TrackpadPointsPerStepMinimum, accumulator.PointsPerStep);
        accumulator.PointsPerStep = 100;
        Assert.Equal(InputOptions.TrackpadPointsPerStepMaximum, accumulator.PointsPerStep);
        accumulator.PointsPerStep = double.NaN;
        Assert.Equal(InputOptions.TrackpadPointsPerStepMinimum, accumulator.PointsPerStep);
    }
}

public class IntentCoalescerTests
{
    [Fact]
    public void SumsConsecutiveAdjustmentsAndKeepsOrder()
    {
        var coalescer = new IntentCoalescer();
        Assert.False(coalescer.Add(new KeyIntent.Adjust(Dev("Exposure"), 0.05)));
        coalescer.Add(new KeyIntent.Adjust(Dev("Exposure"), 0.05));
        coalescer.Add(new KeyIntent.Adjust(Dev("Contrast"), 1));
        coalescer.Add(new KeyIntent.Adjust(Dev("Exposure"), 0.05));
        Assert.Equal(
        new KeyIntent[]
        {
            new KeyIntent.Adjust(Dev("Exposure"), 0.1),
            new KeyIntent.Adjust(Dev("Contrast"), 1),
            new KeyIntent.Adjust(Dev("Exposure"), 0.05),
        }, coalescer.Flush());
        Assert.True(coalescer.IsEmpty);
    }

    [Fact]
    public void SplitsAboveTheLimitAndDropsZero()
    {
        var coalescer = new IntentCoalescer();
        for (var i = 0; i < 5; i++) coalescer.Add(new KeyIntent.Adjust(Dev("Exposure"), 0.5));
        Assert.Equal(
        new KeyIntent[]
        {
            new KeyIntent.Adjust(Dev("Exposure"), 1),
            new KeyIntent.Adjust(Dev("Exposure"), 1),
            new KeyIntent.Adjust(Dev("Exposure"), 0.5),
        }, coalescer.Flush());
        coalescer.Add(new KeyIntent.Adjust(Dev("Tint"), 2));
        coalescer.Add(new KeyIntent.Adjust(Dev("Tint"), -2));
        Assert.Empty(coalescer.Flush());
    }

    [Fact]
    public void OtherIntentsAskForAnImmediateFlushInOrder()
    {
        var coalescer = new IntentCoalescer();
        coalescer.Add(new KeyIntent.GestureBegan(Dev("Exposure")));
        coalescer.Add(new KeyIntent.Adjust(Dev("Exposure"), 0.05));
        Assert.True(coalescer.Add(new KeyIntent.GestureEnded(Dev("Exposure"))));
        Assert.Equal(
        new KeyIntent[]
        {
            new KeyIntent.GestureBegan(Dev("Exposure")),
            new KeyIntent.Adjust(Dev("Exposure"), 0.05),
            new KeyIntent.GestureEnded(Dev("Exposure")),
        }, coalescer.Flush());
    }

    [Fact]
    public void RoundingKeepsFourDecimalsAndRoundsHalvesAwayFromZero()
    {
        Assert.Equal(0.1235, IntentCoalescer.Round(0.12345));
        Assert.Equal(-0.1235, IntentCoalescer.Round(-0.12345));
        Assert.Equal(0.0001, IntentCoalescer.Round(0.00005));
        Assert.Equal(0, IntentCoalescer.Round(double.PositiveInfinity));
        Assert.Equal(0, IntentCoalescer.Round(double.NaN));
    }

    [Fact]
    public void UpdateRateRangeIsSensible()
    {
        Assert.InRange(IntentCoalescer.DefaultRate, IntentCoalescer.RateMinimum, IntentCoalescer.RateMaximum);
        Assert.True(IntentCoalescer.RateMinimum >= 1);
    }
}

public class KeyGestureEngineTests
{
    private static readonly EngineContext Develop = new();
    private static readonly EngineContext Masking = new(Tool: "masking");
    private static readonly KeyChord Q = new(ScanCodes.Q);
    private static readonly KeyChord H = new(ScanCodes.H);

    private static KeyGestureEngine<string>.Outcome Down(KeyGestureEngine<string> engine, KeyChord chord,
        EngineContext? context = null) =>
        engine.KeyDown(chord, KeyNames.UsName(chord.ScanCode) + "↓", false, 0, context ?? Develop, () => false);

    private static KeyGestureEngine<string>.Outcome Wheel(KeyGestureEngine<string> engine, double lines,
        EngineContext? context = null) =>
        engine.Scroll(lines, lines * 10, isContinuous: false, isMomentum: false, KeyModifiers.None, context ?? Develop);

    [Fact]
    public void ContextDefaultsToDevelopWithLightroomInFront()
    {
        var context = new EngineContext();
        Assert.True(context.IsLightroomFrontmost);
        Assert.Equal(ParameterScope.Develop, context.Scope);
        Assert.False(context.IsMasking);
        Assert.Equal(ParameterScope.Library, new EngineContext(Module: "library").Scope);
        Assert.Null(new EngineContext(Module: "map").Scope);
        Assert.True(Masking.IsMasking);
    }

    [Fact]
    public void MouseButtonNumbersMatchTheButtons()
    {
        Assert.Equal(MouseButton.Left, MouseButton.FromButtonNumber(0));
        Assert.Equal(MouseButton.Right, MouseButton.FromButtonNumber(1));
        Assert.Equal(MouseButton.Middle, MouseButton.FromButtonNumber(2));
        Assert.Equal(MouseButton.Back, MouseButton.FromButtonNumber(3));
        Assert.Equal(MouseButton.Forward, MouseButton.FromButtonNumber(4));
        Assert.Equal(MouseButton.Other(7), MouseButton.FromButtonNumber(7));
        // Windows names the side buttons XBUTTON1 (back) and XBUTTON2 (forward).
        Assert.Equal(MouseButton.Back, MouseButton.FromXButton(1));
        Assert.Equal(MouseButton.Forward, MouseButton.FromXButton(2));
        Assert.Equal(MouseButtonKind.Other, MouseButton.FromXButton(3).Kind);
        Assert.NotEqual(MouseButton.Back, MouseButton.Other(3));
        Assert.Equal(MouseButton.Left, default(MouseButton));
    }

    [Fact]
    public void TheEngineOwnsItsKeymap()
    {
        var keymap = Keymap.StandardLayout();
        var engine = new KeyGestureEngine<string>(keymap);
        // Changing the caller's keymap afterwards does not reach the engine.
        keymap.UpdateSlot(Dev("Exposure"), slot => slot with { Dial = H });
        keymap.Input.InvertWheel = true;
        Down(engine, Q);
        Assert.Equal(Dev("Exposure"), engine.ActiveParameter);
        Assert.Equal(new KeyIntent.Adjust(Dev("Exposure"), 0.05), Wheel(engine, 1).Intents[^1]);
        engine.KeyUp(ScanCodes.Q, "Q↑", 0, Develop);

        // Nor does changing the copy the engine hands out.
        var copy = engine.Keymap;
        copy.UpdateSlot(Dev("Exposure"), slot => slot with { Dial = H });
        Assert.Equal(Q, engine.Keymap.Slot(Dev("Exposure"))!.Dial);

        // Update takes a copy too.
        engine.Update(keymap);
        keymap.UpdateSlot(Dev("Exposure"), slot => slot with { Dial = Q });
        keymap.Input.InvertWheel = false;
        Assert.IsType<KeyGestureEngine<string>.Decision.Pass>(Down(engine, Q).Decision);
        Down(engine, H);
        Assert.Equal(new KeyIntent.Adjust(Dev("Exposure"), -0.05), Wheel(engine, 1).Intents[^1]);
    }

    [Fact]
    public void MouseMovesMatterOnlyWhileADialKeyIsHeld()
    {
        var engine = new KeyGestureEngine<string>(Keymap.StandardLayout());
        Assert.False(engine.NeedsMouseMoves);
        Down(engine, Q);
        Assert.True(engine.NeedsMouseMoves);
        Wheel(engine, 1);
        Assert.True(engine.NeedsMouseMoves);
        engine.KeyUp(ScanCodes.Q, "Q↑", 0, Develop);
        Assert.False(engine.NeedsMouseMoves);

        // A key without a mask slider holds nothing to drag.
        Down(engine, new KeyChord(ScanCodes.Digit3), Masking);
        Assert.True(engine.IsEngaged);
        Assert.False(engine.NeedsMouseMoves);
        engine.KeyUp(ScanCodes.Digit3, "3↑", 0, Masking);

        Down(engine, KeyChord.LeftOf1);
        Assert.True(engine.IsPaletteOpen);
        Assert.False(engine.NeedsMouseMoves);
    }

    [Fact]
    public void HeldEventsListEveryKeyHeldBack()
    {
        var engine = new KeyGestureEngine<string>(Keymap.StandardLayout());
        Down(engine, KeyChord.Tab);
        Assert.Equal(new[] { "Tab↓" }, engine.HeldEvents);
        engine.KeyUp(ScanCodes.Tab, "Tab↑", 0, Develop);

        Down(engine, Q);
        Assert.Equal(new[] { "Q↓" }, engine.HeldEvents);
        engine.KeyUp(ScanCodes.Q, "Q↑", 0, Develop);

        Down(engine, new KeyChord(ScanCodes.Digit3), Masking);
        Assert.Equal(new[] { "3↓" }, engine.HeldEvents);
        Wheel(engine, 1, Masking); // scrolled: the key is no longer replayed
        Assert.Empty(engine.HeldEvents);
    }

    [Fact]
    public void ResetForgetsEveryGestureButNotThePause()
    {
        var engine = new KeyGestureEngine<string>(Keymap.StandardLayout());
        Down(engine, Q);
        Wheel(engine, 1);
        engine.Reset();
        Assert.False(engine.IsEngaged);
        Assert.False(engine.NeedsScrollEvents);
        Assert.Null(engine.ActiveParameter);
        Assert.IsType<KeyGestureEngine<string>.Decision.Pass>(engine.KeyUp(ScanCodes.Q, "Q↑", 0, Develop).Decision);

        Down(engine, KeyChord.ControlEscape);
        engine.Reset();
        Assert.True(engine.IsPaused);
    }

    [Fact]
    public void LosingLightroomClosesThePaletteAndDeliversItsKey()
    {
        var engine = new KeyGestureEngine<string>(Keymap.StandardLayout());
        Down(engine, KeyChord.LeftOf1);
        var away = Down(engine, H, new EngineContext(IsLightroomFrontmost: false));
        Assert.Equal(new KeyGestureEngine<string>.Decision.Replace(["`↓", "H↓"]), away.Decision);
        Assert.Equal(new KeyIntent[] { new KeyIntent.ClosePalette() }, away.Intents);
        Assert.False(engine.IsPaletteOpen);
    }

    [Fact]
    public void AnyEventTypeWorks()
    {
        var engine = new KeyGestureEngine<int>(Keymap.StandardLayout());
        var develop = new EngineContext();
        Assert.IsType<KeyGestureEngine<int>.Decision.Consume>(
            engine.KeyDown(Q, 1, false, 0, develop, () => false).Decision);
        Assert.Equal(new[] { 1 }, engine.HeldEvents);
        Assert.Equal(new KeyGestureEngine<int>.Decision.Replace([1, 2]), engine.KeyUp(ScanCodes.Q, 2, 0, develop).Decision);
    }
}
