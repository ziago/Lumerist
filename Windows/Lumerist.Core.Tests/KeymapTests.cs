using System.Text.Json;
using Xunit;
using static Lumerist.Core.Tests.KeymapTestSupport;

namespace Lumerist.Core.Tests;

// Ported from Tests/LumeristCoreTests/KeymapTests.swift: the parts the shared
// fixtures (keymap-rules, standard-layout) do not already pin down.

internal static class KeymapTestSupport
{
    public static ParameterKey Dev(string id) => new(ParameterScope.Develop, id);

    public static ParameterKey Lib(string id) => new(ParameterScope.Library, id);

    public static KeyChord Key(int scanCode, KeyModifiers modifiers = KeyModifiers.None) => new(scanCode, modifiers);

    public static KeymapException Throws(KeymapErrorKind kind, Action action)
    {
        var error = Assert.Throws<KeymapException>(action);
        Assert.Equal(kind, error.Kind);
        return error;
    }

    public static IReadOnlyList<KeyConflict> Errors(Keymap keymap) =>
        keymap.Conflicts().Where(conflict => conflict.Severity == ConflictSeverity.Error).ToList();

    /// <summary>Keymap is a mutable class without value equality: compare every part.</summary>
    public static void AssertSame(Keymap expected, Keymap actual)
    {
        Assert.Equal(expected.Develop.OrderBy(pair => pair.Key, StringComparer.Ordinal),
            actual.Develop.OrderBy(pair => pair.Key, StringComparer.Ordinal));
        Assert.Equal(expected.Library.OrderBy(pair => pair.Key, StringComparer.Ordinal),
            actual.Library.OrderBy(pair => pair.Key, StringComparer.Ordinal));
        Assert.Equal(expected.CommandKeys, actual.CommandKeys);
        Assert.Equal(expected.SystemKeys, actual.SystemKeys);
        Assert.Equal(expected.ImportKeys, actual.ImportKeys);
        Assert.Equal(expected.Palette.KeepVisible, actual.Palette.KeepVisible);
        Assert.Equal(expected.Palette.Favourites, actual.Palette.Favourites);
        Assert.Equal(expected.Input, actual.Input);
    }
}

