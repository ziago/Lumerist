using System.Text.Json;
using System.Text.Json.Nodes;
using Lumerist.Core.Input;
using Xunit;

namespace Lumerist.Core.Tests;

// Runs Shared/fixtures/lightroom-keys (format: Shared/fixtures/README.md):
// which keys Lumerist presses for Lightroom's Import keys and fallbacks, in a
// Lightroom language, on a keyboard layout written as the characters its keys
// type. The Mac runs the same cases in Tests/LumeristCoreTests/SharedFixtureTests.swift.

public class LightroomKeysFixtureTests
{
    [Fact]
    public void LightroomKeys()
    {
        // The group arrives with the shared data; every file in the folder is run.
        if (!Directory.Exists(Path.Combine(Fixtures.Root, "Shared", "fixtures", LightroomKeysRunner.Group))) return;
        Fixtures.Run(LightroomKeysRunner.Group, Fixtures.Cases(LightroomKeysRunner.Group), (testCase, _) => LightroomKeysRunner.Run(testCase));
    }
}

internal static class LightroomKeysRunner
{
    public const string Group = "lightroom-keys";

    private static readonly HashSet<string> Fields =
        ["id", "about", "lightroomLanguage", "keyboard", "layout", "deadKeys", "importKeys", "fallbacks"];

    /// <summary>The US layout the cases start from: each key, the character it types alone, its virtual key.</summary>
    private static readonly (string Key, string Text, int VirtualKey)[] UsKeys =
    [
        .. Enumerable.Range('A', 26).Select(letter => (((char)letter).ToString(), char.ToLowerInvariant((char)letter).ToString(), letter)),
        .. Enumerable.Range('0', 10).Select(digit => (((char)digit).ToString(), ((char)digit).ToString(), digit)),
        ("LeftOf1", "`", 0xC0), ("'", "'", 0xDE), (",", ",", 0xBC), (".", ".", 0xBE), ("/", "/", 0xBF), (";", ";", 0xBA),
        ("[", "[", 0xDB), ("]", "]", 0xDD), ("\\", "\\", 0xDC), ("=", "=", 0xBB), ("-", "-", 0xBD),
    ];

    private const string IsoLeftOfZ = "IsoLeftOfZ";

    /// <summary>A virtual key nobody else uses, for a key whose own one went to the key that types its letter.</summary>
    private const int SpareVirtualKey = 0xDF;

    public static IEnumerable<string> Run(JsonObject testCase)
    {
        foreach (var (name, _) in testCase)
        {
            if (!Fields.Contains(name)) throw new FixtureException("unknown field " + name);
        }
        var language = FixtureValues.OptionalText(testCase["lightroomLanguage"]);
        var layout = Layout(testCase);
        var failures = new List<string>();

        if (testCase["importKeys"] is JsonObject expected)
        {
            var keys = ImportShortcuts.ForLayout(layout, language);
            foreach (var (name, value) in expected)
            {
                KeyChord? actual = name switch
                {
                    "include" => keys.Include,
                    "exclude" => keys.Exclude,
                    "toggle" => keys.Toggle,
                    _ => throw new FixtureException("importKeys: unknown field " + name),
                };
                var wanted = FixtureValues.ChordOrNull(value);
                if (actual != wanted) failures.Add($"importKeys.{name}: expected {Describe(wanted)}, got {Describe(actual)}");
            }
        }
        if (testCase["fallbacks"] is JsonObject fallbacks)
        {
            foreach (var (name, value) in fallbacks)
            {
                KeyChord? actual = name == AppCommands.PasteSettingsFromPrevious
                    ? AppCommands.Shortcut(name, layout, language)
                    : LightroomCommandNames.FromWireName(name) is { } command && FallbackCommands.Contains(command)
                        ? FallbackShortcuts.For(command, layout, language)
                        : throw new FixtureException("fallbacks: unknown command " + name);
                var wanted = WinKeys.Chord(FixtureValues.Text(value, name));
                if (actual != wanted) failures.Add($"fallbacks.{name}: expected {Describe(wanted)}, got {Describe(actual)}");
            }
        }
        return failures;
    }

    private static readonly HashSet<LightroomCommandName> FallbackCommands =
    [
        LightroomCommandName.AutoTone, LightroomCommandName.Undo, LightroomCommandName.Redo, LightroomCommandName.FlagPick,
        LightroomCommandName.FlagReject, LightroomCommandName.FlagRemove, LightroomCommandName.ShowClipping,
    ];

