using System.Text;
using System.Text.Json;

namespace Lumerist.Core;

/// <summary>Why <see cref="PluginLink"/> could not be read, as OpenApp.lua names it.</summary>
public enum PluginLinkProblem
{
    None,
    /// <summary>No plugin-link.json.</summary>
    Missing,
    /// <summary>Not JSON, not an object, or "exe" is not text.</summary>
    Unreadable,
    /// <summary>"exe" is not a path the plug-in would start (<see cref="PluginLink.IsAcceptedByPlugin"/>).</summary>
    Invalid,
}

/// <summary>The link read back, or why there is none.</summary>
public readonly record struct PluginLinkReading(PluginLink? Link, PluginLinkProblem Problem);

/// <summary>
/// %APPDATA%\Lumerist\plugin-link.json (<see cref="Contract.PluginLinkFileName"/>),
/// written by the app at every start and read by the plug-in's OpenApp.lua to
/// find the app: <c>{ "exe": "C:\\…\\Lumerist.exe", "openWithLightroom": true }</c>.
/// </summary>
/// <param name="Exe">Absolute path to Lumerist.exe.</param>
/// <param name="OpenWithLightroom">"Open Lumerist when Lightroom starts".</param>
public sealed record PluginLink(string Exe, bool OpenWithLightroom = true)
{
    private const string ExeKey = "exe";
    private const string OpenWithLightroomKey = "openWithLightroom";
    private const string ExeSuffix = "\\lumerist.exe";

    /// <summary>
    /// The plug-in's rule, character for character: a plain absolute drive path
    /// ("C:\…") ending in \Lumerist.exe (any case), with nothing cmd.exe would
    /// read as quoting, variables or escapes (no ", %, ^ or control characters).
    /// </summary>
    public static bool IsAcceptedByPlugin(string? exe)
    {
        // Lua: exe:match("^%a:\\")
        if (exe is null || exe.Length < 3 || !char.IsAsciiLetter(exe[0]) || exe[1] != ':' || exe[2] != '\\') return false;
        // Lua: exe:find('[%c"%%^]') — control characters are 0x00–0x1F and 0x7F.
        foreach (var c in exe)
        {
            if (c < 0x20 || c == 0x7F || c == '"' || c == '%' || c == '^') return false;
        }
        // Lua: exe:lower():match("\\lumerist%.exe$") — lower() changes only A–Z.
        if (exe.Length < ExeSuffix.Length) return false;
        var tail = exe.AsSpan(exe.Length - ExeSuffix.Length);
        for (var i = 0; i < ExeSuffix.Length; i++)
        {
            if (AsciiLower(tail[i]) != ExeSuffix[i]) return false;
        }
        return true;
    }

    private static char AsciiLower(char c) => c is >= 'A' and <= 'Z' ? (char)(c + ('a' - 'A')) : c;

    public bool IsAccepted => IsAcceptedByPlugin(Exe);

    /// <summary>The file in a folder (normally %APPDATA%\Lumerist).</summary>
    public static string PathIn(string folder) => Path.Combine(folder, Contract.PluginLinkFileName);

    /// <summary>One line of ASCII JSON (anything else is \u-escaped), UTF-8 without a byte order mark.</summary>
    public byte[] Encode()
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString(ExeKey, Exe);
            writer.WriteBoolean(OpenWithLightroomKey, OpenWithLightroom);
            writer.WriteEndObject();
        }
        return buffer.ToArray();
    }

    /// <summary>
    /// Reads the file's text as the plug-in does: "exe" must be text, and only
    /// an explicit <c>false</c> turns "openWithLightroom" off. Duplicate keys:
    /// the last one wins, as in the plug-in's decoder.
    /// </summary>
    public static PluginLinkReading Decode(string json)
    {
        JsonElement? exe = null;
        JsonElement? open = null;
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return new(null, PluginLinkProblem.Unreadable);
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (property.NameEquals(ExeKey)) exe = property.Value.Clone();
                else if (property.NameEquals(OpenWithLightroomKey)) open = property.Value.Clone();
            }
        }
        catch (JsonException)
        {
            return new(null, PluginLinkProblem.Unreadable);
        }
        if (exe is not { ValueKind: JsonValueKind.String } text) return new(null, PluginLinkProblem.Unreadable);
        var path = text.GetString()!;
        if (!IsAcceptedByPlugin(path)) return new(null, PluginLinkProblem.Invalid);
        return new(new PluginLink(path, open?.ValueKind != JsonValueKind.False), PluginLinkProblem.None);
    }

    /// <summary>Reads plugin-link.json from a folder.</summary>
    public static PluginLinkReading Load(string folder)
    {
        var path = PathIn(folder);
        if (!File.Exists(path)) return new(null, PluginLinkProblem.Missing);
        try
        {
            // No BOM stripping: the plug-in would refuse a file that starts with one.
            var bytes = File.ReadAllBytes(path);
            return Decode(new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(bytes));
        }
        catch (Exception error) when (error is DecoderFallbackException or IOException or UnauthorizedAccessException)
        {
            return new(null, PluginLinkProblem.Unreadable);
        }
    }

    /// <summary>
    /// Writes plugin-link.json into a folder (created if needed) atomically: a
    /// temporary file in the same folder, then a rename over the old one, so
    /// the plug-in never reads half a file. Returns the file's path.
    /// </summary>
    public string Save(string folder)
    {
        Directory.CreateDirectory(folder);
        var path = PathIn(folder);
        var temporary = Path.Combine(folder, $".{Contract.PluginLinkFileName}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(Encode());
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, path, overwrite: true);
        }
        catch
        {
            try { File.Delete(temporary); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            throw;
        }
        return path;
    }
}
