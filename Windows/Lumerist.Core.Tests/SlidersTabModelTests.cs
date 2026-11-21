using Xunit;
using static Lumerist.Core.Tests.KeymapTestSupport;

namespace Lumerist.Core.Tests;

// The Sliders tab (Views/SlidersTab.swift), the chord recorder
// (Design/KeySlot.swift) and the named choices of the settings screens.

public sealed class SlidersTabModelTests
{
    private static ParameterDescriptor Exposure => ParameterCatalog.Descriptor(Dev("Exposure"))!;

    [Fact]
    public void StepsFollowTheLargeStepSwitch()
    {
        var plain = StepPair.For(Exposure, null, swapped: false);
        Assert.Equal((0.05, 0.33), (plain.Plain, plain.Shifted));
        Assert.False(plain.IsCustom);
        var swapped = StepPair.For(Exposure, new ParameterSlot(CoarseStep: 0.5), swapped: true);
        Assert.Equal((0.5, 0.05), (swapped.Plain, swapped.Shifted));
        Assert.Equal((0.33, 0.05), (swapped.DefaultPlain, swapped.DefaultShifted));
        Assert.True(swapped.IsCustom);
    }

    [Theory]
    [InlineData(1.0, "1")]
    [InlineData(0.05, "0.05")]
    [InlineData(0.333, "0.33")]
    [InlineData(2.5, "2.5")]
    public void StepText(double value, string expected) => Assert.Equal(expected, StepPair.Text(value));

    [Fact]
    public void TypedStepsAreClampedAndTheDefaultMeansNoOverride()
    {
        Assert.Null(StepPair.Stored(Exposure, 0.05, normal: true));
        Assert.Equal(2.0, StepPair.Stored(Exposure, 9, normal: true));
        Assert.Equal(0.01, StepPair.Stored(Exposure, 0, normal: false));
        Assert.Equal(0.5, StepPair.Parse(" 0,5 "));
        Assert.Null(StepPair.Parse("abc"));
        Assert.Equal("0.125", StepPair.FieldText(0.125));
    }

    [Fact]
    public void UnitsLegendAndChoices()
    {
        Assert.Equal("pt · 50 K", SlidersTabText.UnitLabel("temp"));
        Assert.Equal("pt (50 K each)", SlidersTabText.EditorUnit("temp"));
        Assert.Equal("EV", SlidersTabText.UnitLabel("EV"));
        Assert.Equal("Click a box, then press a key · hold Tab as well for a combo", SlidersTabText.Legend(KeyChord.Tab));
        Assert.Equal("Click a box, then press a key · combos need a Combo Key", SlidersTabText.Legend(null));
        Assert.True(SlidersTabText.IsChoice(ParameterCatalog.Descriptor(Dev("PostCropVignetteStyle"))!));
        Assert.False(SlidersTabText.IsChoice(Exposure));
    }

    [Fact]
    public void SearchMatchesNamesSectionsAndKeys()
    {
        var keymap = Keymap.StandardLayout();
        Assert.Equal(ParameterCatalog.Sections(ParameterScope.Develop).Count,
            SlidersTabText.FilteredSections(keymap, ParameterScope.Develop, " ").Count);
        var byName = SlidersTabText.FilteredSections(keymap, ParameterScope.Develop, "expo");
        Assert.Contains(byName, section => section.Parameters.Any(parameter => parameter.Id == "Exposure"));
        var bySection = SlidersTabText.FilteredSections(keymap, ParameterScope.Develop, "tone curve");
        Assert.Equal(5, Assert.Single(bySection).Parameters.Count);
        var byKey = SlidersTabText.FilteredSections(keymap, ParameterScope.Library, "Q");
        Assert.Contains(byKey.Single().Parameters, parameter => parameter.Id == "Exposure");
        Assert.Empty(SlidersTabText.FilteredSections(keymap, ParameterScope.Develop, "zzz"));
        Assert.Equal(13, SlidersTabText.AssignedCount(ParameterCatalog.Sections(ParameterScope.Develop)[0].Parameters, keymap));
    }
}

