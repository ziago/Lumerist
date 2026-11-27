namespace Lumerist.Core;

/// <summary>
/// Lightroom Classic's keys follow its interface language: its shortcuts are
/// part of its translations, written as characters, and Lightroom matches
/// the character a key types on the keyboard layout. Lumerist takes them
/// from the language the plug-in reports (<see cref="LightroomContext.Language"/>,
/// "it", "de", "zh_cn"…) and presses them with the keys of Lightroom's layout
/// (<see cref="Input.KeyboardLayoutTable"/>). The languages are the <c>languages</c>
/// of Shared/lightroom-shortcuts.json (<see cref="LightroomShortcuts.LanguageEntry"/>):
/// <list type="bullet">
/// <item>a code selects the language listing it, else the one listing the part before "_"
/// (never for "zh"), else English, the keys of Adobe's help (also for an unknown, null language);</item>
/// <item>the Import window's Toggle is the language's character: &lt; in Italian and Swedish, `
/// in every other language (<see cref="ImportToggleCharacter"/>);</item>
/// <item>every other key Lumerist presses is the English one, unless the language's override for it
/// is marked to be pressed (<see cref="PressedChord"/>); none is today. German lists Strg+I for
/// Auto Tone and for the info overlay in one module, so Lumerist keeps Ctrl+U.</item>
/// </list>
/// </summary>
public static class LightroomLanguage
{
    /// <summary>The language of Adobe's help, and of Lumerist's keys while Lightroom's is not known.</summary>
    public const string English = "en";

    /// <summary>The language a code selects, English when it selects none.</summary>
    public static LightroomShortcuts.LanguageEntry Entry(string? language) => LightroomShortcuts.Shared.Resolve(language);

    internal static LightroomShortcuts.LanguageEntry Entry(string? language, LightroomShortcuts.Data data) => data.Resolve(language);

    /// <summary>
    /// The character Lightroom's Import window toggles a photo with in this
    /// language. The key that types it alone presses it; where none does, the
    /// Phase A rule takes over (<see cref="ImportShortcuts.ForLayout"/>).
    /// </summary>
    public static char ImportToggleCharacter(string? language) => Entry(language).ImportToggle;

    /// <summary>
    /// The chord a language's override has Lumerist press for a command
    /// ("autoTone", "pasteSettingsFromPrevious"…: the shared file's <c>fallbackFor</c>);
    /// null keeps the English chord.
    /// </summary>
    internal static LightroomShortcuts.LogicalChord? PressedChord(string command, string? language) =>
        LightroomShortcuts.Shared.PressedChord(command, Entry(language));

    /// <summary>"Italiano" for "it", "Português (Brasil)" for "pt_br"; the code itself for a language the file does not list.</summary>
    public static string DisplayName(string language) => LightroomShortcuts.Shared.Find(language)?.Name ?? language;

    /// <summary>General's line: "Lightroom's language: Italiano (it)", the code alone when it is not listed, or "not known yet".</summary>
    public static string DisplayLine(string? language) => DisplayLine(language, LightroomShortcuts.Shared);

    internal static string DisplayLine(string? language, LightroomShortcuts.Data data)
    {
        if (language is null) return "Lightroom's language: not known yet";
        return data.Find(language) is { } entry
            ? $"Lightroom's language: {entry.Name} ({language})"
            : $"Lightroom's language: {language}";
    }
}
