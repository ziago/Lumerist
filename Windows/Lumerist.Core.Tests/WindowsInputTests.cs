using Lumerist.Core;
using Lumerist.Core.Input;
using Xunit;

namespace Lumerist.Core.Tests;

/// <summary>Hook-event arithmetic of the Windows app (no Windows APIs).</summary>
public class WindowsInputTests
{
    private const int LeftControlScan = 0x1D, LeftMenuScan = 0x38, ShiftScan = 0x2A;

    [Fact]
    public void ModifiersFollowTheHookEvents()
    {
        var tracker = new ModifierTracker();
        Assert.True(tracker.Observe(VirtualKeys.LeftShift, ShiftScan, false, isUp: false));
        Assert.True(tracker.Observe(VirtualKeys.RightControl, LeftControlScan, true, isUp: false));
        Assert.Equal(KeyModifiers.Shift | KeyModifiers.Control, tracker.Current);
        tracker.Observe(VirtualKeys.LeftShift, ShiftScan, false, isUp: true);
        Assert.Equal(KeyModifiers.Control, tracker.Current);
        tracker.Observe(VirtualKeys.RightControl, LeftControlScan, true, isUp: true);
        Assert.Equal(KeyModifiers.None, tracker.Current);
    }

    [Fact]
    public void OrdinaryKeysAreNotModifiers()
    {
        var tracker = new ModifierTracker();
        Assert.False(tracker.Observe(0x51, 0x10, false, isUp: false)); // Q
        Assert.False(tracker.Observe(0x09, 0x0F, false, isUp: false)); // Tab, the Combo Key
        Assert.Equal(KeyModifiers.None, tracker.Current);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AltGrIsNeitherCtrlNorAlt(bool fakeControlFirst)
    {
        var tracker = new ModifierTracker();
        if (fakeControlFirst)
        {
            tracker.Observe(VirtualKeys.LeftControl, ModifierTracker.AltGrFakeControlScanCode, false, isUp: false);
            tracker.Observe(VirtualKeys.RightMenu, LeftMenuScan, true, isUp: false);
        }
        else
        {
            tracker.Observe(VirtualKeys.RightMenu, LeftMenuScan, true, isUp: false);
            tracker.Observe(VirtualKeys.LeftControl, ModifierTracker.AltGrFakeControlScanCode, false, isUp: false);
        }
        Assert.Equal(KeyModifiers.None, tracker.Current);
        Assert.False(tracker.IsMenuKeyDown);
        Assert.Empty(tracker.HeldModifierKeys);

        // Releasing in either order never shows a moment of Alt.
        tracker.Observe(VirtualKeys.LeftControl, ModifierTracker.AltGrFakeControlScanCode, false, isUp: true);
        Assert.Equal(KeyModifiers.None, tracker.Current);
        tracker.Observe(VirtualKeys.RightMenu, LeftMenuScan, true, isUp: true);
        Assert.Equal(KeyModifiers.None, tracker.Current);
    }

    [Fact]
    public void RightAltWithoutAltGrIsAlt()
    {
        var tracker = new ModifierTracker();
        tracker.Observe(VirtualKeys.RightMenu, LeftMenuScan, true, isUp: false);
        Assert.Equal(KeyModifiers.Alt, tracker.Current);
        Assert.True(tracker.IsMenuKeyDown);
    }

    [Fact]
    public void RealCtrlWithAltGrStillCounts()
    {
        var tracker = new ModifierTracker();
        tracker.Observe(VirtualKeys.LeftControl, LeftControlScan, false, isUp: false);
        tracker.Observe(VirtualKeys.LeftControl, ModifierTracker.AltGrFakeControlScanCode, false, isUp: false);
        tracker.Observe(VirtualKeys.RightMenu, LeftMenuScan, true, isUp: false);
        Assert.Equal(KeyModifiers.Control, tracker.Current);
        Assert.Equal([VirtualKeys.LeftControl], tracker.HeldModifierKeys);
    }

    [Fact]
    public void GenericCodesFromInjectedEventsMapToASide()
    {
        var tracker = new ModifierTracker();
        tracker.Observe(VirtualKeys.Control, LeftControlScan, true, isUp: false);
        tracker.Observe(VirtualKeys.Menu, LeftMenuScan, false, isUp: false);
        Assert.Equal([VirtualKeys.RightControl, VirtualKeys.LeftMenu], tracker.HeldModifierKeys);
    }

    [Fact]
    public void WinBelongsToWindows()
    {
        var tracker = new ModifierTracker();
        tracker.Observe(VirtualKeys.LeftWin, 0x5B, true, isUp: false);
        Assert.True(tracker.IsWinDown);
        Assert.True(tracker.IsMenuKeyDown);
        Assert.Equal(KeyModifiers.None, tracker.Current);
        Assert.Empty(tracker.HeldModifierKeys);
        tracker.Reset();
        Assert.False(tracker.IsWinDown);
    }

    [Fact]
    public void ModifiersReleasedOnAnotherDesktopAreForgotten()
    {
        // Win+L: the key-ups happen on the lock screen, where the hook sees nothing.
        var tracker = new ModifierTracker();
        tracker.Observe(VirtualKeys.LeftWin, 0x5B, true, isUp: false);
        tracker.Observe(VirtualKeys.LeftControl, LeftControlScan, false, isUp: false);
        Assert.True(tracker.Retain(key => key == VirtualKeys.LeftControl));
        Assert.False(tracker.IsWinDown);
        Assert.Equal(KeyModifiers.Control, tracker.Current);
        Assert.False(tracker.Retain(_ => true));
    }

    [Fact]
    public void AltGrIsForgottenWithItsKeys()
    {
        var tracker = new ModifierTracker();
        tracker.Observe(VirtualKeys.LeftControl, ModifierTracker.AltGrFakeControlScanCode, false, isUp: false);
        tracker.Observe(VirtualKeys.RightMenu, LeftMenuScan, true, isUp: false);
        tracker.Retain(_ => false);
        tracker.Observe(VirtualKeys.RightMenu, LeftMenuScan, true, isUp: false);
        Assert.Equal(KeyModifiers.Alt, tracker.Current); // A plain right Alt now.
    }

    [Fact]
    public void MasksRoundTrip()
    {
        int[] keys = [VirtualKeys.LeftShift, VirtualKeys.RightControl, VirtualKeys.LeftMenu];
        Assert.Equal(keys, ModifierPlan.Keys(ModifierPlan.Mask(keys)));
        Assert.Equal(0, ModifierPlan.Mask([VirtualKeys.LeftWin, 0x51]));
        var tracker = new ModifierTracker();
        tracker.Observe(VirtualKeys.RightShift, 0x36, false, isUp: false);
        Assert.Equal(ModifierPlan.Mask([VirtualKeys.RightShift]), tracker.HeldMask);
    }

    [Fact]
    public void AReplayedKeyGetsTheModifiersItWasPressedWith()
    {
        // Q was held back with nothing; Shift went down since. Lift Shift, send Q, press Shift again.
        var shift = ModifierPlan.Mask([VirtualKeys.LeftShift]);
        Assert.Equal([(VirtualKeys.LeftShift, true)], ModifierPlan.Transition(shift, 0));
        Assert.Equal([(VirtualKeys.LeftShift, false)], ModifierPlan.Transition(0, shift));
        Assert.Empty(ModifierPlan.Transition(shift, shift));
    }

    [Fact]
    public void AltNeverGoesUpOrDownAlone()
    {
        var alt = ModifierPlan.Mask([VirtualKeys.LeftMenu]);
        var mask = (VirtualKeys.MenuMask, false);
        var maskUp = (VirtualKeys.MenuMask, true);
        Assert.Equal([mask, maskUp, (VirtualKeys.LeftMenu, true)], ModifierPlan.Transition(alt, 0));
        Assert.Equal([(VirtualKeys.LeftMenu, false), mask, maskUp], ModifierPlan.Transition(0, alt));
        // Releases come before presses.
        var control = ModifierPlan.Mask([VirtualKeys.LeftControl]);
        Assert.Equal([mask, maskUp, (VirtualKeys.LeftMenu, true), (VirtualKeys.LeftControl, false)],
            ModifierPlan.Transition(alt, control));
    }

    [Fact]
    public void AShortcutKeepsHeldKeysOfItsOwnFamilies()
    {
        var rightControl = ModifierPlan.Mask([VirtualKeys.RightControl]);
        var shift = ModifierPlan.Mask([VirtualKeys.LeftShift]);
        // Ctrl+Z with right Ctrl and Shift held: keep right Ctrl, drop Shift.
        Assert.Equal(rightControl, ModifierPlan.ForChord(KeyModifiers.Control, rightControl | shift));
        // Ctrl+Alt+V with nothing held: left Ctrl and left Alt.
        Assert.Equal(ModifierPlan.Mask([VirtualKeys.LeftControl, VirtualKeys.LeftMenu]),
            ModifierPlan.ForChord(KeyModifiers.Control | KeyModifiers.Alt, 0));
        Assert.Equal(0, ModifierPlan.ForChord(KeyModifiers.None, shift));
    }

    [Fact]
    public void ARepeatIsAKeyDownForAKeyAlreadyDown()
    {
        var repeats = new KeyRepeatTracker();
        Assert.False(repeats.KeyDown(0x10));
        Assert.True(repeats.KeyDown(0x10));
        Assert.False(repeats.KeyDown(0x11));
        repeats.KeyUp(0x10);
        Assert.False(repeats.KeyDown(0x10));
    }

    [Fact]
    public void ClicksCountWithinTheDoubleClickTimeAndRectangle()
    {
        var clicks = new ClickCounter();
        Assert.Equal(1, clicks.Press(MouseButton.Left, 1_000, 100, 100, 500, 4, 4));
        Assert.Equal(2, clicks.Press(MouseButton.Left, 1_300, 101, 99, 500, 4, 4));
        Assert.Equal(3, clicks.Press(MouseButton.Left, 1_700, 102, 100, 500, 4, 4));
        // Too late, too far, another button.
        Assert.Equal(1, clicks.Press(MouseButton.Left, 2_300, 102, 100, 500, 4, 4));
        Assert.Equal(1, clicks.Press(MouseButton.Left, 2_400, 110, 100, 500, 4, 4));
        Assert.Equal(1, clicks.Press(MouseButton.Right, 2_450, 110, 100, 500, 4, 4));
    }

    [Fact]
    public void ClickTimesSurviveTheTickWrap()
    {
        var clicks = new ClickCounter();
        clicks.Press(MouseButton.Middle, uint.MaxValue - 100, 0, 0, 500, 4, 4);
        Assert.Equal(2, clicks.Press(MouseButton.Middle, 200, 0, 0, 500, 4, 4));
    }

    [Fact]
    public void HooksKeepingUpAreNotSilent()
    {
        var watch = new HookSilenceWatch(1_500);
        Assert.Equal(HookSilence.None, watch.Check(lastInput: 10_000, lastCallback: 10_000));
        Assert.Equal(HookSilence.None, watch.Check(lastInput: 11_500, lastCallback: 10_000));
        // A callback after the last input: the difference wraps, still not silent.
        Assert.Equal(HookSilence.None, watch.Check(lastInput: 10_000, lastCallback: 10_200));
    }

    [Fact]
    public void OneInputWithoutCallbackIsNotSilence()
    {
        // The mouse move that wakes the screen, 26 minutes after the last callback,
        // and then nothing: the watchdog keeps checking the same input.
        var watch = new HookSilenceWatch(1_500);
        const uint lastCallback = 1_000_000, wake = lastCallback + 26 * 60_000;
        Assert.Equal(HookSilence.FirstUnseenInput, watch.Check(wake, lastCallback));
        for (var tick = 0; tick < 5; tick++) Assert.Equal(HookSilence.None, watch.Check(wake, lastCallback));
        // Then input reaches the hooks again.
        Assert.Equal(HookSilence.None, watch.Check(wake + 3_000, wake + 3_000));
    }

    [Fact]
    public void InputThatKeepsMissingTheHooksIsSilence()
    {
        var watch = new HookSilenceWatch(1_500);
        Assert.Equal(HookSilence.FirstUnseenInput, watch.Check(lastInput: 20_000, lastCallback: 18_000));
        Assert.Equal(HookSilence.Silent, watch.Check(lastInput: 20_900, lastCallback: 18_000));
        // Installed again: the next unseen input starts a new wait.
        Assert.Equal(HookSilence.FirstUnseenInput, watch.Check(lastInput: 24_000, lastCallback: 22_000));
    }

    [Fact]
    public void ACallbackInBetweenStartsTheWaitAgain()
    {
        var watch = new HookSilenceWatch(1_500);
        Assert.Equal(HookSilence.FirstUnseenInput, watch.Check(lastInput: 30_000, lastCallback: 20_000));
        Assert.Equal(HookSilence.None, watch.Check(lastInput: 30_500, lastCallback: 30_400));
        Assert.Equal(HookSilence.FirstUnseenInput, watch.Check(lastInput: 33_000, lastCallback: 30_400));
        Assert.Equal(HookSilence.Silent, watch.Check(lastInput: 33_800, lastCallback: 30_400));
    }

    [Fact]
    public void SilenceSurvivesTheTickWrap()
    {
        var watch = new HookSilenceWatch(1_500);
        const uint lastCallback = uint.MaxValue - 1_000;
        Assert.Equal(HookSilence.FirstUnseenInput, watch.Check(lastInput: 1_000, lastCallback));
        Assert.Equal(HookSilence.Silent, watch.Check(lastInput: 1_700, lastCallback));
    }

    [Fact]
    public void ResetForgetsTheFirstUnseenInput()
    {
        var watch = new HookSilenceWatch(1_500);
        Assert.Equal(HookSilence.FirstUnseenInput, watch.Check(lastInput: 50_000, lastCallback: 40_000));
        watch.Reset();
        Assert.Equal(HookSilence.FirstUnseenInput, watch.Check(lastInput: 50_600, lastCallback: 40_000));
    }

    [Fact]
    public void NotchesAreLinesAndOtherDeltasAreContinuous()
    {
        Assert.Equal((1.0, 10.0, false), WheelInput.FromDelta(120));
        Assert.Equal((-2.0, -20.0, false), WheelInput.FromDelta(-240));
        var (lines, points, continuous) = WheelInput.FromDelta(30);
        Assert.Equal(0.25, lines);
        Assert.Equal(2.5, points);
        Assert.True(continuous);
    }

    [Fact]
    public void ScanCodesCarryTheExtendedPrefix()
    {
        Assert.Equal(ScanCodes.RightArrow, HookScanCodes.FromHook(0x4D, isExtended: true));
        Assert.Equal(0x4D, HookScanCodes.FromHook(0x4D, isExtended: false)); // Num 6
        Assert.Equal(0, HookScanCodes.FromHook(0, isExtended: true));
        Assert.True(HookScanCodes.IsExtended(ScanCodes.LeftArrow));
        Assert.Equal(0x4B, HookScanCodes.Low(ScanCodes.LeftArrow));
    }

    [Fact]
    public void FallbacksUseTheKeyThatTypesTheCharacter()
    {
        // US positions without a layout.
        Assert.Equal(new KeyChord(0x2C, KeyModifiers.Control), FallbackShortcuts.For(LightroomCommandName.Undo));
        Assert.Equal(new KeyChord(0x2C, KeyModifiers.Control | KeyModifiers.Shift), FallbackShortcuts.For(LightroomCommandName.Redo));
        // On QWERTZ "z" is the key at the US "y" position (0x15).
        Assert.Equal(new KeyChord(0x15, KeyModifiers.Control),
            FallbackShortcuts.For(LightroomCommandName.Undo, character => character == 'z' ? 0x15 : null));
        Assert.Equal(new KeyChord(ScanCodes.RightArrow), FallbackShortcuts.For(LightroomCommandName.NextPhoto, _ => 0x99));
        Assert.Null(FallbackShortcuts.For(LightroomCommandName.ApplyPreset));
    }
}
