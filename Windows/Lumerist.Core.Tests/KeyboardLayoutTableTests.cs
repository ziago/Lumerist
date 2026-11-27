using Lumerist.Core.Input;
using Xunit;

namespace Lumerist.Core.Tests;

/// <summary>
/// Keyboard layouts as the app reads them from Windows (MapVirtualKeyEx and
/// ToUnicodeEx), written by hand from the tables of real layouts (read from a
/// Windows 11 PC's layout DLLs, 27 Nov 2026).
/// </summary>
internal static class LayoutTables
{
    /// <summary>The US main block: scan code, virtual key, what it types alone.</summary>
    private static readonly (int ScanCode, int VirtualKey, string Text)[] UsKeys =
    [
        (0x01, 0x1B, "\u001b"), (0x02, 0x31, "1"), (0x03, 0x32, "2"), (0x04, 0x33, "3"), (0x05, 0x34, "4"), (0x06, 0x35, "5"),
        (0x07, 0x36, "6"), (0x08, 0x37, "7"), (0x09, 0x38, "8"), (0x0A, 0x39, "9"), (0x0B, 0x30, "0"), (0x0C, 0xBD, "-"),
        (0x0D, 0xBB, "="), (0x0E, 0x08, "\b"), (0x0F, 0x09, "\t"),
        (0x10, 0x51, "q"), (0x11, 0x57, "w"), (0x12, 0x45, "e"), (0x13, 0x52, "r"), (0x14, 0x54, "t"), (0x15, 0x59, "y"),
        (0x16, 0x55, "u"), (0x17, 0x49, "i"), (0x18, 0x4F, "o"), (0x19, 0x50, "p"), (0x1A, 0xDB, "["), (0x1B, 0xDD, "]"),
        (0x1C, 0x0D, "\r"),
        (0x1E, 0x41, "a"), (0x1F, 0x53, "s"), (0x20, 0x44, "d"), (0x21, 0x46, "f"), (0x22, 0x47, "g"), (0x23, 0x48, "h"),
        (0x24, 0x4A, "j"), (0x25, 0x4B, "k"), (0x26, 0x4C, "l"), (0x27, 0xBA, ";"), (0x28, 0xDE, "'"), (0x29, 0xC0, "`"),
        (0x2B, 0xDC, "\\"), (0x2C, 0x5A, "z"), (0x2D, 0x58, "x"), (0x2E, 0x43, "c"), (0x2F, 0x56, "v"), (0x30, 0x42, "b"),
        (0x31, 0x4E, "n"), (0x32, 0x4D, "m"), (0x33, 0xBC, ","), (0x34, 0xBE, "."), (0x35, 0xBF, "/"), (0x37, 0x6A, "*"),
        (0x39, 0x20, " "), (0x4A, 0x6D, "-"), (0x4E, 0x6B, "+"), (0x56, 0xE2, "\\"),
    ];

    /// <summary>The US keys with some keys replaced (a null text: the key types nothing alone).</summary>
    public static KeyboardLayoutTable UsWith(params LayoutKey[] changes)
    {
        var keys = UsKeys.ToDictionary(key => key.ScanCode, key => new LayoutKey(key.ScanCode, key.VirtualKey, key.Text));
        foreach (var change in changes) keys[change.ScanCode] = change;
        return new KeyboardLayoutTable(keys.Values);
    }

    public static LayoutKey Key(int scanCode, int virtualKey, string? text, bool dead = false, string? altGr = null) =>
        new(scanCode, virtualKey, text, dead, altGr);

    public static KeyboardLayoutTable Us { get; } = UsWith();

    /// <summary>UK: ` alone on 0x29 (VK_OEM_8 there), ' on 0x28 has VK_OEM_3.</summary>
    public static KeyboardLayoutTable Uk { get; } = UsWith(Key(0x29, 0xDF, "`", altGr: "¦"), Key(0x28, 0xC0, "'"), Key(0x2B, 0xDE, "#"));

    /// <summary>Italian: \ left of 1, ò on VK_OEM_3, &lt; on the ISO key; no ` at all.</summary>
    public static KeyboardLayoutTable Italian { get; } = UsWith(
        Key(0x29, 0xDC, "\\"), Key(0x27, 0xC0, "ò"), Key(0x28, 0xDE, "à"), Key(0x56, 0xE2, "<"),
        Key(0x1A, 0xBA, "è", altGr: "["), Key(0x1B, 0xBB, "+", altGr: "]"));

