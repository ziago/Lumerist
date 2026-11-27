using Lumerist.App.Design.Controls;
using Lumerist.Core;

namespace Lumerist.App.Views;

/// <summary>Top-level sections of the main window (the Mac's MainSection).</summary>
internal enum MainSection
{
    Develop,
    Library,
    /// <summary>Import keys, for culling in Lightroom's Import window.</summary>
    Import,
    MouseTrackpad,
    FloatingPanels,
    General,
}

/// <summary>Tabs inside a module page (Develop or Library).</summary>
internal enum ModuleTab
{
    Sliders,
    Commands,
    Keyboard,
}

internal static class MainSections
{
    public static IReadOnlyList<MainSection> All { get; } =
        [MainSection.Develop, MainSection.Library, MainSection.Import, MainSection.MouseTrackpad, MainSection.FloatingPanels, MainSection.General];

    public static string Title(this MainSection section) => section switch
    {
        MainSection.Develop => "Develop",
        MainSection.Library => "Library",
        MainSection.Import => "Import",
        MainSection.MouseTrackpad => "Mouse & Trackpad",
        MainSection.FloatingPanels => "Floating panels",
        _ => "General",
    };

    /// <summary>Shown beside the tab title in the Galerist theme.</summary>
    public static string Icon(this MainSection section) => section switch
    {
        MainSection.Develop => Icons.Sliders,
        MainSection.Library => Icons.Grid,
        MainSection.Import => Icons.Import,
        MainSection.MouseTrackpad => Icons.Mouse,
        MainSection.FloatingPanels => Icons.Windows,
        _ => Icons.Settings,
    };

    /// <summary>Pages for Lightroom's modules and its Import window left of the divider, settings on the right.</summary>
    public static bool IsModulePage(this MainSection section) =>
        section is MainSection.Develop or MainSection.Library or MainSection.Import;

    public static ParameterScope? Scope(this MainSection section) => section switch
    {
        MainSection.Develop => ParameterScope.Develop,
        MainSection.Library => ParameterScope.Library,
        _ => null,
    };

    /// <summary>"develop", "mouseTrackpad"…: how the section is remembered (and the Mac's snapshot names).</summary>
    public static string WireName(this MainSection section) => section switch
    {
        MainSection.Develop => "develop",
        MainSection.Library => "library",
        MainSection.Import => "importWindow",
        MainSection.MouseTrackpad => "mouseTrackpad",
        MainSection.FloatingPanels => "floatingPanels",
        _ => "general",
    };

    public static MainSection Parse(string? name) => All.FirstOrDefault(section => section.WireName() == name, MainSection.Develop);
}

internal static class ModuleTabs
{
    public static IReadOnlyList<ModuleTab> All { get; } = [ModuleTab.Sliders, ModuleTab.Commands, ModuleTab.Keyboard];

    public static string Title(this ModuleTab tab, ParameterScope scope) => tab switch
    {
        ModuleTab.Sliders => scope == ParameterScope.Develop ? "Sliders" : "Quick Develop",
        ModuleTab.Commands => "Commands",
        _ => "Keyboard",
    };

    public static string WireName(this ModuleTab tab) => tab switch
    {
        ModuleTab.Sliders => "sliders",
        ModuleTab.Commands => "commands",
        _ => "keyboard",
    };

    public static ModuleTab Parse(string? name) => All.FirstOrDefault(tab => tab.WireName() == name, ModuleTab.Sliders);
}

/// <summary>The Keyboard tab's two layers.</summary>
internal enum KeyboardLayer
{
    Plain,
    Combos,
}

/// <summary>
/// What a module page remembers while the window is open (and across a
/// theme change, which rebuilds every view): the open tab, the search, the
/// open groups and the keyboard layer.
/// </summary>
internal sealed class ModulePageState(ParameterScope scope)
{
    public ParameterScope Scope { get; } = scope;

    public ModuleTab Tab { get; set; } = ModuleTab.Sliders;

    public string Search { get; set; } = "";

    /// <summary>Open slider sections (Basic and Quick Develop at first).</summary>
    public HashSet<string> ExpandedSections { get; } = new(SlidersTabText.DefaultExpanded, StringComparer.Ordinal);

    /// <summary>Closed Command Key groups (all open at first).</summary>
    public HashSet<string> CollapsedGroups { get; } = new(StringComparer.Ordinal);

    public KeyboardLayer Layer { get; set; } = KeyboardLayer.Plain;
}
