using System.Globalization;
using System.Threading.Channels;

namespace Lumerist.App.Services;

/// <summary>
/// Optional troubleshooting log in %LOCALAPPDATA%\Lumerist\Logs\Lumerist.log
/// (the Mac's DebugLog). It records connection states, commands and Lightroom
/// outcomes only, never the keys typed. Off unless the user turns it on.
/// </summary>
internal static class Log
{
    private const long MaximumBytes = 5_000_000;

    private static readonly Channel<string> Lines = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true });

    static Log() => _ = Task.Run(WriteLinesAsync);

    public static volatile bool IsEnabled;

    public static string FilePath => Path.Combine(AppPaths.LogFolder, "Lumerist.log");

    /// <summary>Never blocks and never throws: safe from the input hook.</summary>
    public static void Write(string message)
    {
        if (!IsEnabled) return;
        try
        {
            Lines.Writer.TryWrite($"{DateTimeOffset.Now.ToString("yyyy-MM-ddTHH:mm:ss.fffzzz", CultureInfo.InvariantCulture)} {message}\n");
        }
        catch (Exception)
        {
            // See the summary.
        }
    }

    private static async Task WriteLinesAsync()
    {
        await foreach (var line in Lines.Reader.ReadAllAsync())
        {
            try
            {
                Directory.CreateDirectory(AppPaths.LogFolder);
                var file = new FileInfo(FilePath);
                // Start over rather than grow without bound.
                if (file.Exists && file.Length > MaximumBytes) file.Delete();
                await File.AppendAllTextAsync(FilePath, line);
            }
            catch (Exception)
            {
                // A log that cannot be written must never stop the app.
            }
        }
    }
}
