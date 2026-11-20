using Lumerist.Core;
using Xunit;
using static Lumerist.Core.Tests.KeymapTestSupport;

namespace Lumerist.Core.Tests;

// The main window's key editing (Views/KeymapEditing.swift on the Mac).

public sealed class KeymapEditingTests
{
    [Fact]
    public void UsageLabelsNameTheSliderTheCommandKeyAndTheSystemKey()
    {
        Assert.Equal("the Dial Key of Exposure", new KeyUsage.Dial(Dev("Exposure")).Label());
        Assert.Equal("the Nudge − key of Contrast", new KeyUsage.NudgeDown(Dev("Contrast")).Label());
        Assert.Equal("the Nudge + key of Contrast", new KeyUsage.NudgeUp(Dev("Contrast")).Label());
        Assert.Equal("the Command Key “Auto Tone”", new KeyUsage.Action(Guid.Empty, "Auto Tone").Label());
        Assert.Equal("the Pause toggle", new KeyUsage.SystemKey(SystemKeyRole.PauseToggle).Label());
        Assert.Equal("the Dial Key of Mystery", new KeyUsage.Dial(Dev("Mystery")).Label());
    }

    [Fact]
    public void SystemKeyDetailsWriteCombosWithTheComboKey()
    {
        Assert.Contains("(shown as Tab + K)", SystemKeyRole.ComboKey.Detail(KeyChord.Tab));
        Assert.Contains("(shown as Combo + K)", SystemKeyRole.ComboKey.Detail(null));
        Assert.EndsWith("The standard layout uses the combo Tab + H.", SystemKeyRole.KeyReferenceToggle.Detail(KeyChord.Tab));
        Assert.Equal("Hands every key back to Lightroom until you press it again. Handy when you need to type.",
            SystemKeyRole.PauseToggle.Detail(KeyChord.Tab));
    }

    [Fact]
    public void AFreeKeyIsCommittedAndTheCurrentKeymapIsLeftAlone()
    {
        var current = Keymap.StandardLayout();
        var edit = KeymapEditing.PlanAssign(current, Key(0x22), SlotRole.Dial, Dev("Texture")); // G
        var commit = Assert.IsType<KeymapEdit.Commit>(edit);
        Assert.Equal(Key(0x22), commit.Keymap.Slot(Dev("Texture"))?.Dial);
        Assert.Equal(Key(ScanCodes.Digit4), current.Slot(Dev("Texture"))?.Dial);
    }

    [Fact]
    public void ClearingAKeyRemovesAnEmptySlot()
    {
        var edit = KeymapEditing.PlanAssign(Keymap.StandardLayout(), null, SlotRole.Dial, Dev("Exposure"));
        Assert.Null(Assert.IsType<KeymapEdit.Commit>(edit).Keymap.Slot(Dev("Exposure")));
    }

    [Fact]
    public void AKeyInUseAsksToMoveItAndMovingFreesTheOtherSlider()
    {
        var current = Keymap.StandardLayout();
        var edit = KeymapEditing.PlanAssign(current, Key(ScanCodes.Q), SlotRole.Dial, Dev("Texture"));
        var ask = Assert.IsType<KeymapEdit.AskToMove>(edit);
        Assert.Equal("Q is already in use", ask.Title);
        Assert.Equal("Q is the Dial Key of Exposure. Move it here?", ask.Message);
        Assert.True(ask.CanMove);
        Assert.NotNull(ask.Moved);
        Assert.Null(ask.Moved!.Slot(Dev("Exposure")));
        Assert.Equal(Key(ScanCodes.Q), ask.Moved.Slot(Dev("Texture"))?.Dial);
    }

    [Fact]
    public void ASystemKeyInTheWayCannotBeMoved()
    {
        var edit = KeymapEditing.PlanSystemKey(Keymap.StandardLayout(), SystemKeyRole.PaletteKey, KeyChord.Tab);
        var ask = Assert.IsType<KeymapEdit.AskToMove>(edit);
        Assert.False(ask.CanMove);
        Assert.Equal("Tab is the Combo Key. Change that system key first (General → System Keys).", ask.Message);
    }

    [Fact]
    public void AComboOnADialKeyIsRefusedWithItsReason()
    {
        var edit = KeymapEditing.PlanAssign(Keymap.StandardLayout(), Key(0x22, KeyModifiers.Combo), SlotRole.Dial, Dev("Texture"));
        Assert.Equal(new ComboMisuse.DialKey(Dev("Texture")).Message, Assert.IsType<KeymapEdit.Refuse>(edit).Message);
    }

    [Fact]
    public void ASystemKeyOnASliderKeyPointsToTheSystemKeys()
    {
        var edit = KeymapEditing.PlanAssign(Keymap.StandardLayout(), KeyChord.Tab, SlotRole.Dial, Dev("Texture"));
        var ask = Assert.IsType<KeymapEdit.AskToMove>(edit);
        Assert.False(ask.CanMove);
        Assert.Null(ask.Moved);
        Assert.Equal("Tab is the Combo Key. Change that system key first (General → System Keys).", ask.Message);
    }

    [Fact]
    public void RemoveChordKeepsTheUsageBeingAssignedAndOtherScopes()
    {
        var keymap = Keymap.StandardLayout();
        keymap.RemoveChord(Key(ScanCodes.Q), ParameterScope.Develop, new KeyUsage.Dial(Dev("Contrast")));
        Assert.Null(keymap.Slot(Dev("Exposure")));
        Assert.Equal(Key(ScanCodes.Q), keymap.Slot(Lib("Exposure"))?.Dial);
        var autoTone = keymap.CommandKeys[0];
        keymap.RemoveChord(autoTone.Chord, ParameterScope.Develop, new KeyUsage.Action(autoTone.Id, autoTone.Title));
        Assert.Contains(keymap.CommandKeys, key => key.Id == autoTone.Id);
        keymap.RemoveChord(autoTone.Chord, ParameterScope.Develop, null);
        Assert.DoesNotContain(keymap.CommandKeys, key => key.Id == autoTone.Id);
    }

    [Fact]
    public void ConflictIndexFindsTheMessageOfEachUsage()
    {
        var keymap = Keymap.StandardLayout();
        keymap.SetSlot(new ParameterSlot(NudgeUp: Key(ScanCodes.Q)), Dev("Contrast"));
        var index = new ConflictIndex(keymap, ParameterScope.Develop);
        Assert.Equal("Q is both a Nudge Key and another key.", index[new KeyUsage.NudgeUp(Dev("Contrast"))]);
        Assert.Null(index[new KeyUsage.Dial(Dev("Shadows"))]);
        Assert.Null(new ConflictIndex(keymap, ParameterScope.Library)[new KeyUsage.NudgeUp(Dev("Contrast"))]);
    }

    [Fact]
    public void RestoringTheStandardLayoutKeepsOptionsAndThePalette()
    {
        var current = new Keymap();
        current.Input.InvertWheel = true;
        current.Palette.Favourites.Add(new PaletteEntry(PaletteEntryKind.Tool, "createMask", "aiSelection/sky", "Sky"));
        var restored = KeymapEditing.StandardLayoutKeeping(current);
        Assert.True(restored.Input.InvertWheel);
        Assert.Single(restored.Palette.Favourites);
        Assert.Equal(Keymap.StandardLayout().CommandKeys, restored.CommandKeys);
        Assert.NotSame(current.Input, restored.Input);
    }
}
