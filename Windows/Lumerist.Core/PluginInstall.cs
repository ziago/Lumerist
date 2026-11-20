using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Lumerist.Core;

/// <summary>Where the Lightroom Classic plug-in lives and where replaced copies go.</summary>
public static class PluginInstallLayout
{
    public const string BundleName = Contract.PluginBundleName;
    public const string BackupPrefix = Contract.PluginBundleName + ".backup-";

    /// <summary>Lightroom Classic's per-user plug-in folder: %APPDATA%\Adobe\Lightroom\Modules.</summary>
    public static string ModulesDirectory(string appData) => Path.Combine(appData, "Adobe", "Lightroom", "Modules");

    /// <summary>
    /// Replaced plug-ins are kept in %APPDATA%\Lumerist\plugin-backups, OUTSIDE
    /// Modules: Lightroom scans that folder, and two plug-ins with the same
    /// identifier disable each other.
    /// </summary>
    public static string BackupDirectory(string appData) => Path.Combine(appData, Contract.AppDataFolderName, "plugin-backups");

    /// <summary>"Lumerist.lrplugin.backup-yyyyMMdd-HHmmss" in the given time zone (local by default).</summary>
    public static string BackupName(DateTimeOffset date, TimeZoneInfo? timeZone = null)
    {
        var local = TimeZoneInfo.ConvertTime(date, timeZone ?? TimeZoneInfo.Local);
        return BackupPrefix + local.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
    }
}

/// <summary>How the installed plug-in compares with the one shipped with the app.</summary>
public abstract record PluginInstallStatus
{
    private PluginInstallStatus() { }

    /// <summary>No Lumerist plug-in in Lightroom's Modules folder.</summary>
    public sealed record NotInstalled() : PluginInstallStatus;

    /// <summary>The installed copy is older (or unreadable): offer "Update".</summary>
    public sealed record Outdated(SemanticVersion? Installed, SemanticVersion Bundled) : PluginInstallStatus;

    /// <summary>Same version but different files (a development build): offer "Reinstall".</summary>
    public sealed record Modified(SemanticVersion Version) : PluginInstallStatus;

    public sealed record UpToDate(SemanticVersion Version) : PluginInstallStatus;

    /// <summary>The installed copy is newer than this app's (an older app was opened).</summary>
    public sealed record NewerInstalled(SemanticVersion Installed, SemanticVersion Bundled) : PluginInstallStatus;

    public bool NeedsAction => this is NotInstalled or Outdated or Modified;

    /// <summary>
    /// The app replaces the installed plug-in by itself whenever Lightroom is
    /// closed: only an older copy of Lumerist's own plug-in. A missing one is
    /// installed by the installer or the user (first-run guide), and a copy
    /// changed by hand or newer is never touched.
    /// </summary>
    public bool UpdatesAutomatically => this is Outdated;

    public static PluginInstallStatus Compare(SemanticVersion? installed, bool installedExists, SemanticVersion bundled, bool sameContents)
    {
        if (!installedExists) return new NotInstalled();
        if (installed is null) return new Outdated(null, bundled);
        if (installed < bundled) return new Outdated(installed, bundled);
        if (bundled < installed) return new NewerInstalled(installed, bundled);
        return sameContents ? new UpToDate(bundled) : new Modified(bundled);
    }
}

/// <summary>
/// Installs, updates and removes the plug-in with plain file operations.
/// Nothing is ever deleted: a replaced or removed copy is moved to the backup
/// folder. Callers must make sure Lightroom Classic is not running.
/// </summary>
/// <param name="Source">The plug-in folder shipped with the app.</param>
/// <param name="ModulesDirectory">%APPDATA%\Adobe\Lightroom\Modules (<see cref="PluginInstallLayout.ModulesDirectory"/>).</param>
/// <param name="BackupDirectory">%APPDATA%\Lumerist\plugin-backups (<see cref="PluginInstallLayout.BackupDirectory"/>).</param>
public sealed record PluginInstallOperation(string Source, string ModulesDirectory, string BackupDirectory)
{
    private const string FolderMetadata = ".DS_Store";

    public string Destination => Path.Combine(ModulesDirectory, PluginInstallLayout.BundleName);

    /// <summary>
    /// True for a real folder and for a symbolic link or junction (even a broken
    /// one, as developers sometimes link a working copy): both occupy the slot.
    /// </summary>
    public bool IsInstalled => Exists(Destination);

    /// <summary>
    /// Checks the entry itself, not what a link points at: a dangling link would
    /// otherwise look absent and then block the move into place.
    /// </summary>
    internal static bool Exists(string path) =>
        File.Exists(path) || Directory.Exists(path) || new FileInfo(path).LinkTarget is not null;

    public SemanticVersion? BundledVersion => VersionOfPlugin(Source);

    public SemanticVersion? InstalledVersion => VersionOfPlugin(Destination);

    public PluginInstallStatus Status()
    {
        var bundled = BundledVersion ?? throw PluginInstallException.BundledPluginUnreadable(Source);
        var installed = IsInstalled;
        return PluginInstallStatus.Compare(
            installed ? InstalledVersion : null,
            installed,
            bundled,
            installed && FingerprintOfPlugin(Destination) == FingerprintOfPlugin(Source));
    }

