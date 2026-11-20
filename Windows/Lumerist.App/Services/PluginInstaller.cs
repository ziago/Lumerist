using Lumerist.App.Input;
using Lumerist.Core;

namespace Lumerist.App.Services;

/// <summary>
/// Installs and updates the Lightroom Classic plug-in that ships next to
/// Lumerist.exe, into %APPDATA%\Adobe\Lightroom\Modules (PluginInstaller.swift).
/// Lightroom keeps plug-in code in memory, so files are replaced only while
/// it is closed. Backups go to %APPDATA%\Lumerist\plugin-backups, never into Modules.
/// </summary>
internal sealed class PluginInstaller
{
    private readonly PluginInstallOperation? operation;

    public PluginInstaller()
    {
        var appData = AppPaths.RoamingAppData;
        // LUMERIST_* overrides exist for tests and development only; a
        // release build ignores them, so it can only touch the real folders.
#if DEBUG
        var modulesOverride = Environment.GetEnvironmentVariable("LUMERIST_MODULES_DIR");
        var backupOverride = Environment.GetEnvironmentVariable("LUMERIST_BACKUP_DIR");
        var sourceOverride = Environment.GetEnvironmentVariable("LUMERIST_PLUGIN_SOURCE");
#else
        string? modulesOverride = null, backupOverride = null, sourceOverride = null;
#endif
        var modules = modulesOverride ?? PluginInstallLayout.ModulesDirectory(appData);
        var backups = backupOverride
            ?? (modulesOverride is not null
                ? Path.Combine(Path.GetDirectoryName(Path.GetFullPath(modules))!, "Lumerist-plugin-backups")
                : PluginInstallLayout.BackupDirectory(appData));
        var source = sourceOverride ?? Path.Combine(AppPaths.AppFolder, PluginInstallLayout.BundleName);

        if (File.Exists(Path.Combine(source, "Info.lua")))
        {
            operation = new PluginInstallOperation(source, modules, backups);
        }
        else
        {
            BundleProblem = "This build does not include the Lightroom plug-in. Download Lumerist from GitHub, or build it with dotnet build Windows.";
        }
    }

    /// <summary>Set when this build has no readable bundled plug-in.</summary>
    public string? BundleProblem { get; }

    public SemanticVersion? BundledVersion => operation?.BundledVersion;

    public SemanticVersion? InstalledVersion => operation?.InstalledVersion;

    /// <summary>Null when there is nothing to compare (no bundled plug-in, unreadable folder).</summary>
    public PluginInstallStatus? Status()
    {
        try
        {
            return operation?.Status();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or PluginInstallException)
        {
            Log.Write($"Plug-in status unavailable: {error.Message}");
            return null;
        }
    }

    /// <summary>What <see cref="InstallFromCommandLine"/> tells the installer (its exit code).</summary>
    public enum CommandLineOutcome
    {
        /// <summary>Installed, updated, or nothing to do.</summary>
        Done = 0,
        Failed = 1,
        /// <summary>Lightroom Classic is open: the installer asks the user to close it.</summary>
        LightroomIsRunning = 2,
    }

    /// <summary>
    /// <c>Lumerist.exe --install-plugin</c>, run by the Windows installer after
    /// it copied the app: installs the plug-in when it is missing, older, or
    /// a different build of the same version, and leaves an up-to-date or
    /// newer copy alone. The previous copy goes to the backup folder.
    /// </summary>
    public CommandLineOutcome InstallFromCommandLine()
    {
        if (operation is null) return CommandLineOutcome.Failed;
        if (Status() is not { NeedsAction: true }) return CommandLineOutcome.Done;
        if (LightroomWindow.IsRunning) return CommandLineOutcome.LightroomIsRunning;
        Install();
        return Status() is { NeedsAction: false } ? CommandLineOutcome.Done : CommandLineOutcome.Failed;
    }

    /// <summary>Installs or updates; returns the message for the user. Lightroom must be closed.</summary>
    public string Install()
    {
        if (operation is null) return BundleProblem ?? "The plug-in is not available.";
        if (LightroomWindow.IsRunning) return PluginInstallException.LightroomIsRunning().Message;
        try
        {
            var backup = operation.Install();
            var message = backup is null
                ? "Plug-in installed. Open Lightroom Classic: Lumerist connects by itself."
                : "Plug-in updated; the previous copy was kept in Lumerist's backup folder.";
            Log.Write($"Plug-in: {message}");
            return message;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or PluginInstallException)
        {
            Log.Write($"Plug-in operation failed: {error.Message}");
            return error.Message;
        }
    }
}
