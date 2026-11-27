using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace Lumerist.Core.Tests;

// The "keymap-rules" and "standard-layout" groups of Shared/fixtures, with the
// same semantics as RuleRunner and LayoutRunner in
// Tests/LumeristCoreTests/SharedFixtureTests.swift.

public class KeymapFixtureTests
{
    [Fact]
    public void KeymapRules() =>
        Fixtures.Run("keymap-rules", Fixtures.Cases("keymap-rules"), (testCase, _) => RuleRunner.Run(testCase));

    [Fact]
    public void StandardLayout()
    {
        var fixture = Fixtures.Object(Path.Combine("Shared", "fixtures", "standard-layout.json"));
        if (fixture["id"] is null) fixture["id"] = "standard-layout";
        Fixtures.Run("standard-layout", [fixture], (testCase, _) => LayoutRunner.Run(testCase));
    }
}

internal static class RuleRunner
{
    public static IEnumerable<string> Run(JsonObject testCase)
    {
        var keymap = KeymapChanges.Apply(testCase["keymap"]);
        var failures = new List<string>();

        if (testCase.ContainsKey("validate"))
        {
            var expected = testCase["validate"];
            try
            {
                keymap.Validate();
                if (FixtureValues.OptionalText(expected) != "ok") failures.Add($"validate passed, expected {Json(expected)}");
            }
            catch (KeymapException error)
            {
                if (Mismatch(error, expected) is { } mismatch) failures.Add(mismatch);
            }
        }
        if (testCase["conflicts"] is JsonObject expectedConflicts)
        {
            var conflicts = keymap.Conflicts();
            var errors = conflicts.Where(conflict => conflict.Severity == ConflictSeverity.Error).ToList();
            var warnings = conflicts.Where(conflict => conflict.Severity == ConflictSeverity.Warning).ToList();
            if (Count(expectedConflicts["errors"]) is { } errorCount && errorCount != errors.Count)
                failures.Add($"expected {errorCount} errors, got {Messages(errors)}");
            if (Count(expectedConflicts["warnings"]) is { } warningCount && warningCount != warnings.Count)
                failures.Add($"expected {warningCount} warnings, got {Messages(warnings)}");
            foreach (var matcher in (expectedConflicts["matches"] as JsonArray ?? []).Select(node => node as JsonObject))
            {
                if (matcher is null) throw new FixtureException("a conflict matcher must be an object");
                if (!conflicts.Any(conflict => Matches(conflict, matcher)))
                    failures.Add($"no conflict matches {Json(matcher)}; got {Messages(conflicts)}");
            }
        }
        return failures;
    }

    private static int? Count(JsonNode? node) =>
        node?.GetValueKind() == JsonValueKind.Number ? node.GetValue<int>() : null;

    private static string Messages(IEnumerable<KeyConflict> conflicts) =>
        "[" + string.Join(", ", conflicts.Select(conflict => $"\"{conflict.Message}\"")) + "]";

    private static string Json(JsonNode? node) => node?.ToJsonString() ?? "null";

    public static string WireName(ConflictSeverity severity) => severity == ConflictSeverity.Error ? "error" : "warning";

    /// <summary>Every field of the matcher must hold; "scope": null means a System Key conflict.</summary>
    public static bool Matches(KeyConflict conflict, JsonObject matcher)
    {
        foreach (var (field, value) in matcher)
        {
            switch (field)
            {
                case "severity":
                    if (WireName(conflict.Severity) != FixtureValues.OptionalText(value)) return false;
                    break;
                case "chord":
                    if (conflict.Chord != WinKeys.Chord(FixtureValues.Text(value, "chord"))) return false;
                    break;
                case "scope":
                    if (conflict.Scope?.WireName() != (value is null ? null : FixtureValues.Text(value, "scope"))) return false;
                    break;
                case "message":
                    if (conflict.Message != FixtureValues.OptionalText(value)) return false;
                    break;
                case "lightroomAction":
                    if (conflict.LightroomAction != FixtureValues.OptionalText(value)) return false;
                    break;
                case "usesSystem":
                    var role = SystemKeyRoles.FromWireName(FixtureValues.OptionalText(value))
                        ?? throw new FixtureException("unknown role " + Json(value));
                    if (!conflict.Usages.Contains(new KeyUsage.SystemKey(role))) return false;
                    break;
                default:
                    throw new FixtureException("unknown matcher field " + field);
            }
        }
        return true;
    }

    /// <summary>Null when <paramref name="error"/> is the expected one.</summary>
    public static string? Mismatch(KeymapException error, JsonNode? expected)
    {
        if (expected is not JsonObject obj || FixtureValues.OptionalText(obj["error"]) is not { } name)
            return $"validate threw {error.Kind}: {error.Message}, expected {Json(expected)}";
        ParameterKey? parameter = obj["parameter"] is null ? null : FixtureValues.Parameter(obj["parameter"]);
        var misuse = FixtureValues.OptionalText(obj["misuse"]);
        var same = error.Kind switch
        {
            KeymapErrorKind.InvalidStep => name == "invalidStep" && error.Parameter == parameter,
            KeymapErrorKind.InvalidOption => name == "invalidOption" && error.Option == FixtureValues.OptionalText(obj["option"]),
            KeymapErrorKind.InvalidCommandKey => name == "invalidCommandKey" && error.Reason == FixtureValues.OptionalText(obj["reason"]),
            KeymapErrorKind.InvalidCombo => name == "invalidCombo" && error.Misuse switch
            {
                ComboMisuse.DialKey dial => misuse == "dialKey" && dial.Parameter == parameter,
                ComboMisuse.LightroomShortcut => misuse == "lightroomShortcut",
                ComboMisuse.SystemKey system => misuse == "systemKey" && system.Role.WireName() == FixtureValues.OptionalText(obj["role"]),
                ComboMisuse.ImportKey import => misuse == "importKey" && import.Role.WireName() == FixtureValues.OptionalText(obj["role"]),
                _ => false,
            },
            KeymapErrorKind.Conflicts => name == "conflicts",
            _ => false,
        };
        return same ? null : $"validate threw {error.Kind}: {error.Message}, expected {Json(obj)}";
    }
}

