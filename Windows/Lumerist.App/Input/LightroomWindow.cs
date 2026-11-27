using System.Diagnostics;
using static Lumerist.App.Interop.Native;

namespace Lumerist.App.Input;

/// <summary>Whether Lightroom Classic is the foreground app, and which of its threads owns the window.</summary>
internal static class LightroomWindow
{
    /// <summary>Lightroom Classic's executable on Windows (the cloud-based Lightroom uses the same name).</summary>
    public const string ExecutableName = "Lightroom.exe";

    private static readonly Lock Gate = new();
    private static nint lastWindow;
    private static uint lastProcess;
    private static uint lastThread;
    private static bool lastIsLightroom;
    private static uint lastLightroomThread;

    /// <summary>
    /// The thread of the last Lightroom window seen in front (0 before any):
    /// its keyboard layout is Lightroom's, also while Lumerist's window has the focus.
    /// </summary>
    public static uint LastThreadId
    {
        get
        {
            lock (Gate) return lastLightroomThread;
        }
    }

    /// <summary>
    /// Cheap enough for every hook event: the process is looked up only when
    /// the foreground window changes.
    /// </summary>
    public static (bool IsInFront, uint ThreadId) Foreground()
    {
        var (isInFront, threadId, _) = ForegroundWindow();
        return (isInFront, threadId);
    }

    /// <summary><see cref="Foreground"/> plus the foreground window itself.</summary>
    public static (bool IsInFront, uint ThreadId, nint Window) ForegroundWindow()
    {
        var window = GetForegroundWindow();
        lock (Gate)
        {
            if (window != lastWindow || window == 0)
            {
                lastWindow = window;
                uint process = 0;
                lastThread = window == 0 ? 0 : GetWindowThreadProcessId(window, out process);
                if (process != lastProcess)
                {
                    lastProcess = process;
                    lastIsLightroom = process != 0 && IsLightroomClassic(process);
                }
            }
            if (lastIsLightroom && lastThread != 0) lastLightroomThread = lastThread;
            return (lastIsLightroom, lastThread, window);
        }
    }

    public static bool IsRunning
    {
        get
        {
            var processes = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(ExecutableName));
            try
            {
                return processes.Any(process => IsLightroomClassic((uint)process.Id));
            }
            finally
            {
                foreach (var process in processes) process.Dispose();
            }
        }
    }

    /// <summary>
    /// The running Lightroom Classic processes and their program files, for
    /// "Quit Lightroom &amp; update" after an update. The caller disposes them.
    /// </summary>
    public static List<(Process Process, string Path)> ClassicProcesses()
    {
        var found = new List<(Process, string)>();
        foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(ExecutableName)))
        {
            if (IsLightroomClassic((uint)process.Id) && ProcessImagePath((uint)process.Id) is { } path) found.Add((process, path));
            else process.Dispose();
        }
        return found;
    }

    /// <summary>
    /// Lightroom.exe that is Lightroom Classic, not the cloud-based Lightroom:
    /// by the product name in its version information, or by its folder
    /// ("Adobe Lightroom Classic") when that cannot be read.
    /// </summary>
    private static bool IsLightroomClassic(uint processId)
    {
        if (ProcessImagePath(processId) is not { } path) return false;
        if (!string.Equals(Path.GetFileName(path), ExecutableName, StringComparison.OrdinalIgnoreCase)) return false;
        try
        {
            if (FileVersionInfo.GetVersionInfo(path).ProductName is { Length: > 0 } product)
                return product.Contains("Classic", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // Fall back to the folder name.
        }
        return path.Contains("Lightroom Classic", StringComparison.OrdinalIgnoreCase);
    }
}