public class KeymapPersistenceTests
{
    [Fact]
    public void RoundTripsEverything()
    {
        var keymap = Keymap.StandardLayout();
        keymap.UpdateSlot(Dev("Exposure"), slot => slot with
        {
            NudgeDown = new KeyChord(0x1A), NudgeUp = new KeyChord(0x1B), NormalStep = 0.1, CoarseStep = 0.5,
        });
        keymap.UpdateSlot(Dev("Contrast"), slot => slot with { NudgeDown = new KeyChord(0x1A, KeyModifiers.Combo) }); // Tab + [
        keymap.SystemKeys.ComboKey = null;
        keymap.Palette.KeepVisible = true;
        keymap.Palette.Favourites = [new PaletteEntry(PaletteEntryKind.Tool, "createMask", "aiSelection/sky", "Sky"),
            new PaletteEntry(PaletteEntryKind.Preset, "B1E2C3D4-0000-4000-8000-000000000001", null, "Négatif doux")];
        keymap.Input.SwapNormalAndCoarse = true;
        keymap.Input.DragSensitivity = 12.5;
        keymap.CommandKeys.Add(CommandKey.Create(CommandScope.Any, new KeyChord(ScanCodes.H, KeyModifiers.Alt | KeyModifiers.Combo),
            "Flag and next", new CommandAction.Macro([
                new MacroStep(new CommandAction.Command("flagPick"), 0.2),
                new MacroStep(new CommandAction.Command("createMask", "aiSelection/subject")),
                new MacroStep(new CommandAction.Preset("A1B2C3D4-0000-4000-8000-000000000002", "Warm"), 1),
                new MacroStep(new CommandAction.Shortcut(new KeyChord(ScanCodes.RightArrow))),
            ])));

        var json = KeymapStore.Encode(keymap);
        var decoded = KeymapStore.Decode(json);
        AssertSame(keymap, decoded);
        Assert.Null(decoded.SystemKeys.ComboKey); // a cleared key stays cleared
        Assert.Equal(json, KeymapStore.Encode(decoded));

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Assert.Equal(1, root.GetProperty("format").GetInt32());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("systemKeys").GetProperty("comboKey").ValueKind);
        Assert.Equal("0x10", root.GetProperty("develop").GetProperty("Exposure").GetProperty("dial").GetProperty("scanCode").GetString());
        var chord = root.GetProperty("commandKeys")[4].GetProperty("chord");
        Assert.Equal("0x23", chord.GetProperty("scanCode").GetString());
        Assert.Equal(["Alt", "Combo"], chord.GetProperty("modifiers").EnumerateArray().Select(item => item.GetString()));
        var arrow = root.GetProperty("commandKeys")[4].GetProperty("action").GetProperty("macro")[3].GetProperty("action");
        Assert.Equal("0xE04D", arrow.GetProperty("shortcut").GetProperty("scanCode").GetString());
        Assert.Contains("Négatif doux", json); // readable, not escaped
        Assert.False(root.GetProperty("develop").GetProperty("Contrast").GetProperty("dial").TryGetProperty("modifiers", out _));
    }

    [Fact]
    public void TheStandardLayoutRoundTrips()
    {
        var standard = Keymap.StandardLayout();
        AssertSame(standard, KeymapStore.Decode(KeymapStore.Encode(standard)));
    }

    [Fact]
    public void MissingSectionsAndOptionsTakeDefaults()
    {
        const string json = """
            {"format":1,"develop":{"Exposure":{"dial":{"scanCode":"0x10"}}},
             "systemKeys":{"pauseToggle":null},"input":{"invertWheel":true}}
            """;
        var keymap = KeymapStore.Decode(json);
        Assert.Equal(new KeyChord(ScanCodes.Q), keymap.Develop["Exposure"].Dial);
        Assert.Null(keymap.SystemKeys.PauseToggle);
        Assert.Equal(KeyChord.LeftOf1, keymap.SystemKeys.PaletteKey);
        Assert.Equal(KeyChord.Tab, keymap.SystemKeys.ComboKey);
        Assert.True(keymap.Input.InvertWheel);
        Assert.Equal(25, keymap.Input.DragSensitivity);
        Assert.Empty(keymap.Library);
        Assert.Empty(keymap.CommandKeys);
        Assert.Empty(keymap.Palette.Favourites);
    }

    [Fact]
    public void ImportKeysAreUnassignedUntilPickedAndRoundTrip()
    {
        // Keymaps saved before Import keys existed: none assigned, like the standard layout.
        var old = KeymapStore.Decode("""{"format":1}""");
        Assert.Equal(new ImportKeys(), old.ImportKeys);
        Assert.True(old.ImportKeys.IsDefault);
        Assert.Null(old.ImportKeys.Include);
        Assert.Null(old.ImportKeys.Exclude);
        Assert.Null(old.ImportKeys.Toggle);
        Assert.False(old.ImportKeys.Advance);
        Assert.True(Keymap.StandardLayout().ImportKeys.IsDefault);

        var keymap = Keymap.StandardLayout();
        keymap.ImportKeys = new ImportKeys { Include = Key(0x2C), Exclude = Key(ScanCodes.X, KeyModifiers.Shift), Advance = true };
        var json = KeymapStore.Encode(keymap);
        using (var document = JsonDocument.Parse(json))
        {
            var importKeys = document.RootElement.GetProperty("importKeys");
            Assert.Equal("0x2C", importKeys.GetProperty("include").GetProperty("scanCode").GetString());
            Assert.Equal(["Shift"], importKeys.GetProperty("exclude").GetProperty("modifiers").EnumerateArray().Select(item => item.GetString()));
            Assert.Equal(JsonValueKind.Null, importKeys.GetProperty("toggle").ValueKind); // written, a cleared key as null
            Assert.True(importKeys.GetProperty("advance").GetBoolean());
        }
        AssertSame(keymap, KeymapStore.Decode(json));
        Assert.Equal(json, KeymapStore.Encode(KeymapStore.Decode(json)));

        // Untouched Import keys are left out, so older builds (strict decoding) still read the file.
        Assert.DoesNotContain("importKeys", KeymapStore.Encode(Keymap.StandardLayout()));
        Assert.True(KeymapStore.Decode(KeymapStore.Encode(Keymap.StandardLayout())).ImportKeys.IsDefault);
        // "Move to the next photo" alone is a change, so it is written.
        var advanceOnly = Keymap.StandardLayout();
        advanceOnly.ImportKeys.Advance = true;
        Assert.True(KeymapStore.Decode(KeymapStore.Encode(advanceOnly)).ImportKeys.Advance);

        // Missing fields take the defaults; unknown ones are refused.
        var partial = KeymapStore.Decode("""{"format":1,"importKeys":{"include":{"scanCode":"0x2C"}}}""");
        Assert.Equal(new ImportKeys { Include = Key(0x2C) }, partial.ImportKeys);
        var error = Throws(KeymapErrorKind.UnknownFields, () => KeymapStore.Decode("""{"format":1,"importKeys":{"next":null}}"""));
        Assert.Equal(["next"], error.Fields);
        Throws(KeymapErrorKind.UnknownFields,
            () => KeymapStore.Decode("""{"format":1,"importKeys":{"include":{"scanCode":"0x2C","keyCode":6}}}"""));
        Assert.Throws<JsonException>(() => KeymapStore.Decode("""{"format":1,"importKeys":{"advance":"yes"}}"""));
        // Decoding validates: a combo is no Import key.
        Throws(KeymapErrorKind.InvalidCombo,
            () => KeymapStore.Decode("""{"format":1,"importKeys":{"toggle":{"scanCode":"0x2E","modifiers":["Combo"]}}}"""));
    }

    [Fact]
    public void ChordsReadLowerCaseHexAndDropEmptySlots()
    {
        const string json = """
            {"format":1,"develop":{"Exposure":{"nudgeUp":{"scanCode":"0xe04d","modifiers":["Shift","Combo"]}},
             "Contrast":{},"Tint":{"dial":null}}}
            """;
        var keymap = KeymapStore.Decode(json);
        Assert.Equal(new KeyChord(ScanCodes.RightArrow, KeyModifiers.Shift | KeyModifiers.Combo), keymap.Develop["Exposure"].NudgeUp);
        Assert.Equal(["Exposure"], keymap.Develop.Keys);
    }

    [Fact]
    public void StoredKeymapsWithUnknownFieldsAreRejectedNotLoadedEmpty()
    {
        // A slot written with a field this build does not know must not decode
        // as an empty slot: that would silently disable every key.
        void Rejects(string json, params string[] fields)
        {
            var error = Throws(KeymapErrorKind.UnknownFields, () => KeymapStore.Decode(json));
            Assert.Equal(fields, error.Fields);
        }
        Rejects("""{"format":1,"develop":{"Exposure":{"speed":{"scanCode":"0x10"}}}}""", "speed");
        Rejects("""{"format":1,"actionKeys":[]}""", "actionKeys");
        Rejects("""
            {"format":1,"commandKeys":[{"id":"0B5C3F4E-2D1A-4C8B-9E7F-6A5B4C3D2E1F","scope":"develop",
             "chord":{"scanCode":"0x25"},"requiresShiftKey":true,"group":"Lightroom","title":"Undo",
             "action":{"command":"undo"}}]}
            """, "requiresShiftKey");
        Rejects("""{"format":1,"systemKeys":{"menuKey":null}}""", "menuKey");
        // Every level is checked, and every unknown name is reported, sorted.
        Rejects("""{"format":1,"library":{"Exposure":{"dial":{"scanCode":"0x10","keyCode":12}}}}""", "keyCode");
        Rejects("""{"format":1,"palette":{"enabled":true,"favourites":[{"kind":"tool","id":"x","title":"X","icon":"y"}]}}""",
            "enabled", "icon");
        Rejects("""{"format":1,"input":{"wheelSpeed":3},"systemKeys":{"comboKey":{"scanCode":"0x0F","sticky":true}}}""",
            "sticky", "wheelSpeed");
        Rejects("""
            {"format":1,"commandKeys":[{"id":"0B5C3F4E-2D1A-4C8B-9E7F-6A5B4C3D2E1F","scope":"develop",
             "chord":{"scanCode":"0x25"},"group":"Lightroom","title":"Run",
             "action":{"macro":[{"action":{"script":"x"},"delay":0,"repeat":2}]}}]}
            """, "repeat", "script");
    }

    [Fact]
    public void DataWithoutAFormatIsRejected()
    {
        var error = Throws(KeymapErrorKind.UnsupportedFormat,
            () => KeymapStore.Decode("""{"bindings":[{"action":"contrast","scanCode":"0x11"}]}"""));
        Assert.Equal(0, error.Format);
        Assert.Equal(0, Throws(KeymapErrorKind.UnsupportedFormat, () => KeymapStore.Decode("not json")).Format);
        Assert.Equal(0, Throws(KeymapErrorKind.UnsupportedFormat, () => KeymapStore.Decode("[1]")).Format);
        Assert.Equal(0, Throws(KeymapErrorKind.UnsupportedFormat, () => KeymapStore.Decode("""{"format":"1"}""")).Format);
    }

    [Fact]
    public void RejectsMacFilesFutureFormatsAndUnknownParameters()
    {
        // The Mac's format 3 never loads on Windows.
        const string mac = """{"format":3,"develop":{"Exposure":{"dial":{"keyCode":12,"modifiers":0}}}}""";
        Assert.Equal(3, Throws(KeymapErrorKind.UnsupportedFormat, () => KeymapStore.Decode(mac)).Format);
        Assert.Equal(2, Throws(KeymapErrorKind.UnsupportedFormat, () => KeymapStore.Decode("""{"format":2}""")).Format);
        Assert.Equal("Unsupported keymap format: 2.",
            Assert.Throws<KeymapException>(() => KeymapStore.Decode("""{"format":2}""")).Message);

        var unknown = Throws(KeymapErrorKind.UnknownParameter,
            () => KeymapStore.Decode("""{"format":1,"develop":{"NoSuchSlider":{"dial":{"scanCode":"0x10"}}}}"""));
        Assert.Equal(Dev("NoSuchSlider"), unknown.Parameter);
        Assert.Equal("Unknown parameter develop:NoSuchSlider.", unknown.Message);
    }

    [Fact]
    public void ContentOfTheWrongShapeIsRejected()
    {
        string[] invalid =
        [
            """{"format":1,"develop":[]}""",
            """{"format":1,"develop":{"Exposure":{"dial":"Q"}}}""",
            """{"format":1,"develop":{"Exposure":{"dial":{"scanCode":16}}}}""",
            """{"format":1,"develop":{"Exposure":{"dial":{"scanCode":"0x00"}}}}""",
            """{"format":1,"develop":{"Exposure":{"dial":{"scanCode":"0x80"}}}}""",
            """{"format":1,"develop":{"Exposure":{"dial":{"scanCode":"0xF010"}}}}""",
            """{"format":1,"develop":{"Exposure":{"dial":{"scanCode":"16"}}}}""",
            """{"format":1,"develop":{"Exposure":{"dial":{"scanCode":"0x10","modifiers":["Cmd"]}}}}""",
            """{"format":1,"develop":{"Exposure":{"dial":{"scanCode":"0x10","modifiers":["Shift","Shift"]}}}}""",
            """{"format":1,"develop":{"Exposure":{"dial":{"scanCode":"0x10"},"dial":{"scanCode":"0x11"}}}}""",
            """{"format":1,"develop":{"Exposure":{"normalStep":"0.1"}}}""",
            """{"format":1,"commandKeys":{}}""",
            """{"format":1,"commandKeys":[{"id":"nope","scope":"develop","chord":{"scanCode":"0x25"},"group":"G","title":"T","action":{"command":"undo"}}]}""",
            """{"format":1,"commandKeys":[{"id":"0B5C3F4E-2D1A-4C8B-9E7F-6A5B4C3D2E1F","scope":"everywhere","chord":{"scanCode":"0x25"},"group":"G","title":"T","action":{"command":"undo"}}]}""",
            """{"format":1,"commandKeys":[{"id":"0B5C3F4E-2D1A-4C8B-9E7F-6A5B4C3D2E1F","scope":"develop","chord":{"scanCode":"0x25"},"title":"T","action":{"command":"undo"}}]}""",
            """{"format":1,"commandKeys":[{"id":"0B5C3F4E-2D1A-4C8B-9E7F-6A5B4C3D2E1F","scope":"develop","chord":{"scanCode":"0x25"},"group":"G","title":"T","action":{"command":"undo","preset":"x"}}]}""",
            """{"format":1,"commandKeys":[{"id":"0B5C3F4E-2D1A-4C8B-9E7F-6A5B4C3D2E1F","scope":"develop","chord":{"scanCode":"0x25"},"group":"G","title":"T","action":{"command":"undo","name":"x"}}]}""",
            """{"format":1,"commandKeys":[{"id":"0B5C3F4E-2D1A-4C8B-9E7F-6A5B4C3D2E1F","scope":"develop","chord":{"scanCode":"0x25"},"group":"G","title":"T","action":{"preset":"x"}}]}""",
            """{"format":1,"commandKeys":[{"id":"0B5C3F4E-2D1A-4C8B-9E7F-6A5B4C3D2E1F","scope":"develop","chord":{"scanCode":"0x25"},"group":"G","title":"T","action":{"macro":[{"action":{"command":"undo"}}]}}]}""",
            """{"format":1,"systemKeys":{"comboKey":"Tab"}}""",
            """{"format":1,"palette":{"favourites":[{"kind":"menu","id":"x","title":"X"}]}}""",
            """{"format":1,"input":{"invertWheel":1}}""",
        ];
        foreach (var json in invalid)
        {
            var error = Record.Exception(() => KeymapStore.Decode(json));
            Assert.True(error is JsonException, $"{json} → {error?.GetType().Name ?? "no error"}: {error?.Message}");
        }
    }

    [Fact]
    public void DecodingValidates()
    {
        // Two Dial Keys on Q: a stored keymap with conflicts does not load.
        const string json = """{"format":1,"develop":{"Exposure":{"dial":{"scanCode":"0x10"}},"Contrast":{"dial":{"scanCode":"0x10"}}}}""";
        var error = Throws(KeymapErrorKind.Conflicts, () => KeymapStore.Decode(json));
        Assert.Equal("Q is the Dial Key of more than one slider.", error.Message);
        Assert.Single(error.Conflicts);

        const string steps = """{"format":1,"develop":{"Exposure":{"normalStep":3}}}""";
        Assert.Equal(Dev("Exposure"),
            Throws(KeymapErrorKind.InvalidStep, () => KeymapStore.Decode(steps)).Parameter);
    }

    [Fact]
    public void FilesAreSavedAtomicallyAndLoadedBack()
    {
        var folder = Path.Combine(Path.GetTempPath(), "lumerist-keymap-" + Guid.NewGuid().ToString("N"));
        try
        {
            var path = Path.Combine(folder, "Lumerist", "keymap.json");
            Assert.Null(KeymapStore.Load(path)); // no file yet

            var keymap = Keymap.StandardLayout();
            KeymapStore.Save(keymap, path); // creates the folder
            AssertSame(keymap, KeymapStore.Load(path)!);

            keymap.Input.InvertWheel = true;
            KeymapStore.Save(keymap, path); // replaces the file
            Assert.True(KeymapStore.Load(path)!.Input.InvertWheel);
            Assert.Equal(["keymap.json"], Directory.GetFiles(Path.GetDirectoryName(path)!).Select(Path.GetFileName));

            // An invalid keymap never replaces a good file.
            keymap.Input.DragSensitivity = 99;
            Throws(KeymapErrorKind.InvalidOption, () => KeymapStore.Save(keymap, path));
            Assert.Equal(25, KeymapStore.Load(path)!.Input.DragSensitivity);
            Assert.Equal(["keymap.json"], Directory.GetFiles(Path.GetDirectoryName(path)!).Select(Path.GetFileName));
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
    }
}

