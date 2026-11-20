using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Lumerist.Core;
using Xunit;

namespace Lumerist.Core.Tests;

/// <summary>
/// Runs Shared/fixtures/protocol (requests.json and messages.json) as one
/// "protocol" group, like ProtocolRunner in the Mac's SharedFixtureTests.swift.
/// </summary>
public class ProtocolFixtureTests
{
    [Fact]
    public void ProtocolLines()
    {
        var requests = Fixtures.Object(Path.Combine("Shared", "fixtures", "protocol", "requests.json"));
        var messages = Fixtures.Object(Path.Combine("Shared", "fixtures", "protocol", "messages.json"));
        var cases = CasesOf(requests).Concat(CasesOf(messages));
        Fixtures.Run("protocol", cases, (testCase, id) =>
            id.StartsWith("protocol-requests/", StringComparison.Ordinal) ? RunRequest(testCase) : RunMessage(testCase));
    }

    private static IEnumerable<JsonObject> CasesOf(JsonObject file) =>
        (file["cases"] as JsonArray ?? []).Select(node => node as JsonObject ?? throw new FixtureException("a case must be an object"));

    // Requests ----------------------------------------------------------------

    private static LightroomRequestKind Request(JsonObject obj)
    {
        ParameterScope Scope() => ParameterScopes.FromWireName(FixtureValues.OptionalText(obj["scope"]))
            ?? throw new FixtureException("bad scope");
        string Parameter() => FixtureValues.OptionalText(obj["parameter"]) ?? "";

        return FixtureValues.OptionalText(obj["kind"]) switch
        {
            "ping" => new LightroomRequestKind.Ping(),
            "adjust" => new LightroomRequestKind.Adjust(Scope(), Parameter(), FixtureValues.Number(obj["delta"], "delta")),
            "reset" => new LightroomRequestKind.Reset(Scope(), Parameter()),
            "gesture" => new LightroomRequestKind.Gesture(
                GesturePhases.FromWireName(FixtureValues.OptionalText(obj["phase"])) ?? throw new FixtureException("bad phase"),
                FixtureValues.OptionalText(obj["parameter"])),
            "command" => new LightroomRequestKind.Command(
                LightroomCommandNames.FromWireName(FixtureValues.OptionalText(obj["name"])) ?? throw new FixtureException("unknown command"),
                FixtureValues.OptionalText(obj["argument"])),
            "query" => new LightroomRequestKind.Query(
                LightroomQueries.FromWireName(FixtureValues.OptionalText(obj["name"])) ?? throw new FixtureException("unknown query")),
            _ => throw new FixtureException("unknown request kind"),
        };
    }

    private static string ErrorName<TEnum>(TEnum error) where TEnum : struct, Enum
    {
        var name = error.ToString();
        return char.ToLowerInvariant(name[0]) + name[1..];
    }

    private static IEnumerable<string> RunRequest(JsonObject testCase)
    {
        var obj = testCase["request"] as JsonObject ?? throw new FixtureException("no request");
        var kind = Request(obj);
        if (FixtureValues.OptionalText(testCase["error"]) is { } expected)
        {
            try
            {
                _ = new LightroomRequest(kind);
                return [$"request accepted, expected {expected}"];
            }
            catch (LightroomRequest.ValidationException error)
            {
                return ErrorName(error.Error) == expected ? [] : [$"expected {expected}, got {ErrorName(error.Error)}: {error.Message}"];
            }
        }
        if (OptionalInteger(testCase["wireId"]) is not { } id || testCase["line"] is not JsonObject line)
            throw new FixtureException("needs wireId and line");

        var data = RequestCodec.EncodeLine(new LightroomRequest(kind), id);
        if (data.Length == 0 || data[^1] != (byte)'\n' || Array.IndexOf(data, (byte)'\n') != data.Length - 1)
            return ["a line must end with one newline"];
        var decoded = JsonNode.Parse(data.AsSpan(0, data.Length - 1));
        return JsonSame(decoded, line) ? [] : [$"expected {line.ToJsonString()}, got {Encoding.UTF8.GetString(data)}"];
    }

