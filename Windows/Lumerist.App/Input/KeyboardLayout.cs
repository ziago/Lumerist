using System.Collections.Concurrent;
using Lumerist.Core;
using Lumerist.Core.Input;
using static Lumerist.App.Interop.Native;

namespace Lumerist.App.Input;

/// <summary>
/// What keys print in a keyboard layout, and which key types a character. The
/// lookups themselves are <see cref="KeyboardLayoutTable"/>'s; this reads the
/// table from Windows once per layout.
/// </summary>
internal static unsafe class KeyboardLayout
{
    private const int VK_CONTROL = 0x11, VK_MENU = 0x12, VK_LCONTROL = 0xA2, VK_RMENU = 0xA5;

    /// <summary>GetKeyboardType(0) for a Japanese keyboard, whose key left of 1 is 半角/全角.</summary>
    private const int JapaneseKeyboardType = 7;

    /// <summary>Tables by layout handle: a handle always names the same layout.</summary>
    private static readonly ConcurrentDictionary<nint, KeyboardLayoutTable> Tables = new();

    /// <summary>The layout of the foreground window (Lightroom's, when it is in front).</summary>
    public static nint OfForeground() => GetKeyboardLayout(LightroomWindow.Foreground().ThreadId);

    /// <summary>
    /// Lightroom's layout, wherever the focus is: the one its window used the
    /// last time it was in front (with "a different input method for each app
    /// window", Lumerist's own may differ). The foreground layout until Lightroom has been seen.
    /// </summary>
    public static nint OfLightroom()
    {
        var thread = LightroomWindow.LastThreadId;
        var layout = thread == 0 ? 0 : GetKeyboardLayout(thread);
        return layout != 0 ? layout : OfForeground();
    }

    /// <summary>The layout read once: every key's virtual key, and what it types alone and with AltGr.</summary>
    public static KeyboardLayoutTable Table(nint layout) => Tables.GetOrAdd(layout, Read);

    private static KeyboardLayoutTable Read(nint layout) => KeyboardLayoutTable.Read(scanCode =>
    {
        var virtualKey = MapVirtualKeyExW((uint)scanCode, MAPVK_VSC_TO_VK_EX, layout);
        if (virtualKey == 0) return null;
        var plain = stackalloc byte[256];
        var altGr = stackalloc byte[256];
        altGr[VK_CONTROL] = altGr[VK_MENU] = altGr[VK_LCONTROL] = altGr[VK_RMENU] = 0x80;
        var (text, isDead) = Type(virtualKey, scanCode, plain, layout);
        var (altGrText, _) = Type(virtualKey, scanCode, altGr, layout);
        return new LayoutKey(scanCode, (int)virtualKey, text, isDead, altGrText);
    });

    /// <summary>ToUnicodeEx without touching the keyboard state: a dead key reports −1 and its spacing character.</summary>
    private static (string? Text, bool IsDead) Type(uint virtualKey, int scanCode, byte* state, nint layout)
    {
        var buffer = stackalloc char[8];
        var count = ToUnicodeEx(virtualKey, (uint)scanCode, state, buffer, 8, TOUNICODE_NO_STATE_CHANGE, layout);
        if (count == 0) return (null, false);
        return (new string(buffer, 0, Math.Min(Math.Abs(count), 8)), count < 0);
    }

    /// <summary>
    /// "Z" for the key that types z, "É" for a key that types é; for a key that
    /// types nothing, Windows' name for it (半角/全角, 変換), else null.
    /// </summary>
    public static string? Name(int chordScanCode, nint layout) =>
        (HookScanCodes.IsExtended(chordScanCode) ? null : Table(layout).Name(chordScanCode)) ?? KeyNameText(chordScanCode);

    /// <summary>What a key types without modifiers, as typed ("ò", "`"); null for keys that type nothing.</summary>
    public static string? Typed(int chordScanCode, nint layout) =>
        HookScanCodes.IsExtended(chordScanCode) ? null : Table(layout).Typed(chordScanCode);

    /// <summary>
    /// Lightroom's Import keys in this layout and Lightroom's language (null: English). Toggle: the key typing the
    /// language's character alone (&lt; in Italian and Swedish, ` otherwise), else `, else &lt;, else none.
    /// </summary>
    public static ImportShortcuts ImportShortcutsFor(nint layout, string? language) => ImportShortcuts.ForLayout(Table(layout), language);

    /// <summary>Right Alt is AltGr in this layout (some key types a character with it).</summary>
    public static bool HasAltGr(nint layout) => Table(layout).HasAltGr;

    /// <summary>Why the standard layout gets no Palette Key on this keyboard, or null when it gets the key left of 1.</summary>
    public static PaletteKeyDefault.Unavailable? PaletteKeyUnavailable() =>
        PaletteKeyDefault.Reason(GetKeyboardType(0) == JapaneseKeyboardType, Typed(ScanCodes.LeftOf1, OfLightroom()));

    /// <summary>The standard layout's Palette Key on this keyboard (<see cref="PaletteKeyDefault"/>).</summary>
    public static KeyChord? StandardPaletteKey() => PaletteKeyDefault.Key(PaletteKeyUnavailable());

    /// <summary>What the key left of 1 types in Lightroom's layout, for General's explanation.</summary>
    public static string? LeftOf1Text() => Typed(ScanCodes.LeftOf1, OfLightroom());

    /// <summary>GetKeyNameText: the calling thread's layout names keys such as 半角/全角 or 한/영; null when it has no name.</summary>
    private static string? KeyNameText(int chordScanCode)
    {
        var lParam = (HookScanCodes.Low(chordScanCode) << 16) | (HookScanCodes.IsExtended(chordScanCode) ? 1 << 24 : 0);
        var buffer = stackalloc char[64];
        var length = GetKeyNameTextW(lParam, buffer, 64);
        if (length <= 0) return null;
        var name = new string(buffer, 0, length).Trim();
        return name.Length == 0 ? null : name;
    }
}
