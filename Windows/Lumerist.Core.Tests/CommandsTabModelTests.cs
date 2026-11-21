using Xunit;
using static Lumerist.Core.Tests.KeymapTestSupport;

namespace Lumerist.Core.Tests;

// The Commands tab and the Command Key editor (Views/CommandsTab.swift).

public sealed class CommandsTabModelTests
{
    [Fact]
    public void EveryCommandHasATitleAndAGroup()
    {
        foreach (var command in LightroomCommandNames.All)
        {
            Assert.False(string.IsNullOrEmpty(command.Title()));
            Assert.Contains(command.Group(), CommandTitles.MenuGroups.Append("Presets"));
        }
        Assert.Equal("Reset all Develop settings", LightroomCommandName.ResetAll.Title());
        Assert.Equal("Masking", LightroomCommandName.InvertMask.Group());
    }

    [Fact]
    public void DefaultArgumentsAreAcceptedByTheirCommands()
    {
        foreach (var command in LightroomCommandNames.All.Where(command => command != LightroomCommandName.ApplyPreset))
            Assert.True(command.Accepts(command.DefaultArgument()), command.ToString());
    }

    [Theory]
    [InlineData("autoTone", null, "Auto Tone")]
    [InlineData("rating", "5", "Set star rating: 5 ★")]
    [InlineData("colorLabel", "red", "Set colour label: Red")]
    [InlineData("createMask", "aiSelection/sky", "New mask: Sky")]
    [InlineData("revealPanel", "Exposure", "Open a slider's panel: Exposure")]
    [InlineData("switchModule", "develop", "Switch module: Develop")]
    [InlineData("somethingNew", null, "somethingNew")]
    public void CommandSummaries(string name, string? argument, string expected) =>
        Assert.Equal(expected, new CommandAction.Command(name, argument).Summary());

    [Fact]
    public void OtherSummaries()
    {
        Assert.Equal("Preset: Matte", new CommandAction.Preset("1", "Matte").Summary());
        Assert.Equal("Lightroom shortcut Ctrl+Alt+V",
            new CommandAction.Shortcut(Key(ScanCodes.V, KeyModifiers.Control | KeyModifiers.Alt)).Summary());
        var one = new CommandAction.Macro([new MacroStep(new CommandAction.Command("undo"))]);
        Assert.Equal("Macro · 1 step", one.Summary());
        Assert.Equal("Macro · 2 steps", new CommandAction.Macro([.. one.Steps, .. one.Steps]).Summary());
    }

    [Fact]
    public void CapitalizedFollowsSwift()
    {
        Assert.Equal("Develop", CommandTitles.Capitalized("develop"));
        Assert.Equal("Slideshow", CommandTitles.Capitalized("SLIDESHOW"));
        Assert.Equal("Hello World", CommandTitles.Capitalized("hELLO world"));
        Assert.Equal("Two Words", CommandTitles.Capitalized("two wORDS"));
    }

    [Fact]
    public void HintNamesTheComboKey()
    {
        Assert.Equal("One press runs a Lightroom command, a preset, a Lightroom shortcut or a macro.", CommandTitles.Hint(null));
        Assert.EndsWith(" For a combo, hold Tab while you press the key: Tab + K.", CommandTitles.Hint(KeyChord.Tab));
        Assert.EndsWith(" For a combo, hold Tab and press the key.", CommandTitles.KeyDetail(KeyChord.Tab));
    }

    [Fact]
    public void DraftRoundTripsEveryKindOfAction()
    {
        CommandAction[] actions =
        [
            new CommandAction.Command("rating", "3"),
            new CommandAction.Preset("ABC-1", "Matte"),
            new CommandAction.Shortcut(Key(ScanCodes.V, KeyModifiers.Control)),
            new CommandAction.Macro([new MacroStep(new CommandAction.Command("undo"), 1.5), new MacroStep(new CommandAction.Command("redo"))]),
        ];
        foreach (var action in actions) Assert.Equal(action, new ActionDraft(action).Action);
    }

    [Fact]
    public void AnIncompleteDraftHasNoAction()
    {
        Assert.Null(new ActionDraft { Type = ActionDraft.Kind.Preset }.Action);
        Assert.Null(new ActionDraft { Type = ActionDraft.Kind.Shortcut }.Action);
        Assert.Null(new ActionDraft { Type = ActionDraft.Kind.Macro }.Action);
        Assert.Null(new ActionDraft { Command = LightroomCommandName.Rating }.Action);
        var macro = new ActionDraft { Type = ActionDraft.Kind.Macro };
        macro.Steps.Add(new ActionDraft.StepDraft(new ActionDraft()));
        macro.Steps.Add(new ActionDraft.StepDraft(new ActionDraft { Type = ActionDraft.Kind.Preset }));
        Assert.Null(macro.Action);
        var picked = new ActionDraft();
        picked.Choose(LightroomCommandName.CreateMask);
        Assert.Equal(new CommandAction.Command("createMask", "aiSelection/subject"), picked.Action);
        Assert.Equal("Preset: ID", new ActionDraft { Type = ActionDraft.Kind.Preset, PresetId = "ID" }.Action!.Summary());
    }

    [Fact]
    public void MacroStepsMoveAndDelaysClamp()
    {
        var macro = new ActionDraft { Type = ActionDraft.Kind.Macro };
        var first = new ActionDraft.StepDraft(new ActionDraft());
        var second = new ActionDraft.StepDraft(new ActionDraft());
        macro.Steps.AddRange([first, second]);
        macro.MoveStep(0, -1);
        Assert.Same(first, macro.Steps[0]);
        macro.MoveStep(0, 1);
        Assert.Same(second, macro.Steps[0]);
        Assert.Equal(10, ActionDraft.ClampDelay(12));
        Assert.Equal(0, ActionDraft.ClampDelay(double.NaN));
    }

    [Fact]
    public void FilterAndGroups()
    {
        var keymap = Keymap.StandardLayout();
        var develop = keymap.CommandKeysIn(ParameterScope.Develop).ToList();
        Assert.Equal(4, CommandTitles.Filter(develop, "  ", keymap).Count);
        Assert.Equal(["Auto Tone", "Auto White Balance"], CommandTitles.Filter(develop, "auto", keymap).Select(key => key.Title));
        Assert.Single(CommandTitles.Filter(develop, "Tab + R", keymap));
        Assert.Single(CommandTitles.Filter(develop, "shortcut ctrl", keymap));
        var groups = CommandTitles.OrderedGroups([.. develop, develop[0] with { Id = Guid.NewGuid(), Group = "Mine" }]);
        Assert.Equal(["Lightroom", "Mine"], groups.Select(group => group.Group));
        Assert.Equal(4, groups[0].Keys.Count);
    }

    [Fact]
    public void DuplicateAndSavedTitles()
    {
        var original = Keymap.StandardLayout().CommandKeys[0];
        var copy = CommandTitles.Duplicate(original);
        Assert.NotEqual(original.Id, copy.Id);
        Assert.Equal("Auto Tone copy", copy.Title);
        var longTitle = new string('x', 80);
        Assert.Equal(80, CommandTitles.Duplicate(original with { Title = longTitle }).Title.Length);
        Assert.Equal("Name", CommandTitles.SavedTitle("  Name "));
        Assert.Equal("Lightroom", CommandTitles.SavedGroup("  "));
        var keymap = Keymap.StandardLayout();
        keymap.Upsert(copy);
        keymap.Upsert(copy with { Title = "Again" });
        Assert.Equal("Again", keymap.CommandKeys.Last().Title);
        Assert.Equal(5, keymap.CommandKeys.Count);
    }
}
