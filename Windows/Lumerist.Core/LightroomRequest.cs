using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Lumerist.Core;

/// <summary>
/// Phase of a Dial Key gesture: the plug-in turns begin/end into Lightroom
/// tracking so one gesture becomes one history step.
/// </summary>
public enum GesturePhase
{
    Begin,
    End,
}

public static class GesturePhases
{
    /// <summary>"begin" / "end", as on the wire.</summary>
    public static string WireName(this GesturePhase phase) => phase == GesturePhase.Begin ? "begin" : "end";

    public static GesturePhase? FromWireName(string? name) => name switch
    {
        "begin" => GesturePhase.Begin,
        "end" => GesturePhase.End,
        _ => null,
    };
}

/// <summary>Read-only questions the app can ask the plug-in.</summary>
public enum LightroomQuery
{
    /// <summary>Current module and Develop tool.</summary>
    Context,

    /// <summary>Every develop preset (UUID, name, folder).</summary>
    Presets,

    /// <summary>Catalog parameters this Lightroom does not support.</summary>
    Catalog,
}

public static class LightroomQueries
{
    public static IReadOnlyList<LightroomQuery> All { get; } = [LightroomQuery.Context, LightroomQuery.Presets, LightroomQuery.Catalog];

    /// <summary>"context" / "presets" / "catalog", as on the wire.</summary>
    public static string WireName(this LightroomQuery query) => query switch
    {
        LightroomQuery.Context => "context",
        LightroomQuery.Presets => "presets",
        _ => "catalog",
    };

    public static LightroomQuery? FromWireName(string? name) => name switch
    {
        "context" => LightroomQuery.Context,
        "presets" => LightroomQuery.Presets,
        "catalog" => LightroomQuery.Catalog,
        _ => null,
    };
}

/// <summary>Allow-listed Lightroom commands (docs/protocol-v4.md).</summary>
public enum LightroomCommandName
{
    Undo, Redo, AutoTone, AutoWhiteBalance, ResetAll,
    NextPhoto, PreviousPhoto, FlagPick, FlagReject, FlagRemove,
    Rating, ColorLabel, SwitchModule, SelectTool, RevealPanel,
    ZoomToggle, ShowClipping,
    CreateMask, AddToMask, SubtractFromMask, IntersectWithMask, InvertMask, ToggleMaskOverlay,
    ApplyPreset, CopySettings, PasteSettings,
}

public static class LightroomCommandNames
{
    public static IReadOnlyList<LightroomCommandName> All { get; } = Enum.GetValues<LightroomCommandName>();

    public static IReadOnlySet<string> ColorLabels { get; } = new HashSet<string>(StringComparer.Ordinal)
        { "red", "yellow", "green", "blue", "purple", "none" };

    public static IReadOnlySet<string> Modules { get; } = new HashSet<string>(StringComparer.Ordinal)
        { "library", "develop", "map", "book", "slideshow", "print", "web" };

    public static IReadOnlySet<string> Tools { get; } = new HashSet<string>(StringComparer.Ordinal)
        { "loupe", "crop", "dust", "redeye", "masking", "upright" };

    public static IReadOnlySet<string> MaskTypes { get; } = new HashSet<string>(StringComparer.Ordinal)
        { "brush", "gradient", "radialGradient", "rangeMask", "aiSelection" };

    public static IReadOnlySet<string> MaskSubtypes { get; } = new HashSet<string>(StringComparer.Ordinal)
        { "subject", "sky", "background", "objects", "people", "landscape", "color", "luminance", "depth" };

    // The wire names are the member names starting in lower case ("autoTone").
    private static readonly Dictionary<LightroomCommandName, string> WireNames =
        All.ToDictionary(name => name, name => char.ToLowerInvariant(name.ToString()[0]) + name.ToString()[1..]);

    private static readonly Dictionary<string, LightroomCommandName> ByWireName =
        WireNames.ToDictionary(pair => pair.Value, pair => pair.Key, StringComparer.Ordinal);

