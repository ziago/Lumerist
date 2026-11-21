namespace Lumerist.App.Services;

/// <summary>The main window's preferences.</summary>
internal sealed partial class AppSettings
{
    /// <summary>
    /// The last section viewed ("develop", "library", "mouseTrackpad",
    /// "floatingPanels", "general"): a convenience only, read leniently, so an
    /// unknown value just opens Develop.
    /// </summary>
    public string? MainWindowSection { get; set; }

    /// <summary>The open tab of the Develop page ("sliders", "commands", "keyboard").</summary>
    public string? DevelopTab { get; set; }

    /// <summary>The open tab of the Library page.</summary>
    public string? LibraryTab { get; set; }
}