    /// <summary>German: ^ is a dead key left of 1, ` only with Shift (dead), &lt; on the ISO key.</summary>
    public static KeyboardLayoutTable German { get; } = UsWith(
        Key(0x29, 0xDC, "^", dead: true), Key(0x0D, 0xDD, "´", dead: true), Key(0x56, 0xE2, "<", altGr: "|"),
        Key(0x10, 0x51, "q", altGr: "@"), Key(0x15, 0x5A, "z"), Key(0x2C, 0x59, "y"));

    /// <summary>Croatian ("Standard"): &lt; alone on the ISO key and with AltGr on the comma key.</summary>
    public static KeyboardLayoutTable Croatian { get; } = UsWith(
        Key(0x29, 0xC0, "¸", dead: true), Key(0x08, 0x37, "7", altGr: "`"), Key(0x33, 0xBC, ",", altGr: "<"),
        Key(0x56, 0xE2, "<"), Key(0x21, 0x46, "f", altGr: "["));

    /// <summary>United States-International: ` is a dead key, &lt; needs Shift: no Toggle.</summary>
    public static KeyboardLayoutTable UsInternational { get; } = UsWith(Key(0x29, 0xC0, "`", dead: true), Key(0x33, 0xBC, ",", altGr: "ç"));

    /// <summary>Hungarian: 0 left of 1 (VK_0), í on the ISO key, &lt; only with AltGr.</summary>
    public static KeyboardLayoutTable Hungarian { get; } = UsWith(
        Key(0x29, 0x30, "0"), Key(0x0B, 0xC0, "ö"), Key(0x56, 0xE2, "í", altGr: "<"), Key(0x32, 0x4D, "m", altGr: "<"),
        Key(0x2F, 0x56, "v", altGr: "@"), Key(0x15, 0x5A, "z"), Key(0x2C, 0x59, "y"));

    /// <summary>Russian: Cyrillic letters on the US virtual keys (VK_P on з), ё on VK_OEM_3.</summary>
    public static KeyboardLayoutTable Russian { get; } = UsWith(
        Key(0x19, 0x50, "з"), Key(0x2D, 0x58, "ч"), Key(0x2C, 0x5A, "я"), Key(0x16, 0x55, "г"), Key(0x24, 0x4A, "о"),
        Key(0x2F, 0x56, "м"), Key(0x29, 0xC0, "ё"), Key(0x33, 0xBC, "б"), Key(0x10, 0x51, "й"), Key(0x15, 0x59, "н"),
        Key(0x12, 0x45, "у"), Key(0x13, 0x52, "к"), Key(0x14, 0x54, "е"), Key(0x17, 0x49, "ш"), Key(0x18, 0x4F, "щ"),
        Key(0x11, 0x57, "ц"), Key(0x1E, 0x41, "ф"), Key(0x1F, 0x53, "ы"), Key(0x20, 0x44, "в"), Key(0x21, 0x46, "а"),
        Key(0x22, 0x47, "п"), Key(0x23, 0x48, "р"), Key(0x25, 0x4B, "л"), Key(0x26, 0x4C, "д"), Key(0x2E, 0x43, "с"),
        Key(0x30, 0x42, "и"), Key(0x31, 0x4E, "т"), Key(0x32, 0x4D, "ь"), Key(0x56, 0xE2, "\\"));

    /// <summary>Macedonian (Standard): Cyrillic, and VK_Z on 0x15 where US has Y (VK_Y on 0x2C).</summary>
    public static KeyboardLayoutTable MacedonianStandard { get; } = UsWith(
        Key(0x15, 0x5A, "ѕ"), Key(0x2C, 0x59, "з"), Key(0x19, 0x50, "п"), Key(0x2D, 0x58, "џ"), Key(0x16, 0x55, "у"),
        Key(0x2F, 0x56, "в"), Key(0x24, 0x4A, "ј"));

    /// <summary>Latvian (legacy): x only with AltGr on the key at US X, which is VK_B; VK_X is on 0x19.</summary>
    public static KeyboardLayoutTable LatvianLegacy { get; } = UsWith(
        Key(0x2D, 0x42, "b", altGr: "x"), Key(0x19, 0x58, "č"), Key(0x30, 0x50, "p"), Key(0x15, 0x56, "v", altGr: "y"));