    /// <summary>The protocol name, e.g. "autoWhiteBalance".</summary>
    public static string WireName(this LightroomCommandName name) => WireNames[name];

    public static LightroomCommandName? FromWireName(string? name) =>
        name is not null && ByWireName.TryGetValue(name, out var command) ? command : null;

    /// <summary>Whether <paramref name="argument"/> is acceptable for this command (null = none given).</summary>
    public static bool Accepts(this LightroomCommandName name, string? argument)
    {
        switch (name)
        {
            case LightroomCommandName.Rating:
                // A plain number of stars, 0 to 5 ("05" and "+5" are refused).
                return argument is { Length: 1 } && argument[0] is >= '0' and <= '5';
            case LightroomCommandName.ColorLabel:
                return argument is not null && ColorLabels.Contains(argument);
            case LightroomCommandName.SwitchModule:
                return argument is not null && Modules.Contains(argument);
            case LightroomCommandName.SelectTool:
                return argument is not null && Tools.Contains(argument);
            case LightroomCommandName.RevealPanel:
                return argument is not null && ParameterCatalog.Descriptor(ParameterScope.Develop, argument) is not null;
            case LightroomCommandName.CreateMask:
            case LightroomCommandName.AddToMask:
            case LightroomCommandName.SubtractFromMask:
            case LightroomCommandName.IntersectWithMask:
            {
                // "type" or "type/subtype".
                if (argument is null) return false;
                var parts = argument.Split('/');
                if (parts.Length > 2 || !MaskTypes.Contains(parts[0])) return false;
                return parts.Length == 1 || MaskSubtypes.Contains(parts[1]);
            }
            case LightroomCommandName.ApplyPreset:
                // A preset UUID: 1 to 64 ASCII letters, digits or hyphens.
                return argument is { Length: >= 1 and <= 64 } && argument.All(character => char.IsAsciiLetterOrDigit(character) || character == '-');
            default:
                return argument is null;
        }
    }
}

/// <summary>What a request asks the plug-in to do.</summary>
public abstract record LightroomRequestKind
{
    private LightroomRequestKind() { }

    /// <summary>Diagnostic, no catalog change.</summary>
    public sealed record Ping() : LightroomRequestKind;

    /// <summary>Change a parameter by <paramref name="Delta"/> (slider units; see the catalog unit).</summary>
    public sealed record Adjust(ParameterScope Scope, string Parameter, double Delta) : LightroomRequestKind;

    /// <summary>Reset a develop parameter to its default.</summary>
    public sealed record Reset(ParameterScope Scope, string Parameter) : LightroomRequestKind;

    /// <summary>Begin or end a Dial Key gesture; end may omit the parameter.</summary>
    public sealed record Gesture(GesturePhase Phase, string? Parameter) : LightroomRequestKind;

    /// <summary>An allow-listed Lightroom command with an optional argument.</summary>
    public sealed record Command(LightroomCommandName Name, string? Argument = null) : LightroomRequestKind;

    /// <summary>A question answered with a result.</summary>
    public sealed record Query(LightroomQuery Name) : LightroomRequestKind;
}

/// <summary>
/// One request from the app to the plug-in (protocol 4). A request is checked
/// against the catalog when it is made; the client assigns the wire id when it
/// sends the request.
/// </summary>
public sealed record LightroomRequest
{
    public const int ProtocolVersion = Contract.ProtocolVersion;

    /// <exception cref="ValidationException">The request names an unknown parameter, a change that is too large, or a bad argument.</exception>
    public LightroomRequest(LightroomRequestKind kind)
    {
        ArgumentNullException.ThrowIfNull(kind);
        Kind = kind;
        Validate();
    }

    public LightroomRequestKind Kind { get; }

    public static LightroomRequest Ping { get; } = new(new LightroomRequestKind.Ping());

