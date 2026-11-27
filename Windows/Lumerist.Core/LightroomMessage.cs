using System.Text;
using System.Text.Json;

namespace Lumerist.Core;

/// <summary>
/// The plug-in's answer to one request (protocol 4). For an adjustment it may
/// carry the parameter's resulting value, range and unit for the overlay.
/// </summary>
public sealed record LightroomAck
{
    public const int MaximumMessageBytes = 1_024;
    public const int MaximumUnitLength = 8;

    /// <exception cref="MessageCodec.ValidationException">messageTooLong, invalidNumber or unitTooLong.</exception>
    public LightroomAck(
        long? id,
        bool accepted,
        string? message = null,
        string? parameter = null,
        ParameterScope? scope = null,
        double? value = null,
        double? minimum = null,
        double? maximum = null,
        string? unit = null)
    {
        Id = id;
        Accepted = accepted;
        Message = message;
        Parameter = parameter;
        Scope = scope;
        Value = value;
        Minimum = minimum;
        Maximum = maximum;
        Unit = unit;
        Validate();
    }

    /// <summary>The request id (null when the plug-in could not read the request).</summary>
    public long? Id { get; }

    public bool Accepted { get; }

    public string? Message { get; }

    public string? Parameter { get; }

    public ParameterScope? Scope { get; }

    public double? Value { get; }

    public double? Minimum { get; }

    public double? Maximum { get; }

    /// <summary>"EV", "K", "deg", "px", "%", "pt" or "choice".</summary>
    public string? Unit { get; }

    /// <summary>The catalog entry the ack refers to, when it names one.</summary>
    public ParameterKey? ParameterKey =>
        Parameter is null ? null : new ParameterKey(Scope ?? ParameterScope.Develop, Parameter);

    /// <exception cref="MessageCodec.ValidationException">See the constructor.</exception>
    public void Validate()
    {
        if (Message is not null && Encoding.UTF8.GetByteCount(Message) > MaximumMessageBytes)
            throw new MessageCodec.ValidationException(MessageCodec.ValidationError.MessageTooLong);
        foreach (var number in (ReadOnlySpan<double?>)[Value, Minimum, Maximum])
        {
            if (number is { } finite && !double.IsFinite(finite))
                throw new MessageCodec.ValidationException(MessageCodec.ValidationError.InvalidNumber);
        }
        if (Unit is not null && Encoding.UTF8.GetByteCount(Unit) > MaximumUnitLength)
            throw new MessageCodec.ValidationException(MessageCodec.ValidationError.UnitTooLong);
    }
}

/// <summary>
/// Where the user is in Lightroom: the module and, in Develop, the tool
/// ("masking" while the Masking panel is open).
/// </summary>
public sealed record LightroomContext(string Module, string Tool)
{
    public bool IsMasking => Module == "develop" && Tool == "masking";
}

/// <summary>One develop preset as listed by the plug-in.</summary>
/// <param name="Id">The preset's UUID (the applyPreset argument).</param>
/// <param name="Group">The preset folder.</param>
public sealed record PresetItem(string Id, string Name, string Group);

/// <summary>The answer to a query.</summary>
public abstract record LightroomResult
{
    private LightroomResult() { }

    /// <summary>The id of the query this answers.</summary>
    public abstract long? Id { get; }

    public sealed record Context(long? Id, LightroomContext Value) : LightroomResult
    {
        public override long? Id { get; } = Id;
    }

    public sealed record Presets(long? Id, IReadOnlyList<PresetItem> Items, bool Truncated) : LightroomResult
    {
        public override long? Id { get; } = Id;

        public bool Equals(Presets? other) =>
            other is not null && Id == other.Id && Truncated == other.Truncated && Items.SequenceEqual(other.Items);

        public override int GetHashCode() => HashCode.Combine(Id, Truncated, Items.Count);
    }

    /// <summary>
    /// Develop parameters whose range Lightroom could not report, and the
    /// mask parameters that could not be checked (no mask selected).
    /// </summary>
    public sealed record Catalog(long? Id, IReadOnlyList<string> Unsupported, IReadOnlyList<string> Skipped) : LightroomResult
    {
        public override long? Id { get; } = Id;

