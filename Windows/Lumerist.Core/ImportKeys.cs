using Lumerist.Core.Input;

namespace Lumerist.Core;

// Import keys (Sources/LumeristCore/ImportKeys.swift): culling keys for
// Lightroom Classic's Import window. Shared/fixtures/gestures/import-keys.json
// and the keymap-rules cases pin them down for both apps.

/// <summary>The three Import keys.</summary>
public enum ImportKeyRole
{
    Include,
    Exclude,
    Toggle,
}

public static class ImportKeyRoles
{
    public static IReadOnlyList<ImportKeyRole> All { get; } = [ImportKeyRole.Include, ImportKeyRole.Exclude, ImportKeyRole.Toggle];

    /// <summary>"include", "exclude", "toggle": the names in files and shared cases.</summary>
    public static string WireName(this ImportKeyRole role) => role switch
    {
        ImportKeyRole.Include => "include",
        ImportKeyRole.Exclude => "exclude",
        _ => "toggle",
    };

    public static ImportKeyRole? FromWireName(string? name) =>
        All.Cast<ImportKeyRole?>().FirstOrDefault(role => role!.Value.WireName() == name);
}

/// <summary>
/// Keys for culling in Lightroom Classic's Import window, where the Develop
/// and Library keys pause. Each one presses one of Lightroom's own Import
/// shortcuts (<see cref="ImportShortcuts"/>), so Lightroom itself ticks or
/// unticks the photo.
/// <para>
/// None is assigned by default: Lumerist takes over a key only when the user
/// picks one (a preset Z would hide Lightroom's zoom). A null key is not assigned.
/// </para>
/// </summary>
public sealed record ImportKeys
{
    /// <summary>Ticks the photo, so Lightroom imports it (Lightroom's P).</summary>
    public KeyChord? Include { get; set; }

    /// <summary>Unticks the photo, so Lightroom leaves it out (Lightroom's X).</summary>
    public KeyChord? Exclude { get; set; }

    /// <summary>Ticks or unticks the photo (Lightroom's ` key, see <see cref="ImportShortcuts.Toggle"/>).</summary>
    public KeyChord? Toggle { get; set; }

    /// <summary>"Move to the next photo": after Include or Exclude, Lumerist also presses →.</summary>
    public bool Advance { get; set; }

    /// <summary>Nothing assigned and "Move to the next photo" off: what a keymap without Import keys means.</summary>
    public bool IsDefault => this == new ImportKeys();

    public KeyChord? ChordFor(ImportKeyRole role) => role switch
    {
        ImportKeyRole.Include => Include,
        ImportKeyRole.Exclude => Exclude,
        _ => Toggle,
    };

    /// <summary>Assigns <paramref name="chord"/> to <paramref name="role"/> (null clears it).</summary>
    public void Set(ImportKeyRole role, KeyChord? chord)
    {
        switch (role)
        {
            case ImportKeyRole.Include: Include = chord; break;
            case ImportKeyRole.Exclude: Exclude = chord; break;
            default: Toggle = chord; break;
        }
    }

    /// <summary>The Import key pressed, by exact chord: Shift+Z is not Z.</summary>
    public ImportKeyRole? RoleFor(KeyChord chord) =>
        ImportKeyRoles.All.Cast<ImportKeyRole?>().FirstOrDefault(role => ChordFor(role!.Value) == chord);
}

/// <summary>
/// Lightroom Classic's own keys in its Import window, which the Import keys
/// press: P includes, X excludes, ` (Toggle) ticks or unticks, and →
/// selects the next photo.
/// <para>
/// <see cref="Standard"/> holds the US scan codes the shared cases expect.
/// The app uses <see cref="ForLayout"/> with Lightroom's layout:
/// <list type="bullet">
/// <item>P and X are Lightroom's keys for p and x, like its flag shortcuts in
/// <see cref="FallbackShortcuts"/> (<see cref="KeyboardLayoutTable.LetterKey"/>);</item>
/// <item>Toggle is the key that types ` alone (no Shift or AltGr, not a dead
/// key): 0x29 on US and UK keyboards. Otherwise the key that types &lt; alone,
/// the ISO key left of Z (0x56 on Italian, German, French, Spanish keyboards),
/// which is what Lightroom's Import window toggles with on an Italian keyboard
/// (checked on the PC, 27 Nov 2026), as key code 50 does on the Mac.
/// Otherwise there is no Toggle (null) and the key sends nothing. Never the
/// key left of 1 by position (\ on Italian, a real Lightroom shortcut that hung
/// the Import window) and never VK_OEM_3 alone (ò on Italian);</item>
/// <item>→ is the same everywhere.</item>
/// </list>
/// </para>
/// </summary>
/// <param name="Toggle">Null when no key of the layout types ` or &lt; alone.</param>
public sealed record ImportShortcuts(KeyChord Include, KeyChord Exclude, KeyChord? Toggle, KeyChord NextPhoto)
{
    public static ImportShortcuts Standard { get; } = new(
        new KeyChord(ScanCodes.P),
        new KeyChord(ScanCodes.X),
        new KeyChord(0x29), // ` on US keyboards
        new KeyChord(ScanCodes.RightArrow));

    /// <param name="layout">Lightroom's keyboard layout. Null: the US keys.</param>
    /// <param name="language">
    /// Lightroom's interface language (null: English). It names Toggle's
    /// character (<see cref="LightroomLanguage.ImportToggleCharacter"/>: &lt; in
    /// Italian and Swedish, ` otherwise), pressed with the key that types it
    /// alone; where none does, the key that types ` alone, else &lt;, else none,
    /// so a language never leaves a layout worse off than English.
    /// </param>
    public static ImportShortcuts ForLayout(KeyboardLayoutTable? layout, string? language = null) =>
        ForLayout(layout, language, LightroomLanguage.ImportToggleCharacter(language));

    /// <param name="toggle">The language's Toggle character (<see cref="LightroomLanguage.ImportToggleCharacter"/>).</param>
    internal static ImportShortcuts ForLayout(KeyboardLayoutTable? layout, string? language, char toggle) => layout is null ? Standard : Standard with
    {
        Include = FallbackShortcuts.For(LightroomCommandName.FlagPick, layout, language) ?? Standard.Include,
        Exclude = FallbackShortcuts.For(LightroomCommandName.FlagReject, layout, language) ?? Standard.Exclude,
        Toggle = (layout.PlainKeyFor(toggle) ?? layout.PlainKeyFor('`') ?? layout.PlainKeyFor('<')) is { } scanCode ? new KeyChord(scanCode) : null,
    };

    /// <summary>The key a role presses; null for Toggle when the layout has none.</summary>
    public KeyChord? ChordFor(ImportKeyRole role) => role switch
    {
        ImportKeyRole.Include => Include,
        ImportKeyRole.Exclude => Exclude,
        _ => Toggle,
    };
}
