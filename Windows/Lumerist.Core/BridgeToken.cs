using System.Security.Cryptography;
using System.Text;

namespace Lumerist.Core;

/// <summary>
/// The secret that proves a request comes from this Lumerist app (protocol 4),
/// the Mac's BridgeToken.swift.
/// </summary>
/// <remarks>
/// Created once: 32 random bytes written as 64 lowercase hexadecimal characters
/// to %APPDATA%\Lumerist\bridge-token (<see cref="Contract.BridgeTokenFileName"/>).
/// %APPDATA% belongs to the user: other standard accounts cannot read it.
/// Every request line carries the token, and the plug-in (BridgeToken.lua)
/// reads the same file and refuses any request without it, so another account
/// on the PC, or a web page posting to 127.0.0.1, cannot drive Lightroom.
/// </remarks>
public static class BridgeToken
{
    public const int Length = 64;

    /// <summary>64 characters, 0-9 and a-f only.</summary>
    public static bool IsValid(string? token) =>
        token is { Length: Length } && token.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');

    /// <summary>32 bytes from the system's secure random generator, as lowercase hex.</summary>
    public static string Generate() => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(Length / 2));

    /// <summary>The file in a folder (normally %APPDATA%\Lumerist).</summary>
    public static string PathIn(string folder) => Path.Combine(folder, Contract.BridgeTokenFileName);

    /// <summary>
    /// The token in <paramref name="folder"/>, created first when the file is
    /// missing or does not hold a valid token.
    /// </summary>
    /// <exception cref="IOException">The file could not be written.</exception>
    /// <exception cref="UnauthorizedAccessException">The folder is not writable.</exception>
    public static string LoadOrCreate(string folder)
    {
        var path = PathIn(folder);
        if (Read(path) is { } existing) return existing;
        var token = Generate();
        Save(token, folder);
        return token;
    }

    private static string? Read(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            var text = File.ReadAllText(path, Encoding.UTF8).Trim().ToLowerInvariant();
            return IsValid(text) ? text : null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// A temporary file in the same folder, then a rename over the old one, so
    /// the plug-in never reads half a token (as <see cref="PluginLink.Save"/>).
    /// </summary>
    private static void Save(string token, string folder)
    {
        Directory.CreateDirectory(folder);
        var path = PathIn(folder);
        var temporary = Path.Combine(folder, $".{Contract.BridgeTokenFileName}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(Encoding.ASCII.GetBytes(token));
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, path, overwrite: true);
        }
        catch
        {
            try { File.Delete(temporary); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            throw;
        }
    }
}
