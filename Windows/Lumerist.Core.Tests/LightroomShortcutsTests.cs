using System.Text.Json.Nodes;
using Xunit;
using static Lumerist.Core.Tests.KeymapTestSupport;

namespace Lumerist.Core.Tests;

// Lightroom Classic's own keys (LightroomShortcutsTests.swift). The warnings
// themselves are shared cases: keymap-rules/lightroom-shortcuts.json.

public class LightroomShortcutsTests
{
    [Fact]
    public void TheBuiltInTableIsTheSharedFileEntryForEntry()
    {
        var file = File.ReadAllText(Path.Combine(Fixtures.Root, "Shared", "lightroom-shortcuts.json"));
        var expected = new List<string>();
        foreach (var section in JsonNode.Parse(file)!["sections"]!.AsArray())
        {
            var contexts = section!["contexts"]!.AsArray().Select(item => item!.GetValue<string>()).ToList();
            var community = section["communityReported"]?.GetValue<bool>() ?? false;
            foreach (var shortcut in section["shortcuts"]!.AsArray())
            {
                var windows = shortcut!["windows"]!.AsArray().Select(item => item!.GetValue<string>()).ToList();
                if (windows.Count == 0) continue;
                var name = shortcut["name"]?.GetValue<string>() ?? shortcut["action"]!.GetValue<string>();
                var own = shortcut["contexts"]?.AsArray().Select(item => item!.GetValue<string>()).ToList() ?? contexts;
                expected.Add(string.Join(" | ", name, string.Join(" ", windows), string.Join(" ", own), community,
                    shortcut["importRole"]?.GetValue<string>() ?? "-"));
            }
        }
        var actual = LightroomShortcuts.Table.Select(entry => string.Join(" | ", entry.Name, string.Join(" ", entry.Windows),
            string.Join(" ", entry.Contexts.Select(context => context.WireName())), entry.IsCommunityReported,
            entry.ImportRole?.WireName() ?? "-")).ToList();
        Assert.Equal(expected, actual);
        Assert.True(actual.Count > 100);
        // Every chord can be read.
        foreach (var entry in LightroomShortcuts.Table)
        {
            foreach (var chord in entry.Windows) Assert.True(LightroomShortcuts.ParseChord(chord) is not null, chord);
        }
    }

    [Fact]
    public void LettersFollowTheLayoutAndPunctuationIsNeverMatched()
    {
        Func<int, string?> us = _ => null;
        // Scan code 0x2C is Z on US keyboards and Y on German ones.
        Func<int, string?> german = scanCode => scanCode switch { 0x2C => "Y", 0x15 => "Z", 0x1A => "Ü", 0x0D => "´", _ => null };
        var library = LightroomShortcuts.Context.Library;
        var develop = LightroomShortcuts.Context.Develop;
        Assert.Equal("Toggle Zoom view", LightroomShortcuts.For(Key(0x2C), library, us)?.Name);
        Assert.Equal("View Before and After left/right", LightroomShortcuts.For(Key(0x2C), develop, german)?.Name);
        Assert.Equal("Toggle Zoom view", LightroomShortcuts.For(Key(0x15), library, german)?.Name);
        // [ and = on US keyboards: Lightroom uses both, but they are not matched.
        Assert.Null(LightroomShortcuts.For(Key(0x1A), develop, us));
        Assert.Null(LightroomShortcuts.For(Key(0x0D), library, us));
        Assert.Null(LightroomShortcuts.For(Key(0x1A), develop, german));
        // The key left of 1 (Cycle flag settings) is never matched either.
        Assert.Null(LightroomShortcuts.For(KeyChord.LeftOf1, library, us));
        // Fixed keys match on any layout; combos never.
        Assert.Equal("Move between photos",
            LightroomShortcuts.For(Key(ScanCodes.RightArrow), LightroomShortcuts.Context.ImportWindow, german)?.Name);
        Assert.Equal("Show/hide Module Picker", LightroomShortcuts.For(Key(0x3F), develop, german)?.Name); // F5
        Assert.Null(LightroomShortcuts.For(Key(0x2C, KeyModifiers.Combo), library, us));
        // Modifiers must match exactly; Primary is Ctrl.
        Assert.Equal("Auto tone", LightroomShortcuts.For(Key(0x16, KeyModifiers.Control), develop, us)?.Name);
        Assert.Null(LightroomShortcuts.For(Key(0x16, KeyModifiers.Control | KeyModifiers.Alt), develop, us));
        // A letter a layout types as something else (ò, é) is not matched.
        Assert.Null(LightroomShortcuts.For(Key(0x2C), library, _ => "Ò"));
    }

