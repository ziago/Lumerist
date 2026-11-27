using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace Lumerist.Core.Tests;

// Support for the cases in Shared/fixtures (format: Shared/fixtures/README.md).
// The Mac app runs the same files in Tests/LumeristCoreTests/SharedFixtureTests.swift.
// Key names in the cases are logical ("Q", "LeftOf1", "Combo+K"); WinKeys turns
// them into scan codes. Every runner records the ids it ran in
// build/parity/csharp/<group>.txt for Scripts/check-parity.py.

public sealed class FixtureException(string message) : Exception(message);

public static class Fixtures
{
    /// <summary>The repository root (the folder holding Shared/contract.json).</summary>
    public static string Root { get; } = FindRoot();

    private static string FindRoot()
    {
        for (var folder = new DirectoryInfo(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
        {
            if (File.Exists(Path.Combine(folder.FullName, "Shared", "contract.json"))) return folder.FullName;
        }
        throw new FixtureException("Shared/contract.json not found above " + AppContext.BaseDirectory);
    }

    public static JsonObject Object(string relativePath) =>
        JsonNode.Parse(File.ReadAllText(Path.Combine(Root, relativePath)))?.AsObject()
        ?? throw new FixtureException(relativePath + " is not a JSON object");

    /// <summary>Every case of every file in Shared/fixtures/&lt;directory&gt;, in file order.</summary>
    public static List<JsonObject> Cases(string directory)
    {
        var folder = Path.Combine(Root, "Shared", "fixtures", directory);
        var cases = new List<JsonObject>();
        foreach (var file in Directory.GetFiles(folder, "*.json").Order(StringComparer.Ordinal))
        {
            var list = Object(Path.GetRelativePath(Root, file))["cases"]?.AsArray()
                ?? throw new FixtureException(Path.GetFileName(file) + " has no cases");
            cases.AddRange(list.Select(node => node!.AsObject()));
        }
        return cases;
    }

    /// <summary>Runs <paramref name="body"/> for each case, then fails with every problem, named by case id.</summary>
    public static void Run(string group, IEnumerable<JsonObject> cases, Func<JsonObject, string, IEnumerable<string>> body)
    {
        var ids = new List<string>();
        var failures = new List<string>();
        foreach (var testCase in cases)
        {
            var id = testCase["id"]?.GetValue<string>() ?? "<no id>";
            ids.Add(id);
            try
            {
                failures.AddRange(body(testCase, id).Select(failure => $"{id}: {failure}"));
            }
            catch (Exception error)
            {
                failures.Add($"{id}: {error.GetType().Name}: {error.Message}");
            }
        }
        if (ids.Count == 0) failures.Add($"no cases in {group}");
        if (ids.Distinct().Count() != ids.Count) failures.Add($"duplicate case ids in {group}");
        RecordRun(group, ids);
        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    private static void RecordRun(string group, IEnumerable<string> ids)
    {
        var folder = Path.Combine(Root, "build", "parity", "csharp");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, group + ".txt"),
            string.Join("\n", ids.Order(StringComparer.Ordinal)) + "\n");
    }
}

/// <summary>Logical key names → Windows Set 1 scan codes (extended keys carry 0xE000).</summary>
public static class WinKeys
{
    public static IReadOnlyDictionary<string, int> Codes { get; } = new Dictionary<string, int>
    {
        ["A"] = 0x1E, ["S"] = 0x1F, ["D"] = 0x20, ["F"] = 0x21, ["H"] = 0x23, ["G"] = 0x22, ["Z"] = 0x2C,
        ["X"] = 0x2D, ["C"] = 0x2E, ["V"] = 0x2F, ["B"] = 0x30, ["Q"] = 0x10, ["W"] = 0x11, ["E"] = 0x12,
        ["R"] = 0x13, ["Y"] = 0x15, ["T"] = 0x14, ["O"] = 0x18, ["U"] = 0x16, ["I"] = 0x17, ["P"] = 0x19,
        ["L"] = 0x26, ["J"] = 0x24, ["K"] = 0x25, ["N"] = 0x31, ["M"] = 0x32,
        ["1"] = 0x02, ["2"] = 0x03, ["3"] = 0x04, ["4"] = 0x05, ["5"] = 0x06, ["6"] = 0x07, ["7"] = 0x08,
        ["8"] = 0x09, ["9"] = 0x0A, ["0"] = 0x0B,
        ["["] = 0x1A, ["]"] = 0x1B, ["="] = 0x0D, ["-"] = 0x0C, ["'"] = 0x28, [";"] = 0x27, ["\\"] = 0x2B,
        [","] = 0x33, ["."] = 0x34, ["/"] = 0x35,
        ["Return"] = 0x1C, ["Tab"] = 0x0F, ["Space"] = 0x39, ["LeftOf1"] = 0x29, ["IsoLeftOfZ"] = 0x56,
        ["Backspace"] = 0x0E, ["Esc"] = 0x01, ["ForwardDelete"] = 0xE053, ["Home"] = 0xE047, ["End"] = 0xE04F,
        ["PageUp"] = 0xE049, ["PageDown"] = 0xE051,
        ["ArrowLeft"] = 0xE04B, ["ArrowRight"] = 0xE04D, ["ArrowUp"] = 0xE048, ["ArrowDown"] = 0xE050,
        ["F1"] = 0x3B, ["F2"] = 0x3C, ["F3"] = 0x3D, ["F4"] = 0x3E, ["F5"] = 0x3F, ["F6"] = 0x40,
        ["F7"] = 0x41, ["F8"] = 0x42, ["F9"] = 0x43, ["F10"] = 0x44, ["F11"] = 0x57, ["F12"] = 0x58,
    };