    /// <summary>
    /// Copies the bundled plug-in into Modules. An existing copy is moved to the
    /// backup folder first; its new location is returned (null when there was none).
    /// </summary>
    public string? Install(DateTimeOffset? now = null)
    {
        if (BundledVersion is null) throw PluginInstallException.BundledPluginUnreadable(Source);
        Directory.CreateDirectory(ModulesDirectory);
        var backup = MoveExistingToBackup(now ?? DateTimeOffset.Now);
        // Copy to a temporary sibling first, then move into place, so Lightroom
        // never sees a half-copied plug-in.
        var staging = Path.Combine(ModulesDirectory, $".{PluginInstallLayout.BundleName}.installing-{Guid.NewGuid():D}");
        try
        {
            CopyDirectory(Source, staging);
            RemoveFolderMetadata(staging);
            Directory.Move(staging, Destination);
        }
        catch
        {
            try { if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            if (backup is not null && !Exists(Destination))
            {
                try
                {
                    MoveEntry(backup, Destination); // put the old copy back
                }
                catch (Exception)
                {
                    // Never leave the user guessing where their plug-in went.
                    throw PluginInstallException.RestoreFailed(backup);
                }
            }
            throw;
        }
        return backup;
    }

    /// <summary>Moves the installed plug-in to the backup folder; returns where it went (null when not installed).</summary>
    public string? Remove(DateTimeOffset? now = null) => MoveExistingToBackup(now ?? DateTimeOffset.Now);

    private string? MoveExistingToBackup(DateTimeOffset now)
    {
        if (!Exists(Destination)) return null;
        Directory.CreateDirectory(BackupDirectory);
        var name = PluginInstallLayout.BackupName(now);
        var target = Path.Combine(BackupDirectory, name);
        for (var suffix = 2; Exists(target); suffix++)
            target = Path.Combine(BackupDirectory, $"{name}-{suffix}");
        MoveEntry(Destination, target);
        return target;
    }

    /// <summary>Moves a folder, a file, or a link itself (never what it points at).</summary>
    private static void MoveEntry(string from, string to)
    {
        if (Directory.Exists(from)) Directory.Move(from, to);
        else File.Move(from, to);
    }

    public static SemanticVersion? VersionOfPlugin(string plugin)
    {
        try
        {
            var info = Path.Combine(plugin, "Info.lua");
            return File.Exists(info) ? SemanticVersion.FromPluginInfo(File.ReadAllText(info, Encoding.UTF8)) : null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// A digest of the plug-in's files (names and contents; .DS_Store folder
    /// metadata ignored), to tell two builds with the same version apart. Names are
    /// relative to the plug-in with "/" separators ("/Info.lua"), as on the Mac.
    /// </summary>
    public static string? FingerprintOfPlugin(string plugin)
    {
        if (!Directory.Exists(plugin)) return null;
        try
        {
            var options = new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = 0, IgnoreInaccessible = false };
            var files = Directory.EnumerateFiles(plugin, "*", options)
                .Where(path => Path.GetFileName(path) != FolderMetadata)
                .Select(path => (Relative: "/" + Path.GetRelativePath(plugin, path).Replace(Path.DirectorySeparatorChar, '/'), Path: path))
                .OrderBy(file => file.Relative, StringComparer.Ordinal)
                .ToList();
            if (files.Count == 0) return null;
            using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            foreach (var (relative, path) in files)
            {
                hasher.AppendData(Encoding.UTF8.GetBytes(relative));
                hasher.AppendData(File.ReadAllBytes(path));
            }
            return Convert.ToHexStringLower(hasher.GetHashAndReset());
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static void CopyDirectory(string from, string to)
    {
        if (!Directory.Exists(from)) throw new DirectoryNotFoundException("Plug-in folder not found: " + from);
        Directory.CreateDirectory(to);
        foreach (var file in Directory.EnumerateFiles(from))
            File.Copy(file, Path.Combine(to, Path.GetFileName(file)));
        foreach (var folder in Directory.EnumerateDirectories(from))
            CopyDirectory(folder, Path.Combine(to, Path.GetFileName(folder)));
    }

    private static void RemoveFolderMetadata(string folder)
    {
        var options = new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = 0 };
        foreach (var file in Directory.EnumerateFiles(folder, FolderMetadata, options).ToList())
        {
            try { File.Delete(file); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}

public enum PluginInstallErrorKind
{
    BundledPluginUnreadable,
    LightroomIsRunning,
    /// <summary>Installing failed and the previous copy could not be put back.</summary>
    RestoreFailed,
}

/// <summary>A plug-in install problem, with the message the app shows.</summary>
public sealed class PluginInstallException : Exception
{
    public PluginInstallErrorKind Kind { get; }

    /// <summary>The bundled plug-in (BundledPluginUnreadable) or the backup (RestoreFailed).</summary>
    public string? FolderPath { get; }

    private PluginInstallException(PluginInstallErrorKind kind, string? path, string message) : base(message)
    {
        Kind = kind;
        FolderPath = path;
    }

    public static PluginInstallException BundledPluginUnreadable(string source) => new(
        PluginInstallErrorKind.BundledPluginUnreadable, source,
        "This copy of Lumerist does not contain a readable Lightroom plug-in. Download Lumerist again from GitHub.");

    public static PluginInstallException LightroomIsRunning() => new(
        PluginInstallErrorKind.LightroomIsRunning, null,
        "Quit Lightroom Classic first: it keeps plug-in code in memory.");

    public static PluginInstallException RestoreFailed(string backup) => new(
        PluginInstallErrorKind.RestoreFailed, backup,
        $"Installing failed and the previous plug-in could not be put back. Your copy is safe in {backup}; move it back to Lightroom's Modules folder or install again.");
}
