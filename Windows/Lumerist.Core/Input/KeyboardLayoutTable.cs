namespace Lumerist.Core.Input;

/// <summary>One key of a keyboard layout, as Windows maps it.</summary>
/// <param name="ScanCode">Set 1 scan code, 0x01–0x7F.</param>
/// <param name="VirtualKey">MapVirtualKeyEx(scan code, MAPVK_VSC_TO_VK_EX); 0 for none.</param>
/// <param name="Text">What the key types alone (ToUnicodeEx with no key held); a dead key gives its spacing character; null for nothing.</param>
/// <param name="IsDead">The key starts an accent (ToUnicodeEx answered −1).</param>
/// <param name="AltGrText">What the key types with AltGr (Ctrl+Alt held); null for nothing.</param>
public readonly record struct LayoutKey(int ScanCode, int VirtualKey, string? Text, bool IsDead = false, string? AltGrText = null);

/// <summary>
/// A keyboard layout as Lumerist reads it: every scan code 0x01–0x7F with its
/// virtual key and what it types. The app fills it from Windows
/// (KeyboardLayout.Table: MapVirtualKeyEx and ToUnicodeEx), tests by hand.
/// </summary>
/// <remarks>
/// Lookups walk the keys themselves instead of asking VkKeyScanEx, which
/// returns the first match in the layout DLL's table order: where a character
/// is typed both alone and with AltGr (&lt; on Croatian, Slovenian, Turkish,
/// Polish 214…), that can be the AltGr key, and the plain key is then missed.
/// </remarks>
public sealed class KeyboardLayoutTable
{
    public const int FirstScanCode = 0x01, LastScanCode = 0x7F;

    /// <summary>Scan codes Windows can map without the 0xE0 prefix, in the order they are probed.</summary>
    public static IReadOnlyList<int> ProbedScanCodes { get; } = [.. Enumerable.Range(FirstScanCode, LastScanCode - FirstScanCode + 1)];

    private readonly Dictionary<int, LayoutKey> keys;

    /// <summary>Main-block keys first (letters, digits, punctuation), then the rest, each by scan code.</summary>
    private readonly List<LayoutKey> searchOrder;

    public KeyboardLayoutTable(IEnumerable<LayoutKey> keys)
    {
        this.keys = [];
        foreach (var key in keys)
        {
            if (key.VirtualKey != 0) this.keys.TryAdd(key.ScanCode, key);
        }
        searchOrder = [.. this.keys.Values
            .OrderBy(key => KeyNames.LayoutDependentScanCodes.Contains(key.ScanCode) ? 0 : 1)
            .ThenBy(key => key.ScanCode)];
    }

    /// <summary>Reads every probed scan code; <paramref name="probe"/> returns null for a scan code with no key.</summary>
    public static KeyboardLayoutTable Read(Func<int, LayoutKey?> probe) =>
        new(ProbedScanCodes.Select(probe).OfType<LayoutKey>());

    public LayoutKey? Key(int scanCode) => keys.TryGetValue(scanCode, out var key) ? key : null;

    /// <summary>MapVirtualKeyEx(scan code, MAPVK_VSC_TO_VK_EX): 0 for a scan code with no key.</summary>
    public int VirtualKey(int scanCode) => keys.TryGetValue(scanCode, out var key) ? key.VirtualKey : 0;

    /// <summary>
    /// Some key types a character with AltGr: then right Alt is AltGr (Windows
    /// sends it as left Ctrl + right Alt), otherwise it is a plain Alt.
    /// </summary>
    public bool HasAltGr => keys.Values.Any(key => IsPrintable(key.AltGrText));

    /// <summary>What a key types alone, as typed ("ò", "`" for a dead `); null when it types nothing printable.</summary>
    public string? Typed(int scanCode) =>
        keys.TryGetValue(scanCode, out var key) && IsPrintable(key.Text) ? key.Text : null;

    /// <summary>"Z" for the key that types z, "É" for é; null for keys that type nothing.</summary>
    public string? Name(int scanCode) => Typed(scanCode)?.ToUpperInvariant();

    /// <summary>
    /// The key that types <paramref name="character"/> alone: no Shift, AltGr
    /// or other modifier, and not a dead key (which would only start an
    /// accent). With several, the one whose virtual key is the character
    /// (letters, digits), else a main-block key, else the lowest scan code. Null when none does.
    /// </summary>
    public int? PlainKeyFor(char character)
    {
        var text = character.ToString();
        LayoutKey? first = null;
        foreach (var key in searchOrder)
        {
            if (key.IsDead || key.Text != text) continue;
            if (VirtualKeyAgrees(key.VirtualKey, character)) return key.ScanCode;
            first ??= key;
        }
        return first?.ScanCode;
    }

    /// <summary>MapVirtualKeyEx(vk, MAPVK_VK_TO_VSC_EX): the first key with this virtual key, or null.</summary>
    public int? KeyForVirtualKey(int virtualKey)
    {
        foreach (var key in keys.Values.OrderBy(key => key.ScanCode))
        {
            if (key.VirtualKey == virtualKey) return key.ScanCode;
        }
        return null;
    }

    /// <summary>
    /// The key Lightroom's shortcut for a letter or digit is pressed with:
    /// <list type="number">
    /// <item>the key that types it alone and whose virtual key is that letter (both agree);</item>
    /// <item>else the key that types it alone (Lightroom's keys follow characters);</item>
    /// <item>else, when no key types it (Cyrillic, Greek…), the key whose virtual key is the letter:
    /// those layouts keep VK_P on the P key, and on Macedonian (Standard) VK_Z is not where US Z is;</item>
    /// <item>else null.</item>
    /// </list>
    /// </summary>
    public int? LetterKey(char letter)
    {
        var lower = char.ToLowerInvariant(letter);
        if (PlainKeyFor(lower) is { } typed) return typed;
        return LetterVirtualKey(lower) is { } virtualKey ? KeyForVirtualKey(virtualKey) : null;
    }

    /// <summary>VK_A…VK_Z and VK_0…VK_9 are the characters' upper-case codes.</summary>
    public static int? LetterVirtualKey(char character) => character switch
    {
        >= 'a' and <= 'z' => char.ToUpperInvariant(character),
        >= 'A' and <= 'Z' or >= '0' and <= '9' => character,
        _ => null,
    };

    private static bool VirtualKeyAgrees(int virtualKey, char character) =>
        LetterVirtualKey(character) is { } expected && expected == virtualKey;

    private static bool IsPrintable(string? text) =>
        !string.IsNullOrWhiteSpace(text) && !char.IsControl(text[0]);
}
