using Lumerist.Core;

namespace Lumerist.App.Services;

/// <summary>Where the Windows app keeps its files.</summary>
internal static class AppPaths
{
    /// <summary>%APPDATA% (Roaming): the plug-in looks here for plugin-link.json.</summary>
    public static string RoamingAppData => Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

    /// <summary>%APPDATA%\Lumerist: keymap.json, settings.json, plugin-link.json, plugin-backups.</summary>
    public static string DataFolder => Path.Combine(RoamingAppData, Contract.AppDataFolderName);

    public static string KeymapFile => Path.Combine(DataFolder, "keymap.json");

    public static string SettingsFile => Path.Combine(DataFolder, "settings.json");

    /// <summary>%LOCALAPPDATA%\Lumerist\Logs (the Mac's ~/Library/Logs/Lumerist).</summary>
    public static string LogFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Contract.AppDataFolderName, "Logs");

    /// <summary>The folder Lumerist.exe runs from; the plug-in ships next to it.</summary>
    public static string AppFolder => AppContext.BaseDirectory;
}