        public bool Equals(Catalog? other) =>
            other is not null && Id == other.Id && Unsupported.SequenceEqual(other.Unsupported) && Skipped.SequenceEqual(other.Skipped);

        public override int GetHashCode() => HashCode.Combine(Id, Unsupported.Count, Skipped.Count);
    }
}

/// <summary>Everything the plug-in sends on the response channel.</summary>
public abstract record LightroomMessage
{
    private LightroomMessage() { }

    public sealed record Ack(LightroomAck Value) : LightroomMessage;

    /// <summary>Pushed after connecting and whenever the module or tool changes.</summary>
    public sealed record Context(LightroomContext Value) : LightroomMessage;

    public sealed record Result(LightroomResult Value) : LightroomMessage;
}

/// <summary>JSON-lines codec for plug-in messages (protocol 4).</summary>
public static class MessageCodec
{
    /// <summary>Lines may be large (preset lists).</summary>
    public const int MaximumLineBytes = 1_048_576;

    /// <summary>
    /// Decodes one line (a trailing "\n" is allowed). Returns null for kinds,
    /// events or results this build does not know (a newer plug-in may add them).
    /// Unknown fields are ignored.
    /// </summary>
    /// <exception cref="ValidationException">The line is not a protocol-3 message.</exception>
    public static LightroomMessage? DecodeLine(ReadOnlyMemory<byte> data)
    {
        var trimmed = data.Span.Length > 0 && data.Span[^1] == (byte)'\n' ? data[..^1] : data;
        if (trimmed.Length > MaximumLineBytes) throw new ValidationException(ValidationError.LineTooLong);

        Wire wire;
        try
        {
            wire = Wire.Read(trimmed);
        }
        catch (WireJson.NonFiniteNumberException error)
        {
            throw new ValidationException(ValidationError.InvalidNumber, inner: error);
        }
        catch (Exception error) when (WireJson.IsReadError(error))
        {
            throw new ValidationException(ValidationError.Malformed, inner: error);
        }
        if (wire.Version != LightroomRequest.ProtocolVersion)
            throw new ValidationException(ValidationError.UnsupportedVersion, wire.Version);
        // Optional in the wire form so that a line from an older plug-in
        // (no kind) still reaches the version check and gets a useful error.
        if (wire.Kind is not { } kind) throw new ValidationException(ValidationError.Malformed);

        switch (kind)
        {
            case "ack":
                if (wire.Accepted is not { } accepted) throw new ValidationException(ValidationError.Malformed);
                return new LightroomMessage.Ack(new LightroomAck(
                    wire.Id, accepted, wire.Message, wire.Parameter, ParameterScopes.FromWireName(wire.Scope),
                    wire.Value, wire.Minimum, wire.Maximum, wire.Unit));
            case "event":
                return wire.Name == "context" ? new LightroomMessage.Context(new LightroomContext(wire.Module ?? "", wire.Tool ?? "")) : null;
            case "result":
                LightroomResult? result = wire.Name switch
                {
                    "context" => new LightroomResult.Context(wire.Id, new LightroomContext(wire.Module ?? "", wire.Tool ?? "")),
                    "presets" => new LightroomResult.Presets(wire.Id, wire.Items ?? [], wire.Truncated ?? false),
                    "catalog" => new LightroomResult.Catalog(wire.Id, wire.Unsupported ?? [], wire.Skipped ?? []),
                    _ => null,
                };
                return result is null ? null : new LightroomMessage.Result(result);
            default:
                return null;
        }
    }

