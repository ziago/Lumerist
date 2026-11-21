using Xunit;

namespace Lumerist.Core.Tests;

/// <summary>
/// The Palette's mask modes, actions and texts (the Mac's PaletteMaskMode and
/// PaletteView), with Shift and Alt in place of ⇧ and ⌥.
/// </summary>
public class PaletteMenuTests
{
    [Theory]
    [InlineData(false, false, PaletteMaskMode.New)]
    [InlineData(true, false, PaletteMaskMode.Add)]
    [InlineData(false, true, PaletteMaskMode.Subtract)]
    [InlineData(true, true, PaletteMaskMode.Intersect)]
    public void ModifiersChooseTheModeOverASelectedMask(bool shift, bool alt, PaletteMaskMode expected)
    {
        Assert.Equal(expected, PaletteMaskModes.From(shift, alt, masking: true));
        // Without a selected mask a click always creates a new mask.
        Assert.Equal(PaletteMaskMode.New, PaletteMaskModes.From(shift, alt, masking: false));
    }

    [Fact]
    public void ModesNameTheirCommandsKeysAndBadges()
    {
        Assert.Equal(["createMask", "addToMask", "subtractFromMask", "intersectWithMask"],
            Enum.GetValues<PaletteMaskMode>().Select(mode => mode.Command().WireName()));
        Assert.Equal(["New", "Add", "Subtract", "Intersect"], Enum.GetValues<PaletteMaskMode>().Select(mode => mode.Title()));
        Assert.Equal(["", "Shift", "Alt", "Shift+Alt"], Enum.GetValues<PaletteMaskMode>().Select(mode => mode.Keys()));
        Assert.Equal([null, "+", "−", "∩"], Enum.GetValues<PaletteMaskMode>().Select(mode => mode.Badge()));
        Assert.Equal(["Shift Add", "Alt Subtract", "Shift+Alt Intersect"], PaletteMaskModes.Modifying.Select(mode => mode.HintText()));
    }

    [Fact]
    public void TheMenuOffersEightToolsWithLightroomsNames()
    {
        Assert.Equal(["Subject", "Sky", "Background", "People", "Objects", "Brush", "Linear", "Radial"],
            PaletteMenu.MenuToolIds.Select(id => PaletteMenu.MaskTool(id)?.Title));
        Assert.Null(PaletteMenu.MaskTool("aiSelection/unknown"));
        Assert.Equal(12, PaletteMenu.MaskTools.Count);
    }

    [Fact]
    public void MaskToolFavouritesFollowTheClick()
    {
        var landscape = new PaletteEntry(PaletteEntryKind.Tool, "createMask", "aiSelection/landscape", "Landscape");
        Assert.True(PaletteMenu.IsMaskTool(landscape));
        Assert.Equal(new CommandAction.Command("subtractFromMask", "aiSelection/landscape"),
            PaletteMenu.Action(landscape, PaletteMaskMode.Subtract));
        Assert.Equal(new CommandAction.Command("createMask", "aiSelection/landscape"), PaletteMenu.Action(landscape, PaletteMaskMode.New));
        Assert.Equal("mountain.2", PaletteMenu.Symbol(landscape));
        Assert.Equal("Landscape", PaletteMenu.EntryLabel(landscape));
    }

    [Fact]
    public void OtherFavouritesKeepTheirAction()
    {
        var preset = new PaletteEntry(PaletteEntryKind.Preset, "6A5B-11", null, "B&W · contrast");
        Assert.False(PaletteMenu.IsMaskTool(preset));
        Assert.Equal(new CommandAction.Preset("6A5B-11", "B&W · contrast"), PaletteMenu.Action(preset, PaletteMaskMode.Add));
        Assert.Equal("slider.horizontal.3", PaletteMenu.Symbol(preset));
        Assert.Equal("Apply preset B&W · contrast", PaletteMenu.EntryLabel(preset));

        var tool = new PaletteEntry(PaletteEntryKind.Tool, "autoTone", null, "Auto Tone");
        Assert.False(PaletteMenu.IsMaskTool(tool));
        Assert.Equal(new CommandAction.Command("autoTone"), PaletteMenu.Action(tool, PaletteMaskMode.Intersect));
        Assert.Equal("circle.dashed", PaletteMenu.Symbol(tool));
    }

    [Fact]
    public void MaskActionsUseTheModesCommand()
    {
        Assert.Equal(new CommandAction.Command("intersectWithMask", "brush"), PaletteMenu.MaskAction("brush", PaletteMaskMode.Intersect));
    }

    [Fact]
    public void TheFooterNamesThePaletteKey()
    {
        var keymap = Keymap.StandardLayout();
        Assert.Equal("Hold ` for a look · tap it to keep it open", PaletteMenu.FooterText(keymap, keepVisible: false));
        Assert.Equal("Pinned · press ` or × to close", PaletteMenu.FooterText(keymap, keepVisible: true));
        keymap.SystemKeys.PaletteKey = new KeyChord(0x22, KeyModifiers.Combo);
        Assert.Equal("Hold Tab + G for a look · tap it to keep it open", PaletteMenu.FooterText(keymap, keepVisible: false));
        keymap.SystemKeys.PaletteKey = null;
        Assert.Equal("Pinned · press its key or × to close", PaletteMenu.FooterText(keymap, keepVisible: true));
    }

    [Fact]
    public void TheSubtitleNamesTheModuleOrTheMask()
    {
        Assert.Equal("Mask selected", PaletteMenu.Subtitle(new LightroomContext("develop", "masking")));
        Assert.Equal("Develop", PaletteMenu.Subtitle(new LightroomContext("develop", "loupe")));
        Assert.Equal("Library", PaletteMenu.Subtitle(new LightroomContext("library", "")));
        Assert.Null(PaletteMenu.Subtitle(new LightroomContext("", "")));
    }

    [Fact]
    public void ToolLabelsSayWhatAClickMakes()
    {
        Assert.Equal("New Sky mask", PaletteMenu.ToolLabel("Sky", masking: false));
        Assert.Equal("Sky mask", PaletteMenu.ToolLabel("Sky", masking: true));
    }
}