    /// <summary>Primary (Lightroom's command key) and Control are both Ctrl on Windows.</summary>
    public static IReadOnlyDictionary<string, KeyModifiers> ModifierNames { get; } = new Dictionary<string, KeyModifiers>
    {
        ["Shift"] = KeyModifiers.Shift, ["Alt"] = KeyModifiers.Alt, ["Primary"] = KeyModifiers.Control,
        ["Control"] = KeyModifiers.Control, ["Combo"] = KeyModifiers.Combo,
    };

    public static int ScanCode(string name) =>
        Codes.TryGetValue(name, out var code) ? code : throw new FixtureException("unknown key " + name);

    public static KeyModifiers Modifiers(IEnumerable<string> names)
    {
        var result = KeyModifiers.None;
        foreach (var name in names)
            result |= ModifierNames.TryGetValue(name, out var modifier) ? modifier : throw new FixtureException("unknown modifier " + name);
        return result;
    }

    /// <summary>"Combo+Shift+[" → the chord; the last part is the key.</summary>
    public static KeyChord Chord(string text)
    {
        var parts = text.Split('+');
        if (parts[^1].Length == 0) throw new FixtureException("bad chord " + text);
        return new KeyChord(ScanCode(parts[^1]), Modifiers(parts[..^1]));
    }

    public static string BaseKey(string text) => text.Split('+')[^1];
}

/// <summary>Values written in the cases: parameters, actions, intents.</summary>
public static class FixtureValues
{
    public static bool IsNull(JsonNode? node) => node is null;

    public static string Text(JsonNode? node, string name) =>
        node?.GetValueKind() == JsonValueKind.String ? node.GetValue<string>() : throw new FixtureException(name + " must be text");

    public static double Number(JsonNode? node, string name) =>
        node?.GetValueKind() == JsonValueKind.Number ? node.GetValue<double>() : throw new FixtureException(name + " must be a number");

    public static bool Flag(JsonNode? node, string name) => node?.GetValueKind() switch
    {
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => throw new FixtureException(name + " must be true or false"),
    };

    public static string? OptionalText(JsonNode? node) =>
        node?.GetValueKind() == JsonValueKind.String ? node.GetValue<string>() : null;

    public static double? OptionalNumber(JsonNode? node) =>
        node?.GetValueKind() == JsonValueKind.Number ? node.GetValue<double>() : null;

    /// <summary>"develop/Exposure" → the parameter key.</summary>
    public static ParameterKey Parameter(JsonNode? node)
    {
        var text = Text(node, "parameter");
        var slash = text.IndexOf('/');
        if (slash < 0 || ParameterScopes.FromWireName(text[..slash]) is not { } scope)
            throw new FixtureException("bad parameter " + text);
        return new ParameterKey(scope, text[(slash + 1)..]);
    }

    public static KeyChord? ChordOrNull(JsonNode? node) => node is null ? null : WinKeys.Chord(Text(node, "chord"));

    public static CommandAction Action(JsonNode? node)
    {
        var obj = node as JsonObject ?? throw new FixtureException("bad action");
        if (obj["command"] is { } command) return new CommandAction.Command(Text(command, "command"), OptionalText(obj["argument"]));
        if (obj["shortcut"] is { } shortcut) return new CommandAction.Shortcut(WinKeys.Chord(Text(shortcut, "shortcut")));
        if (obj["preset"] is { } preset) return new CommandAction.Preset(Text(preset, "preset"), OptionalText(obj["name"]) ?? "");
        if (obj["macro"] is JsonArray steps)
        {
            return new CommandAction.Macro(steps.Select(step => new MacroStep(Action(step?["action"]),
                step?["delay"] is { } delay ? Number(delay, "delay") : 0)).ToList());
        }
        throw new FixtureException("bad action " + obj.ToJsonString());
    }