    /// <summary>United States-Dvorak: p on 0x13, x on 0x30, v on 0x34; virtual keys follow the letters.</summary>
    public static KeyboardLayoutTable Dvorak { get; } = UsWith(
        Key(0x13, 0x50, "p"), Key(0x30, 0x58, "x"), Key(0x34, 0x56, "v"), Key(0x19, 0x4C, "l"), Key(0x2D, 0x51, "q"),
        Key(0x2F, 0x4B, "k"), Key(0x23, 0x44, "d"));

    /// <summary>Japanese keyboard (kbd106): 0x29 is 半角/全角 and types nothing; ` and &lt; need Shift.</summary>
    public static KeyboardLayoutTable Japanese { get; } = UsWith(
        Key(0x29, 0xF3, null), Key(0x1A, 0xC0, "@"), Key(0x33, 0xBC, ","), Key(0x56, 0xE2, null),
        Key(0x73, 0xE2, "\\"), Key(0x7D, 0xDC, "\\"), Key(0x79, 0x1C, null), Key(0x7B, 0x1D, null), Key(0x70, 0xF2, null));
}

public class KeyboardLayoutTableTests
{
    private static LayoutKey Key(int scanCode, int virtualKey, string? text, bool dead = false, string? altGr = null) =>
        LayoutTables.Key(scanCode, virtualKey, text, dead, altGr);

    [Fact]
    public void APlainKeyTypesTheCharacterAloneAndIsNotDead()
    {
        Assert.Equal(0x29, LayoutTables.Us.PlainKeyFor('`'));
        Assert.Equal(0x56, LayoutTables.Italian.PlainKeyFor('<'));
        // A dead ` only starts an accent; < needs Shift on US-International.
        Assert.Null(LayoutTables.UsInternational.PlainKeyFor('`'));
        Assert.Null(LayoutTables.UsInternational.PlainKeyFor('<'));
        // German types ` only with Shift, as a dead key.
        Assert.Null(LayoutTables.German.PlainKeyFor('`'));
    }

    [Fact]
    public void AnAltGrCopyOfTheCharacterNeverHidesThePlainKey()
    {
        // VkKeyScanEx may answer with AltGr+, here; the plain < key on 0x56 is the one.
        Assert.Equal(0x56, LayoutTables.Croatian.PlainKeyFor('<'));
        Assert.Equal(0x56, ImportShortcuts.ForLayout(LayoutTables.Croatian).Toggle?.ScanCode);
        // Hungarian types < only with AltGr: no plain key.
        Assert.Null(LayoutTables.Hungarian.PlainKeyFor('<'));
    }

    [Fact]
    public void AmongSeveralKeysTheOneWhoseVirtualKeyAgreesWins()
    {
        // p typed by 0x10 (VK_Q) and by 0x19 (VK_P): VK_P's key.
        var layout = LayoutTables.UsWith(Key(0x10, 0x51, "p"));
        Assert.Equal(0x19, layout.PlainKeyFor('p'));
        // Without agreement, a main-block key comes before the keypad: - on 0x0C, not Num - (0x4A).
        Assert.Equal(0x0C, LayoutTables.Us.PlainKeyFor('-'));
    }

    [Fact]
    public void ALetterNoKeyTypesFallsBackToItsVirtualKey()
    {
        // Russian: no key types p or x; VK_P and VK_X stay on the US keys.
        Assert.Equal(0x19, LayoutTables.Russian.LetterKey('p'));
        Assert.Equal(0x2D, LayoutTables.Russian.LetterKey('x'));
        // Macedonian (Standard): VK_Z is on 0x15, not on the US Z key (VK_Y there: Ctrl+Y is Redo).
        Assert.Equal(0x15, LayoutTables.MacedonianStandard.LetterKey('z'));
        Assert.Equal(new KeyChord(0x15, KeyModifiers.Control), FallbackShortcuts.For(LightroomCommandName.Undo, LayoutTables.MacedonianStandard));
        // Latvian (legacy): x only with AltGr on VK_B's key; VK_X's key, not B.
        Assert.Equal(0x19, LayoutTables.LatvianLegacy.LetterKey('x'));
        Assert.Equal(new KeyChord(0x19), FallbackShortcuts.For(LightroomCommandName.FlagReject, LayoutTables.LatvianLegacy));
        Assert.Equal(new KeyChord(0x19), ImportShortcuts.ForLayout(LayoutTables.LatvianLegacy).Exclude);
        // A letter typed alone keeps its key.
        Assert.Equal(0x13, LayoutTables.Dvorak.LetterKey('P'));
    }

