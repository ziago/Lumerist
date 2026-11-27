using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Lumerist.Core;

/// <summary>
/// The Windows keymap file (the app keeps it in %APPDATA%\Lumerist\keymap.json).
/// Windows has its own format: files from the Mac app never load here.
/// <code>
/// {
///   "format": 1,
///   "develop": {
///     "Exposure": { "dial": { "scanCode": "0x10" }, "nudgeUp": { "scanCode": "0x1B", "modifiers": ["Combo"] },
///                   "normalStep": 0.1, "coarseStep": 0.5 }
///   },
///   "library": { "Contrast": { "dial": { "scanCode": "0x11" } } },
///   "commandKeys": [
///     { "id": "8022e100-5e15-408a-a02c-9796e4b60cba", "scope": "develop",
///       "chord": { "scanCode": "0x1E", "modifiers": ["Combo"] }, "group": "Lightroom", "title": "Auto Tone",
///       "action": { "command": "autoTone" } }
///   ],
///   "systemKeys": { "comboKey": { "scanCode": "0x0F" }, "paletteKey": { "scanCode": "0x29" },
///                   "keyReferenceToggle": { "scanCode": "0x23", "modifiers": ["Combo"] }, "pauseToggle": null },
///   "importKeys": { "include": { "scanCode": "0x2C" }, "exclude": { "scanCode": "0x2D" }, "toggle": null, "advance": true },
///   "palette": { "keepVisible": false, "favourites": [ { "kind": "tool", "id": "createMask", "argument": "aiSelection/sky", "title": "Sky" } ] },
///   "input": { "invertWheel": false, "swapNormalAndCoarse": false, "trackpadPointsPerStep": 12, … }
/// }
/// </code>
/// <list type="bullet">
/// <item><c>format</c> is required. Every other section is optional and takes its defaults when missing.</item>
/// <item>Slots are keyed by parameter id; each field is optional. Empty slots are dropped.</item>
/// <item>A chord is a Set 1 scan code as hex text ("0x10" is Q, "0xE04D" is →, extended keys carry 0xE0)
/// plus its modifiers, in the order Ctrl, Alt, AltGr, Shift, Combo; no modifiers leaves the list out.
/// A chord with "Combo" is a combo ("Tab + K").</item>
/// <item>An action is one of <c>{"command": name, "argument"?: text}</c>, <c>{"preset": uuid, "name": text}</c>,
/// <c>{"shortcut": chord}</c> or <c>{"macro": [{"action": action, "delay": seconds}]}</c>.</item>
/// <item>System Keys: null means cleared (that key is off); a missing key takes its default.</item>
/// <item>Import keys: written only once they differ from the default (no key, "Move to the next photo"
/// off), so an untouched keymap keeps the exact shape older builds read, since their decoding is
/// strict. A missing section or field takes the default; null is a key not assigned.</item>
/// </list>
/// Decoding is strict on purpose: an unknown format, an unknown field at any
/// level, or content of the wrong shape throws instead of silently loading
/// empty key slots (the app then starts from the standard layout).
/// </summary>
public static class KeymapStore
{
    /// <summary>The Windows file format, independent of the Mac's.</summary>
    public const int CurrentFormat = 1;

    private static readonly string[] TopFields =
        ["format", "develop", "library", "commandKeys", "systemKeys", "importKeys", "palette", "input"];
    private static readonly string[] SlotFields = ["dial", "nudgeDown", "nudgeUp", "normalStep", "coarseStep"];
    private static readonly string[] ChordFields = ["scanCode", "modifiers"];
    private static readonly string[] CommandKeyFields = ["id", "scope", "chord", "group", "title", "action"];
    private static readonly string[] ActionFields = ["command", "argument", "preset", "name", "shortcut", "macro"];
    private static readonly string[] MacroStepFields = ["action", "delay"];
    private static readonly string[] ImportKeyFields = ["include", "exclude", "toggle", "advance"];
    private static readonly string[] PaletteFields = ["keepVisible", "favourites"];
    private static readonly string[] PaletteEntryFields = ["kind", "id", "argument", "title"];
    private static readonly string[] InputFields =
    [
        "invertWheel", "swapNormalAndCoarse", "trackpadPointsPerStep", "requireMouseDown", "dragSensitivity",
        "returnCursorAfterRelease", "doubleClickReset", "middleClickReset", "mouseBackForward",
        "followSelectedMask", "revealPanels", "arrowKeysAdjust",
    ];

