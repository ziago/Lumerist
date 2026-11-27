using Xunit;
using static Lumerist.Core.Tests.KeymapTestSupport;

namespace Lumerist.Core.Tests;

/// <summary>Lightroom's keys in its interface language, pressed on Lightroom's keyboard layout.</summary>
public class LightroomLanguageTests
{
    /// <summary>Greek: ` alone left of 1 and &lt; alone on the ISO key, so the language decides.</summary>
    private static readonly Lumerist.Core.Input.KeyboardLayoutTable Greek = LayoutTables.UsWith(LayoutTables.Key(0x56, 0xE2, "<"));

    /// <summary>A small shared file in the shape of Shared/lightroom-shortcuts.json.</summary>
    private const string Sample = """
        { "sections": [
            { "id": "views", "contexts": ["library", "develop"], "shortcuts": [
              { "action": "Cycle info overlay", "mac": ["I"], "windows": ["I"] },
              { "action": "Toggle HDR view", "mac": ["Shift+H"], "windows": ["Shift+H"] },
              { "action": "Previous screen mode", "mac": ["Shift+F"], "windows": [] } ] },
            { "id": "develop", "contexts": ["develop"], "shortcuts": [
              { "action": "Auto tone", "mac": ["Primary+U"], "windows": ["Primary+U"], "fallbackFor": "autoTone" },
              { "action": "Show clipping", "mac": ["J"], "windows": ["J"], "fallbackFor": "showClipping" } ] } ],
          "languages": [
            { "id": "en", "name": "English", "codes": ["en"], "importToggle": "`", "overrides": [] },
            { "id": "it", "name": "Italiano", "codes": ["it"], "importToggle": "<", "overrides": [
              { "section": "views", "action": "Toggle HDR view", "mac": ["Alt+Shift+Primary+J"], "windows": ["Alt+Shift+Primary+J"], "press": false },
              { "section": "views", "action": "Previous screen mode", "mac": ["Shift+F"], "windows": ["Shift+F"], "press": false } ] },
            { "id": "pt", "name": "Português (Brasil)", "codes": ["pt", "pt_br"], "importToggle": "`" },
            { "id": "zh_cn", "name": "简体中文", "codes": ["zh_cn"], "importToggle": "`" },
            { "id": "xx", "name": "Pressing", "codes": ["xx"], "importToggle": "`", "overrides": [
              { "section": "develop", "action": "Auto tone", "mac": ["Primary+K"], "windows": ["Primary+K"], "press": true },
              { "section": "develop", "action": "Show clipping", "mac": ["O"], "windows": ["O"], "press": false } ] } ] }
        """;

    private static readonly LightroomShortcuts.Data Data = LightroomShortcuts.ReadData(Sample);

    [Fact]
    public void ACodeSelectsItsLanguageElseItsBaseElseEnglish()
    {
        Assert.Equal("it", Data.Resolve("it").Id);
        Assert.Equal("it", Data.Resolve("it_ch").Id);       // the part before _
        Assert.Equal("pt", Data.Resolve("pt_br").Id);       // listed
        Assert.Equal("pt", Data.Resolve("pt_pt").Id);
        Assert.Equal("zh_cn", Data.Resolve("zh_cn").Id);
        Assert.Equal("en", Data.Resolve("zh_hk").Id);       // never by "zh"
        Assert.Null(Data.Find("zh_hk"));
        Assert.Equal("en", Data.Resolve("fi").Id);
        Assert.Equal("en", Data.Resolve(null).Id);
        // A file without languages: English, `.
        var bare = LightroomShortcuts.ReadData("""{ "sections": [] }""");
        Assert.Equal('`', bare.Resolve("it").ImportToggle);
    }

    [Fact]
    public void GeneralNamesTheLanguage()
    {
        Assert.Equal("Lightroom's language: Italiano (it)", LightroomLanguage.DisplayLine("it", Data));
        Assert.Equal("Lightroom's language: Português (Brasil) (pt_br)", LightroomLanguage.DisplayLine("pt_br", Data));
        Assert.Equal("Lightroom's language: Italiano (it_ch)", LightroomLanguage.DisplayLine("it_ch", Data));
        Assert.Equal("Lightroom's language: fi", LightroomLanguage.DisplayLine("fi", Data));
        Assert.Equal("Lightroom's language: zh_hk", LightroomLanguage.DisplayLine("zh_hk", Data));
        Assert.Equal("Lightroom's language: not known yet", LightroomLanguage.DisplayLine(null, Data));
    }