    [Fact]
    public void FallbacksFollowTheLayout()
    {
        Assert.Equal(new KeyChord(0x2C, KeyModifiers.Control), FallbackShortcuts.For(LightroomCommandName.Undo, LayoutTables.Us));
        Assert.Equal(new KeyChord(0x15, KeyModifiers.Control | KeyModifiers.Shift),
            FallbackShortcuts.For(LightroomCommandName.Redo, LayoutTables.German));
        Assert.Equal(new KeyChord(0x13), FallbackShortcuts.For(LightroomCommandName.FlagPick, LayoutTables.Dvorak));
        Assert.Equal(new KeyChord(ScanCodes.RightArrow), FallbackShortcuts.For(LightroomCommandName.NextPhoto, LayoutTables.Russian));
        // No layout: the US keys.
        Assert.Equal(new KeyChord(0x24), FallbackShortcuts.For(LightroomCommandName.ShowClipping));
    }

    [Fact]
    public void NamesAndTypedCharacters()
    {
        Assert.Equal("^", LayoutTables.German.Typed(0x29)); // a dead key shows its accent
        Assert.Equal("Z", LayoutTables.German.Name(0x15));
        Assert.Equal("Ё", LayoutTables.Russian.Name(0x29));
        // Keys that type nothing, or only control characters.
        Assert.Null(LayoutTables.Japanese.Typed(0x29));
        Assert.Null(LayoutTables.Us.Typed(ScanCodes.Tab));
        Assert.Null(LayoutTables.Us.Typed(ScanCodes.Escape));
        Assert.Null(LayoutTables.Us.Typed(0x7F));
        Assert.Equal(0xE2, LayoutTables.Us.VirtualKey(0x56));
        Assert.Equal(0, LayoutTables.Us.VirtualKey(0x7F));
    }

    [Fact]
    public void AltGrIsThereWhenSomeKeyTypesWithIt()
    {
        Assert.False(LayoutTables.Us.HasAltGr);
        Assert.True(LayoutTables.Italian.HasAltGr);
        Assert.True(LayoutTables.UsInternational.HasAltGr);
        // A control character with Ctrl+Alt is no AltGr.
        Assert.False(LayoutTables.UsWith(Key(0x1A, 0xDB, "[", altGr: "\u001b")).HasAltGr);
    }

    [Fact]
    public void ReadProbesEveryScanCodeAndSkipsKeysWithoutAVirtualKey()
    {
        var probed = new List<int>();
        var table = KeyboardLayoutTable.Read(scanCode =>
        {
            probed.Add(scanCode);
            return scanCode == 0x10 ? new LayoutKey(0x10, 0x51, "q") : scanCode == 0x11 ? new LayoutKey(0x11, 0, "w") : null;
        });
        Assert.Equal(Enumerable.Range(0x01, 0x7F), probed);
        Assert.Equal(0x10, table.PlainKeyFor('q'));
        Assert.Null(table.PlainKeyFor('w'));
    }
}

public class ImportShortcutsLayoutTests
{
    [Fact]
    public void TogglesOnRealLayouts()
    {
        Assert.Equal(new KeyChord(0x29), ImportShortcuts.ForLayout(LayoutTables.Us).Toggle);
        Assert.Equal(new KeyChord(0x29), ImportShortcuts.ForLayout(LayoutTables.Uk).Toggle);
        Assert.Equal(new KeyChord(0x56), ImportShortcuts.ForLayout(LayoutTables.Italian).Toggle);
        Assert.Equal(new KeyChord(0x56), ImportShortcuts.ForLayout(LayoutTables.German).Toggle);
        // Neither ` nor < alone: no Toggle, never the key left of 1.
        Assert.Null(ImportShortcuts.ForLayout(LayoutTables.UsInternational).Toggle);
        Assert.Null(ImportShortcuts.ForLayout(LayoutTables.Hungarian).Toggle);
        Assert.Null(ImportShortcuts.ForLayout(LayoutTables.Russian).Toggle);
        Assert.Null(ImportShortcuts.ForLayout(LayoutTables.Japanese).Toggle);
    }