    /// <summary>Modifier names in the order they are written.</summary>
    private static readonly (string Name, KeyModifiers Modifier)[] ModifierNames =
    [
        ("Ctrl", KeyModifiers.Control), ("Alt", KeyModifiers.Alt), ("AltGr", KeyModifiers.AltGr), ("Shift", KeyModifiers.Shift),
        ("Combo", KeyModifiers.Combo),
    ];

    // Encoding ------------------------------------------------------------------------

    /// <summary>The keymap as indented JSON text. Does not validate; <see cref="Save"/> does.</summary>
    public static string Encode(Keymap keymap)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions
        {
            Indented = true,
            // Titles stay readable ("Négatif"); the file is never embedded in HTML.
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("format", CurrentFormat);
            WriteSlots(writer, "develop", keymap.Develop);
            WriteSlots(writer, "library", keymap.Library);

            writer.WriteStartArray("commandKeys");
            foreach (var commandKey in keymap.CommandKeys)
            {
                writer.WriteStartObject();
                writer.WriteString("id", commandKey.Id.ToString("D"));
                writer.WriteString("scope", commandKey.Scope.WireName());
                writer.WritePropertyName("chord");
                WriteChord(writer, commandKey.Chord);
                writer.WriteString("group", commandKey.Group);
                writer.WriteString("title", commandKey.Title);
                writer.WritePropertyName("action");
                WriteAction(writer, commandKey.Action);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();

            writer.WriteStartObject("systemKeys");
            foreach (var role in SystemKeyRoles.All)
            {
                writer.WritePropertyName(role.WireName());
                if (keymap.SystemKeys.ChordFor(role) is { } chord) WriteChord(writer, chord);
                else writer.WriteNullValue();
            }
            writer.WriteEndObject();

            if (!keymap.ImportKeys.IsDefault)
            {
                writer.WriteStartObject("importKeys");
                foreach (var role in ImportKeyRoles.All)
                {
                    writer.WritePropertyName(role.WireName());
                    if (keymap.ImportKeys.ChordFor(role) is { } chord) WriteChord(writer, chord);
                    else writer.WriteNullValue();
                }
                writer.WriteBoolean("advance", keymap.ImportKeys.Advance);
                writer.WriteEndObject();
            }

            writer.WriteStartObject("palette");
            writer.WriteBoolean("keepVisible", keymap.Palette.KeepVisible);
            writer.WriteStartArray("favourites");
            foreach (var entry in keymap.Palette.Favourites)
            {
                writer.WriteStartObject();
                writer.WriteString("kind", entry.Kind == PaletteEntryKind.Preset ? "preset" : "tool");
                writer.WriteString("id", entry.Id);
                if (entry.Argument is not null) writer.WriteString("argument", entry.Argument);
                writer.WriteString("title", entry.Title);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();

            var input = keymap.Input;
            writer.WriteStartObject("input");
            writer.WriteBoolean("invertWheel", input.InvertWheel);
            writer.WriteBoolean("swapNormalAndCoarse", input.SwapNormalAndCoarse);
            writer.WriteNumber("trackpadPointsPerStep", input.TrackpadPointsPerStep);
            writer.WriteBoolean("requireMouseDown", input.RequireMouseDown);
            writer.WriteNumber("dragSensitivity", input.DragSensitivity);
            writer.WriteBoolean("returnCursorAfterRelease", input.ReturnCursorAfterRelease);
            writer.WriteBoolean("doubleClickReset", input.DoubleClickReset);
            writer.WriteBoolean("middleClickReset", input.MiddleClickReset);
            writer.WriteBoolean("mouseBackForward", input.MouseBackForward);
            writer.WriteBoolean("followSelectedMask", input.FollowSelectedMask);
            writer.WriteBoolean("revealPanels", input.RevealPanels);
            writer.WriteBoolean("arrowKeysAdjust", input.ArrowKeysAdjust);
            writer.WriteEndObject();

            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(buffer.WrittenSpan) + "\n";
    }

    private static void WriteSlots(Utf8JsonWriter writer, string name, Dictionary<string, ParameterSlot> slots)
    {
        writer.WriteStartObject(name);
        foreach (var (id, slot) in slots.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            writer.WriteStartObject(id);
            void Chord(string field, KeyChord? chord)
            {
                if (chord is not { } value) return;
                writer.WritePropertyName(field);
                WriteChord(writer, value);
            }
            Chord("dial", slot.Dial);
            Chord("nudgeDown", slot.NudgeDown);
            Chord("nudgeUp", slot.NudgeUp);
            if (slot.NormalStep is { } normal) writer.WriteNumber("normalStep", normal);
            if (slot.CoarseStep is { } coarse) writer.WriteNumber("coarseStep", coarse);
            writer.WriteEndObject();
        }
        writer.WriteEndObject();
    }

    private static void WriteChord(Utf8JsonWriter writer, KeyChord chord)
    {
        writer.WriteStartObject();
        writer.WriteString("scanCode", "0x" + chord.ScanCode.ToString("X2", CultureInfo.InvariantCulture));
        var names = ModifierNames.Where(pair => (chord.Modifiers & pair.Modifier) != 0).ToList();
        if (names.Count > 0)
        {
            writer.WriteStartArray("modifiers");
            foreach (var (name, _) in names) writer.WriteStringValue(name);
            writer.WriteEndArray();
        }
        writer.WriteEndObject();
    }

    private static void WriteAction(Utf8JsonWriter writer, CommandAction action)
    {
        writer.WriteStartObject();
        switch (action)
        {
            case CommandAction.Command command:
                writer.WriteString("command", command.Name);
                if (command.Argument is not null) writer.WriteString("argument", command.Argument);
                break;
            case CommandAction.Preset preset:
                writer.WriteString("preset", preset.Uuid);
                writer.WriteString("name", preset.Name);
                break;
            case CommandAction.Shortcut shortcut:
                writer.WritePropertyName("shortcut");
                WriteChord(writer, shortcut.Chord);
                break;
            case CommandAction.Macro macro:
                writer.WriteStartArray("macro");
                foreach (var step in macro.Steps)
                {
                    writer.WriteStartObject();
                    writer.WritePropertyName("action");
                    WriteAction(writer, step.Action);
                    writer.WriteNumber("delay", step.Delay);
                    writer.WriteEndObject();
                }
                writer.WriteEndArray();
                break;
        }
        writer.WriteEndObject();
    }

    // Decoding ------------------------------------------------------------------------

    /// <summary>
    /// Decodes a stored keymap and validates it. Throws <see cref="KeymapException"/>
    /// for an unsupported format (0 when the text has none or is not JSON), unknown
    /// fields or a keymap that does not validate, and <see cref="JsonException"/>
    /// for content of the wrong shape.
    /// </summary>
    public static Keymap Decode(string json)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            throw KeymapException.UnsupportedFormat(0);
        }
        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("format", out var formatElement)
                || formatElement.ValueKind != JsonValueKind.Number
                || !formatElement.TryGetInt32(out var format))
            {
                throw KeymapException.UnsupportedFormat(0);
            }
            if (format != CurrentFormat) throw KeymapException.UnsupportedFormat(format);