public class KeymapValidationTests
{
    [Fact]
    public void ErrorsDescribeThemselves()
    {
        var keymap = Keymap.StandardLayout();
        keymap.UpdateSlot(Dev("GrainAmount"), slot => slot with { Dial = new KeyChord(0x25, KeyModifiers.Combo) });
        var error = Throws(KeymapErrorKind.InvalidCombo, keymap.Validate);
        Assert.Contains("Dial Key can't be a combo", error.Message);
        Assert.Equal(new ComboMisuse.DialKey(Dev("GrainAmount")), error.Misuse);

        keymap = Keymap.StandardLayout();
        keymap.Input.DragSensitivity = 40;
        Assert.Equal("The drag sensitivity setting is out of range.",
            Throws(KeymapErrorKind.InvalidOption, keymap.Validate).Message);
        keymap.Input.DragSensitivity = double.NaN;
        Throws(KeymapErrorKind.InvalidOption, keymap.Validate);
    }

    [Fact]
    public void CommandKeysNeedATitleAndValidActions()
    {
        string Reason(string title, CommandAction action)
        {
            var keymap = Keymap.StandardLayout();
            keymap.CommandKeys = [CommandKey.Create(CommandScope.Develop, new KeyChord(ScanCodes.H), title, action)];
            var error = Throws(KeymapErrorKind.InvalidCommandKey, keymap.Validate);
            Assert.Equal($"Invalid command key: {error.Reason}.", error.Message);
            return error.Reason!;
        }
        var undo = new CommandAction.Command("undo");
        Assert.Equal("a title of 1–80 characters is required", Reason("", undo));
        Assert.Equal("a title of 1–80 characters is required", Reason(new string('x', 81), undo));
        Assert.Equal("empty command", Reason("Undo", new CommandAction.Command("")));
        Assert.Equal("missing preset", Reason("Warm", new CommandAction.Preset("", "Warm")));
        Assert.Equal("a macro needs 1–50 steps", Reason("Empty", new CommandAction.Macro([])));
        Assert.Equal("a macro needs 1–50 steps",
            Reason("Long", new CommandAction.Macro(Enumerable.Repeat(new MacroStep(undo), 51).ToList())));
        Assert.Equal("macro delays must be 0–10 s", Reason("Back", new CommandAction.Macro([new MacroStep(undo, -1)])));

        // 80 characters, counted as the user sees them, are fine.
        var keymap = Keymap.StandardLayout();
        keymap.CommandKeys = [CommandKey.Create(CommandScope.Develop, new KeyChord(ScanCodes.H), new string('é', 80), undo),
            CommandKey.Create(CommandScope.Library, new KeyChord(ScanCodes.H), string.Concat(Enumerable.Repeat("👍🏽", 80)), undo)];
        keymap.Validate();
    }

