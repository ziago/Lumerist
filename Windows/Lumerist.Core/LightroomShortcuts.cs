using System.Text.Json;

namespace Lumerist.Core;

/// <summary>
/// Lightroom Classic's own default keys where Lumerist can take a key over:
/// Library, Develop and the Import window (Sources/LumeristCore/LightroomShortcuts.swift).
/// Lumerist warns, without blocking, when a tap key (Command Key, Nudge Key,
/// Import key) is one of them in its own place. The list is
/// Shared/lightroom-shortcuts.json, from Adobe's public help (the Import
/// window's keys are community reports); the Windows app reads its
/// <c>windows</c> chords from the copy built into this assembly.
/// <para>
/// Adobe writes the keys for US keyboards. Letters and digits match by the
/// character the key types on the active layout, as Lightroom's own
/// shortcuts do; arrows, F-keys and named keys (Tab, Space, Enter…) match by
/// key. Punctuation never matches: which key types "[" or "`" differs from
/// layout to layout, and a missing warning is better than a wrong one.
/// </para>
/// </summary>
public static class LightroomShortcuts
{
    public enum Context
    {
        Library,
        Develop,
        ImportWindow,
    }

    /// <summary>One of Lightroom's keys found for a chord.</summary>
    /// <param name="Name">What the key does in Lightroom, for the warning: "Toggle Zoom view".</param>
    /// <param name="IsCommunityReported">From community reports rather than Adobe's help.</param>
    /// <param name="ImportRole">In the Import window, the key that Import key itself sends.</param>
    public sealed record Match(string Name, Context Context, bool IsCommunityReported, ImportKeyRole? ImportRole);

    /// <summary>One shortcut of the shared file, with its Windows chords as written there ("Primary+Shift+Z").</summary>
    internal sealed record Entry(string Name, IReadOnlyList<string> Windows, IReadOnlyList<Context> Contexts,
        bool IsCommunityReported, ImportKeyRole? ImportRole);

    /// <summary>A chord by logical key name ("Z", "F2", "ArrowRight") and modifiers.</summary>
    internal readonly record struct LogicalChord(string Key, KeyModifiers Modifiers);

    private const string ResourceName = "lightroom-shortcuts.json";

    public static Context ContextOf(ParameterScope scope) => scope == ParameterScope.Develop ? Context.Develop : Context.Library;

    /// <summary>"library", "develop", "import": the names in the shared file.</summary>
    public static string WireName(this Context context) => context switch
    {
        Context.Library => "library",
        Context.Develop => "develop",
        _ => "import",
    };

    /// <summary>"Library", "Develop", "the Import window", as in "… in Library."</summary>
    public static string Place(this Context context) => context switch
    {
        Context.Library => "Library",
        Context.Develop => "Develop",
        _ => "the Import window",
    };

    /// <summary>Lightroom's default use of <paramref name="chord"/> in <paramref name="context"/>, if Lumerist can tell. Combos never match.</summary>
    public static Match? For(KeyChord chord, Context context) => For(chord, context, KeyNames.LayoutName);

    /// <param name="layout">The active layout's key names, as <see cref="KeyNames.LayoutName"/>.</param>
    internal static Match? For(KeyChord chord, Context context, Func<int, string?>? layout)
    {
        if (chord.IsCombo || MatchableKey(chord.ScanCode, layout) is not { } key) return null;
        return Index.Value.TryGetValue(context, out var chords)
            && chords.TryGetValue(new LogicalChord(key, chord.Modifiers), out var match)
            ? match
            : null;
    }

    // Table --------------------------------------------------------------------------

    /// <summary>Every shortcut of the shared file that exists on Windows, in file order.</summary>
    internal static IReadOnlyList<Entry> Table => LazyTable.Value;

    private static readonly Lazy<IReadOnlyList<Entry>> LazyTable = new(() => Read(LoadResource()));

    /// <summary>The first entry for a chord wins, in the order of Adobe's help.</summary>
    private static readonly Lazy<Dictionary<Context, Dictionary<LogicalChord, Match>>> Index = new(() =>
    {
        var index = new Dictionary<Context, Dictionary<LogicalChord, Match>>();
        foreach (var entry in Table)
        {
            foreach (var text in entry.Windows)
            {
                if (ParseChord(text) is not { } chord) continue;
                foreach (var context in entry.Contexts)
                {
                    if (!index.TryGetValue(context, out var chords)) index[context] = chords = [];
                    chords.TryAdd(chord, new Match(entry.Name, context, entry.IsCommunityReported, entry.ImportRole));
                }
            }
        }
        return index;
    });