    public static KeyIntent Intent(JsonNode? node)
    {
        if (node?.GetValueKind() == JsonValueKind.String)
        {
            return node.GetValue<string>() switch
            {
                "openPalette" => new KeyIntent.OpenPalette(),
                "closePalette" => new KeyIntent.ClosePalette(),
                "toggleKeyReference" => new KeyIntent.ToggleKeyReference(),
                "restorePointer" => new KeyIntent.RestorePointer(),
                var name => throw new FixtureException("unknown intent " + name),
            };
        }
        var obj = node as JsonObject ?? throw new FixtureException("bad intent");
        if (obj["adjust"] is { } adjust) return new KeyIntent.Adjust(Parameter(adjust), Number(obj["delta"], "delta"));
        if (obj["reset"] is { } reset) return new KeyIntent.Reset(Parameter(reset));
        if (obj["gestureBegan"] is { } began) return new KeyIntent.GestureBegan(Parameter(began));
        if (obj["gestureEnded"] is { } ended) return new KeyIntent.GestureEnded(Parameter(ended));
        if (obj["revealPanel"] is { } reveal) return new KeyIntent.RevealPanel(Parameter(reveal));
        if (obj["unavailableInMask"] is { } unavailable) return new KeyIntent.UnavailableInMask(Parameter(unavailable));
        if (obj["action"] is { } action) return new KeyIntent.RunAction(Action(action));
        if (obj["togglePause"] is { } paused) return new KeyIntent.TogglePause(Flag(paused, "togglePause"));
        throw new FixtureException("unknown intent " + obj.ToJsonString());
    }

    /// <summary>Equal, with slider deltas compared to within 1e-9.</summary>
    public static bool Same(KeyIntent a, KeyIntent b) =>
        a is KeyIntent.Adjust x && b is KeyIntent.Adjust y
            ? x.Key == y.Key && Math.Abs(x.Delta - y.Delta) < 1e-9
            : a.Equals(b);

    public static bool Same(IReadOnlyList<KeyIntent> a, IReadOnlyList<KeyIntent> b) =>
        a.Count == b.Count && a.Zip(b).All(pair => Same(pair.First, pair.Second));
}

/// <summary>Builds the standard layout plus the changes a case lists ("keymap" or "setKeymap").</summary>
public static class KeymapChanges
{
    private static readonly HashSet<string> Known =
        ["input", "palette", "systemKeys", "importKeys", "slots", "commandKeys", "replaceCommandKeys"];