    // Messages ----------------------------------------------------------------

    private static LightroomContext Context(JsonObject obj) =>
        new(FixtureValues.OptionalText(obj["module"]) ?? "", FixtureValues.OptionalText(obj["tool"]) ?? "");

    private static LightroomMessage Message(JsonObject obj)
    {
        if (obj["ack"] is JsonObject ack)
        {
            return new LightroomMessage.Ack(new LightroomAck(
                OptionalInteger(ack["id"]), FixtureValues.Flag(ack["accepted"], "accepted"),
                FixtureValues.OptionalText(ack["message"]), FixtureValues.OptionalText(ack["parameter"]),
                ParameterScopes.FromWireName(FixtureValues.OptionalText(ack["scope"])),
                OptionalNumber(ack["value"]), OptionalNumber(ack["minimum"]), OptionalNumber(ack["maximum"]),
                FixtureValues.OptionalText(ack["unit"])));
        }
        if (obj["context"] is JsonObject context) return new LightroomMessage.Context(Context(context));
        if (obj["result"] is JsonObject result)
        {
            if (result["context"] is JsonObject value)
                return new LightroomMessage.Result(new LightroomResult.Context(OptionalInteger(value["id"]), Context(value)));
            if (result["presets"] is JsonObject presets)
            {
                var items = (presets["items"] as JsonArray ?? []).Select(node => node as JsonObject ?? []).Select(item => new PresetItem(
                    FixtureValues.OptionalText(item["id"]) ?? "", FixtureValues.OptionalText(item["name"]) ?? "",
                    FixtureValues.OptionalText(item["group"]) ?? "")).ToList();
                var truncated = presets["truncated"]?.GetValueKind() == JsonValueKind.True;
                return new LightroomMessage.Result(new LightroomResult.Presets(OptionalInteger(presets["id"]), items, truncated));
            }
            if (result["catalog"] is JsonObject catalog)
            {
                return new LightroomMessage.Result(new LightroomResult.Catalog(OptionalInteger(catalog["id"]),
                    OptionalTexts(catalog["unsupported"]), OptionalTexts(catalog["skipped"])));
            }
        }
        throw new FixtureException("bad expected message " + obj.ToJsonString());
    }

    private static IEnumerable<string> RunMessage(JsonObject testCase)
    {
        if (!testCase.TryGetPropertyValue("line", out var line) || line is null) throw new FixtureException("no line");
        var data = Encoding.UTF8.GetBytes(line.ToJsonString() + "\n");
        if (FixtureValues.OptionalText(testCase["error"]) is { } expected)
        {
            try
            {
                var decoded = MessageCodec.DecodeLine(data);
                return [$"decoded {Describe(decoded)}, expected {expected}"];
            }
            catch (MessageCodec.ValidationException error)
            {
                return ErrorName(error.Error) == expected ? [] : [$"expected {expected}, got {ErrorName(error.Error)}: {error.Message}"];
            }
        }
        if (!testCase.TryGetPropertyValue("message", out var expectedNode)) throw new FixtureException("needs message or error");
        var message = MessageCodec.DecodeLine(data);
        if (expectedNode is null)
            return message is null ? [] : [$"expected the line to be ignored, got {Describe(message)}"];
        var wanted = Message(expectedNode as JsonObject ?? throw new FixtureException("bad message"));
        return Equals(message, wanted) ? [] : [$"expected {Describe(wanted)}, got {Describe(message)}"];
    }

    // Values ------------------------------------------------------------------

    private static string Describe(LightroomMessage? message) => message switch
    {
        null => "nothing",
        LightroomMessage.Result { Value: LightroomResult.Presets presets } =>
            $"{presets} items [{string.Join(", ", presets.Items)}]",
        LightroomMessage.Result { Value: LightroomResult.Catalog catalog } =>
            $"{catalog} unsupported [{string.Join(", ", catalog.Unsupported)}] skipped [{string.Join(", ", catalog.Skipped)}]",
        _ => message.ToString(),
    };

