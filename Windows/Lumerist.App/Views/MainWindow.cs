using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Lumerist.App.Design;
using Lumerist.App.Design.Controls;
using Lumerist.Core;

namespace Lumerist.App.Views;

/// <summary>
/// The main window (ContentView.swift): the header row, the section tabs
/// (Develop, Library | Mouse &amp; Trackpad, Floating panels, General) and the
/// current screen, scrolling. A theme change rebuilds everything, as the
/// Mac rebuilds its views; keymap and settings changes rebuild the screen.
/// </summary>
internal sealed class MainWindow : Window
{
    private static MainWindow? open;

    private readonly AppController controller;
    private readonly ScreenContext context;
    private readonly Dictionary<MainSection, Screen> screens = [];
    private readonly ContentControl header = new() { Focusable = false, IsTabStop = false };
    private readonly ContentControl tabs = new() { Focusable = false, IsTabStop = false };
    private readonly ScrollViewer scroll = new()
    {
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        Focusable = false,
    };
    private readonly LeadingColumn screenColumn = new() { MaximumWidth = MainWindowParts.MaximumScreenWidth };
    private readonly Border screenHost = new();
    private readonly SynthBackground background = new();
    private MainSection section;
    private bool refreshPending;
    private DispatcherTimer? retry;

    /// <summary>Shows the one main window, or brings it to the front.</summary>
    public static void ShowOrActivate(AppController controller)
    {
        open ??= new MainWindow(controller);
        open.Show();
        if (open.WindowState == WindowState.Minimized) open.WindowState = WindowState.Normal;
        open.Activate();
    }

    /// <summary>Opens the window on a section (the tray, the guide, a link between pages).</summary>
    public static void ShowSection(AppController controller, MainSection section, ModuleTab? tab = null)
    {
        ShowOrActivate(controller);
        open?.Go(section, tab, null);
    }

    private MainWindow(AppController controller)
    {
        this.controller = controller;
        context = new ScreenContext(controller, Go);
        var settings = controller.Settings;
        section = MainSections.Parse(settings.MainWindowSection);
        context.Module(ParameterScope.Develop).Tab = ModuleTabs.Parse(settings.DevelopTab);
        context.Module(ParameterScope.Library).Tab = ModuleTabs.Parse(settings.LibraryTab);

        Title = "Lumerist";
        Width = 1000;
        Height = 760;
        MinWidth = 860;
        MinHeight = 600;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        SetResourceReference(StyleProperty, "Synth.Window");
        Resources.MergedDictionaries.Add(new ControlStyles());
        SourceInitialized += (_, _) => Theme.ApplyTitleBar(this);

        screenHost.Child = screenColumn;
        scroll.Content = screenHost;
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetRowSpan(background, 3);
        root.Children.Add(background);
        root.Children.Add(header);
        Grid.SetRow(tabs, 1);
        root.Children.Add(tabs);
        Grid.SetRow(scroll, 2);
        root.Children.Add(scroll);
        Content = root;

        Theme.Changed += OnThemeChanged;
        controller.Changed += OnStatusChanged;
        controller.KeymapChanged += RequestRefresh;
        controller.SettingsChanged += RequestRefresh;
        controller.CatalogCheckChanged += RequestRefresh;
        controller.PresetsChanged += RequestRefresh;
        KeymapEditor.Changed += RequestRefresh;
        // No field starts focused: keystrokes must never land in a step field by accident.
        Loaded += (_, _) => Keyboard.ClearFocus();
        Closed += OnClosed;
        RebuildAll();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        Theme.Changed -= OnThemeChanged;
        controller.Changed -= OnStatusChanged;
        controller.KeymapChanged -= RequestRefresh;
        controller.SettingsChanged -= RequestRefresh;
        controller.CatalogCheckChanged -= RequestRefresh;
        controller.PresetsChanged -= RequestRefresh;
        KeymapEditor.Changed -= RequestRefresh;
        if (screens.TryGetValue(section, out var screen)) screen.Leave();
        ChordRecorder.Shared.Stop();
        retry?.Stop();
        // Where the user was, for next time (a convenience only).
        var settings = controller.Settings;
        settings.MainWindowSection = section.WireName();
        settings.DevelopTab = context.Module(ParameterScope.Develop).Tab.WireName();
        settings.LibraryTab = context.Module(ParameterScope.Library).Tab.WireName();
        controller.SaveSettings();
        open = null;
    }

