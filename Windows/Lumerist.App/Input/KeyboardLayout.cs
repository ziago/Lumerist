using Lumerist.Core.Input;
using static Lumerist.App.Interop.Native;

namespace Lumerist.App.Input;

/// <summary>What keys print in a keyboard layout, and which key types a character.</summary>
internal static unsafe class KeyboardLayout
{
    /// <summary>The layout of the foreground window (Lightroom's, when it is in front).</summary>
    public static nint OfForeground() => GetKeyboardLayout(LightroomWindow.Foreground().ThreadId);

    /// <summary>"Z" for the key that types z, "É" for a key that types é; null for keys that type nothing.</summary>
    public static string? Name(int chordScanCode, nint layout)
    {
        var virtualKey = MapVirtualKeyExW((uint)chordScanCode, MAPVK_VSC_TO_VK_EX, layout);
        if (virtualKey == 0) return null;
        var state = stackalloc byte[256];
        var buffer = stackalloc char[8];
        var count = ToUnicodeEx(virtualKey, (uint)HookScanCodes.Low(chordScanCode), state, buffer, 8, TOUNICODE_NO_STATE_CHANGE, layout);
        // A dead key (^ on French layouts) reports -1 and its spacing character.
        count = Math.Abs(count);
        if (count == 0) return null;
        var text = new string(buffer, 0, count);
        if (string.IsNullOrWhiteSpace(text) || char.IsControl(text[0])) return null;
        return text.ToUpperInvariant();
    }

    /// <summary>The chord scan code of the key that types <paramref name="character"/> without modifiers, or null.</summary>
    public static int? ScanCodeFor(char character, nint layout)
    {
        var result = VkKeyScanExW(character, layout);
        if (result == -1) return null;
        var virtualKey = result & 0xFF;
        // Needs Shift, Ctrl or Alt in this layout: not the plain key.
        if (((result >> 8) & 0xFF) != 0) return null;
        var scanCode = MapVirtualKeyExW((uint)virtualKey, MAPVK_VK_TO_VSC_EX, layout);
        if (scanCode == 0) return null;
        return (scanCode & 0xFF00) == 0xE000 ? HookScanCodes.ExtendedPrefix | (int)(scanCode & 0xFF) : (int)(scanCode & 0xFF);
    }
}
