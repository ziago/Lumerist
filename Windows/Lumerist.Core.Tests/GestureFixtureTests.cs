using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace Lumerist.Core.Tests;

// Runs Shared/fixtures/gestures (format: Shared/fixtures/README.md) against
// KeyGestureEngine. The Mac app runs the same cases with GestureRunner in
// Tests/LumeristCoreTests/SharedFixtureTests.swift; both runners read a step
// the same way and check the same things.

public class GestureFixtureTests
{
    [Fact]
    public void Gestures() => Fixtures.Run("gestures", Fixtures.Cases("gestures"), (testCase, _) => Run(testCase));

    private static readonly Dictionary<string, EngineContext> Contexts = new()
    {
        ["develop"] = new EngineContext(),
        ["masking"] = new EngineContext(Tool: "masking"),
        ["library"] = new EngineContext(Module: "library"),
        ["map"] = new EngineContext(Module: "map"),
        ["away"] = new EngineContext(IsLightroomFrontmost: false),
    };

    private static readonly HashSet<string> Actions =
        ["down", "up", "wheel", "swipe", "click", "release", "move", "paletteDidClose", "setKeymap"];

    private static readonly HashSet<string> Options = ["repeat", "at", "in", "textFocused", "modifiers", "momentum", "count"];

    private static readonly HashSet<string> Expectations = ["decision", "intents", "lastIntent", "focusQueries", "state"];

    /// <summary>Failure messages show ↓ and ↑ as written in the cases.</summary>
    private static readonly JsonSerializerOptions Readable = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private static MouseButton Button(string name) => name switch
    {
        "left" => MouseButton.Left,
        "right" => MouseButton.Right,
        "middle" => MouseButton.Middle,
        "back" => MouseButton.Back,
        "forward" => MouseButton.Forward,
        _ => throw new FixtureException("unknown button " + name),
    };

