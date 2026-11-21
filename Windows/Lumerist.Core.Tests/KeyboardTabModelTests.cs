using Xunit;
using static Lumerist.Core.Tests.KeymapTestSupport;

namespace Lumerist.Core.Tests;

// The Keyboard tab (Views/KeyboardTab.swift).

public sealed class KeyboardTabModelTests
{
    [Fact]
    public void AssignmentsOfTheStandardLayout()
    {
        var keymap = Keymap.StandardLayout();
        var develop = KeyAssignment.All(keymap, ParameterScope.Develop);
        Assert.Equal(4, develop.Count(item => item.Type == KeyAssignment.Kind.System));
        Assert.Equal(21, develop.Count(item => item.Type == KeyAssignment.Kind.Dial));
        Assert.Equal(4, develop.Count(item => item.Type == KeyAssignment.Kind.Command));
        var exposure = develop.Single(item => item.Usage == new KeyUsage.Dial(Dev("Exposure")));
        Assert.Equal("Q · Exposure, Dial Key: hold it, then scroll, drag or press ← →", exposure.Description(keymap));
        Assert.Equal(new KeyboardReveal.Slider("Exposure"), exposure.Reveal);

        // The Palette is a Develop tool: its key does nothing in Library.
        var library = KeyAssignment.All(keymap, ParameterScope.Library);
        Assert.DoesNotContain(library, item => item.Usage == new KeyUsage.SystemKey(SystemKeyRole.PaletteKey));
        Assert.DoesNotContain(library, item => item.Type == KeyAssignment.Kind.Command);
    }

    [Fact]
    public void NudgeFacesAndModifierPrefixes()
    {
        var keymap = new Keymap();
        keymap.SetSlot(new ParameterSlot(NudgeDown: Key(0x1A), NudgeUp: Key(0x1B, KeyModifiers.Alt)), Dev("Exposure"));
        var items = KeyAssignment.All(keymap, ParameterScope.Develop).Where(item => item.Type != KeyAssignment.Kind.System).ToList();
        Assert.Equal("− Exposure", items[0].FaceText);
        Assert.Equal("Alt + Exposure", items[1].FaceText);
        Assert.Equal("Tab + Alt+] · Exposure, Nudge +: one step up per press",
            (items[1] with { Chord = items[1].Chord.AsCombo }).Line(KeyChord.Tab));
    }

    [Theory]
    [InlineData("Exposure", "Exposure")]
    [InlineData("Red saturation", "Red sat.")]
    [InlineData("Saturation boost amount", "Sat. boost amt.")]
    [InlineData("Auto White Balance", "Auto WB")]
    [InlineData("Paste settings from previous", "Paste sett. from prev.")]
    [InlineData("Luminance NR contrast", "Lum. NR contrast")]
    public void AbbreviationsKeepTheFirstLettersCase(string name, string expected) =>
        Assert.Equal(expected, KeyAssignment.Abbreviated(name));

    [Fact]
    public void QuickPrefixIsDropped()
    {
        Assert.Equal("Exposure", KeyAssignment.DisplayName("Quick exposure"));
        Assert.Equal("Quick ", KeyAssignment.DisplayName("Quick "));
        Assert.Equal("Contrast", KeyAssignment.DisplayName("Contrast"));
    }

    [Fact]
    public void PlainBindingsComeBeforeThoseWithModifiers()
    {
        var plain = new KeyAssignment("a", Key(0x10), KeyAssignment.Kind.Command, new KeyUsage.Action(Guid.Empty, "a"), "a", "a", "", null);
        var shifted = plain with { Id = "b", Chord = Key(0x10, KeyModifiers.Shift), Type = KeyAssignment.Kind.Dial };
        var dial = plain with { Id = "c", Type = KeyAssignment.Kind.Dial };
        Assert.Equal(["c", "a", "b"], KeyAssignment.Sorted([shifted, plain, dial]).Select(item => item.Id));
    }

    [Theory]
    [InlineData(KeyboardShape.Ansi)]
    [InlineData(KeyboardShape.Iso)]
    public void EveryRowIsFifteenUnitsWideAndKeysAreUnique(KeyboardShape shape)
    {
        var spots = KeySpot.Spots(shape);
        foreach (var row in spots.Where(spot => spot.Height == 1).GroupBy(spot => spot.Y))
        {
            var width = row.Sum(spot => spot.Width);
            // ISO: the tall Enter key fills the ends of rows 1 and 2 (narrower at the bottom).
            var expected = row.Key switch
            {
                4 => 12.75,
                1 when shape == KeyboardShape.Iso => 13.5,
                2 when shape == KeyboardShape.Iso => 13.75,
                _ => 15,
            };
            Assert.Equal(expected, width, 6);
        }
        var codes = spots.Select(spot => spot.ScanCode).OfType<int>().ToList();
        Assert.Equal(codes.Count, codes.Distinct().Count());
        Assert.Contains(ScanCodes.Q, codes);
        Assert.Contains(ScanCodes.RightArrow, codes);
        Assert.Equal(shape == KeyboardShape.Iso, codes.Contains(0x56));
        Assert.All(spots, spot => Assert.True(spot.X + spot.Width <= KeySpot.TotalWidth + 1e-9));
    }

    [Fact]
    public void ShapeFollowsTheLayoutLanguage()
    {
        Assert.Equal(KeyboardShape.Ansi, KeySpot.ShapeFor(0x04090409));
        Assert.Equal(KeyboardShape.Iso, KeySpot.ShapeFor(0x0410));
        Assert.Equal(KeyboardShape.Iso, KeySpot.ShapeFor(0x0809));
    }

    [Fact]
    public void ClearTargetsOfferOnlyWhatExists()
    {
        var keymap = Keymap.StandardLayout();
        Assert.Equal([ClearTarget.Dial], ClearTargets.Available(keymap, ParameterScope.Develop));
        keymap.ClearDialKeys(ParameterScope.Develop);
        Assert.Empty(ClearTargets.Available(keymap, ParameterScope.Develop));
        Assert.Equal("Clear every Nudge Key in Library?", ClearTarget.Nudge.Question(ParameterScope.Library));
    }

    [Fact]
    public void RevealHints()
    {
        Assert.True(new KeyboardReveal.SystemKey(SystemKeyRole.PaletteKey).OpensFloatingPanels);
        Assert.False(new KeyboardReveal.SystemKey(SystemKeyRole.PauseToggle).OpensFloatingPanels);
        Assert.Equal("Opens General, where you can change this key", new KeyboardReveal.SystemKey(SystemKeyRole.ComboKey).Hint);
    }
}