    public static LightroomRequest Adjust(ParameterScope scope, string parameter, double delta) =>
        new(new LightroomRequestKind.Adjust(scope, parameter, delta));

    public static LightroomRequest Reset(ParameterScope scope, string parameter) =>
        new(new LightroomRequestKind.Reset(scope, parameter));

    public static LightroomRequest Gesture(GesturePhase phase, string? parameter) =>
        new(new LightroomRequestKind.Gesture(phase, parameter));

    public static LightroomRequest Command(LightroomCommandName name, string? argument = null) =>
        new(new LightroomRequestKind.Command(name, argument));

    public static LightroomRequest Query(LightroomQuery query) => new(new LightroomRequestKind.Query(query));

    /// <summary>The parameter a request touches, if any.</summary>
    public ParameterKey? ParameterKey => Kind switch
    {
        LightroomRequestKind.Adjust adjust => new ParameterKey(adjust.Scope, adjust.Parameter),
        LightroomRequestKind.Reset reset => new ParameterKey(reset.Scope, reset.Parameter),
        LightroomRequestKind.Gesture { Parameter: { } parameter } => new ParameterKey(ParameterScope.Develop, parameter),
        _ => null,
    };

    /// <exception cref="ValidationException">See the constructor.</exception>
    public void Validate()
    {
        switch (Kind)
        {
            case LightroomRequestKind.Adjust(var scope, var parameter, var delta):
            {
                var key = new ParameterKey(scope, parameter);
                var descriptor = ParameterCatalog.Descriptor(key)
                    ?? throw ValidationException.UnknownParameter(key);
                if (!double.IsFinite(delta) || Math.Abs(delta) > descriptor.MaximumDelta)
                    throw ValidationException.InvalidDelta(descriptor.Key, descriptor.MaximumDelta);
                break;
            }
            case LightroomRequestKind.Reset(var scope, var parameter):
            {
                var key = new ParameterKey(scope, parameter);
                if (ParameterCatalog.Descriptor(key) is null) throw ValidationException.UnknownParameter(key);
                if (scope != ParameterScope.Develop) throw ValidationException.ResetNotAvailable(scope);
                break;
            }
            case LightroomRequestKind.Gesture(var phase, var parameter):
                if (parameter is not null)
                {
                    var key = new ParameterKey(ParameterScope.Develop, parameter);
                    if (ParameterCatalog.Descriptor(key) is null) throw ValidationException.UnknownParameter(key);
                }
                else if (phase == GesturePhase.Begin)
                {
                    throw ValidationException.MissingGestureParameter();
                }
                break;
            case LightroomRequestKind.Command(var name, var argument):
                if (!name.Accepts(argument)) throw ValidationException.InvalidArgument(name, argument);
                break;
        }
    }

    public enum ValidationError
    {
        UnknownParameter,
        InvalidDelta,
        ResetNotAvailable,
        MissingGestureParameter,
        InvalidArgument,
    }

    /// <summary>A request the plug-in would refuse. <see cref="Error"/> says why; the other properties carry the details.</summary>
    public sealed class ValidationException : Exception
    {
        private ValidationException(ValidationError error, string message) : base(message) => Error = error;

        public ValidationError Error { get; }

        /// <summary>The parameter (unknownParameter, invalidDelta).</summary>
        public ParameterKey? Key { get; private init; }

        /// <summary>The largest change allowed (invalidDelta).</summary>
        public double? Limit { get; private init; }

        /// <summary>The scope (resetNotAvailable).</summary>
        public ParameterScope? Scope { get; private init; }

        /// <summary>The command and its argument (invalidArgument).</summary>
        public LightroomCommandName? CommandName { get; private init; }

        public string? Argument { get; private init; }

        internal static ValidationException UnknownParameter(ParameterKey key) =>
            new(ValidationError.UnknownParameter, $"Unknown parameter {key}.") { Key = key };

