using System.Text.Json;
using Lumerist.App.Input;
using Lumerist.Core;

namespace Lumerist.App.Services;

/// <summary>
/// keymap.json in %APPDATA%\Lumerist: loads it, or the standard layout for
/// this keyboard, and never saves over a file this build could not read
/// until a copy of it is kept (<see cref="KeymapStore.PreserveUnreadable"/>).
/// </summary>
internal static class KeymapFile
{
    /// <summary>The file could not be read and no copy could be made yet: it must not be replaced.</summary>
    private static bool unreadableNotPreserved;

    /// <summary>The standard layout for this keyboard: the Palette Key only where the key left of 1 allows it.</summary>
    public static Keymap StandardLayout() => Keymap.StandardLayout(KeyboardLayout.StandardPaletteKey());

    /// <summary>The saved keymap; the standard layout when there is none or it can't be read (a copy is kept).</summary>
    public static Keymap Load()
    {
        var path = AppPaths.KeymapFile;
        try
        {
            if (KeymapStore.Load(path) is { } saved) return saved;
        }
        catch (Exception error) when (error is KeymapException or JsonException)
        {
            Log.Write($"Keymap not loaded, using the standard layout: {error.Message}");
            Preserve(path);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // Not read at all (locked, no access): its content is unknown, so keep a copy before saving over it.
            Log.Write($"Keymap not loaded, using the standard layout: {error.Message}");
            unreadableNotPreserved = File.Exists(path);
        }
        return StandardLayout();
    }

    /// <summary>
    /// Validates and saves; throws like <see cref="KeymapStore.Save"/>, and
    /// <see cref="IOException"/> while an unreadable file has no copy yet.
    /// </summary>
    public static void Save(Keymap keymap)
    {
        var path = AppPaths.KeymapFile;
        if (unreadableNotPreserved && File.Exists(path) && !Preserve(path))
            throw new IOException("Lumerist could not keep a copy of the keymap.json it could not read, so it did not save over it.");
        KeymapStore.Save(keymap, path);
        unreadableNotPreserved = false;
    }

    private static bool Preserve(string path)
    {
        try
        {
            var copy = KeymapStore.PreserveUnreadable(path, DateTimeOffset.Now);
            Log.Write($"Kept the unreadable keymap as {Path.GetFileName(copy)}");
            unreadableNotPreserved = false;
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Log.Write($"Could not keep a copy of the unreadable keymap: {error.Message}");
            unreadableNotPreserved = true;
            return false;
        }
    }
}
