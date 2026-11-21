namespace Lumerist.Core;

/// <summary>
/// The two status lines at the top of the tray menu (the Mac's StatusMenu):
/// the link to Lightroom and Lightroom's module.
/// </summary>
public static class StatusMenuText
{
    public static string Link(bool isConnected) => isConnected ? "Lightroom linked" : "Waiting for Lightroom";

    /// <summary>"Develop", "Develop · mask selected", or "Plug-in not connected".</summary>
    public static string Context(bool isConnected, LightroomContext context)
    {
        if (!isConnected) return "Plug-in not connected";
        var module = context.Module.Length == 0 ? "—" : CommandTitles.Capitalized(context.Module);
        return context.IsMasking ? $"{module} · mask selected" : module;
    }
}