        internal static ValidationException InvalidDelta(ParameterKey key, double limit)
        {
            var text = limit.ToString(CultureInfo.InvariantCulture);
            return new(ValidationError.InvalidDelta, $"{key.Id} needs a finite change between -{text} and {text}.") { Key = key, Limit = limit };
        }

        internal static ValidationException ResetNotAvailable(ParameterScope scope) =>
            new(ValidationError.ResetNotAvailable, $"Reset is not available in {scope.WireName()}.") { Scope = scope };

        internal static ValidationException MissingGestureParameter() =>
            new(ValidationError.MissingGestureParameter, "A gesture begin needs a parameter.");

        internal static ValidationException InvalidArgument(LightroomCommandName name, string? argument) =>
            new(ValidationError.InvalidArgument, $"Invalid argument for {name.WireName()}: {argument ?? "none"}.")
            {
                CommandName = name,
                Argument = argument,
            };
    }
}

/// <summary>The JSON-lines wire form of a request (sorted keys, newline-terminated).</summary>
internal static class RequestCodec
{
    /// <summary>
    /// One UTF-8 JSON object with sorted keys, absent fields left out, ending
    /// with "\n". <paramref name="token"/> is the bridge token the plug-in
    /// asks for (protocol 4).
    /// </summary>
    public static byte[] EncodeLine(LightroomRequest request, long id, string token)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(token);
        var line = new JsonLine()
            .Add("version", LightroomRequest.ProtocolVersion)
            .Add("id", id)
            .Add("token", token);
        switch (request.Kind)
        {
            case LightroomRequestKind.Adjust(var scope, var parameter, var delta):
                line.Add("kind", "adjust").Add("scope", scope.WireName()).Add("parameter", parameter).Add("delta", delta);
                break;
            case LightroomRequestKind.Reset(var scope, var parameter):
                line.Add("kind", "reset").Add("scope", scope.WireName()).Add("parameter", parameter);
                break;
            case LightroomRequestKind.Gesture(var phase, var parameter):
                line.Add("kind", "gesture").Add("phase", phase.WireName()).Add("parameter", parameter);
                break;
            case LightroomRequestKind.Command(var name, var argument):
                line.Add("kind", "command").Add("name", name.WireName()).Add("argument", argument);
                break;
            case LightroomRequestKind.Query(var query):
                line.Add("kind", "query").Add("name", query.WireName());
                break;
            default:
                line.Add("kind", "ping");
                break;
        }
        return line.ToBytes();
    }

    /// <summary>Decodes a request line (used by tests and diagnostics).</summary>
    /// <exception cref="DecodingException">Another protocol version, or not a request.</exception>
    /// <exception cref="LightroomRequest.ValidationException">A request the plug-in would refuse.</exception>
    public static (long Id, LightroomRequest Request, string? Token) DecodeLine(ReadOnlyMemory<byte> data)
    {
        var trimmed = data.Span.Length > 0 && data.Span[^1] == (byte)'\n' ? data[..^1] : data;
        Wire wire;
        try
        {
            wire = Wire.Read(trimmed);
        }
        catch (Exception error) when (WireJson.IsReadError(error))
        {
            throw new DecodingException(DecodingError.Malformed, null, error);
        }
        if (wire.Version != LightroomRequest.ProtocolVersion)
            throw new DecodingException(DecodingError.UnsupportedVersion, wire.Version);

        ParameterScope Scope() =>
            ParameterScopes.FromWireName(wire.Scope) ?? throw new DecodingException(DecodingError.Malformed);
        string Required(string? value) => value ?? throw new DecodingException(DecodingError.Malformed);

        LightroomRequestKind kind = wire.Kind switch
        {
            "ping" => new LightroomRequestKind.Ping(),
            "adjust" => new LightroomRequestKind.Adjust(Scope(), Required(wire.Parameter),
                wire.Delta ?? throw new DecodingException(DecodingError.Malformed)),
            "reset" => new LightroomRequestKind.Reset(Scope(), Required(wire.Parameter)),
            "gesture" => new LightroomRequestKind.Gesture(
                GesturePhases.FromWireName(wire.Phase) ?? throw new DecodingException(DecodingError.Malformed), wire.Parameter),
            "command" => new LightroomRequestKind.Command(
                LightroomCommandNames.FromWireName(wire.Name) ?? throw new DecodingException(DecodingError.Malformed), wire.Argument),
            "query" => new LightroomRequestKind.Query(
                LightroomQueries.FromWireName(wire.Name) ?? throw new DecodingException(DecodingError.Malformed)),
            _ => throw new DecodingException(DecodingError.Malformed),
        };
        return (wire.Id, new LightroomRequest(kind), wire.Token);
    }

    public enum DecodingError
    {
        UnsupportedVersion,
        Malformed,
    }

    public sealed class DecodingException : Exception
    {
        internal DecodingException(DecodingError error, long? version = null, Exception? inner = null)
            : base(error == DecodingError.UnsupportedVersion
                ? $"The request speaks protocol {version}; protocol {LightroomRequest.ProtocolVersion} is required."
                : "The request is malformed.", inner)
        {
            Error = error;
            Version = version;
        }

        public DecodingError Error { get; }

        /// <summary>The version the line named (unsupportedVersion).</summary>
        public long? Version { get; }
    }

    /// <summary>Every field a request line may carry; version, id and kind are required.</summary>
    private sealed record Wire(long Version, long Id, string? Token, string Kind, string? Scope, string? Parameter, double? Delta,
        string? Phase, string? Name, string? Argument)
    {
        public static Wire Read(ReadOnlyMemory<byte> data)
        {
            using var document = JsonDocument.Parse(data);
            var fields = WireJson.Fields(document.RootElement);
            return new Wire(
                WireJson.Integer(fields, "version") ?? throw new FormatException("version is missing"),
                WireJson.Integer(fields, "id") ?? throw new FormatException("id is missing"),
                WireJson.Text(fields, "token"),
                WireJson.Text(fields, "kind") ?? throw new FormatException("kind is missing"),
                WireJson.Text(fields, "scope"),
                WireJson.Text(fields, "parameter"),
                WireJson.Number(fields, "delta"),
                WireJson.Text(fields, "phase"),
                WireJson.Text(fields, "name"),
                WireJson.Text(fields, "argument"));
        }
    }
}

