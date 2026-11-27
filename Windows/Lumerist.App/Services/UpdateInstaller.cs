using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using Lumerist.Core;

namespace Lumerist.App.Services;

/// <summary>
/// "Update now" on Windows, around the checks in Lumerist.Core
/// (<see cref="UpdatePreparation"/>): only for the installed copy, the
/// download goes into a new random folder under %TEMP%, and the installer
/// runs from there by its full path with fixed arguments, after one more
/// check of its SHA-256 while the file is locked. Then Lumerist quits so the
/// installer can replace it. The installer is per-user: no administrator
/// prompt, and none is ever asked for.
/// </summary>
internal sealed class UpdateInstaller(AppController controller)
{
    /// <summary>The download folders: %TEMP%\Lumerist-update-&lt;random&gt;.</summary>
    private const string FolderPrefix = "Lumerist-update-";

    /// <summary>
    /// <see cref="WindowsInstaller.UpdatingMutexName"/>, kept from just before the
    /// installer starts for the life of the process (never released by hand).
    /// </summary>
    private static Mutex? updating;

    /// <summary>This PC's processor as the release files name it ("x64", "arm64"), or null.</summary>
    public static string? Architecture => UpdateAssets.ArchitectureName(RuntimeInformation.OSArchitecture);

    /// <summary>
    /// Lumerist runs from the folder the installer manages: the one its
    /// uninstall entry names, or %LOCALAPPDATA%\Programs\Lumerist. A
    /// portable copy (the zip) elsewhere can't update itself.
    /// </summary>
    public static bool IsInstalledCopy()
    {
        if (Environment.ProcessPath is not { } exe) return false;
        string? folder;
        try
        {
            folder = Path.GetDirectoryName(Path.GetFullPath(exe));
        }
        catch (Exception error) when (error is ArgumentException or IOException or NotSupportedException)
        {
            return false;
        }
        return UpdateInstallLocation.IsInstalledCopy(folder,
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), RegisteredLocation());
    }

    private static string? RegisteredLocation()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(UpdateInstallLocation.UninstallKey);
            return key?.GetValue(UpdateInstallLocation.InstallLocationValue) as string;
        }
        catch (Exception error) when (error is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }

    /// <summary>
    /// Downloads and checks the installer (Lumerist.Core's order). On any
    /// failure the folder is removed and <see cref="UpdateFailure"/> says why.
    /// </summary>
    public async Task<PreparedInstaller> PrepareAsync(UpdatePackage package, bool allowLocalFiles,
        IProgress<(UpdateStep Step, DownloadProgress Progress)> progress, CancellationToken cancellationToken)
    {
        DirectoryInfo folder;
        try
        {
            folder = Directory.CreateTempSubdirectory(FolderPrefix);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            throw new UpdateFailure($"Lumerist could not make a folder for the download: {error.Message}", error);
        }
        try
        {
            using var downloader = new UpdateDownloader(allowLocalFiles: allowLocalFiles);
            var preparation = new UpdatePreparation(downloader, new SignedChecksumSource(downloader, package, UpdateSignature.TrustedKeys));
            var installer = await preparation.PrepareAsync(package, folder.FullName, progress, cancellationToken);
            Log.Write($"Update {package.Version}: {package.Installer.Name} checked (SHA-256 {installer.Sha256})");
            return installer;
        }
        catch
        {
            Remove(folder.FullName);
            throw;
        }
    }

    /// <summary>
    /// Runs the checked installer and quits. The file is checked once more
    /// while it is locked, then started by its full path. Throws
    /// <see cref="UpdateFailure"/> (and leaves everything as it was) when it
    /// can't start.
    /// </summary>
    public void LaunchAndQuit(PreparedInstaller installer, string version)
    {
        var folder = Path.GetDirectoryName(installer.Path)!;
        FileStream? locked = null;
        Mutex? mutex = null;
        try
        {
            locked = installer.OpenVerified();
            controller.Settings.PendingUpdate = new PendingUpdate(version, AppController.AppVersion, DateTimeOffset.Now);
            controller.SaveSettings();
            mutex = new Mutex(initiallyOwned: true, WindowsInstaller.UpdatingMutexName, out _);
            using var process = Process.Start(StartInfo(installer.Path, folder))
                ?? throw new UpdateFailure("Windows didn't start the installer.");
            Log.Write($"Update {version}: installer started (process {process.Id}); Lumerist quits");
        }
        catch (Exception error) when (error is UpdateFailure or Win32Exception or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            locked?.Dispose();
            mutex?.Dispose();
            controller.Settings.PendingUpdate = null;
            controller.SaveSettings();
            Remove(folder);
            Log.Write($"Update {version}: installer not started: {error.Message}");
            throw error as UpdateFailure ?? new UpdateFailure(error is Win32Exception { NativeErrorCode: 740 }
                ? "The installer asked for administrator rights, which a Lumerist update never needs."
                : $"Windows didn't start the installer: {error.Message}", error);
        }
        locked.Dispose();
        updating = mutex;
        // The installer checks the single-instance mutex first: let go of it now.
        Program.ReleaseInstance();
        System.Windows.Application.Current?.Shutdown();
    }

    private static ProcessStartInfo StartInfo(string path, string folder)
    {
        // No shell: the file itself runs, from its own folder, and an installer
        // that wanted administrator rights fails instead of asking.
        var start = new ProcessStartInfo(path) { UseShellExecute = false, WorkingDirectory = folder };
        foreach (var argument in WindowsInstaller.Arguments) start.ArgumentList.Add(argument);
        if (InstallerLog() is { } log) start.ArgumentList.Add("/LOG=" + log);
        // Test settings of a debug build never reach the installer or the new version.
        foreach (var name in start.Environment.Keys.Where(name => name.StartsWith("LUMERIST_", StringComparison.OrdinalIgnoreCase)).ToList())
            start.Environment.Remove(name);
        return start;
    }

    /// <summary>The installer's own log, next to Lumerist's (tray → "Open the log folder").</summary>
    private static string? InstallerLog()
    {
        try
        {
            Directory.CreateDirectory(AppPaths.LogFolder);
            return Path.Combine(AppPaths.LogFolder, "update-installer.log");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>At launch: download folders of earlier updates (the installer ran from them), once they are an hour old.</summary>
    public static void RemoveOldDownloads()
    {
        try
        {
            foreach (var folder in new DirectoryInfo(Path.GetTempPath()).EnumerateDirectories(FolderPrefix + "*"))
            {
                if (DateTime.UtcNow - folder.CreationTimeUtc > TimeSpan.FromHours(1)) Remove(folder.FullName);
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            Log.Write($"Old update downloads not removed: {error.Message}");
        }
    }

    private static void Remove(string folder)
    {
        try
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Log.Write($"Update folder not removed: {error.Message}");
        }
    }
}