    [Fact]
    public void OverridesAlwaysChangeTheWarnings()
    {
        var develop = LightroomShortcuts.Context.Develop;
        var library = LightroomShortcuts.Context.Library;
        // English: Shift+H is Toggle HDR view.
        Assert.Equal("Toggle HDR view", LightroomShortcuts.For(Key(0x23, KeyModifiers.Shift), library, null, "en", Data)?.Name);
        // Italian moves it to Ctrl+Alt+Shift+J; Shift+H is free there.
        Assert.Null(LightroomShortcuts.For(Key(0x23, KeyModifiers.Shift), library, null, "it", Data));
        Assert.Equal("Toggle HDR view",
            LightroomShortcuts.For(Key(0x24, KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Shift), library, null, "it", Data)?.Name);
        // An entry with no Windows chord in English gets one.
        Assert.Null(LightroomShortcuts.For(Key(0x21, KeyModifiers.Shift), develop, null, "en", Data));
        Assert.Equal("Previous screen mode", LightroomShortcuts.For(Key(0x21, KeyModifiers.Shift), develop, null, "it", Data)?.Name);
        // A press: false override still warns.
        Assert.Equal("Show clipping", LightroomShortcuts.For(Key(0x18), develop, null, "xx", Data)?.Name);
        Assert.Null(LightroomShortcuts.For(Key(0x24), develop, null, "xx", Data));
        // Unknown and null: English.
        Assert.Equal("Show clipping", LightroomShortcuts.For(Key(0x24), develop, null, "fi", Data)?.Name);
        Assert.Equal("Show clipping", LightroomShortcuts.For(Key(0x24), develop, null, null, Data)?.Name);
    }

    [Fact]
    public void OnlyAPressOverrideOnAFallbackChangesAPressedKey()
    {
        Assert.Equal(new LightroomShortcuts.LogicalChord("K", KeyModifiers.Control), Data.PressedChord("autoTone", Data.Resolve("xx")));
        Assert.Null(Data.PressedChord("showClipping", Data.Resolve("xx"))); // press: false
        Assert.Null(Data.PressedChord("autoTone", Data.Resolve("it")));
        Assert.Null(Data.PressedChord("undo", Data.Resolve("xx")));       // no fallbackFor entry
    }

    [Fact]
    public void ABrokenOverrideIsRefused()
    {
        Assert.Throws<InvalidDataException>(() => LightroomShortcuts.ReadData("""
            { "sections": [], "languages": [ { "id": "it", "name": "Italiano", "codes": ["it"], "importToggle": "<",
              "overrides": [ { "section": "views", "action": "Nothing", "mac": [], "windows": [] } ] } ] }
            """));
        Assert.Throws<InvalidDataException>(() => LightroomShortcuts.ReadData("""
            { "sections": [], "languages": [ { "id": "it", "name": "Italiano", "codes": ["it"], "importToggle": "<<" } ] }
            """));
    }

    [Fact]
    public void ToggleIsTheKeyThatTypesTheLanguagesCharacterElseThePhaseARule()
    {
        // Both ` and < alone: the language decides.
        Assert.Equal(new KeyChord(0x56), ImportShortcuts.ForLayout(Greek, "it", '<').Toggle);
        Assert.Equal(new KeyChord(0x29), ImportShortcuts.ForLayout(Greek, "en", '`').Toggle);
        // Italian on an Italian keyboard: < (tried in Lightroom Classic 16 on Windows, 27 Nov 2026).
        Assert.Equal(new KeyChord(0x56), ImportShortcuts.ForLayout(LayoutTables.Italian, "it", '<').Toggle);
        // Never worse than English: no < alone on US, so `; no ` alone on Italian, so <.
        Assert.Equal(new KeyChord(0x29), ImportShortcuts.ForLayout(LayoutTables.Us, "it", '<').Toggle);
        Assert.Equal(new KeyChord(0x56), ImportShortcuts.ForLayout(LayoutTables.Italian, "en", '`').Toggle);
        // Neither alone: off.
        Assert.Null(ImportShortcuts.ForLayout(LayoutTables.Hungarian, "it", '<').Toggle);
        Assert.Null(ImportShortcuts.ForLayout(LayoutTables.UsInternational, "en", '`').Toggle);
    }