/// <summary>
/// Builds one wire line: keys sorted by ordinal order, null fields left out,
/// '/' and non-ASCII text written as is, one trailing "\n".
/// </summary>
internal sealed class JsonLine
{
    private static readonly JsonWriterOptions Options = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Indented = false,
    };

    private readonly SortedDictionary<string, Action<Utf8JsonWriter>> fields = new(StringComparer.Ordinal);

    public JsonLine Add(string key, string? value)
    {
        if (value is not null) fields[key] = writer => writer.WriteStringValue(value);
        return this;
    }

    public JsonLine Add(string key, long? value)
    {
        if (value is { } number) fields[key] = writer => writer.WriteNumberValue(number);
        return this;
    }

    /// <summary>Numbers are written in their shortest round-trip form (5, -20, 0.05).</summary>
    public JsonLine Add(string key, double? value)
    {
        if (value is { } number) fields[key] = writer => writer.WriteNumberValue(number);
        return this;
    }

    public JsonLine Add(string key, bool? value)
    {
        if (value is { } flag) fields[key] = writer => writer.WriteBooleanValue(flag);
        return this;
    }

    public JsonLine Add(string key, IReadOnlyList<string>? values)
    {
        if (values is not null)
        {
            fields[key] = writer =>
            {
                writer.WriteStartArray();
                foreach (var value in values) writer.WriteStringValue(value);
                writer.WriteEndArray();
            };
        }
        return this;
    }

    public JsonLine Add(string key, IEnumerable<JsonLine>? objects)
    {
        if (objects is not null)
        {
            var list = objects.ToList();
            fields[key] = writer =>
            {
                writer.WriteStartArray();
                foreach (var item in list) item.WriteObject(writer);
                writer.WriteEndArray();
            };
        }
        return this;
    }

    private void WriteObject(Utf8JsonWriter writer)
    {
        writer.WriteStartObject();
        foreach (var (key, write) in fields)
        {
            writer.WritePropertyName(key);
            write(writer);
        }
        writer.WriteEndObject();
    }

    public byte[] ToBytes()
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, Options)) WriteObject(writer);
        buffer.WriteByte((byte)'\n');
        return buffer.ToArray();
    }
}

