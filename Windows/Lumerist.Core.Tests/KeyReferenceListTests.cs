using Lumerist.Core;
using Xunit;

namespace Lumerist.Core.Tests;

/// <summary>
/// The Key Reference's rows, search and pages (the Mac's KeyReferenceView).
/// </summary>
public class KeyReferenceListTests
{
    [Fact]
    public void DevelopListsSystemKeysThenDialKeysThenTheCombos()
    {
        var keymap = Keymap.StandardLayout();
        var (regular, combos) = KeyReferenceList.Entries(keymap, ParameterScope.Develop);
        Assert.Equal(
            [
                new KeyReferenceEntry("system.comboKey", ["Tab"], "Combo Key", "System"),
                new KeyReferenceEntry("system.paletteKey", ["`"], "Palette Key", "System"),
                new KeyReferenceEntry("system.keyReferenceToggle", ["Tab + H"], "Key Reference toggle", "System"),
                new KeyReferenceEntry("system.pauseToggle", ["Ctrl+Esc"], "Pause toggle", "System"),
            ],
            regular.Take(4));
        Assert.Equal(25, regular.Count);
        // Dial Keys in catalog order: Temperature comes first.
        Assert.Equal(new KeyReferenceEntry("dial.Temperature", ["1"], "Temperature", "Dial"), regular[4]);
        Assert.All(regular.Skip(4), entry => Assert.Equal("Dial", entry.Kind));
        // The four Develop combos show only their own key; the search also knows "Tab + A".
        Assert.Equal(4, combos.Count);
        var autoTone = combos.Single(entry => entry.Title == "Auto Tone");
        Assert.Equal(["A"], autoTone.Keys);
        Assert.Equal("Command", autoTone.Kind);
        Assert.Equal("Tab + A", autoTone.SearchText);
        Assert.Equal("command.8022e100-5e15-408a-a02c-9796e4b60cba", autoTone.Id);
    }

    [Fact]
    public void LibraryHasNoPaletteKeyAndNoDevelopCommands()
    {
        var (regular, combos) = KeyReferenceList.Entries(Keymap.StandardLayout(), ParameterScope.Library);
        Assert.DoesNotContain(regular, entry => entry.Id == "system.paletteKey");
        Assert.Equal(3 + 12, regular.Count);
        Assert.Empty(combos);
    }

    [Fact]
    public void ClearedSystemKeysAreLeftOut()
    {
        var keymap = Keymap.StandardLayout();
        keymap.SystemKeys.PauseToggle = null;
        var (regular, _) = KeyReferenceList.Entries(keymap, ParameterScope.Develop);
        Assert.DoesNotContain(regular, entry => entry.Id == "system.pauseToggle");
    }

    [Fact]
    public void NudgeKeysSitOnTheirSide()
    {
        var keymap = Keymap.StandardLayout();
        var exposure = new ParameterKey(ParameterScope.Develop, "Exposure");
        keymap.UpdateSlot(exposure, slot => slot with
        {
            NudgeDown = new KeyChord(0x1A),
            NudgeUp = new KeyChord(0x1B, KeyModifiers.Combo),
        });
        keymap.UpdateSlot(new ParameterKey(ParameterScope.Develop, "Contrast"), slot => slot with
        {
            NudgeDown = new KeyChord(0x27),
            NudgeUp = new KeyChord(0x28),
        });
        var (regular, combos) = KeyReferenceList.Entries(keymap, ParameterScope.Develop);
        Assert.Contains(new KeyReferenceEntry("nudge.Exposure", ["−["], "Exposure", "Nudge", "["), regular);
        Assert.Contains(new KeyReferenceEntry("nudge.combo.Exposure", ["+]"], "Exposure", "Nudge", "Tab + ]"), combos);
        Assert.Contains(new KeyReferenceEntry("nudge.Contrast", ["−;", "+'"], "Contrast", "Nudge", "; '"), regular);
    }

    [Fact]
    public void TheCombosComeLastUnderTheirHeader()
    {
        var rows = KeyReferenceList.Rows(Keymap.StandardLayout(), ParameterScope.Develop, "");
        Assert.Equal(30, rows.Count);
        Assert.Equal(new KeyReferenceEntry("header.combos", [], "Combos (hold Tab)", "", IsHeader: true), rows[25]);
        Assert.All(rows.Skip(26), entry => Assert.Equal("Command", entry.Kind));
    }

    [Fact]
    public void WithoutAComboKeyTheHeaderSaysSo()
    {
        var keymap = Keymap.StandardLayout();
        keymap.SystemKeys.ComboKey = null;
        Assert.Equal("Combos (set a Combo Key)", KeyReferenceList.ComboHeader(keymap).Title);
    }

    [Fact]
    public void SearchMatchesTitlesKeysAndFullComboNames()
    {
        var keymap = Keymap.StandardLayout();
        Assert.Equal(["Combos (hold Tab)", "Auto Tone", "Auto White Balance"],
            KeyReferenceList.Rows(keymap, ParameterScope.Develop, "AUTO").Select(entry => entry.Title));
        Assert.Equal(["Combos (hold Tab)", "Auto Tone"],
            KeyReferenceList.Rows(keymap, ParameterScope.Develop, " tab + a ").Select(entry => entry.Title));
        Assert.Equal(["Exposure"], KeyReferenceList.Rows(keymap, ParameterScope.Develop, "expo").Select(entry => entry.Title));
        // Keys: "Esc" is the Pause toggle's.
        Assert.Equal(["Pause toggle"], KeyReferenceList.Rows(keymap, ParameterScope.Develop, "esc").Select(entry => entry.Title));
        Assert.Empty(KeyReferenceList.Rows(keymap, ParameterScope.Develop, "nothing like this"));
        Assert.Equal(30, KeyReferenceList.Rows(keymap, ParameterScope.Develop, "   ").Count);
    }

    [Fact]
    public void PagesHoldFourteenRows()
    {
        var rows = KeyReferenceList.Rows(Keymap.StandardLayout(), ParameterScope.Develop, "");
        var first = KeyReferenceList.Page(rows, 0);
        Assert.Equal(14, first.Rows.Count);
        Assert.Equal(3, first.Count);
        Assert.Equal("1 / 3", KeyReferenceList.PageText(first));
        var beyond = KeyReferenceList.Page(rows, 9);
        Assert.Equal(2, beyond.Index);
        Assert.Equal(2, beyond.Rows.Count);
        Assert.Equal("3 / 3", KeyReferenceList.PageText(beyond));
        var empty = KeyReferenceList.Page([], 4);
        Assert.Empty(empty.Rows);
        Assert.Equal((0, 1), (empty.Index, empty.Count));
    }

    [Fact]
    public void ScopeAndSubtitleFollowLightroom()
    {
        Assert.Equal(ParameterScope.Library, KeyReferenceList.Scope(new LightroomContext("library", "")));
        Assert.Equal(ParameterScope.Develop, KeyReferenceList.Scope(new LightroomContext("", "")));
        Assert.Equal("Develop · mask", KeyReferenceList.Subtitle(new LightroomContext("develop", "masking")));
        Assert.Equal("Develop", KeyReferenceList.Subtitle(new LightroomContext("map", "")));
        Assert.Equal("Library", KeyReferenceList.Subtitle(new LightroomContext("library", "loupe")));
    }
}