    /// <summary>A whole number, or null (like Swift's "as? Int").</summary>
    private static long? OptionalInteger(JsonNode? node) =>
        node is JsonValue value && value.GetValueKind() == JsonValueKind.Number && value.TryGetValue<long>(out var number) ? number : null;

    private static double? OptionalNumber(JsonNode? node) =>
        node?.GetValueKind() == JsonValueKind.Number ? node.GetValue<double>() : null;

    private static List<string> OptionalTexts(JsonNode? node) =>
        node is JsonArray list && list.All(item => item?.GetValueKind() == JsonValueKind.String)
            ? list.Select(item => item!.GetValue<string>()).ToList()
            : [];

    /// <summary>JSON equality with numbers compared by value (5 equals 5.0), like NSDictionary.isEqual.</summary>
    internal static bool JsonSame(JsonNode? a, JsonNode? b)
    {
        if (a is null || b is null) return a is null && b is null;
        var kind = a.GetValueKind();
        if (kind != b.GetValueKind()) return false;
        switch (kind)
        {
            case JsonValueKind.Object:
            {
                var left = a.AsObject();
                var right = b.AsObject();
                return left.Count == right.Count
                    && left.All(pair => right.TryGetPropertyValue(pair.Key, out var other) && JsonSame(pair.Value, other));
            }
            case JsonValueKind.Array:
            {
                var left = a.AsArray();
                var right = b.AsArray();
                return left.Count == right.Count && left.Zip(right).All(pair => JsonSame(pair.First, pair.Second));
            }
            case JsonValueKind.Number:
                return a.GetValue<double>() == b.GetValue<double>();
            case JsonValueKind.String:
                return a.GetValue<string>() == b.GetValue<string>();
            default:
                return true; // true, false, null: equal kinds are equal values
        }
    }
}

/// <summary>Protocol 3 requests: validation against the catalog and the wire format (LumeristCoreTests.swift).</summary>
public class ProtocolTests
{
    private static string Text(byte[] line) => Encoding.UTF8.GetString(line);

    [Fact]
    public void EncodesAnAdjustmentWithSortedKeysAndANewline()
    {
        var request = LightroomRequest.Adjust(ParameterScope.Develop, "Exposure", 0.05);
        Assert.Equal("{\"delta\":0.05,\"id\":7,\"kind\":\"adjust\",\"parameter\":\"Exposure\",\"scope\":\"develop\",\"version\":3}\n",
            Text(RequestCodec.EncodeLine(request, 7)));
    }

    [Fact]
    public void WholeNumbersHaveNoFraction()
    {
        var line = Text(RequestCodec.EncodeLine(LightroomRequest.Adjust(ParameterScope.Library, "Temperature", -20), 8));
        Assert.Contains("\"delta\":-20,", line);
    }

    [Fact]
    public void RoundTripsEveryKind()
    {
        LightroomRequest[] requests =
        [
            LightroomRequest.Ping,
            LightroomRequest.Adjust(ParameterScope.Library, "Exposure", -0.3),
            LightroomRequest.Reset(ParameterScope.Develop, "lumerist.cropZoom"),
            LightroomRequest.Gesture(GesturePhase.Begin, "local_Exposure"),
            LightroomRequest.Gesture(GesturePhase.End, null),
            LightroomRequest.Command(LightroomCommandName.CreateMask, "aiSelection/subject"),
            LightroomRequest.Command(LightroomCommandName.Undo),
            LightroomRequest.Query(LightroomQuery.Presets),
        ];
        for (var index = 0; index < requests.Length; index++)
        {
            var decoded = RequestCodec.DecodeLine(RequestCodec.EncodeLine(requests[index], index));
            Assert.Equal(index, decoded.Id);
            Assert.Equal(requests[index], decoded.Request);
        }
    }