    [Fact]
    public void EmptySlotsAreRemoved()
    {
        var keymap = Keymap.StandardLayout();
        keymap.UpdateSlot(Dev("Exposure"), slot => slot with { Dial = null });
        Assert.False(keymap.Develop.ContainsKey("Exposure"));
        keymap.ClearDialKeys(ParameterScope.Library);
        Assert.Empty(keymap.Library);
    }

    [Fact]
    public void ClearNudgeKeysKeepsDialKeys()
    {
        var keymap = Keymap.StandardLayout();
        keymap.UpdateSlot(Dev("Exposure"), slot => slot with { NudgeUp = new KeyChord(0x1B) });
        keymap.UpdateSlot(Dev("GrainAmount"), slot => slot with { NudgeDown = new KeyChord(0x1A) });
        keymap.ClearNudgeKeys(ParameterScope.Develop);
        Assert.Null(keymap.Develop["Exposure"].NudgeUp);
        Assert.Equal(new KeyChord(ScanCodes.Q), keymap.Develop["Exposure"].Dial);
        Assert.False(keymap.Develop.ContainsKey("GrainAmount")); // nothing left: removed
    }
}

public class KeymapConflictMessageTests
{
    [Fact]
    public void TypingKeysAndReservedModifiersNameWindows()
    {
        var keymap = Keymap.StandardLayout();
        keymap.UpdateSlot(Dev("GrainAmount"), slot => slot with { Dial = new KeyChord(ScanCodes.Space) });
        keymap.UpdateSlot(Dev("GrainSize"), slot => slot with { Dial = new KeyChord(ScanCodes.H, KeyModifiers.Control) });
        keymap.UpdateSlot(Dev("GrainFrequency"), slot => slot with { Dial = new KeyChord(ScanCodes.Enter) });
        var warnings = keymap.Conflicts().Select(conflict => conflict.Message).ToList();
        Assert.Equal(
        [
            "Ctrl+H is also used for typing or by Lightroom and Windows.",
            "Enter is also used for typing or by Lightroom and Windows.",
            "Space is also used for typing or by Lightroom and Windows.",
        ], warnings);
        // Tab is not a typing key: once it is no longer the Combo Key, a Nudge Key may take it.
        // Its only note is the warning about Lightroom's own Tab (Show/hide side panels).
        keymap.SystemKeys.ComboKey = new KeyChord(ScanCodes.Backspace);
        keymap.CommandKeys.Clear();
        keymap.SystemKeys.KeyReferenceToggle = null;
        keymap.UpdateSlot(Dev("Dehaze"), slot => slot with { NudgeUp = new KeyChord(ScanCodes.Tab) });
        var tab = Assert.Single(keymap.Conflicts(), conflict => conflict.Chord == new KeyChord(ScanCodes.Tab));
        Assert.Equal(ConflictSeverity.Warning, tab.Severity);
        Assert.Equal("Show/hide side panels", tab.LightroomAction);
    }