    private void OnThemeChanged()
    {
        Theme.ApplyTitleBar(this);
        background.InvalidateVisual();
        RebuildAll();
    }

    /// <summary>Header and tabs follow the link and shortcut state; General shows the plug-in too.</summary>
    private void OnStatusChanged()
    {
        header.Content = MainWindowParts.Header(controller);
        if (screens.TryGetValue(section, out var screen) && screen.ShowsStatus) RequestRefresh();
    }

    /// <summary>Every view again, in the theme now in effect (screens keep their remembered state).</summary>
    private void RebuildAll()
    {
        ChordRecorder.Shared.Stop();
        if (screens.TryGetValue(section, out var current)) current.Leave();
        screens.Clear();
        header.Content = MainWindowParts.Header(controller);
        ShowSection(section, keepOffset: true);
    }

    private void Go(MainSection target, ModuleTab? tab, string? search)
    {
        if (target.Scope() is { } scope)
        {
            var module = context.Module(scope);
            if (tab is { } newTab) module.Tab = newTab;
            if (search is not null) module.Search = search;
        }
        if (target == section && screens.TryGetValue(section, out var same))
        {
            same.Refresh();
            return;
        }
        ShowSection(target, keepOffset: false);
    }

    private void ShowSection(MainSection target, bool keepOffset)
    {
        if (target != section && screens.TryGetValue(section, out var previous)) previous.Leave();
        ChordRecorder.Shared.Stop();
        section = target;
        tabs.Content = MainWindowParts.SectionTabs(section, selected => Go(selected, null, null));
        if (!screens.TryGetValue(section, out var screen))
        {
            screen = CreateScreen(context, section);
            screens[section] = screen;
        }
        screen.Refresh();
        screenHost.Padding = MainWindowParts.ScreenPadding;
        screenColumn.Child = screen.View;
        if (!keepOffset) scroll.ScrollToTop();
    }

    /// <summary>The screen for a section; also used by the snapshots.</summary>
    public static Screen CreateScreen(ScreenContext context, MainSection section) => section switch
    {
        MainSection.Develop => new ModulePage(context, ParameterScope.Develop),
        MainSection.Library => new ModulePage(context, ParameterScope.Library),
        MainSection.MouseTrackpad => new MouseTrackpadScreen(context),
        MainSection.FloatingPanels => new FloatingPanelsScreen(context),
        _ => new GeneralScreen(context),
    };

    /// <summary>
    /// Rebuilds the current screen once, after the events of this moment.
    /// While a drag holds the mouse (a slider), it waits: a rebuild would
    /// take the control away from under the pointer.
    /// </summary>
    private void RequestRefresh()
    {
        if (refreshPending) return;
        refreshPending = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, RefreshNow);
    }

    private void RefreshNow()
    {
        if (Mouse.Captured is not null && Mouse.LeftButton == MouseButtonState.Pressed)
        {
            retry ??= new DispatcherTimer(TimeSpan.FromMilliseconds(150), DispatcherPriority.Background, (_, _) =>
            {
                retry?.Stop();
                RefreshNow();
            }, Dispatcher);
            retry.Start();
            return;
        }
        refreshPending = false;
        if (!screens.TryGetValue(section, out var screen)) return;
        var offset = scroll.VerticalOffset;
        screen.Refresh();
        // The new content is laid out first; then the page stays where it was.
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => scroll.ScrollToVerticalOffset(offset));
    }
}