    /// <summary>One gesture case: a fresh engine, then each step and its checks.</summary>
    private static List<string> Run(JsonObject testCase)
    {
        var steps = testCase["steps"] as JsonArray ?? throw new FixtureException("no steps");
        var engine = new KeyGestureEngine<string>(KeymapChanges.Apply(testCase["keymap"]));
        var failures = new List<string>();

        for (var index = 0; index < steps.Count; index++)
        {
            var label = $"step {index + 1}";
            var step = steps[index] as JsonObject ?? throw new FixtureException($"{label} must be an object");
            var fields = step.Select(pair => pair.Key).ToList();
            var actionFields = fields.Where(Actions.Contains).ToList();
            if (actionFields.Count != 1) throw new FixtureException($"{label} must do exactly one thing");
            var action = actionFields[0];
            if (fields.FirstOrDefault(field => !Actions.Contains(field) && !Options.Contains(field) && !Expectations.Contains(field))
                is { } unknown)
            {
                throw new FixtureException($"{label}: unknown field {unknown}");
            }
            var contextName = FixtureValues.OptionalText(step["in"]) ?? "develop";
            var context = Contexts.GetValueOrDefault(contextName) ?? throw new FixtureException($"{label}: unknown context {contextName}");
            var modifiers = WinKeys.Modifiers(Strings(step["modifiers"]) ?? []);
            var time = FixtureValues.OptionalNumber(step["at"]) ?? 0;
            var focusQueries = 0;

            KeyGestureEngine<string>.Outcome? outcome;
            switch (action)
            {
                case "down":
                {
                    var text = FixtureValues.OptionalText(step["down"]) ?? "";
                    var focused = OptionalFlag(step["textFocused"]) ?? false;
                    var chord = WinKeys.Chord(text);
                    outcome = engine.KeyDown(chord, WinKeys.BaseKey(text) + "↓", OptionalFlag(step["repeat"]) ?? false, time,
                        context, () =>
                        {
                            focusQueries += 1;
                            return focused;
                        });
                    break;
                }
                case "up":
                {
                    var key = FixtureValues.OptionalText(step["up"]) ?? "";
                    outcome = engine.KeyUp(WinKeys.ScanCode(key), key + "↑", time, context);
                    break;
                }
                case "wheel":
                {
                    var lines = FixtureValues.Number(step["wheel"], "wheel");
                    outcome = engine.Scroll(lines, lines * 10, isContinuous: false, isMomentum: false, modifiers, context);
                    break;
                }
                case "swipe":
                    outcome = engine.Scroll(0, FixtureValues.Number(step["swipe"], "swipe"), isContinuous: true,
                        isMomentum: OptionalFlag(step["momentum"]) ?? false, modifiers, context);
                    break;
                case "click":
                    outcome = engine.MouseDown(Button(FixtureValues.OptionalText(step["click"]) ?? ""),
                        OptionalInteger(step["count"]) ?? 1, "click", context);
                    break;
                case "release":
                    outcome = engine.MouseUp(Button(FixtureValues.OptionalText(step["release"]) ?? ""), "release", context);
                    break;
                case "move":
                    outcome = engine.MouseMoved(FixtureValues.Number(step["move"], "move"), 0, modifiers, "move", context);
                    break;
                case "paletteDidClose":
                    engine.PaletteDidClose();
                    outcome = null;
                    break;
                default: // setKeymap
                    engine.Update(KeymapChanges.Apply(step["setKeymap"]));
                    outcome = null;
                    break;
            }

            if (step.TryGetPropertyValue("decision", out var expectedDecision))
            {
                if (outcome is null) throw new FixtureException($"{label}: no decision to check");
                var actual = outcome.Decision switch
                {
                    KeyGestureEngine<string>.Decision.Pass => "pass",
                    KeyGestureEngine<string>.Decision.Consume => "consume",
                    KeyGestureEngine<string>.Decision.Replace replace => string.Join(" ", replace.Events),
                    _ => "?",
                };
                var wanted = Strings(expectedDecision) is { } events
                    ? string.Join(" ", events)
                    : FixtureValues.OptionalText(expectedDecision) ?? "?";
                if (actual != wanted) failures.Add($"{label} decision: expected {wanted}, got {actual}");
            }
            if (step["intents"] is JsonArray expectedIntents)
            {
                var wanted = expectedIntents.Select(FixtureValues.Intent).ToList();
                var got = outcome?.Intents ?? [];
                if (!FixtureValues.Same(got, wanted))
                    failures.Add($"{label} intents: expected [{string.Join(", ", wanted)}], got [{string.Join(", ", got)}]");
            }
            if (step.TryGetPropertyValue("lastIntent", out var expectedLast))
            {
                var wanted = FixtureValues.Intent(expectedLast);
                var got = outcome?.Intents.LastOrDefault();
                if (got is null || !FixtureValues.Same(got, wanted))
                    failures.Add($"{label} last intent: expected {wanted}, got {got?.ToString() ?? "nothing"}");
            }
            if (OptionalInteger(step["focusQueries"]) is { } expectedQueries && expectedQueries != focusQueries)
                failures.Add($"{label} focus queries: expected {expectedQueries}, got {focusQueries}");
            foreach (var (name, value) in step["state"] as JsonObject ?? [])
            {
                var matches = name switch
                {
                    "engaged" => engine.IsEngaged == OptionalFlag(value),
                    "paused" => engine.IsPaused == OptionalFlag(value),
                    "paletteOpen" => engine.IsPaletteOpen == OptionalFlag(value),
                    "needsScrollEvents" => engine.NeedsScrollEvents == OptionalFlag(value),
                    "activeParameter" => value is null
                        ? engine.ActiveParameter is null
                        : engine.ActiveParameter == FixtureValues.Parameter(value),
                    "heldEvents" => Strings(value) is { } held && engine.HeldEvents.SequenceEqual(held),
                    _ => throw new FixtureException($"{label}: unknown state {name}"),
                };
                if (!matches) failures.Add($"{label} state {name}: expected {value?.ToJsonString(Readable) ?? "null"}");
            }
        }
        return failures;
    }

    // Loose reads, like the Swift runner's "as? T ?? default": a value of another type counts as missing.

    private static int? OptionalInteger(JsonNode? node) =>
        node?.GetValueKind() == JsonValueKind.Number && node.AsValue().TryGetValue(out int value) ? value : null;

    private static bool? OptionalFlag(JsonNode? node) => node?.GetValueKind() switch
    {
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => null,
    };

    /// <summary>A list of strings, or null when the node is anything else.</summary>
    private static List<string>? Strings(JsonNode? node)
    {
        if (node is not JsonArray array) return null;
        var result = new List<string>();
        foreach (var item in array)
        {
            if (FixtureValues.OptionalText(item) is not { } text) return null;
            result.Add(text);
        }
        return result;
    }
}