    public static Keymap Apply(JsonNode? node)
    {
        var keymap = Keymap.StandardLayout();
        if (node is null) return keymap;
        var changes = node as JsonObject ?? throw new FixtureException("keymap changes must be an object");
        if (changes.Select(pair => pair.Key).FirstOrDefault(key => !Known.Contains(key)) is { } unknown)
            throw new FixtureException("unknown keymap change " + unknown);

        foreach (var (name, value) in changes["input"] as JsonObject ?? [])
        {
            var input = keymap.Input;
            switch (name)
            {
                case "invertWheel": input.InvertWheel = FixtureValues.Flag(value, name); break;
                case "swapNormalAndCoarse": input.SwapNormalAndCoarse = FixtureValues.Flag(value, name); break;
                case "trackpadPointsPerStep": input.TrackpadPointsPerStep = FixtureValues.Number(value, name); break;
                case "requireMouseDown": input.RequireMouseDown = FixtureValues.Flag(value, name); break;
                case "dragSensitivity": input.DragSensitivity = FixtureValues.Number(value, name); break;
                case "returnCursorAfterRelease": input.ReturnCursorAfterRelease = FixtureValues.Flag(value, name); break;
                case "doubleClickReset": input.DoubleClickReset = FixtureValues.Flag(value, name); break;
                case "middleClickReset": input.MiddleClickReset = FixtureValues.Flag(value, name); break;
                case "mouseBackForward": input.MouseBackForward = FixtureValues.Flag(value, name); break;
                case "followSelectedMask": input.FollowSelectedMask = FixtureValues.Flag(value, name); break;
                case "revealPanels": input.RevealPanels = FixtureValues.Flag(value, name); break;
                case "arrowKeysAdjust": input.ArrowKeysAdjust = FixtureValues.Flag(value, name); break;
                default: throw new FixtureException("unknown input option " + name);
            }
        }
        foreach (var (name, value) in changes["palette"] as JsonObject ?? [])
        {
            if (name != "keepVisible") throw new FixtureException("unknown palette option " + name);
            keymap.Palette.KeepVisible = FixtureValues.Flag(value, name);
        }
        foreach (var (name, value) in changes["systemKeys"] as JsonObject ?? [])
        {
            var chord = FixtureValues.ChordOrNull(value);
            switch (name)
            {
                case "comboKey": keymap.SystemKeys.ComboKey = chord; break;
                case "paletteKey": keymap.SystemKeys.PaletteKey = chord; break;
                case "keyReferenceToggle": keymap.SystemKeys.KeyReferenceToggle = chord; break;
                case "pauseToggle": keymap.SystemKeys.PauseToggle = chord; break;
                default: throw new FixtureException("unknown system key " + name);
            }
        }
        foreach (var (name, value) in changes["importKeys"] as JsonObject ?? [])
        {
            if (name == "advance")
            {
                keymap.ImportKeys.Advance = FixtureValues.Flag(value, name);
                continue;
            }
            var role = ImportKeyRoles.FromWireName(name) ?? throw new FixtureException("unknown Import key " + name);
            keymap.ImportKeys.Set(role, FixtureValues.ChordOrNull(value));
        }
        foreach (var (name, value) in changes["slots"] as JsonObject ?? [])
        {
            var key = FixtureValues.Parameter(JsonValue.Create(name));
            var fields = value as JsonObject ?? throw new FixtureException($"slot {name} must be an object");
            keymap.UpdateSlot(key, slot =>
            {
                foreach (var (field, fieldValue) in fields)
                {
                    slot = field switch
                    {
                        "dial" => slot with { Dial = FixtureValues.ChordOrNull(fieldValue) },
                        "nudgeDown" => slot with { NudgeDown = FixtureValues.ChordOrNull(fieldValue) },
                        "nudgeUp" => slot with { NudgeUp = FixtureValues.ChordOrNull(fieldValue) },
                        "normalStep" => slot with { NormalStep = fieldValue is null ? null : FixtureValues.Number(fieldValue, field) },
                        "coarseStep" => slot with { CoarseStep = fieldValue is null ? null : FixtureValues.Number(fieldValue, field) },
                        _ => throw new FixtureException("unknown slot field " + field),
                    };
                }
                return slot;
            });
        }
        if (changes["commandKeys"] is JsonArray list)
        {
            var added = list.Select(item => CommandKey(item as JsonObject ?? throw new FixtureException("bad command key"))).ToList();
            if (changes["replaceCommandKeys"] is { } replace && FixtureValues.Flag(replace, "replaceCommandKeys"))
                keymap.CommandKeys = added;
            else
                keymap.CommandKeys.AddRange(added);
        }
        return keymap;
    }

    public static CommandKey CommandKey(JsonObject obj)
    {
        var scope = CommandScopes.FromWireName(FixtureValues.OptionalText(obj["scope"]))
            ?? throw new FixtureException("bad command key scope");
        var id = Guid.TryParse(FixtureValues.OptionalText(obj["id"]), out var parsed) ? parsed : Guid.NewGuid();
        return new CommandKey(id, scope, WinKeys.Chord(FixtureValues.Text(obj["chord"], "chord")),
            FixtureValues.OptionalText(obj["group"]) ?? "Lightroom", FixtureValues.Text(obj["title"], "title"),
            FixtureValues.Action(obj["action"]));
    }
}

/// <summary>Checks on the support code itself.</summary>
public class SharedFixtureSupportTests
{
    [Fact]
    public void EveryLogicalKeyHasItsOwnScanCode()
    {
        Assert.Equal(WinKeys.Codes.Count, WinKeys.Codes.Values.Distinct().Count());
        Assert.Equal(KeyChord.LeftOf1.ScanCode, WinKeys.Codes["LeftOf1"]);
        Assert.Equal(KeyChord.Tab.ScanCode, WinKeys.Codes["Tab"]);
    }

    [Fact]
    public void ChordsParse()
    {
        Assert.Equal(new KeyChord(0x25, KeyModifiers.Combo | KeyModifiers.Shift), WinKeys.Chord("Combo+Shift+K"));
        Assert.Equal(new KeyChord(0x2F, KeyModifiers.Control | KeyModifiers.Alt), WinKeys.Chord("Primary+Alt+V"));
        Assert.Equal("Tab + Shift+K", WinKeys.Chord("Combo+Shift+K").DisplayTextWith(KeyChord.Tab));
    }

    [Fact]
    public void TheCatalogIsGenerated()
    {
        Assert.True(ParameterCatalog.All.Count > 100);
        Assert.Equal(0.05, ParameterCatalog.Descriptor(ParameterScope.Develop, "Exposure")!.NormalStep);
        Assert.NotNull(ParameterCatalog.Descriptor(ParameterScope.Library, "Temperature"));
    }
}