    [Fact]
    public void IncludeAndExcludeOnRealLayouts()
    {
        var dvorak = ImportShortcuts.ForLayout(LayoutTables.Dvorak);
        Assert.Equal(new KeyChord(0x13), dvorak.Include);
        Assert.Equal(new KeyChord(0x30), dvorak.Exclude);
        var russian = ImportShortcuts.ForLayout(LayoutTables.Russian);
        Assert.Equal(new KeyChord(ScanCodes.P), russian.Include);
        Assert.Equal(new KeyChord(ScanCodes.X), russian.Exclude);
        Assert.Equal(new KeyChord(ScanCodes.RightArrow), russian.NextPhoto);
    }
}

public class AppCommandTests
{
    private static readonly CommandAction OldShortcut =
        new CommandAction.Shortcut(new KeyChord(ScanCodes.V, KeyModifiers.Control | KeyModifiers.Alt));

    [Fact]
    public void PasteSettingsFromPreviousPressesCtrlAltWithTheKeyThatTypesV()
    {
        var name = AppCommands.PasteSettingsFromPrevious;
        Assert.Equal(new KeyChord(0x2F, KeyModifiers.Control | KeyModifiers.Alt), AppCommands.Shortcut(name));
        Assert.Equal(new KeyChord(0x2F, KeyModifiers.Control | KeyModifiers.Alt), AppCommands.Shortcut(name, LayoutTables.Us));
        // Dvorak types v on 0x34; 0x2F there is K.
        Assert.Equal(new KeyChord(0x34, KeyModifiers.Control | KeyModifiers.Alt), AppCommands.Shortcut(name, LayoutTables.Dvorak));
        // Russian: no key types v; VK_V's key.
        Assert.Equal(new KeyChord(0x2F, KeyModifiers.Control | KeyModifiers.Alt), AppCommands.Shortcut(name, LayoutTables.Russian));
        Assert.Null(AppCommands.Shortcut("autoTone"));
        Assert.Equal("Paste settings from previous", AppCommands.Title(name));
        Assert.Equal("Paste settings from previous", new CommandAction.Command(name).Summary());
    }

    [Fact]
    public void TheStandardTabQIsTheAppCommand()
    {
        var tabQ = Keymap.StandardLayout().CommandKeys.Single(key => key.Chord == new KeyChord(ScanCodes.Q, KeyModifiers.Combo));
        Assert.Equal(new CommandAction.Command(AppCommands.PasteSettingsFromPrevious), tabQ.Action);
        Assert.Equal("Paste settings from previous", tabQ.Title);
    }

    [Fact]
    public void OnlyTheExactOldDefaultIsMigrated()
    {
        var old = CommandKey.Create(CommandScope.Library, new KeyChord(0x25), "Paste settings from previous", OldShortcut);
        // Chord, id and scope don't matter.
        Assert.Equal(new CommandAction.Command(AppCommands.PasteSettingsFromPrevious), AppCommands.Migrate(old).Action);
        Assert.Equal(old.Id, AppCommands.Migrate(old).Id);
        // Another title or another shortcut stays as it is.
        var renamed = old with { Title = "Paste from previous" };
        Assert.Same(renamed, AppCommands.Migrate(renamed));
        var shifted = old with { Action = new CommandAction.Shortcut(new KeyChord(ScanCodes.V, KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Shift)) };
        Assert.Same(shifted, AppCommands.Migrate(shifted));
    }

