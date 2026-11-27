using Lumerist.Core.Input;

namespace Lumerist.Core;

/// <summary>
/// Lightroom Classic's own Windows shortcuts for commands the SDK cannot
/// perform in some modules (e.g. Auto Tone in Library), and for the commands
/// Lumerist performs only with a shortcut (<see cref="AppCommands"/>), in
/// Lightroom's interface language (<see cref="LightroomLanguage"/>).
/// Letters are looked up in Lightroom's layout (<see cref="KeyboardLayoutTable.LetterKey"/>),
/// so Ctrl+Z is sent with the key that types "z" (another key on AZERTY and
/// QWERTZ keyboards), or, where no key types it (Cyrillic, Greek…), with the
/// key whose virtual key is VK_Z.
/// </summary>
public static class FallbackShortcuts
{
    private static readonly Dictionary<LightroomCommandName, (char Character, KeyModifiers Modifiers, int UsScanCode)> ByCharacter = new()
    {
        [LightroomCommandName.AutoTone] = ('u', KeyModifiers.Control, 0x16),                       // Ctrl+U
        [LightroomCommandName.Undo] = ('z', KeyModifiers.Control, 0x2C),                           // Ctrl+Z
        [LightroomCommandName.Redo] = ('z', KeyModifiers.Control | KeyModifiers.Shift, 0x2C),      // Ctrl+Shift+Z
        [LightroomCommandName.FlagPick] = ('p', KeyModifiers.None, ScanCodes.P),                    // P
        [LightroomCommandName.FlagReject] = ('x', KeyModifiers.None, ScanCodes.X),                  // X
        [LightroomCommandName.FlagRemove] = ('u', KeyModifiers.None, 0x16),                         // U
        [LightroomCommandName.ShowClipping] = ('j', KeyModifiers.None, 0x24),                       // J
    };

    /// <summary>Arrow keys are the same on every layout.</summary>
    private static readonly Dictionary<LightroomCommandName, KeyChord> ByKey = new()
    {
        [LightroomCommandName.NextPhoto] = new KeyChord(ScanCodes.RightArrow),
        [LightroomCommandName.PreviousPhoto] = new KeyChord(ScanCodes.LeftArrow),
    };

    /// <summary>The US key of each letter and digit, for a letter a language puts in place of the English one.</summary>
    private static readonly Dictionary<char, int> UsKeys = Enumerable.Range(0x02, 0x35 - 0x02 + 1)
        .Select(scanCode => (ScanCode: scanCode, Name: KeyNames.UsName(scanCode)))
        .Where(key => key.Name.Length == 1 && char.IsAsciiLetterOrDigit(key.Name[0]))
        .ToDictionary(key => char.ToLowerInvariant(key.Name[0]), key => key.ScanCode);

    /// <param name="layout">Lightroom's keyboard layout; null: the US keys.</param>
    /// <param name="language">Lightroom's interface language (<see cref="LightroomLanguage"/>); null: English.</param>
    public static KeyChord? For(LightroomCommandName command, KeyboardLayoutTable? layout = null, string? language = null)
    {
        if (ByKey.TryGetValue(command, out var chord)) return chord;
        if (!ByCharacter.TryGetValue(command, out var entry)) return null;
        return Pressed(command.WireName(), language, layout) ?? Letter(entry.Character, entry.Modifiers, entry.UsScanCode, layout);
    }

    /// <summary>
    /// A language's own chord for a command, when the shared file has Lumerist
    /// press it (an override marked <c>press</c>; none is today): its letter on
    /// Lightroom's layout. Null keeps the English chord.
    /// </summary>
    internal static KeyChord? Pressed(string command, string? language, KeyboardLayoutTable? layout) =>
        LightroomLanguage.PressedChord(command, language) is { } chord && chord.Key.Length == 1
            && UsKeys.TryGetValue(char.ToLowerInvariant(chord.Key[0]), out var usKey)
            ? Letter(char.ToLowerInvariant(chord.Key[0]), chord.Modifiers, usKey, layout)
            : null;

    /// <summary>A letter shortcut: the layout's key for the letter, else the US key.</summary>
    internal static KeyChord Letter(char character, KeyModifiers modifiers, int usScanCode, KeyboardLayoutTable? layout) =>
        new(layout?.LetterKey(character) ?? usScanCode, modifiers);
}