    [Fact]
    public void ImportWindowKeysAreCommunityReported()
    {
        var include = LightroomShortcuts.For(Key(ScanCodes.P), LightroomShortcuts.Context.ImportWindow, null);
        Assert.Equal("Include", include?.Name);
        Assert.True(include?.IsCommunityReported);
        Assert.Equal(ImportKeyRole.Include, include?.ImportRole);
        Assert.False(LightroomShortcuts.For(Key(ScanCodes.P), LightroomShortcuts.Context.Library, null)?.IsCommunityReported);
        Assert.Null(LightroomShortcuts.For(Key(0x2C), LightroomShortcuts.Context.ImportWindow, null));
    }

    [Fact]
    public void ChordsReadPrimaryAsCtrl()
    {
        Assert.Equal(new LightroomShortcuts.LogicalChord("Z", KeyModifiers.Control | KeyModifiers.Shift),
            LightroomShortcuts.ParseChord("Primary+Shift+Z"));
        Assert.Equal(new LightroomShortcuts.LogicalChord("F11", KeyModifiers.Shift), LightroomShortcuts.ParseChord("Shift+F11"));
        Assert.Null(LightroomShortcuts.ParseChord("Hyper+Z"));
        Assert.Null(LightroomShortcuts.ParseChord("Shift+"));
    }
}

public class LightroomWarningEditingTests
{
    [Fact]
    public void AUsageNamesItsWarning()
    {
        var keymap = Keymap.StandardLayout();
        keymap.SetSlot(new ParameterSlot(NudgeUp: Key(0x23)), Lib("Exposure")); // H: Shift+H is Toggle HDR view
        Assert.Equal("Lightroom uses Shift+H for “Toggle HDR view” in Library. Lumerist takes it over there.",
            keymap.LightroomWarning(new KeyUsage.NudgeUp(Lib("Exposure"))));
        Assert.Null(keymap.LightroomWarning(new KeyUsage.Dial(Lib("Exposure"))));
        Assert.Equal(ParameterScope.Library, new KeyUsage.NudgeUp(Lib("Exposure")).SlotScope());
        Assert.Null(new KeyUsage.ImportKey(ImportKeyRole.Include).SlotScope());
    }

    [Fact]
    public void TheCommandKeyEditorWarnsForEachModuleTheKeyWorksIn()
    {
        var current = Keymap.StandardLayout();
        var draft = CommandKey.Create(CommandScope.Any, Key(0x30), "Redo", new CommandAction.Command("redo")); // B
        Assert.Equal(
            ["Lightroom uses B for “Add to Quick Collection” in Develop. Lumerist takes it over there.",
                "Lightroom uses B for “Add to Quick Collection” in Library. Lumerist takes it over there."],
            KeymapEditing.LightroomWarnings(current, draft));
        Assert.DoesNotContain(current.CommandKeys, key => key.Id == draft.Id); // nothing saved
        Assert.Empty(KeymapEditing.LightroomWarnings(current, draft with { Chord = Key(0x30, KeyModifiers.Combo) }));
        // Editing an existing key replaces it in the proposal rather than adding a second one.
        var autoTone = current.CommandKeys[0];
        Assert.Empty(KeymapEditing.LightroomWarnings(current, autoTone));
    }

    [Fact]
    public void ALightroomWarningReplacesTheGeneralOne()
    {
        var keymap = Keymap.StandardLayout();
        keymap.CommandKeys.Add(CommandKey.Create(CommandScope.Develop, Key(0x16, KeyModifiers.Control), "Undo",
            new CommandAction.Command("undo"))); // Ctrl+U: Auto tone in Develop
        var warnings = keymap.Conflicts().Where(conflict => conflict.Severity == ConflictSeverity.Warning).ToList();
        Assert.Equal("Auto tone", Assert.Single(warnings).LightroomAction);
    }

    [Fact]
    public void AnImportKeyWarnsOnlyForOtherLightroomKeys()
    {
        var keymap = Keymap.StandardLayout();
        keymap.ImportKeys = new ImportKeys { Include = Key(ScanCodes.X), Exclude = Key(ScanCodes.X, KeyModifiers.Shift) };
        Assert.Equal("Lightroom uses X for “Exclude” in the Import window. Lumerist takes it over there.",
            keymap.LightroomWarning(new KeyUsage.ImportKey(ImportKeyRole.Include)));
        Assert.Equal("Exclude and go to the next photo",
            keymap.Conflicts().Single(conflict => conflict.Usages.Contains(new KeyUsage.ImportKey(ImportKeyRole.Exclude))).LightroomAction);
        keymap.ImportKeys = new ImportKeys { Exclude = Key(ScanCodes.X) };
        Assert.Empty(keymap.Conflicts());
    }
}