    [Fact]
    public void ComboMessagesNameTheKeysTheWindowsWay()
    {
        var itself = Keymap.StandardLayout();
        itself.UpdateSlot(Dev("GrainAmount"), slot => slot with { NudgeUp = new KeyChord(ScanCodes.Tab, KeyModifiers.Shift | KeyModifiers.Combo) });
        var conflict = Errors(itself)[0];
        Assert.Equal("Tab + Shift+Tab can't work: a combo can't use the Combo Key itself.", conflict.Message);
        Assert.Contains(new KeyUsage.SystemKey(SystemKeyRole.ComboKey), conflict.Usages);

        var pause = Keymap.StandardLayout();
        pause.CommandKeys.Add(CommandKey.Create(CommandScope.Develop,
            new KeyChord(ScanCodes.Escape, KeyModifiers.Control | KeyModifiers.Combo), "Undo", new CommandAction.Command("undo")));
        Assert.Equal(["Tab + Ctrl+Esc can't work: Ctrl+Esc always pauses Lumerist."],
            Errors(pause).Select(error => error.Message));

        var systems = Keymap.StandardLayout();
        systems.SystemKeys.PauseToggle = KeyChord.LeftOf1;
        var twice = Assert.Single(Errors(systems));
        Assert.Equal("` is used by more than one system key.", twice.Message);
        Assert.Null(twice.Scope);
        Assert.Equal([new KeyUsage.SystemKey(SystemKeyRole.PaletteKey), new KeyUsage.SystemKey(SystemKeyRole.PauseToggle)],
            twice.Usages);
    }

