namespace Lumerist.Core;

/// <summary>
/// Values the Windows app shares with the Mac app and the Lightroom plug-in
/// (Shared/contract.json; Scripts/check-parity.py checks them).
/// </summary>
public static class Contract
{
    /// <summary>The plug-in listens here for requests (app → plug-in).</summary>
    public const int RequestPort = 50550;

    /// <summary>The plug-in sends acks, events and results here (plug-in → app).</summary>
    public const int ResponsePort = 50551;

    public const int ProtocolVersion = 3;

    /// <summary>LrToolkitIdentifier of Lumerist.lrplugin.</summary>
    public const string PluginId = "io.github.ziago.lumerist.lightroom";

    public const string PluginBundleName = "Lumerist.lrplugin";

    /// <summary>Folder name under %APPDATA% (settings) and %LOCALAPPDATA% (logs).</summary>
    public const string AppDataFolderName = "Lumerist";

    /// <summary>
    /// Written by the app to %APPDATA%\Lumerist at every start, read by the
    /// plug-in's OpenApp.lua: { "exe": "C:\\…\\Lumerist.exe", "openWithLightroom": true }.
    /// </summary>
    public const string PluginLinkFileName = "plugin-link.json";
}