    [Fact]
    public void AStoredOldDefaultLoadsAsTheAppCommandButMacroStepsStay()
    {
        var json = """
            { "format": 1, "commandKeys": [
              { "id": "787e1a28-ed89-40d3-9855-6049a5f3f639", "scope": "develop", "chord": { "scanCode": "0x10", "modifiers": ["Combo"] },
                "group": "Lightroom", "title": "Paste settings from previous",
                "action": { "shortcut": { "scanCode": "0x2F", "modifiers": ["Ctrl", "Alt"] } } },
              { "id": "11111111-2222-3333-4444-555555555555", "scope": "develop", "chord": { "scanCode": "0x25" },
                "group": "Lightroom", "title": "Paste settings from previous",
                "action": { "macro": [ { "action": { "shortcut": { "scanCode": "0x2F", "modifiers": ["Ctrl", "Alt"] } }, "delay": 0 } ] } }
            ] }
            """;
        var keymap = KeymapStore.Decode(json);
        Assert.Equal(new CommandAction.Command(AppCommands.PasteSettingsFromPrevious), keymap.CommandKeys[0].Action);
        var macro = Assert.IsType<CommandAction.Macro>(keymap.CommandKeys[1].Action);
        Assert.Equal(OldShortcut, macro.Steps[0].Action);
        // Saved again, it stays the app command.
        Assert.Contains("\"command\": \"pasteSettingsFromPrevious\"", KeymapStore.Encode(keymap));
    }

    [Fact]
    public void TheCommandEditorKeepsTheAppCommand()
    {
        var draft = new ActionDraft(new CommandAction.Command(AppCommands.PasteSettingsFromPrevious));
        Assert.Equal(AppCommands.PasteSettingsFromPrevious, draft.AppCommand);
        Assert.Equal(AppCommands.PasteSettingsFromPrevious, draft.CommandName);
        Assert.Equal(new CommandAction.Command(AppCommands.PasteSettingsFromPrevious), draft.Action);
        draft.Choose(LightroomCommandName.Undo);
        Assert.Null(draft.AppCommand);
        Assert.Equal(new CommandAction.Command("undo"), draft.Action);
        draft.ChooseAppCommand(AppCommands.PasteSettingsFromPrevious);
        Assert.Equal(new CommandAction.Command(AppCommands.PasteSettingsFromPrevious), draft.Action);
        // With an argument it is not the app command.
        Assert.Null(new ActionDraft(new CommandAction.Command(AppCommands.PasteSettingsFromPrevious, "x")).AppCommand);
    }
}

public class PaletteKeyDefaultTests
{
    [Fact]
    public void TheKeyLeftOf1UnlessItSwitchesTheInputMethodOrIsALightroomKey()
    {
        Assert.Null(PaletteKeyDefault.Reason(false, "`"));
        Assert.Equal(KeyChord.LeftOf1, PaletteKeyDefault.Key(PaletteKeyDefault.Reason(false, "`")));
        // Dead keys and letters Lightroom doesn't use keep it: ^ (German), º (Spanish), ё (Russian).
        Assert.Null(PaletteKeyDefault.Reason(false, "^"));
        Assert.Null(PaletteKeyDefault.Reason(false, "º"));
        Assert.Null(PaletteKeyDefault.Reason(false, "ё"));
        Assert.Null(PaletteKeyDefault.Reason(false, null));
        // Japanese keyboards: 半角/全角. Hungarian: 0, Lightroom's Remove star rating.
        Assert.Equal(PaletteKeyDefault.Unavailable.InputMethodKey, PaletteKeyDefault.Reason(true, null));
        Assert.Equal(PaletteKeyDefault.Unavailable.LightroomKey, PaletteKeyDefault.Reason(false, "0"));
        Assert.Null(PaletteKeyDefault.Key(PaletteKeyDefault.Unavailable.LightroomKey));
        Assert.Contains("半角/全角", PaletteKeyDefault.Explanation(PaletteKeyDefault.Unavailable.InputMethodKey, null));
        Assert.Contains("“0”", PaletteKeyDefault.Explanation(PaletteKeyDefault.Unavailable.LightroomKey, "0"));
    }

    [Fact]
    public void TheStandardLayoutTakesThePaletteKeyItIsGiven()
    {
        Assert.Equal(KeyChord.LeftOf1, Keymap.StandardLayout().SystemKeys.PaletteKey);
        var japanese = Keymap.StandardLayout(null);
        Assert.Null(japanese.SystemKeys.PaletteKey);
        japanese.Validate();
        Assert.Equal(KeyChord.Tab, japanese.SystemKeys.ComboKey);
        var current = Keymap.StandardLayout();
        Assert.Null(KeymapEditing.StandardLayoutKeeping(current, null).SystemKeys.PaletteKey);
        Assert.Equal(KeyChord.LeftOf1, KeymapEditing.StandardLayoutKeeping(current).SystemKeys.PaletteKey);
    }
}