    [Fact]
    public void ErrorsComeFirst()
    {
        var keymap = Keymap.StandardLayout();
        keymap.UpdateSlot(Dev("GrainAmount"), slot => slot with { Dial = new KeyChord(ScanCodes.Space) });
        keymap.UpdateSlot(Dev("GrainSize"), slot => slot with { Dial = new KeyChord(ScanCodes.Q) });
        var severities = keymap.Conflicts().Select(conflict => conflict.Severity).ToList();
        Assert.Equal([ConflictSeverity.Error, ConflictSeverity.Warning], severities);
    }
}

public class KeymapSummaryTests
{
    [Fact]
    public void CountsInCatalogOrder()
    {
        var keymap = Keymap.StandardLayout();
        keymap.UpdateSlot(Dev("Exposure"), slot => slot with { NudgeDown = new KeyChord(0x1A), NudgeUp = new KeyChord(0x1B) });
        keymap.UpdateSlot(Dev("GrainAmount"), slot => slot with { NudgeUp = new KeyChord(0x28) });
        var summary = keymap.Summary(ParameterScope.Develop);
        Assert.Equal(ParameterScope.Develop, summary.Scope);
        Assert.Equal(["Exposure", "GrainAmount"], summary.NudgeKeys.Select(entry => entry.Descriptor.Id));

        var dialIds = summary.DialKeys.Select(entry => entry.Descriptor.Id).ToList();
        Assert.Equal(21, dialIds.Count);
        Assert.Equal("Temperature", dialIds[0]);
        var catalogOrder = ParameterCatalog.All.Where(d => d.Scope == ParameterScope.Develop).Select(d => d.Id);
        Assert.Equal(catalogOrder.Where(dialIds.Contains), dialIds);

        Assert.Equal(12, keymap.Summary(ParameterScope.Library).DialKeys.Count);
        Assert.Empty(keymap.Summary(ParameterScope.Library).NudgeKeys);
    }