public sealed class ChordRecordingTests
{
    [Fact]
    public void APlainKeyIsRecordedWithItsModifiers()
    {
        var recording = new ChordRecording(KeyChord.Tab);
        Assert.Equal(ChordRecording.Outcome.Finish, recording.KeyDown(Key(ScanCodes.Q, KeyModifiers.Shift), isRepeat: false));
        Assert.Equal(Key(ScanCodes.Q, KeyModifiers.Shift), recording.Chord);
    }

    [Fact]
    public void EscCancelsBackspaceAndDeleteClear()
    {
        Assert.Equal(ChordRecording.Outcome.Cancel, new ChordRecording(null).KeyDown(Key(ScanCodes.Escape), false));
        Assert.Equal(ChordRecording.Outcome.Clear, new ChordRecording(null).KeyDown(Key(ScanCodes.Backspace), false));
        Assert.Equal(ChordRecording.Outcome.Clear, new ChordRecording(null).KeyDown(Key(ChordRecording.DeleteScanCode), false));
        // With a modifier they are ordinary keys (Ctrl+Esc is the pause key).
        var pause = new ChordRecording(null);
        Assert.Equal(ChordRecording.Outcome.Finish, pause.KeyDown(KeyChord.ControlEscape, false));
        Assert.Equal(KeyChord.ControlEscape, pause.Chord);
    }

    [Fact]
    public void HoldingTheComboKeyRecordsACombo()
    {
        var recording = new ChordRecording(KeyChord.Tab);
        Assert.Equal(ChordRecording.Outcome.Continue, recording.KeyDown(KeyChord.Tab, false));
        Assert.True(recording.IsComboKeyDown);
        Assert.Equal(ChordRecording.Outcome.Continue, recording.KeyDown(KeyChord.Tab, true));
        Assert.Equal(ChordRecording.Outcome.Finish, recording.KeyDown(Key(0x25, KeyModifiers.Shift), false));
        Assert.Equal(Key(0x25, KeyModifiers.Shift | KeyModifiers.Combo), recording.Chord);
    }

    [Fact]
    public void TheComboKeyAloneRecordsItself()
    {
        var recording = new ChordRecording(KeyChord.Tab);
        recording.KeyDown(KeyChord.Tab, false);
        Assert.Equal(ChordRecording.Outcome.Continue, recording.KeyUp(ScanCodes.Q));
        Assert.Equal(ChordRecording.Outcome.Finish, recording.KeyUp(ScanCodes.Tab));
        Assert.Equal(KeyChord.Tab, recording.Chord);
        // Without a Combo Key, Tab is a plain key.
        var plain = new ChordRecording(null);
        Assert.Equal(ChordRecording.Outcome.Finish, plain.KeyDown(KeyChord.Tab, false));
    }
}

public sealed class SettingsChoicesTests
{
    [Fact]
    public void SmoothnessRatesAndTheNearestChoice()
    {
        Assert.Equal([15.0, 30.0, 50.0], SettingsChoices.AllSmoothness.Select(choice => choice.Hz()));
        Assert.Equal(Smoothness.Balanced, SettingsChoices.Nearest(IntentCoalescer.DefaultRate));
        Assert.Equal(Smoothness.Light, SettingsChoices.Nearest(10));
        Assert.Equal(Smoothness.Smooth, SettingsChoices.Nearest(60));
        Assert.Equal(Smoothness.Light, SettingsChoices.Nearest(22.5));
    }

    [Fact]
    public void ResetClickFoldsBothOptions()
    {
        foreach (var choice in SettingsChoices.AllResetClicks)
        {
            var input = new InputOptions { DoubleClickReset = choice.DoubleClick(), MiddleClickReset = choice.MiddleClick() };
            Assert.Equal(choice, SettingsChoices.ResetClickOf(input));
        }
    }

    [Fact]
    public void SlidersAndHideAfter()
    {
        Assert.Equal(25, SettingsChoices.Stepped(24.6, 1, 30, 1));
        Assert.Equal(30, SettingsChoices.Stepped(44, 1, 30, 1));
        Assert.Equal(0.35, SettingsChoices.Stepped(0.33, 0, 1, 0.05), 9);
        Assert.Equal(4.1, SettingsChoices.HideAfterNeverValue(4), 9);
        Assert.True(SettingsChoices.HideAfterIsNever(4.06, 4));
        Assert.False(SettingsChoices.HideAfterIsNever(4.0, 4));
        Assert.Equal("1.2 s", SettingsChoices.Seconds(1.2));
    }
}