    [Fact]
    public void SlashesInArgumentsAreNotEscaped()
    {
        var line = Text(RequestCodec.EncodeLine(LightroomRequest.Command(LightroomCommandName.AddToMask, "rangeMask/luminance"), 1));
        Assert.Contains("\"argument\":\"rangeMask/luminance\"", line);
    }

    [Fact]
    public void AdjustmentsAreLimitedByTheCatalog()
    {
        _ = LightroomRequest.Adjust(ParameterScope.Develop, "Exposure", 1);
        var error = Assert.Throws<LightroomRequest.ValidationException>(() => LightroomRequest.Adjust(ParameterScope.Develop, "Exposure", 1.01));
        Assert.Equal(LightroomRequest.ValidationError.InvalidDelta, error.Error);
        Assert.Equal(new ParameterKey(ParameterScope.Develop, "Exposure"), error.Key);
        Assert.Equal(1, error.Limit);
        _ = LightroomRequest.Adjust(ParameterScope.Develop, "Contrast", -20);
        Assert.Throws<LightroomRequest.ValidationException>(() => LightroomRequest.Adjust(ParameterScope.Develop, "Contrast", 20.5));
        Assert.Throws<LightroomRequest.ValidationException>(() => LightroomRequest.Adjust(ParameterScope.Develop, "Exposure", double.NaN));
        Assert.Throws<LightroomRequest.ValidationException>(() => LightroomRequest.Adjust(ParameterScope.Develop, "Exposure", double.PositiveInfinity));
    }

    [Fact]
    public void ParametersMustExistInTheirScope()
    {
        _ = LightroomRequest.Adjust(ParameterScope.Develop, "SplitToningShadowHue", 20);
        _ = LightroomRequest.Adjust(ParameterScope.Library, "Clarity", 1);
        var error = Assert.Throws<LightroomRequest.ValidationException>(() => LightroomRequest.Adjust(ParameterScope.Library, "Dehaze", 1));
        Assert.Equal(LightroomRequest.ValidationError.UnknownParameter, error.Error);
        Assert.Equal(new ParameterKey(ParameterScope.Library, "Dehaze"), error.Key);
        Assert.Throws<LightroomRequest.ValidationException>(() => LightroomRequest.Adjust(ParameterScope.Develop, "NotAParameter", 1));
    }

    [Fact]
    public void ResetIsOnlyForDevelop()
    {
        _ = LightroomRequest.Reset(ParameterScope.Develop, "Exposure");
        var error = Assert.Throws<LightroomRequest.ValidationException>(() => LightroomRequest.Reset(ParameterScope.Library, "Exposure"));
        Assert.Equal(LightroomRequest.ValidationError.ResetNotAvailable, error.Error);
        Assert.Equal(ParameterScope.Library, error.Scope);
    }

    [Fact]
    public void GestureBeginNeedsAParameter()
    {
        _ = LightroomRequest.Gesture(GesturePhase.End, null);
        var error = Assert.Throws<LightroomRequest.ValidationException>(() => LightroomRequest.Gesture(GesturePhase.Begin, null));
        Assert.Equal(LightroomRequest.ValidationError.MissingGestureParameter, error.Error);
        Assert.Throws<LightroomRequest.ValidationException>(() => LightroomRequest.Gesture(GesturePhase.Begin, "Nope"));
    }