    [Fact]
    public void EffectiveStepsFallBackToTheCatalog()
    {
        var keymap = Keymap.StandardLayout();
        Assert.Equal((0.05, 0.33), keymap.Steps(Dev("Exposure")));
        Assert.Equal((5.0, 20.0), keymap.Steps(Lib("Temperature")));
        keymap.UpdateSlot(Dev("Exposure"), slot => slot with { CoarseStep = 0.5 });
        Assert.Equal((0.05, 0.5), keymap.Steps(Dev("Exposure")));
        Assert.Null(keymap.Steps(Dev("Nope")));
    }
}

public class KeymapMaskTests
{
    [Fact]
    public void LocalEquivalents()
    {
        Assert.Equal(Dev("local_Exposure"), Keymap.LocalKey(Dev("Exposure")));
        Assert.Equal(Dev("local_LuminanceNoise"), Keymap.LocalKey(Dev("LuminanceSmoothing")));
        Assert.Equal(Dev("local_Grain"), Keymap.LocalKey(Dev("GrainAmount")));
        Assert.Equal(Dev("local_Amount"), Keymap.LocalKey(Dev("PresetAmount")));
        Assert.Equal(Dev("local_Amount"), Keymap.LocalKey(Dev("local_Amount")));
        Assert.Null(Keymap.LocalKey(Dev("Vibrance")));
        Assert.Null(Keymap.LocalKey(Dev("ParametricShadows")));
        Assert.Null(Keymap.LocalKey(Lib("Exposure")));
    }

    [Fact]
    public void EveryLocalTargetIsInTheCatalog()
    {
        foreach (var local in Keymap.LocalParameterMap.Values.Distinct())
            Assert.True(ParameterCatalog.Descriptor(ParameterScope.Develop, local) is not null, local);
        var hue = ParameterCatalog.Descriptor(ParameterScope.Develop, "local_Hue");
        Assert.True(hue?.Wraps);
        Assert.Equal("Masking (local)", hue?.Section);
    }
}

