using System.Runtime.InteropServices;

namespace Lumerist.App.Guide;

/// <summary>The few Windows calls these windows and the tray icon need.</summary>
internal static partial class GuideNative
{
    /// <summary>
    /// The user's display languages in order ("it-IT", "en-US"), the Mac's
    /// Locale.preferredLanguages. The app runs with invariant globalization,
    /// so the culture cannot tell. Empty when Windows does not say.
    /// </summary>
    public static unsafe IReadOnlyList<string> PreferredLanguages()
    {
        const uint languageNames = 0x8; // MUI_LANGUAGE_NAME
        uint length = 0;
        if (!GetUserPreferredUILanguages(languageNames, out _, null, ref length) || length == 0) return [];
        var buffer = new char[length];
        fixed (char* start = buffer)
        {
            if (!GetUserPreferredUILanguages(languageNames, out _, start, ref length)) return [];
        }
        return new string(buffer, 0, (int)Math.Min(length, (uint)buffer.Length))
            .Split('\0', StringSplitOptions.RemoveEmptyEntries);
    }

    /// <summary>A screen reader is running (SPI_GETSCREENREADER): results stay on screen until dismissed.</summary>
    public static bool IsScreenReaderRunning
    {
        get
        {
            var running = 0;
            return SystemParametersInfo(0x0046, 0, ref running, 0) && running != 0;
        }
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool GetUserPreferredUILanguages(uint flags, out uint count, char* buffer, ref uint length);

    [LibraryImport("user32.dll", EntryPoint = "SystemParametersInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SystemParametersInfo(uint action, uint parameter, ref int value, uint update);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DestroyIcon(nint icon);
}
