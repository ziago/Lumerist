using System.Windows;
using System.Windows.Controls;
using Lumerist.App.Design;
using Lumerist.App.Design.Controls;
using Lumerist.Core;

namespace Lumerist.App.Views;

/// <summary>
/// The Develop or Library page (ModulePage.swift): Sliders (Quick Develop
/// in Library), Commands and Keyboard tabs, with one search shared by the
/// first two. The header and the search box stay while the tab's content is
/// rebuilt, so typing is never interrupted.
/// </summary>
internal sealed class ModulePage(ScreenContext context, ParameterScope scope) : Screen(context)
{
    private readonly ModulePageState state = context.Module(scope);
    private readonly ContentControl tabBar = new() { Focusable = false, IsTabStop = false };
    private readonly ContentControl body = new() { Focusable = false, IsTabStop = false };
    private FrameworkElement? searchField;
    private TextBox? searchBox;
    private ModuleTab? shownTab;
    private bool isHeld;
    private bool refreshWhenReleased;

    public ParameterScope Scope => scope;

    public ModulePageState State => state;

    public new ScreenContext Context => base.Context;

    public override void Refresh()
    {
        if (isHeld)
        {
            refreshWhenReleased = true;
            return;
        }
        View.Content ??= Build();
        if (shownTab != state.Tab) BuildTabBar();
        // A search set from elsewhere (a link to this page) shows in the box too.
        if (searchBox is not null && searchBox.Text != state.Search) searchBox.Text = state.Search;
        RefreshBody();
    }

    /// <summary>
    /// Keeps the content as it is while a popover edits it (the step editor);
    /// the rebuild waits for <see cref="Release"/>.
    /// </summary>
    public void Hold() => isHeld = true;

    public void Release()
    {
        isHeld = false;
        if (!refreshWhenReleased) return;
        refreshWhenReleased = false;
        Refresh();
    }

    protected override FrameworkElement Build()
    {
        var description = SynthText.Body(scope == ParameterScope.Develop
                ? "Keep a Dial Key down and its slider follows the wheel, a drag or ← → until you let go. A Nudge Key moves it one step per press; add Shift for the other step."
                : "These keys work like the Quick Develop buttons in Lightroom's Library: each step is added to what every selected photo already has, so their differences stay.",
            Theme.IsFlat ? 14 : 12, color: Ink.Steel);
        if (Theme.IsFlat)
        {
            description.LineHeight = 14 * 1.25 + 3;
            description.MaxWidth = 640;
            description.HorizontalAlignment = HorizontalAlignment.Left;
        }
        searchField = SynthControls.SearchField("Find a slider, key or command", state.Search, text =>
        {
            if (state.Search == text) return;
            state.Search = text;
            RefreshBody();
        }, out var box);
        searchBox = box;
        searchField.Width = Theme.IsFlat ? 300 : 280;
        BuildTabBar();
        return Layout.VStack(Theme.Space.L,
            Surfaces.ScreenHeader(scope == ParameterScope.Develop ? "Develop" : "Library",
                scope == ParameterScope.Develop ? "active photo" : "all selected photos"),
            description,
            tabBar,
            body);
    }

    private void BuildTabBar()
    {
        shownTab = state.Tab;
        var tabs = SynthControls.Segmented(ModuleTabs.All, state.Tab, tab => tab.Title(scope), tab =>
        {
            state.Tab = tab;
            ChordRecorder.Shared.Stop();
            BuildTabBar();
            RefreshBody();
        }, name: "Tabs");
        if (searchField is not null)
        {
            if (searchField.Parent is Panel parent) parent.Children.Remove(searchField);
            // The bar adds its gap to the margin each time.
            searchField.Margin = new Thickness(0);
        }
        tabBar.Content = Layout.Bar(Theme.Space.M, [tabs], state.Tab == ModuleTab.Keyboard ? [] : [searchField]);
    }

    private void RefreshBody()
    {
        body.Content = state.Tab switch
        {
            ModuleTab.Sliders => SlidersTab.Build(this),
            ModuleTab.Commands => CommandsTab.Build(this),
            _ => KeyboardTab.Build(this),
        };
    }

    /// <summary>A click on the drawn keyboard: the slider or Command Key in its tab, or the page where a System Key is set.</summary>
    public void Reveal(KeyboardReveal target)
    {
        switch (target)
        {
            case KeyboardReveal.Slider slider:
                Show(ModuleTab.Sliders, slider.Label);
                break;
            case KeyboardReveal.Command command:
                Show(ModuleTab.Commands, command.Title);
                break;
            case KeyboardReveal.SystemKey system:
                Context.Go(system.OpensFloatingPanels ? MainSection.FloatingPanels : MainSection.General);
                break;
        }
    }

    private void Show(ModuleTab tab, string search)
    {
        state.Tab = tab;
        state.Search = search;
        BuildTabBar();
        // Setting the text also rebuilds the content (TextChanged).
        if (searchBox is not null && searchBox.Text != search) searchBox.Text = search;
        else RefreshBody();
    }
}