    /// <summary>
    /// The case's layout as the app reads one from Windows: the US keys, the
    /// ISO key left of Z (typing nothing) on an "iso" keyboard, then "layout"
    /// and "deadKeys". A key typing a letter or digit has that letter's virtual
    /// key, as Windows layouts give it; any other key keeps its US virtual key
    /// unless the key that types its letter took it (then a spare one), so
    /// where no key types a letter, its virtual key stays on the US key.
    /// </summary>
    internal static KeyboardLayoutTable Layout(JsonObject testCase)
    {
        var keyboard = FixtureValues.OptionalText(testCase["keyboard"]) ?? "ansi";
        if (keyboard is not ("ansi" or "iso")) throw new FixtureException("keyboard must be ansi or iso");
        var texts = UsKeys.ToDictionary(key => key.Key, key => (string?)key.Text, StringComparer.Ordinal);
        var usVirtualKeys = UsKeys.ToDictionary(key => key.Key, key => key.VirtualKey, StringComparer.Ordinal);
        if (keyboard == "iso")
        {
            texts[IsoLeftOfZ] = null;
            usVirtualKeys[IsoLeftOfZ] = 0xE2;
        }
        if (testCase["layout"] is { } changes)
        {
            foreach (var (key, value) in changes as JsonObject ?? throw new FixtureException("layout must be an object"))
            {
                if (!texts.ContainsKey(key))
                    throw new FixtureException(key == IsoLeftOfZ ? "IsoLeftOfZ needs \"keyboard\": \"iso\"" : "layout: unknown key " + key);
                texts[key] = value is null ? null : FixtureValues.Text(value, key);
            }
        }
        var dead = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in testCase["deadKeys"] as JsonArray ?? [])
        {
            var key = FixtureValues.Text(node, "deadKeys");
            if (!texts.ContainsKey(key)) throw new FixtureException("deadKeys: unknown key " + key);
            dead.Add(key);
        }
        var typed = texts.Where(pair => pair.Value is not null).Select(pair => pair.Value).ToList();
        if (typed.Distinct().Count() != typed.Count) throw new FixtureException("two keys type the same character");

        static int? LetterVirtualKey(string? text) =>
            text is { Length: 1 } ? KeyboardLayoutTable.LetterVirtualKey(text[0]) : null;
        var claimed = texts.Values.Select(LetterVirtualKey).OfType<int>().ToHashSet();
        return new KeyboardLayoutTable(texts.Select(pair =>
        {
            var virtualKey = LetterVirtualKey(pair.Value)
                ?? (IsLetterOrDigitKey(pair.Key) && claimed.Contains(usVirtualKeys[pair.Key]) ? SpareVirtualKey : usVirtualKeys[pair.Key]);
            return new LayoutKey(WinKeys.ScanCode(pair.Key), virtualKey, pair.Value, dead.Contains(pair.Key));
        }));
    }

    private static bool IsLetterOrDigitKey(string key) => key.Length == 1 && char.IsAsciiLetterOrDigit(key[0]);

    private static string Describe(KeyChord? chord) => chord is { } value ? $"{value.DisplayText} (0x{value.ScanCode:X2})" : "none";
}

/// <summary>The runner itself, on cases written here (the shared ones arrive with the data).</summary>
public class LightroomKeysRunnerTests
{
    private static List<string> Run(string json) => [.. LightroomKeysRunner.Run(JsonNode.Parse(json)!.AsObject())];

    [Fact]
    public void ItalianOnAnItalianKeyboard()
    {
        Assert.Empty(Run("""
            { "id": "t", "lightroomLanguage": "it", "keyboard": "iso",
              "layout": { "LeftOf1": "\\", ";": "ò", "'": "à", "[": "è", "]": "+", "\\": "ù", "IsoLeftOfZ": "<" },
              "importKeys": { "include": "P", "exclude": "X", "toggle": "IsoLeftOfZ" },
              "fallbacks": { "autoTone": "Primary+U", "undo": "Primary+Z", "pasteSettingsFromPrevious": "Primary+Alt+V" } }
            """));
    }

    [Fact]
    public void GermanQwertzWithADeadBacktick()
    {
        Assert.Empty(Run("""
            { "id": "t", "lightroomLanguage": "de", "keyboard": "iso",
              "layout": { "Y": "z", "Z": "y", "LeftOf1": "^", "=": "´", "IsoLeftOfZ": "<" }, "deadKeys": ["LeftOf1", "="],
              "importKeys": { "toggle": "IsoLeftOfZ" },
              "fallbacks": { "autoTone": "Primary+U", "undo": "Primary+Y", "redo": "Primary+Shift+Y" } }
            """));
    }

    [Fact]
    public void RussianKeepsTheUsKeysWhereNoKeyTypesTheLetter()
    {
        Assert.Empty(Run("""
            { "id": "t", "lightroomLanguage": null,
              "layout": { "P": "з", "X": "ч", "Z": "я", "U": "г", "J": "о", "V": "м", "LeftOf1": "ё" },
              "importKeys": { "include": "P", "exclude": "X", "toggle": null },
              "fallbacks": { "flagPick": "P", "undo": "Primary+Z", "showClipping": "J", "pasteSettingsFromPrevious": "Primary+Alt+V" } }
            """));
    }

    [Fact]
    public void FailuresAndMistakesAreReported()
    {
        Assert.Single(Run("""{ "id": "t", "importKeys": { "toggle": "IsoLeftOfZ" } }"""));
        Assert.Throws<FixtureException>(() => Run("""{ "id": "t", "layout": { "IsoLeftOfZ": "<" } }"""));
        Assert.Throws<FixtureException>(() => Run("""{ "id": "t", "layout": { "Q": "a" } }"""));
        Assert.Throws<FixtureException>(() => Run("""{ "id": "t", "colour": "red" }"""));
        Assert.Throws<FixtureException>(() => Run("""{ "id": "t", "fallbacks": { "rating": "1" } }"""));
    }
}