internal static class LayoutRunner
{
    public static IEnumerable<string> Run(JsonObject fixture)
    {
        var keymap = Keymap.StandardLayout();
        var failures = new List<string>();

        foreach (var scope in ParameterScopes.All)
        {
            var expected = (fixture[scope.WireName()]?["dial"] as JsonObject ?? [])
                .ToDictionary(pair => pair.Key, pair => FixtureValues.Text(pair.Value, pair.Key));
            var slots = keymap.Slots(scope);
            if (!slots.Keys.ToHashSet().SetEquals(expected.Keys))
                failures.Add($"{scope.WireName()} parameters differ: {string.Join(", ", slots.Keys.Order(StringComparer.Ordinal))}");
            foreach (var (id, key) in expected)
            {
                var slot = slots.GetValueOrDefault(id);
                if (slot?.Dial != WinKeys.Chord(key)) failures.Add($"{scope.WireName()}/{id} should be on {key}");
                if (slot?.NudgeDown is not null || slot?.NudgeUp is not null || slot?.NormalStep is not null || slot?.CoarseStep is not null)
                    failures.Add($"{scope.WireName()}/{id} should only have a Dial Key");
            }
        }

        var expectedKeys = (fixture["commandKeys"] as JsonArray ?? [])
            .Select(node => KeymapChanges.CommandKey(node as JsonObject ?? throw new FixtureException("bad command key")))
            .ToList();
        if (!keymap.CommandKeys.SequenceEqual(expectedKeys))
        {
            failures.Add("command keys differ: " +
                string.Join(", ", keymap.CommandKeys.Select(key => keymap.DisplayText(key.Chord) + " " + key.Title)));
        }

        var system = fixture["systemKeys"] as JsonObject ?? [];
        foreach (var role in SystemKeyRoles.All)
        {
            if (keymap.SystemKeys.ChordFor(role) != FixtureValues.ChordOrNull(system[role.WireName()]))
                failures.Add($"system key {role.WireName()} differs");
        }

        var importKeys = fixture["importKeys"] as JsonObject ?? [];
        string[] importFields = [.. ImportKeyRoles.All.Select(role => role.WireName()), "advance"];
        if (!importKeys.Select(pair => pair.Key).ToHashSet().SetEquals(importFields))
            failures.Add($"Import key fields differ: {string.Join(", ", importKeys.Select(pair => pair.Key).Order(StringComparer.Ordinal))}");
        foreach (var role in ImportKeyRoles.All)
        {
            if (keymap.ImportKeys.ChordFor(role) != FixtureValues.ChordOrNull(importKeys[role.WireName()]))
                failures.Add($"Import key {role.WireName()} differs");
        }
        var advance = importKeys["advance"]?.GetValueKind() is JsonValueKind.True or JsonValueKind.False
            ? importKeys["advance"]!.GetValue<bool>()
            : (bool?)null;
        if (keymap.ImportKeys.Advance != advance) failures.Add("Import advance differs");

        var input = fixture["input"] as JsonObject ?? [];
        var options = keymap.Input;
        var actual = new Dictionary<string, object>
        {
            ["invertWheel"] = options.InvertWheel, ["swapNormalAndCoarse"] = options.SwapNormalAndCoarse,
            ["trackpadPointsPerStep"] = options.TrackpadPointsPerStep, ["requireMouseDown"] = options.RequireMouseDown,
            ["dragSensitivity"] = options.DragSensitivity, ["returnCursorAfterRelease"] = options.ReturnCursorAfterRelease,
            ["doubleClickReset"] = options.DoubleClickReset, ["middleClickReset"] = options.MiddleClickReset,
            ["mouseBackForward"] = options.MouseBackForward, ["followSelectedMask"] = options.FollowSelectedMask,
            ["revealPanels"] = options.RevealPanels, ["arrowKeysAdjust"] = options.ArrowKeysAdjust,
        };
        if (!input.Select(pair => pair.Key).ToHashSet().SetEquals(actual.Keys))
            failures.Add($"input options differ: {string.Join(", ", input.Select(pair => pair.Key).Order(StringComparer.Ordinal))}");
        foreach (var (name, value) in actual)
        {
            var node = input[name];
            var same = value switch
            {
                bool flag => node?.GetValueKind() is JsonValueKind.True or JsonValueKind.False && node.GetValue<bool>() == flag,
                double number => node?.GetValueKind() == JsonValueKind.Number && node.GetValue<double>() == number,
                _ => false,
            };
            if (!same) failures.Add($"input {name} should be {node?.ToJsonString() ?? "missing"}");
        }

        var palette = fixture["palette"] as JsonObject ?? [];
        var keepVisible = palette["keepVisible"]?.GetValueKind() is JsonValueKind.True or JsonValueKind.False
            ? palette["keepVisible"]!.GetValue<bool>()
            : (bool?)null;
        if (keymap.Palette.KeepVisible != keepVisible || keymap.Palette.Favourites.Count != 0
            || (palette["favourites"] as JsonArray)?.Count != 0)
        {
            failures.Add("palette defaults differ");
        }

        var conflicts = keymap.Conflicts();
        if (conflicts.Count > 0) failures.Add("conflicts: " + string.Join(", ", conflicts.Select(conflict => conflict.Message)));
        return failures;
    }
}