    [Fact]
    public void ThePressedKeysAreTheEnglishOnesInEveryLanguageOfTheSharedFile()
    {
        // German lists Strg+I for Auto Tone and for the info overlay: Lumerist keeps Ctrl+U.
        foreach (var language in new string?[] { null, "en", "de", "it", "sv", "fr", "ja", "ru", "zh_cn", "pt_br", "nb" })
        {
            Assert.Equal(new KeyChord(0x16, KeyModifiers.Control), FallbackShortcuts.For(LightroomCommandName.AutoTone, LayoutTables.German, language));
            Assert.Equal(new KeyChord(0x15, KeyModifiers.Control), FallbackShortcuts.For(LightroomCommandName.Undo, LayoutTables.German, language));
            Assert.Equal(new KeyChord(0x15, KeyModifiers.Control | KeyModifiers.Shift), FallbackShortcuts.For(LightroomCommandName.Redo, LayoutTables.German, language));
            Assert.Equal(new KeyChord(ScanCodes.P), FallbackShortcuts.For(LightroomCommandName.FlagPick, LayoutTables.Us, language));
            Assert.Equal(new KeyChord(ScanCodes.X), FallbackShortcuts.For(LightroomCommandName.FlagReject, LayoutTables.Us, language));
            Assert.Equal(new KeyChord(0x16), FallbackShortcuts.For(LightroomCommandName.FlagRemove, LayoutTables.Us, language));
            Assert.Equal(new KeyChord(0x24), FallbackShortcuts.For(LightroomCommandName.ShowClipping, LayoutTables.Us, language));
            Assert.Equal(new KeyChord(ScanCodes.V, KeyModifiers.Control | KeyModifiers.Alt),
                AppCommands.Shortcut(AppCommands.PasteSettingsFromPrevious, LayoutTables.Us, language));
            var import = ImportShortcuts.ForLayout(LayoutTables.Us, language);
            Assert.Equal(new KeyChord(ScanCodes.P), import.Include);
            Assert.Equal(new KeyChord(ScanCodes.X), import.Exclude);
        }
    }

    [Fact]
    public void TheSharedFileListsLightroomsLanguages()
    {
        var shared = LightroomShortcuts.Shared;
        if (shared.Languages.Count == 0) return; // the languages arrive with the shared data
        string[] ids = ["en", "de", "es", "fr", "it", "ja", "ko", "nl", "pt", "sv", "zh_cn", "zh_tw", "ru", "pl", "no", "th"];
        Assert.Equal(ids.Order(StringComparer.Ordinal), shared.Languages.Select(language => language.Id).Order(StringComparer.Ordinal));
        foreach (var language in shared.Languages)
            Assert.Equal(language.Id is "it" or "sv" ? '<' : '`', language.ImportToggle);
        Assert.Equal("pt", shared.Resolve("pt_br").Id);
        Assert.Equal("no", shared.Resolve("nb").Id);
        Assert.Equal("Lightroom's language: Italiano (it)", LightroomLanguage.DisplayLine("it"));
        Assert.Equal('<', LightroomLanguage.ImportToggleCharacter("it"));
        Assert.Equal('`', LightroomLanguage.ImportToggleCharacter("ja"));
        // Every command Lumerist presses has its entry, with the chord FallbackShortcuts presses in English.
        foreach (var (command, chord) in new[]
                 {
                     ("autoTone", "Primary+U"), ("undo", "Primary+Z"), ("redo", "Shift+Primary+Z"), ("flagPick", "P"),
                     ("flagReject", "X"), ("flagRemove", "U"), ("showClipping", "J"), ("pasteSettingsFromPrevious", "Alt+Primary+V"),
                 })
        {
            var entry = Assert.Single(shared.Entries, entry => entry.FallbackFor == command);
            Assert.Contains(LightroomShortcuts.ParseChord(chord), entry.Windows.Select(LightroomShortcuts.ParseChord));
        }
    }

    [Fact]
    public void WarningsTakeTheLanguageAsInput()
    {
        var keymap = Keymap.StandardLayout();
        keymap.CommandKeys.Add(CommandKey.Create(CommandScope.Develop, Key(0x24), "Clipping", new CommandAction.Command("showClipping")));
        Assert.Contains("Lightroom uses J for “Show clipping” in Develop. Lumerist takes it over there.",
            keymap.Conflicts(null).Select(conflict => conflict.Message));
        Assert.Equal("Show clipping", LightroomShortcuts.For(Key(0x24), LightroomShortcuts.Context.Develop, "en")?.Name);
    }
}