    private static string LoadResource()
    {
        using var stream = typeof(LightroomShortcuts).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"{ResourceName} is not built into {typeof(LightroomShortcuts).Assembly.GetName().Name}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>The shortcuts with Windows chords. Shared/lightroom-shortcuts.json is checked by Scripts/generate-lightroom-shortcuts.py.</summary>
    internal static IReadOnlyList<Entry> Read(string json)
    {
        using var document = JsonDocument.Parse(json);
        var entries = new List<Entry>();
        foreach (var section in document.RootElement.GetProperty("sections").EnumerateArray())
        {
            var sectionContexts = Contexts(section.GetProperty("contexts"));
            var community = section.TryGetProperty("communityReported", out var flag) && flag.ValueKind == JsonValueKind.True;
            foreach (var shortcut in section.GetProperty("shortcuts").EnumerateArray())
            {
                var windows = shortcut.GetProperty("windows").EnumerateArray().Select(chord => chord.GetString()!).ToList();
                if (windows.Count == 0) continue;
                var name = shortcut.TryGetProperty("name", out var shortName) ? shortName.GetString()! : shortcut.GetProperty("action").GetString()!;
                var contexts = shortcut.TryGetProperty("contexts", out var own) ? Contexts(own) : sectionContexts;
                var role = shortcut.TryGetProperty("importRole", out var roleName) ? ImportKeyRoles.FromWireName(roleName.GetString()) : null;
                entries.Add(new Entry(name, windows, contexts, community, role));
            }
        }
        return entries;
    }

    private static List<Context> Contexts(JsonElement list) =>
        [.. list.EnumerateArray().Select(item => Enum.GetValues<Context>().First(context => context.WireName() == item.GetString()))];

    /// <summary>"Primary+Shift+Z" → Z with Ctrl and Shift (Primary is Ctrl on Windows); null for an unknown modifier.</summary>
    internal static LogicalChord? ParseChord(string text)
    {
        var parts = text.Split('+');
        var key = parts[^1];
        if (key.Length == 0) return null;
        var modifiers = KeyModifiers.None;
        foreach (var part in parts[..^1])
        {
            switch (part)
            {
                case "Shift": modifiers |= KeyModifiers.Shift; break;
                case "Alt": modifiers |= KeyModifiers.Alt; break;
                case "Primary" or "Control": modifiers |= KeyModifiers.Control; break;
                default: return null;
            }
        }
        return new LogicalChord(key, modifiers);
    }

    // Keys ---------------------------------------------------------------------------

    /// <summary>Keys that are the same on every layout, by scan code.</summary>
    private static readonly Dictionary<int, string> FixedKeys = new()
    {
        [ScanCodes.LeftArrow] = "ArrowLeft", [ScanCodes.RightArrow] = "ArrowRight",
        [ScanCodes.DownArrow] = "ArrowDown", [ScanCodes.UpArrow] = "ArrowUp",
        [ScanCodes.Tab] = "Tab", [ScanCodes.Space] = "Space", [ScanCodes.Enter] = "Return", [ScanCodes.Escape] = "Esc",
        [ScanCodes.Backspace] = "Backspace", [0xE053] = "ForwardDelete",
        [0xE047] = "Home", [0xE04F] = "End", [0xE049] = "PageUp", [0xE051] = "PageDown",
        [0x3B] = "F1", [0x3C] = "F2", [0x3D] = "F3", [0x3E] = "F4", [0x3F] = "F5", [0x40] = "F6",
        [0x41] = "F7", [0x42] = "F8", [0x43] = "F9", [0x44] = "F10", [0x57] = "F11", [0x58] = "F12",
    };

    /// <summary>
    /// The logical name to look up, or null for a key that is never matched:
    /// a letter or digit by the character it types on the active layout
    /// (<see cref="KeyNames"/>), a fixed key by its scan code, punctuation never.
    /// </summary>
    internal static string? MatchableKey(int scanCode, Func<int, string?>? layout)
    {
        if (FixedKeys.TryGetValue(scanCode, out var fixedKey)) return fixedKey;
        if (!KeyNames.LayoutDependentScanCodes.Contains(scanCode)) return null;
        var name = KeyNames.Name(scanCode, layout);
        return name.Length == 1 && name[0] is (>= 'A' and <= 'Z') or (>= '0' and <= '9') ? name : null;
    }
}