public class KeyChordTests
{
    [Fact]
    public void DisplayText()
    {
        Assert.Equal("Shift+Tab", new KeyChord(ScanCodes.Tab, KeyModifiers.Shift).DisplayText);
        Assert.Equal("Ctrl+Esc", KeyChord.ControlEscape.DisplayText);
        Assert.Equal("Num *", new KeyChord(0x37).DisplayText);
        Assert.Equal("Ctrl+Alt+Shift+Q", new KeyChord(ScanCodes.Q, KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Shift).DisplayText);
        Assert.Equal("Ctrl+Alt+V", new KeyChord(ScanCodes.V, KeyModifiers.Control | KeyModifiers.Alt).DisplayText);
        Assert.Equal("Num 9", new KeyChord(0x49).DisplayText);
        Assert.Equal("Num 0", new KeyChord(0x52).DisplayText);
        Assert.Equal("\\", new KeyChord(0x2B).DisplayText);
        Assert.Equal("→", new KeyChord(ScanCodes.RightArrow).DisplayText);
    }

    [Fact]
    public void CombosNameTheComboKey()
    {
        var combo = new KeyChord(0x25, KeyModifiers.Combo); // K
        Assert.True(combo.IsCombo);
        Assert.False(new KeyChord(0x25).IsCombo);
        Assert.Equal("Combo + K", combo.DisplayText);
        Assert.Equal("Tab + K", combo.DisplayTextWith(KeyChord.Tab));
        Assert.Equal("Combo + K", combo.DisplayTextWith(null));
        Assert.Equal("Tab + Shift+K", new KeyChord(0x25, KeyModifiers.Shift | KeyModifiers.Combo).DisplayTextWith(KeyChord.Tab));
        Assert.Equal("Tab + Shift+]", new KeyChord(0x1B, KeyModifiers.Shift | KeyModifiers.Combo).DisplayTextWith(KeyChord.Tab));
        Assert.Equal("Shift+Tab + K", combo.DisplayTextWith(new KeyChord(ScanCodes.Tab, KeyModifiers.Shift)));
        Assert.Equal("K", new KeyChord(0x25).DisplayTextWith(KeyChord.Tab)); // plain keys are unchanged
        Assert.Equal(new KeyChord(0x25), combo.WithoutCombo);
        Assert.Equal(combo, new KeyChord(0x25).AsCombo);
        Assert.Equal("Tab + K", Keymap.StandardLayout().DisplayText(combo));
        Assert.Equal(1 << 8, (int)KeyModifiers.Combo);
        Assert.False(KeyModifiers.Combo.ReservesKey());
    }
}

public class KeyNameTests
{
    [Fact]
    public void LayoutNamesReplaceLettersButNotNamedKeys()
    {
        // An AZERTY-like layout: the physical Q key types A, the Z key types W.
        var azerty = new Dictionary<int, string> { [ScanCodes.Q] = "A", [0x2C] = "W", [ScanCodes.W] = "Z", [0x2B] = "*" };
        string? Layout(int scanCode) => azerty.GetValueOrDefault(scanCode);
        Assert.Equal("A", KeyNames.Name(ScanCodes.Q, Layout));
        Assert.Equal("W", KeyNames.Name(0x2C, Layout));
        Assert.Equal("*", KeyNames.Name(0x2B, Layout));
        // Tab, Space, arrows and the keypad keep their names on every layout.
        Assert.Equal("Tab", KeyNames.Name(ScanCodes.Tab, _ => "x"));
        Assert.Equal("Space", KeyNames.Name(ScanCodes.Space, _ => "x"));
        Assert.Equal("←", KeyNames.Name(ScanCodes.LeftArrow, _ => "x"));
        Assert.Equal("Num *", KeyNames.Name(0x37, _ => "x"));
    }

    [Fact]
    public void MissingLayoutNamesFallBackToUsNames()
    {
        Assert.Equal("Q", KeyNames.Name(ScanCodes.Q, null));
        Assert.Equal("Q", KeyNames.Name(ScanCodes.Q, _ => null));
        Assert.Equal("Q", KeyNames.Name(ScanCodes.Q, _ => ""));
        Assert.Equal("`", KeyNames.UsName(ScanCodes.LeftOf1));
        Assert.Equal("<", KeyNames.UsName(0x56));
        Assert.Equal("Key C8", KeyNames.UsName(200));
    }
}
