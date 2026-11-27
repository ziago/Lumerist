namespace Lumerist.Core;

/// <summary>
/// Which Palette Key the standard layout gets on this keyboard (Windows; the
/// Mac picks the key left of 1 by key code). Normally the key left of 1, but none:
/// <list type="bullet">
/// <item>on Japanese keyboards, where that key is 半角/全角 and switches the input method;</item>
/// <item>where it types a letter or digit that Lightroom uses as a shortcut ("0" on
/// Hungarian keyboards, Remove star rating), since the Palette Key never reaches Lightroom.
/// Other letters (º on Spanish, ё on Russian keyboards) are not Lightroom keys and keep it.</item>
/// </list>
/// General explains why the box is empty (<see cref="Explanation"/>).
/// </summary>
public static class PaletteKeyDefault
{
    public enum Unavailable
    {
        /// <summary>The key left of 1 switches the input method (半角/全角 on Japanese keyboards).</summary>
        InputMethodKey,
        /// <summary>The key left of 1 types a letter or digit Lightroom uses ("0" on Hungarian keyboards).</summary>
        LightroomKey,
    }

    /// <param name="isJapaneseKeyboard">Windows reports a Japanese keyboard (GetKeyboardType(0) = 7).</param>
    /// <param name="leftOf1Text">What the key left of 1 types alone in Lightroom's layout; null for nothing.</param>
    public static Unavailable? Reason(bool isJapaneseKeyboard, string? leftOf1Text)
    {
        if (isJapaneseKeyboard) return Unavailable.InputMethodKey;
        return leftOf1Text is { Length: 1 } text && char.IsAsciiLetterOrDigit(text[0]) ? Unavailable.LightroomKey : null;
    }

    /// <summary>The standard layout's Palette Key: the key left of 1, or none for a <paramref name="reason"/>.</summary>
    public static KeyChord? Key(Unavailable? reason) => reason is null ? KeyChord.LeftOf1 : null;

    /// <summary>General's line under an empty Palette Key box.</summary>
    public static string Explanation(Unavailable reason, string? leftOf1Text) => reason switch
    {
        Unavailable.InputMethodKey =>
            "No Palette Key is set: on Japanese keyboards the key left of 1 is 半角/全角, which switches the input method. Click the box to pick another key.",
        _ =>
            $"No Palette Key is set: with this keyboard layout the key left of 1 types “{leftOf1Text}”, which Lightroom uses as a shortcut. Click the box to pick another key.",
    };
}