/// <summary>Typed access to the fields of a wire line. Type mismatches throw <see cref="FormatException"/>.</summary>
internal static class WireJson
{
    /// <summary>Errors that mean "this line is not what the protocol describes".</summary>
    public static bool IsReadError(Exception error) =>
        error is JsonException or FormatException or InvalidOperationException or ArgumentException;

    /// <summary>The object's fields by name; for a repeated key the last value wins.</summary>
    public static Dictionary<string, JsonElement> Fields(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object) throw new FormatException("a line must be a JSON object");
        var fields = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject()) fields[property.Name] = property.Value;
        return fields;
    }

    private static JsonElement? Present(Dictionary<string, JsonElement> fields, string key) =>
        fields.TryGetValue(key, out var value) && value.ValueKind != JsonValueKind.Null ? value : null;

    public static string? Text(Dictionary<string, JsonElement> fields, string key) => Present(fields, key) is { } value
        ? value.ValueKind == JsonValueKind.String ? value.GetString() : throw new FormatException(key + " must be text")
        : null;

    public static bool? Flag(Dictionary<string, JsonElement> fields, string key) => Present(fields, key) is { } value
        ? value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => throw new FormatException(key + " must be true or false"),
        }
        : null;

    /// <summary>A whole number; 3.0 is accepted as 3.</summary>
    public static long? Integer(Dictionary<string, JsonElement> fields, string key)
    {
        if (Present(fields, key) is not { } value) return null;
        if (value.ValueKind != JsonValueKind.Number) throw new FormatException(key + " must be a number");
        if (value.TryGetInt64(out var integer)) return integer;
        if (value.TryGetDouble(out var number) && Math.Floor(number) == number && Math.Abs(number) < 9.2e18) return (long)number;
        throw new FormatException(key + " must be a whole number");
    }

    /// <summary>A finite number; one too large for a double throws <see cref="NonFiniteNumberException"/>.</summary>
    public static double? Number(Dictionary<string, JsonElement> fields, string key)
    {
        if (Present(fields, key) is not { } value) return null;
        if (value.ValueKind != JsonValueKind.Number) throw new FormatException(key + " must be a number");
        return value.TryGetDouble(out var number) && double.IsFinite(number) ? number : throw new NonFiniteNumberException(key);
    }

    public static List<string>? Texts(Dictionary<string, JsonElement> fields, string key)
    {
        if (Present(fields, key) is not { } value) return null;
        if (value.ValueKind != JsonValueKind.Array) throw new FormatException(key + " must be a list");
        return value.EnumerateArray()
            .Select(item => item.ValueKind == JsonValueKind.String ? item.GetString()! : throw new FormatException(key + " must list text"))
            .ToList();
    }

    public static List<Dictionary<string, JsonElement>>? Objects(Dictionary<string, JsonElement> fields, string key)
    {
        if (Present(fields, key) is not { } value) return null;
        if (value.ValueKind != JsonValueKind.Array) throw new FormatException(key + " must be a list");
        return value.EnumerateArray().Select(Fields).ToList();
    }

    public sealed class NonFiniteNumberException(string key) : FormatException(key + " is not a finite number");
}
