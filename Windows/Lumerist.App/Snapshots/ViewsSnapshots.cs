using System.Runtime.CompilerServices;
using System.Windows;
using Lumerist.App.Views;

namespace Lumerist.App.Snapshots;

/// <summary>
/// The main window's screens at full length, header and tabs drawn above
/// them (the Mac's OnboardingSnapshots "themes" renders: main-develop,
/// main-library, main-importWindow, main-mouseTrackpad, main-floatingPanels,
/// main-general, main-develop-commands), plus the Develop keyboard. 960 px wide like the
/// Mac's; the heights leave room for the longest theme.
/// </summary>
internal static class ViewsSnapshots
{
    [ModuleInitializer]
    internal static void Register()
    {
        Add("main-develop", MainSection.Develop, ModuleTab.Sliders, 2000);
        Add("main-library", MainSection.Library, ModuleTab.Sliders, 1150);
        Add("main-importWindow", MainSection.Import, null, 900);
        Add("main-mouseTrackpad", MainSection.MouseTrackpad, null, 1050);
        Add("main-floatingPanels", MainSection.FloatingPanels, null, 2050);
        Add("main-general", MainSection.General, null, 4600);
        Add("main-develop-commands", MainSection.Develop, ModuleTab.Commands, 820);
        Add("main-develop-keyboard", MainSection.Develop, ModuleTab.Keyboard, 980);
    }

    private static void Add(string name, MainSection section, ModuleTab? tab, double height) =>
        SnapshotRegistry.Add(name, 960, height, controller => Build(controller, section, tab));

    private static FrameworkElement Build(AppController controller, MainSection section, ModuleTab? tab)
    {
        // Nothing navigates in a snapshot, and nothing is saved.
        var context = new ScreenContext(controller, (_, _, _) => { }, isSnapshot: true);
        if (tab is { } moduleTab && section.Scope() is { } scope) context.Module(scope).Tab = moduleTab;
        var screen = MainWindow.CreateScreen(context, section);
        screen.Refresh();
        return MainWindowParts.Compose(controller, section, screen.View);
    }
}