    [Fact]
    public void CommandArgumentsAreChecked()
    {
        (LightroomCommandName, string?)[] good =
        [
            (LightroomCommandName.Undo, null), (LightroomCommandName.Rating, "0"), (LightroomCommandName.Rating, "5"),
            (LightroomCommandName.ColorLabel, "none"), (LightroomCommandName.SwitchModule, "library"),
            (LightroomCommandName.SelectTool, "masking"), (LightroomCommandName.RevealPanel, "Exposure"),
            (LightroomCommandName.CreateMask, "aiSelection/subject"), (LightroomCommandName.AddToMask, "brush"),
            (LightroomCommandName.ApplyPreset, "A1B2-C3D4"),
        ];
        foreach (var (name, argument) in good) _ = LightroomRequest.Command(name, argument);

        (LightroomCommandName, string?)[] bad =
        [
            (LightroomCommandName.Undo, "x"), (LightroomCommandName.Rating, "6"), (LightroomCommandName.Rating, "05"),
            (LightroomCommandName.Rating, null), (LightroomCommandName.Rating, "+5"), (LightroomCommandName.Rating, "-0"),
            (LightroomCommandName.ColorLabel, "pink"), (LightroomCommandName.SwitchModule, "photoshop"),
            (LightroomCommandName.SelectTool, "lasso"), (LightroomCommandName.RevealPanel, "Nope"),
            (LightroomCommandName.CreateMask, "foo/bar"), (LightroomCommandName.CreateMask, "brush/sky/x"),
            (LightroomCommandName.CreateMask, "brush/"), (LightroomCommandName.ApplyPreset, "bad uuid!"),
            (LightroomCommandName.ApplyPreset, ""), (LightroomCommandName.ApplyPreset, new string('A', 65)),
            (LightroomCommandName.ApplyPreset, "café"),
        ];
        foreach (var (name, argument) in bad)
        {
            var error = Assert.Throws<LightroomRequest.ValidationException>(() => LightroomRequest.Command(name, argument));
            Assert.Equal(LightroomRequest.ValidationError.InvalidArgument, error.Error);
            Assert.Equal(name, error.CommandName);
            Assert.Equal(argument, error.Argument);
        }
    }

    [Fact]
    public void CommandNamesUseTheProtocolSpelling()
    {
        Assert.Equal(26, LightroomCommandNames.All.Count);
        Assert.Equal("autoWhiteBalance", LightroomCommandName.AutoWhiteBalance.WireName());
        Assert.Equal("toggleMaskOverlay", LightroomCommandName.ToggleMaskOverlay.WireName());
        foreach (var name in LightroomCommandNames.All) Assert.Equal(name, LightroomCommandNames.FromWireName(name.WireName()));
        Assert.Null(LightroomCommandNames.FromWireName("AutoTone"));
        Assert.Null(LightroomCommandNames.FromWireName(null));
    }

    [Fact]
    public void RejectsOtherProtocolVersionsOnDecode()
    {
        var error = Assert.Throws<RequestCodec.DecodingException>(() => RequestCodec.DecodeLine("{\"id\":1,\"kind\":\"ping\",\"version\":2}"u8.ToArray()));
        Assert.Equal(RequestCodec.DecodingError.UnsupportedVersion, error.Error);
        Assert.Equal(2, error.Version);
        var malformed = Assert.Throws<RequestCodec.DecodingException>(() => RequestCodec.DecodeLine("{\"id\":1,\"kind\":\"dance\",\"version\":3}"u8.ToArray()));
        Assert.Equal(RequestCodec.DecodingError.Malformed, malformed.Error);
        Assert.Throws<RequestCodec.DecodingException>(() => RequestCodec.DecodeLine("not json"u8.ToArray()));
    }

    [Fact]
    public void EveryCatalogEntryIsAddressable()
    {
        foreach (var descriptor in ParameterCatalog.All)
        {
            var request = LightroomRequest.Adjust(descriptor.Scope, descriptor.Id, descriptor.MaximumDelta);
            Assert.Equal(descriptor.Key, request.ParameterKey);
        }
    }

    [Fact]
    public void FixtureNumbersCompareByValue()
    {
        Assert.True(ProtocolFixtureTests.JsonSame(JsonNode.Parse("{\"a\":5,\"b\":[1.0]}"), JsonNode.Parse("{\"b\":[1],\"a\":5.0}")));
        Assert.False(ProtocolFixtureTests.JsonSame(JsonNode.Parse("{\"a\":5}"), JsonNode.Parse("{\"a\":5,\"b\":null}")));
        Assert.False(ProtocolFixtureTests.JsonSame(JsonNode.Parse("{\"a\":\"5\"}"), JsonNode.Parse("{\"a\":5}")));
    }
}