    /// <summary>Encodes a message as the plug-in would (used by tests and loopback tools).</summary>
    public static byte[] EncodeLine(LightroomMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        var line = new JsonLine().Add("version", LightroomRequest.ProtocolVersion);
        switch (message)
        {
            case LightroomMessage.Ack(var ack):
                ack.Validate();
                line.Add("kind", "ack")
                    .Add("id", ack.Id)
                    .Add("accepted", ack.Accepted)
                    .Add("message", ack.Message)
                    .Add("parameter", ack.Parameter)
                    .Add("scope", ack.Scope?.WireName())
                    .Add("value", ack.Value)
                    .Add("minimum", ack.Minimum)
                    .Add("maximum", ack.Maximum)
                    .Add("unit", ack.Unit);
                break;
            case LightroomMessage.Context(var context):
                line.Add("kind", "event").Add("name", "context").Add("module", context.Module).Add("tool", context.Tool);
                break;
            case LightroomMessage.Result(var result):
                line.Add("kind", "result").Add("id", result.Id);
                switch (result)
                {
                    case LightroomResult.Context(_, var context):
                        line.Add("name", "context").Add("module", context.Module).Add("tool", context.Tool);
                        break;
                    case LightroomResult.Presets(_, var items, var truncated):
                        line.Add("name", "presets")
                            .Add("items", items.Select(item => new JsonLine().Add("id", item.Id).Add("name", item.Name).Add("group", item.Group)))
                            .Add("truncated", truncated ? true : null);
                        break;
                    case LightroomResult.Catalog(_, var unsupported, var skipped):
                        line.Add("name", "catalog").Add("unsupported", unsupported).Add("skipped", skipped);
                        break;
                }
                break;
        }
        return line.ToBytes();
    }

    public enum ValidationError
    {
        UnsupportedVersion,
        Malformed,
        LineTooLong,
        MessageTooLong,
        InvalidNumber,
        UnitTooLong,
    }

    /// <summary>A line or ack that breaks protocol 3. <see cref="Error"/> says how.</summary>
    public sealed class ValidationException : Exception
    {
        internal ValidationException(ValidationError error, long? version = null, Exception? inner = null)
            : base(Describe(error, version), inner)
        {
            Error = error;
            Version = version;
        }

        public ValidationError Error { get; }

        /// <summary>The version the plug-in speaks (unsupportedVersion).</summary>
        public long? Version { get; }

        private static string Describe(ValidationError error, long? version) => error switch
        {
            ValidationError.UnsupportedVersion =>
                $"The plug-in speaks protocol {version}; this app needs protocol {LightroomRequest.ProtocolVersion}. Update the plug-in or the app.",
            ValidationError.Malformed => "The plug-in sent a malformed message.",
            ValidationError.LineTooLong => "The plug-in sent a message longer than 1 MB.",
            ValidationError.MessageTooLong => $"A bridge message must not exceed {LightroomAck.MaximumMessageBytes} UTF-8 bytes.",
            ValidationError.InvalidNumber => "A bridge value must be a finite number.",
            _ => $"A bridge unit must not exceed {LightroomAck.MaximumUnitLength} UTF-8 bytes.",
        };
    }

    /// <summary>Every field a message line may carry; only version is required.</summary>
    private sealed record Wire(
        long Version, string? Kind, long? Id, bool? Accepted, string? Message, string? Parameter, string? Scope,
        double? Value, double? Minimum, double? Maximum, string? Unit, string? Name, string? Module, string? Tool,
        List<PresetItem>? Items, bool? Truncated, List<string>? Unsupported, List<string>? Skipped)
    {
        public static Wire Read(ReadOnlyMemory<byte> data)
        {
            using var document = JsonDocument.Parse(data);
            var fields = WireJson.Fields(document.RootElement);
            return new Wire(
                WireJson.Integer(fields, "version") ?? throw new FormatException("version is missing"),
                WireJson.Text(fields, "kind"),
                WireJson.Integer(fields, "id"),
                WireJson.Flag(fields, "accepted"),
                WireJson.Text(fields, "message"),
                WireJson.Text(fields, "parameter"),
                WireJson.Text(fields, "scope"),
                WireJson.Number(fields, "value"),
                WireJson.Number(fields, "minimum"),
                WireJson.Number(fields, "maximum"),
                WireJson.Text(fields, "unit"),
                WireJson.Text(fields, "name"),
                WireJson.Text(fields, "module"),
                WireJson.Text(fields, "tool"),
                WireJson.Objects(fields, "items")?.Select(item => new PresetItem(
                    WireJson.Text(item, "id") ?? throw new FormatException("a preset needs an id"),
                    WireJson.Text(item, "name") ?? throw new FormatException("a preset needs a name"),
                    WireJson.Text(item, "group") ?? throw new FormatException("a preset needs a group"))).ToList(),
                WireJson.Flag(fields, "truncated"),
                WireJson.Texts(fields, "unsupported"),
                WireJson.Texts(fields, "skipped"));
        }
    }
}