            var unknown = new List<string>();
            CheckKnownFields(root, unknown);
            if (unknown.Count > 0) throw KeymapException.UnknownFields(unknown);

            var keymap = ReadKeymap(root);
            keymap.Validate();
            return keymap;
        }
    }

    // Pass 1: names this build does not know, wherever the shape lets us look.

    private static void Unknown(JsonElement element, string[] known, List<string> unknown)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (!known.Contains(property.Name)) unknown.Add(property.Name);
        }
    }

    private static IEnumerable<JsonElement> Objects(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Object => element.EnumerateObject().Select(property => property.Value).Where(IsObject),
        JsonValueKind.Array => element.EnumerateArray().Where(IsObject),
        _ => [],
    };

    private static bool IsObject(JsonElement element) => element.ValueKind == JsonValueKind.Object;

    private static JsonElement? Child(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var child) ? child : null;

    private static void CheckKnownFields(JsonElement root, List<string> unknown)
    {
        Unknown(root, TopFields, unknown);
        foreach (var section in new[] { "develop", "library" })
        {
            if (Child(root, section) is not { } slots || !IsObject(slots)) continue;
            foreach (var slot in Objects(slots))
            {
                Unknown(slot, SlotFields, unknown);
                foreach (var field in new[] { "dial", "nudgeDown", "nudgeUp" })
                    CheckChord(Child(slot, field), unknown);
            }
        }
        if (Child(root, "commandKeys") is { ValueKind: JsonValueKind.Array } commandKeys)
        {
            foreach (var commandKey in Objects(commandKeys))
            {
                Unknown(commandKey, CommandKeyFields, unknown);
                CheckChord(Child(commandKey, "chord"), unknown);
                CheckAction(Child(commandKey, "action"), unknown);
            }
        }
        if (Child(root, "systemKeys") is { ValueKind: JsonValueKind.Object } systemKeys)
        {
            Unknown(systemKeys, SystemKeyRoles.All.Select(role => role.WireName()).ToArray(), unknown);
            foreach (var chord in Objects(systemKeys)) CheckChord(chord, unknown);
        }
        if (Child(root, "importKeys") is { ValueKind: JsonValueKind.Object } importKeys)
        {
            Unknown(importKeys, ImportKeyFields, unknown);
            foreach (var chord in Objects(importKeys)) CheckChord(chord, unknown);
        }
        if (Child(root, "palette") is { ValueKind: JsonValueKind.Object } palette)
        {
            Unknown(palette, PaletteFields, unknown);
            if (Child(palette, "favourites") is { ValueKind: JsonValueKind.Array } favourites)
            {
                foreach (var entry in Objects(favourites)) Unknown(entry, PaletteEntryFields, unknown);
            }
        }
        if (Child(root, "input") is { ValueKind: JsonValueKind.Object } input) Unknown(input, InputFields, unknown);
    }

    private static void CheckChord(JsonElement? chord, List<string> unknown)
    {
        if (chord is { ValueKind: JsonValueKind.Object } value) Unknown(value, ChordFields, unknown);
    }

    private static void CheckAction(JsonElement? action, List<string> unknown)
    {
        if (action is not { ValueKind: JsonValueKind.Object } value) return;
        Unknown(value, ActionFields, unknown);
        CheckChord(Child(value, "shortcut"), unknown);
        if (Child(value, "macro") is { ValueKind: JsonValueKind.Array } steps)
        {
            foreach (var step in Objects(steps))
            {
                Unknown(step, MacroStepFields, unknown);
                CheckAction(Child(step, "action"), unknown);
            }
        }
    }

    // Pass 2: strict reading; every field has the right shape.

    private static JsonException Invalid(string path, string problem) => new($"Invalid keymap: {path} {problem}.");

    /// <summary>The properties of an object, refusing duplicate names.</summary>
    private static Dictionary<string, JsonElement> Fields(JsonElement element, string path)
    {
        if (element.ValueKind != JsonValueKind.Object) throw Invalid(path, "must be an object");
        var fields = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (!fields.TryAdd(property.Name, property.Value)) throw Invalid($"{path}.{property.Name}", "appears twice");
        }
        return fields;
    }

    private static bool IsAbsent(Dictionary<string, JsonElement> fields, string name) =>
        !fields.TryGetValue(name, out var value) || value.ValueKind == JsonValueKind.Null;

    private static JsonElement Required(Dictionary<string, JsonElement> fields, string name, string path) =>
        fields.TryGetValue(name, out var value) && value.ValueKind != JsonValueKind.Null
            ? value
            : throw Invalid($"{path}.{name}", "is required");

    private static string Text(JsonElement element, string path) =>
        element.ValueKind == JsonValueKind.String ? element.GetString()! : throw Invalid(path, "must be text");

    private static double Number(JsonElement element, string path) =>
        element.ValueKind == JsonValueKind.Number ? element.GetDouble() : throw Invalid(path, "must be a number");

    private static bool Flag(JsonElement element, string path) => element.ValueKind switch
    {
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => throw Invalid(path, "must be true or false"),
    };

    private static Keymap ReadKeymap(JsonElement root)
    {
        var fields = Fields(root, "keymap");
        var keymap = new Keymap
        {
            Develop = ReadSlots(fields, "develop"),
            Library = ReadSlots(fields, "library"),
        };
        if (!IsAbsent(fields, "commandKeys"))
        {
            var list = fields["commandKeys"];
            if (list.ValueKind != JsonValueKind.Array) throw Invalid("commandKeys", "must be a list");
            // The old standard Tab + Q (a Ctrl+Alt+V shortcut by position) becomes the app command.
            keymap.CommandKeys = list.EnumerateArray()
                .Select((item, index) => AppCommands.Migrate(ReadCommandKey(item, $"commandKeys[{index}]"))).ToList();
        }
        if (!IsAbsent(fields, "systemKeys")) keymap.SystemKeys = ReadSystemKeys(fields["systemKeys"]);
        // Keymaps saved before Import keys existed get the default: none assigned.
        if (!IsAbsent(fields, "importKeys")) keymap.ImportKeys = ReadImportKeys(fields["importKeys"]);
        if (!IsAbsent(fields, "palette")) keymap.Palette = ReadPalette(fields["palette"]);
        if (!IsAbsent(fields, "input")) keymap.Input = ReadInput(fields["input"]);
        return keymap;
    }

    private static Dictionary<string, ParameterSlot> ReadSlots(Dictionary<string, JsonElement> root, string section)
    {
        var result = new Dictionary<string, ParameterSlot>();
        if (IsAbsent(root, section)) return result;
        foreach (var (id, element) in Fields(root[section], section))
        {
            var path = $"{section}.{id}";
            var fields = Fields(element, path);
            KeyChord? Chord(string name) => IsAbsent(fields, name) ? null : ReadChord(fields[name], $"{path}.{name}");
            double? Step(string name) => IsAbsent(fields, name) ? null : Number(fields[name], $"{path}.{name}");
            var slot = new ParameterSlot(Chord("dial"), Chord("nudgeDown"), Chord("nudgeUp"), Step("normalStep"), Step("coarseStep"));
            if (!slot.IsEmpty) result[id] = slot;
        }
        return result;
    }

    private static KeyChord ReadChord(JsonElement element, string path)
    {
        var fields = Fields(element, path);
        var text = Text(Required(fields, "scanCode", path), $"{path}.scanCode");
        if (!text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            || text.Length is < 3 or > 6
            || !int.TryParse(text.AsSpan(2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var scanCode)
            || !IsScanCode(scanCode))
        {
            throw Invalid($"{path}.scanCode", "must be a scan code such as \"0x10\" or \"0xE04D\"");
        }
        var modifiers = KeyModifiers.None;
        if (!IsAbsent(fields, "modifiers"))
        {
            var list = fields["modifiers"];
            if (list.ValueKind != JsonValueKind.Array) throw Invalid($"{path}.modifiers", "must be a list");
            foreach (var item in list.EnumerateArray())
            {
                var name = Text(item, $"{path}.modifiers");
                var match = ModifierNames.FirstOrDefault(pair => pair.Name == name);
                if (match.Name is null) throw Invalid($"{path}.modifiers", $"has an unknown modifier \"{name}\"");
                if ((modifiers & match.Modifier) != 0) throw Invalid($"{path}.modifiers", $"lists \"{name}\" twice");
                modifiers |= match.Modifier;
            }
        }
        return new KeyChord(scanCode, modifiers);
    }

    /// <summary>A Set 1 make code (0x01–0x7F), alone or after the 0xE0 / 0xE1 prefix.</summary>
    private static bool IsScanCode(int code) => (code & 0xFF) is >= 0x01 and <= 0x7F && (code >> 8) is 0 or 0xE0 or 0xE1;

    private static CommandKey ReadCommandKey(JsonElement element, string path)
    {
        var fields = Fields(element, path);
        var idText = Text(Required(fields, "id", path), $"{path}.id");
        if (!Guid.TryParse(idText, out var id)) throw Invalid($"{path}.id", "must be a UUID");
        var scopeText = Text(Required(fields, "scope", path), $"{path}.scope");
        var scope = CommandScopes.FromWireName(scopeText) ?? throw Invalid($"{path}.scope", "must be develop, library or any");
        return new CommandKey(
            id,
            scope,
            ReadChord(Required(fields, "chord", path), $"{path}.chord"),
            Text(Required(fields, "group", path), $"{path}.group"),
            Text(Required(fields, "title", path), $"{path}.title"),
            ReadAction(Required(fields, "action", path), $"{path}.action"));
    }

    private static CommandAction ReadAction(JsonElement element, string path)
    {
        var fields = Fields(element, path);
        var kinds = new[] { "command", "preset", "shortcut", "macro" }.Where(fields.ContainsKey).ToList();
        if (kinds.Count != 1) throw Invalid(path, "must have exactly one of command, preset, shortcut or macro");
        var kind = kinds[0];
        string[] allowed = kind switch
        {
            "command" => ["command", "argument"],
            "preset" => ["preset", "name"],
            _ => [kind],
        };
        if (fields.Keys.FirstOrDefault(name => !allowed.Contains(name)) is { } extra)
            throw Invalid($"{path}.{extra}", $"does not belong to a {kind} action");

        switch (kind)
        {
            case "command":
                return new CommandAction.Command(
                    Text(Required(fields, "command", path), $"{path}.command"),
                    IsAbsent(fields, "argument") ? null : Text(fields["argument"], $"{path}.argument"));
            case "preset":
                return new CommandAction.Preset(
                    Text(Required(fields, "preset", path), $"{path}.preset"),
                    Text(Required(fields, "name", path), $"{path}.name"));
            case "shortcut":
                return new CommandAction.Shortcut(ReadChord(Required(fields, "shortcut", path), $"{path}.shortcut"));
            default:
                var list = Required(fields, "macro", path);
                if (list.ValueKind != JsonValueKind.Array) throw Invalid($"{path}.macro", "must be a list");
                var steps = new List<MacroStep>();
                foreach (var (item, index) in list.EnumerateArray().Select((item, index) => (item, index)))
                {
                    var stepPath = $"{path}.macro[{index}]";
                    var step = Fields(item, stepPath);
                    steps.Add(new MacroStep(
                        ReadAction(Required(step, "action", stepPath), $"{stepPath}.action"),
                        Number(Required(step, "delay", stepPath), $"{stepPath}.delay")));
                }
                return new CommandAction.Macro(steps);
        }
    }

    private static SystemKeys ReadSystemKeys(JsonElement element)
    {
        var fields = Fields(element, "systemKeys");
        var keys = new SystemKeys();
        KeyChord? Read(SystemKeyRole role, KeyChord? fallback)
        {
            var name = role.WireName();
            if (!fields.TryGetValue(name, out var value)) return fallback;
            return value.ValueKind == JsonValueKind.Null ? null : ReadChord(value, $"systemKeys.{name}");
        }
        keys.ComboKey = Read(SystemKeyRole.ComboKey, keys.ComboKey);
        keys.PaletteKey = Read(SystemKeyRole.PaletteKey, keys.PaletteKey);
        keys.KeyReferenceToggle = Read(SystemKeyRole.KeyReferenceToggle, keys.KeyReferenceToggle);
        keys.PauseToggle = Read(SystemKeyRole.PauseToggle, keys.PauseToggle);
        return keys;
    }

    private static ImportKeys ReadImportKeys(JsonElement element)
    {
        var fields = Fields(element, "importKeys");
        var keys = new ImportKeys();
        foreach (var role in ImportKeyRoles.All)
        {
            var name = role.WireName();
            if (!IsAbsent(fields, name)) keys.Set(role, ReadChord(fields[name], $"importKeys.{name}"));
        }
        if (!IsAbsent(fields, "advance")) keys.Advance = Flag(fields["advance"], "importKeys.advance");
        return keys;
    }

    private static PaletteOptions ReadPalette(JsonElement element)
    {
        var fields = Fields(element, "palette");
        var palette = new PaletteOptions();
        if (!IsAbsent(fields, "keepVisible")) palette.KeepVisible = Flag(fields["keepVisible"], "palette.keepVisible");
        if (!IsAbsent(fields, "favourites"))
        {
            var list = fields["favourites"];
            if (list.ValueKind != JsonValueKind.Array) throw Invalid("palette.favourites", "must be a list");
            foreach (var (item, index) in list.EnumerateArray().Select((item, index) => (item, index)))
            {
                var path = $"palette.favourites[{index}]";
                var entry = Fields(item, path);
                var kind = Text(Required(entry, "kind", path), $"{path}.kind") switch
                {
                    "preset" => PaletteEntryKind.Preset,
                    "tool" => PaletteEntryKind.Tool,
                    _ => throw Invalid($"{path}.kind", "must be preset or tool"),
                };
                palette.Favourites.Add(new PaletteEntry(
                    kind,
                    Text(Required(entry, "id", path), $"{path}.id"),
                    IsAbsent(entry, "argument") ? null : Text(entry["argument"], $"{path}.argument"),
                    Text(Required(entry, "title", path), $"{path}.title")));
            }
        }
        return palette;
    }

    private static InputOptions ReadInput(JsonElement element)
    {
        var fields = Fields(element, "input");
        var input = new InputOptions();
        bool Bool(string name, bool fallback) => IsAbsent(fields, name) ? fallback : Flag(fields[name], $"input.{name}");
        double Double(string name, double fallback) => IsAbsent(fields, name) ? fallback : Number(fields[name], $"input.{name}");
        input.InvertWheel = Bool("invertWheel", input.InvertWheel);
        input.SwapNormalAndCoarse = Bool("swapNormalAndCoarse", input.SwapNormalAndCoarse);
        input.TrackpadPointsPerStep = Double("trackpadPointsPerStep", input.TrackpadPointsPerStep);
        input.RequireMouseDown = Bool("requireMouseDown", input.RequireMouseDown);
        input.DragSensitivity = Double("dragSensitivity", input.DragSensitivity);
        input.ReturnCursorAfterRelease = Bool("returnCursorAfterRelease", input.ReturnCursorAfterRelease);
        input.DoubleClickReset = Bool("doubleClickReset", input.DoubleClickReset);
        input.MiddleClickReset = Bool("middleClickReset", input.MiddleClickReset);
        input.MouseBackForward = Bool("mouseBackForward", input.MouseBackForward);
        input.FollowSelectedMask = Bool("followSelectedMask", input.FollowSelectedMask);
        input.RevealPanels = Bool("revealPanels", input.RevealPanels);
        input.ArrowKeysAdjust = Bool("arrowKeysAdjust", input.ArrowKeysAdjust);
        return input;
    }

    // Files ---------------------------------------------------------------------------

    /// <summary>
    /// Validates, then writes the keymap atomically: a temporary file next to
    /// <paramref name="path"/>, flushed to disk, then swapped in. A reader never
    /// sees half a file, and an invalid keymap never replaces a good one.
    /// </summary>
    public static void Save(Keymap keymap, string path)
    {
        keymap.Validate();
        var target = Path.GetFullPath(path);
        var folder = Path.GetDirectoryName(target) ?? throw new IOException("No folder for " + target);
        Directory.CreateDirectory(folder);
        var temporary = Path.Combine(folder, $".{Path.GetFileName(target)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(Encode(keymap)));
                stream.Flush(flushToDisk: true);
            }
            if (File.Exists(target)) File.Replace(temporary, target, destinationBackupFileName: null, ignoreMetadataErrors: true);
            else File.Move(temporary, target, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    /// <summary>
    /// The keymap stored at <paramref name="path"/>, or null when there is no
    /// file yet. Throws like <see cref="Decode"/> when the file can't be used.
    /// </summary>
    public static Keymap? Load(string path)
    {
        if (!File.Exists(path)) return null;
        return Decode(File.ReadAllText(path, Encoding.UTF8));
    }

    /// <summary>
    /// Keeps a copy of a keymap file this build can't decode, next to it, so
    /// nothing the user made is lost when the standard layout is saved over it:
    /// keymap.json → keymap.unreadable-2026-11-27-153000.json. A copy with the
    /// same content already there is used instead of a new one, so each launch
    /// does not add another. Returns the copy's path; throws <see cref="IOException"/>
    /// or <see cref="UnauthorizedAccessException"/> when no copy could be made.
    /// </summary>
    public static string PreserveUnreadable(string path, DateTimeOffset now)
    {
        var source = Path.GetFullPath(path);
        var folder = Path.GetDirectoryName(source) ?? throw new IOException("No folder for " + source);
        var stem = Path.GetFileNameWithoutExtension(source);
        var extension = Path.GetExtension(source);
        var content = File.ReadAllBytes(source);
        foreach (var existing in Directory.EnumerateFiles(folder, $"{stem}.unreadable-*{extension}"))
        {
            if (File.ReadAllBytes(existing).AsSpan().SequenceEqual(content)) return existing;
        }
        var name = $"{stem}.unreadable-{now.ToLocalTime():yyyy-MM-dd-HHmmss}";
        var target = Path.Combine(folder, name + extension);
        for (var suffix = 2; File.Exists(target); suffix++) target = Path.Combine(folder, $"{name}-{suffix}{extension}");
        File.Copy(source, target, overwrite: false);
        return target;
    }
}
