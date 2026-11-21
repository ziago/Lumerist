namespace Lumerist.Core;

/// <summary>
/// Human-readable names for scan codes. Keys are stored as scan codes, so a
/// binding follows the physical key. The app installs <see cref="LayoutName"/>,
/// which asks the active keyboard layout what a key prints (AZERTY, QWERTZ,
/// Italian…); without it, or for keys it does not name, the US names below are used.
/// </summary>
public static class KeyNames
{
    /// <summary>Layout-aware name for a scan code, or null to use the US name. Set once by the app.</summary>
    public static Func<int, string?>? LayoutName { get; set; }

    /// <summary>Scan codes whose printed character depends on the layout (letters, digits, punctuation, ISO key).</summary>
    public static IReadOnlySet<int> LayoutDependentScanCodes { get; } = new HashSet<int>(
        Enumerable.Range(0x02, 0x0D - 0x02 + 1)        // 1 … =
            .Concat(Enumerable.Range(0x10, 0x1B - 0x10 + 1)) // Q … ]
            .Concat(Enumerable.Range(0x1E, 0x29 - 0x1E + 1)) // A … `
            .Concat(Enumerable.Range(0x2B, 0x35 - 0x2B + 1)) // \ … /
            .Append(0x56));                                  // ISO key left of Z

    private static readonly Dictionary<int, string> UsNames = new()
    {
        [0x01] = "Esc", [0x02] = "1", [0x03] = "2", [0x04] = "3", [0x05] = "4", [0x06] = "5", [0x07] = "6",
        [0x08] = "7", [0x09] = "8", [0x0A] = "9", [0x0B] = "0", [0x0C] = "-", [0x0D] = "=", [0x0E] = "Backspace",
        [0x0F] = "Tab", [0x10] = "Q", [0x11] = "W", [0x12] = "E", [0x13] = "R", [0x14] = "T", [0x15] = "Y",
        [0x16] = "U", [0x17] = "I", [0x18] = "O", [0x19] = "P", [0x1A] = "[", [0x1B] = "]", [0x1C] = "Enter",
        [0x1E] = "A", [0x1F] = "S", [0x20] = "D", [0x21] = "F", [0x22] = "G", [0x23] = "H", [0x24] = "J",
        [0x25] = "K", [0x26] = "L", [0x27] = ";", [0x28] = "'", [0x29] = "`", [0x2B] = "\\", [0x2C] = "Z",
        [0x2D] = "X", [0x2E] = "C", [0x2F] = "V", [0x30] = "B", [0x31] = "N", [0x32] = "M", [0x33] = ",",
        [0x34] = ".", [0x35] = "/", [0x39] = "Space", [0x56] = "<",
        [0x3B] = "F1", [0x3C] = "F2", [0x3D] = "F3", [0x3E] = "F4", [0x3F] = "F5", [0x40] = "F6",
        [0x41] = "F7", [0x42] = "F8", [0x43] = "F9", [0x44] = "F10", [0x57] = "F11", [0x58] = "F12",
        [ScanCodes.LeftArrow] = "←", [ScanCodes.RightArrow] = "→", [ScanCodes.UpArrow] = "↑", [ScanCodes.DownArrow] = "↓",
        [0xE047] = "Home", [0xE04F] = "End", [0xE049] = "PgUp", [0xE051] = "PgDn", [0xE052] = "Ins", [0xE053] = "Del",
        // Numeric keypad: distinct physical keys from the main row.
        [0x52] = "Num 0", [0x4F] = "Num 1", [0x50] = "Num 2", [0x51] = "Num 3", [0x4B] = "Num 4", [0x4C] = "Num 5",
        [0x4D] = "Num 6", [0x47] = "Num 7", [0x48] = "Num 8", [0x49] = "Num 9", [0x53] = "Num .", [0x37] = "Num *",
        [0x4A] = "Num -", [0x4E] = "Num +", [0xE035] = "Num /", [0xE01C] = "Num Enter",
    };

    /// <summary>Windows order and spelling for modifiers: "Ctrl+Alt+Shift+".</summary>
    public static string ModifierPrefix(KeyModifiers modifiers)
    {
        var text = "";
        if ((modifiers & KeyModifiers.Control) != 0) text += "Ctrl+";
        if ((modifiers & KeyModifiers.Alt) != 0) text += "Alt+";
        if ((modifiers & KeyModifiers.Shift) != 0) text += "Shift+";
        return text;
    }

    public static string Name(int scanCode) => Name(scanCode, LayoutName);

    /// <summary>Name using an explicit layout lookup (testable without global state).</summary>
    public static string Name(int scanCode, Func<int, string?>? layout)
    {
        if (LayoutDependentScanCodes.Contains(scanCode) && layout?.Invoke(scanCode) is { Length: > 0 } name)
            return name;
        return UsName(scanCode);
    }

    /// <summary>The US name, independent of the active layout.</summary>
    internal static string UsName(int scanCode) =>
        UsNames.TryGetValue(scanCode, out var name) ? name : $"Key {scanCode:X}";
}
