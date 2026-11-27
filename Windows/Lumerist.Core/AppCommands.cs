using Lumerist.Core.Input;

namespace Lumerist.Core;

/// <summary>
/// Commands Lumerist performs itself with one of Lightroom's own shortcuts,
/// because the SDK has no call for them. A Command Key stores them like
/// Lightroom commands (<c>{"command": "pasteSettingsFromPrevious"}</c>, never
/// with an argument: with one it is not this command), but they never go
/// through the plug-in or the protocol: the app presses the shortcut, with the
/// key that types its letter in Lightroom's layout.
/// </summary>
public static class AppCommands
{
    /// <summary>Lightroom's Paste Settings from Previous (Ctrl+Alt+V): the standard layout's Tab + Q.</summary>
    public const string PasteSettingsFromPrevious = "pasteSettingsFromPrevious";

    /// <summary>Every app command, in menu order.</summary>
    public static IReadOnlyList<string> All { get; } = [PasteSettingsFromPrevious];

    /// <summary>The old standard Tab + Q (0.9.1 and earlier): Lightroom shortcut Ctrl+Alt with the key at US V.</summary>
    private static readonly CommandAction OldPasteShortcut =
        new CommandAction.Shortcut(new KeyChord(ScanCodes.V, KeyModifiers.Control | KeyModifiers.Alt));

    private const string PasteTitle = "Paste settings from previous";

    public static bool IsAppCommand(string? name) => name is not null && All.Contains(name);

    /// <summary>"Paste settings from previous"; null for a name that is not an app command.</summary>
    public static string? Title(string name) => name == PasteSettingsFromPrevious ? PasteTitle : null;

    /// <summary>The Commands menu group the command is listed in.</summary>
    public static string Group(string name) => "Edit";

    /// <summary>
    /// The shortcut an app command presses: Ctrl+Alt+V with the key that types
    /// v in <paramref name="layout"/> (Lightroom's; null: the US key). Null for an unknown name.
    /// </summary>
    public static KeyChord? Shortcut(string name, KeyboardLayoutTable? layout = null) => name switch
    {
        PasteSettingsFromPrevious => FallbackShortcuts.Letter('v', KeyModifiers.Control | KeyModifiers.Alt, ScanCodes.V, layout),
        _ => null,
    };

    /// <summary>
    /// A Command Key saved by 0.9.1 or earlier: the standard Tab + Q, exactly
    /// (title and shortcut), becomes the app command, so it follows the
    /// layout. Anything else, including an edited copy, is left alone.
    /// </summary>
    public static CommandKey Migrate(CommandKey key) =>
        key.Title == PasteTitle && key.Action == OldPasteShortcut
            ? key with { Action = new CommandAction.Command(PasteSettingsFromPrevious) }
            : key;
}
