using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;
using Lumerist.App.Design;
using Lumerist.App.Input;
using Lumerist.Core;

namespace Lumerist.App.Guide;

/// <summary>
/// The first-run guide (the Mac's OnboardingView), with the steps Windows
/// needs: install the plug-in, open Lightroom Classic, turn on shortcuts and
/// try them. Windows asks for no keyboard or mouse permission, so the Mac's
/// permission step is not here. Each step is checked live.
/// </summary>
internal sealed class GuideView : LiveView
{
    private readonly AppController controller;
    private readonly Action? finish;
    private readonly Action refresh;
    private readonly Action<LightroomAck> ackReceived;
    private DispatcherTimer? lightroomWatch;
    private PluginInstallStatus? pluginStatus;
    private bool lightroomIsRunning;
    /// <summary>Lightroom accepted a change since the guide opened (earlier answers don't count as "it works").</summary>
    private bool sawAdjustment;
    /// <summary>Why the last install failed, shown under the steps until the plug-in is ready.</summary>
    private string? installFailure;
    /// <summary>Why the shortcuts did not start, shown until they run.</summary>
    private string? shortcutsFailure;
    private object? shownState;

    /// <param name="finish">Marks the guide done and closes it; null in renders.</param>
    public GuideView(AppController controller, Action? finish)
    {
        this.controller = controller;
        this.finish = finish;
        refresh = Refresh;
        ackReceived = OnAck;
        ReadState();
        Rebuild();
    }

    protected override void StartListening()
    {
        controller.Changed += refresh;
        controller.AckReceived += ackReceived;
        // Lightroom can open or quit at any time; the plug-in status is read
        // again when the controller reports a change (an install included).
        lightroomWatch = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
        lightroomWatch.Tick += (_, _) =>
        {
            var running = LightroomWindow.IsRunning;
            if (running == lightroomIsRunning) return;
            lightroomIsRunning = running;
            RebuildIfChanged();
        };
        lightroomWatch.Start();
        ReadState();
    }

    protected override void StopListening()
    {
        controller.Changed -= refresh;
        controller.AckReceived -= ackReceived;
        lightroomWatch?.Stop();
        lightroomWatch = null;
    }

    private void ReadState()
    {
        pluginStatus = controller.PluginStatus;
        lightroomIsRunning = LightroomWindow.IsRunning;
        if (PluginIsReady) installFailure = null;
        if (controller.ShortcutsEnabled) shortcutsFailure = null;
    }

    private string? Failure => installFailure ?? shortcutsFailure;

    private void Refresh()
    {
        ReadState();
        RebuildIfChanged();
    }

    private void OnAck(LightroomAck ack)
    {
        if (sawAdjustment || !ack.Accepted || ack.Parameter is null) return;
        sawAdjustment = true;
        RebuildIfChanged();
    }

    /// <summary>Rebuilt only when something on screen changes, so a focused button keeps the focus.</summary>
    private void RebuildIfChanged()
    {
        if (Equals(State(), shownState)) return;
        Rebuild();
    }

    private object State() => (pluginStatus, lightroomIsRunning, controller.IsConnected, controller.ShortcutsEnabled, ItWorks, Failure);

    // Steps --------------------------------------------------------------------------

    private bool PluginIsReady => pluginStatus is PluginInstallStatus.UpToDate or PluginInstallStatus.NewerInstalled;

    private bool ItWorks => controller.ShortcutsEnabled && sawAdjustment;

    /// <summary>The three steps.</summary>
    private bool AllDone => PluginIsReady && controller.IsConnected && controller.ShortcutsEnabled;

    private string PluginDetail => pluginStatus switch
    {
        // No readable plug-in in this build, or its folder could not be read.
        null => controller.PluginSummary,
        PluginInstallStatus.NotInstalled => lightroomIsRunning
            ? "Lightroom is open: quit it, then install the plug-in."
            : "Copies the plug-in into Lightroom's Modules folder. Nothing else on your PC changes.",
        PluginInstallStatus.Outdated(var installed, var bundled) => $"Installed: {installed?.ToString() ?? "unknown"} — this app brings {bundled}.",
        PluginInstallStatus.Modified(var version) => $"Version {version} is installed but its files differ from this app's copy.",
        PluginInstallStatus.UpToDate(var version) => $"Plug-in {version} is installed.",
        PluginInstallStatus.NewerInstalled(var installed, _) => $"A newer plug-in ({installed}) is installed; update the app when you can.",
        _ => controller.PluginSummary,
    };

    private string LightroomDetail
    {
        get
        {
            if (controller.IsConnected) return "Connected: Lumerist and Lightroom are talking (only on this PC).";
            if (!PluginIsReady) return "Install the plug-in first.";
            if (lightroomIsRunning) return "Lightroom loads plug-ins when it starts. If it was already open, restart it once.";
            return "Start Lightroom Classic: the plug-in starts with it and connects by itself.";
        }
    }

    private string TryDetail
    {
        get
        {
            if (ItWorks) return "It works! Every key can be changed in the Develop and Library pages.";
            if (controller.ShortcutsEnabled)
                return "In Lightroom's Develop module, hold Q and turn the mouse wheel: Exposure moves. Hold Shift for bigger steps.";
            return "Shortcuts only act while Lightroom Classic is in front; a quick tap of a key still reaches Lightroom.";
        }
    }

    /// <summary>Lightroom must be closed: the installer says so if it is not.</summary>
    private void InstallPlugin()
    {
        var message = controller.InstallPlugin();
        ReadState();
        installFailure = PluginIsReady ? null : message;
        Rebuild();
    }

    /// <summary>
    /// Windows needs no permission, so this only fails when the keyboard and
    /// mouse hooks cannot start; the controller's status then says why.
    /// </summary>
    private void TurnOnShortcuts()
    {
        controller.ToggleShortcuts();
        shortcutsFailure = controller.ShortcutsEnabled ? null : controller.Status;
        Rebuild();
    }

    protected override UIElement Build()
    {
        shownState = State();
        var page = new StackPanel();
        var header = new StackPanel();
        header.Children.Add(GuideKit.Title("Welcome to Lumerist", 26));
        var subtitle = GuideKit.Text("Three steps, then your keyboard and mouse wheel drive Lightroom Classic's sliders.", 13, nameof(ThemePalette.Steel));
        subtitle.Margin = new Thickness(0, Theme.Space.XS, 0, 0);
        header.Children.Add(subtitle);
        Add(page, header);

        Button? pluginAction = null;
        if (pluginStatus is { NeedsAction: true })
        {
            pluginAction = GuideKit.Button(pluginStatus is PluginInstallStatus.NotInstalled ? "Install plug-in" : "Update plug-in",
                                           GuideButtonKind.Primary, InstallPlugin);
        }
        Add(page, Step(1, "Install the Lightroom plug-in", PluginIsReady, PluginDetail, pluginAction));
        Add(page, Step(2, "Open Lightroom Classic", controller.IsConnected, LightroomDetail, null));
        var shortcutsAction = controller.ShortcutsEnabled
            ? null
            : GuideKit.Button("Turn on shortcuts", GuideButtonKind.Primary, TurnOnShortcuts);
        Add(page, Step(3, "Turn on shortcuts and try it", ItWorks, TryDetail, shortcutsAction));

        if (Failure is { } failure)
        {
            var problem = GuideKit.Text(failure, 12, nameof(ThemePalette.Error));
            AutomationProperties.SetLiveSetting(problem, AutomationLiveSetting.Assertive);
            Add(page, problem);
        }
        Add(page, UpdatesChoice());
        Add(page, Footer());

        // 640 wide with the padding, as on the Mac.
        return new Border
        {
            Width = 640,
            Background = GuideKit.WindowBackground(),
            Padding = new Thickness(Theme.Space.XXL, Theme.Space.XXL, Theme.Space.XXL, Theme.Space.XL),
            Child = page,
        };
    }

    private static void Add(StackPanel page, FrameworkElement element)
    {
        if (page.Children.Count > 0) element.Margin = new Thickness(0, Theme.Space.L, 0, 0);
        page.Children.Add(element);
    }

    /// <summary>On by default: the guide is where the user sees and can untick it.</summary>
    private FrameworkElement UpdatesChoice()
    {
        var text = new StackPanel();
        text.Children.Add(GuideKit.Text("Tell me when a new version is out", 13, nameof(ThemePalette.Ice)));
        var detail = GuideKit.Text("Lumerist asks GitHub for its list of releases when it starts, at most once a day. Nothing about you is sent.",
                                   12, nameof(ThemePalette.Steel));
        detail.TextWrapping = TextWrapping.Wrap;
        text.Children.Add(detail);
        var choice = new CheckBox
        {
            Content = text,
            IsChecked = controller.Settings.CheckForUpdatesAutomatically,
            Style = GuideKit.StyleNamed("Guide.CheckBox"),
        };
        AutomationProperties.SetName(choice, "Tell me when a new version is out");
        choice.Checked += (_, _) => SetChecksForUpdates(true);
        choice.Unchecked += (_, _) => SetChecksForUpdates(false);
        return choice;
    }

    private void SetChecksForUpdates(bool on)
    {
        if (controller.Settings.CheckForUpdatesAutomatically == on) return;
        controller.Settings.CheckForUpdatesAutomatically = on;
        controller.SaveSettings();
    }

    private FrameworkElement Footer()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var skip = GuideKit.Button("Skip for now", GuideButtonKind.Quiet, Finish);
        grid.Children.Add(skip);

        var hint = GuideKit.Label("You can reopen this guide from General or the notification area.", 10);
        hint.Margin = new Thickness(Theme.Space.L, 0, Theme.Space.M, 0);
        hint.HorizontalAlignment = HorizontalAlignment.Right;
        hint.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(hint, 1);
        grid.Children.Add(hint);

        var done = GuideKit.Button(AllDone ? "Start editing" : "Finish later",
                                   AllDone ? GuideButtonKind.Primary : GuideButtonKind.Secondary, Finish);
        Grid.SetColumn(done, 2);
        grid.Children.Add(done);
        return grid;
    }

    private void Finish() => finish?.Invoke();

    private static FrameworkElement Step(int number, string title, bool done, string detail, UIElement? action)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        grid.Children.Add(StepNumber(number, done));

        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(Theme.Space.L, 0, 0, 0) };
        var heading = GuideKit.Text(title, 14, nameof(ThemePalette.Ice), FontWeights.SemiBold);
        // The number is drawn; screen readers hear it with the state.
        AutomationProperties.SetName(heading, $"Step {number}: {title}. {(done ? "Done." : "To do.")}");
        texts.Children.Add(heading);
        var line = GuideKit.Text(detail, 12, nameof(ThemePalette.Steel));
        line.Margin = new Thickness(0, 3, 0, 0);
        texts.Children.Add(line);
        Grid.SetColumn(texts, 1);
        grid.Children.Add(texts);

        var trailing = done ? GuideKit.Pill("Done", PillTone.Good) : action as FrameworkElement;
        if (trailing is not null)
        {
            trailing.Margin = new Thickness(Theme.Space.M, 0, 0, 0);
            trailing.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(trailing, 2);
            grid.Children.Add(trailing);
        }

        return GuideKit.Panel(new Border
        {
            Padding = new Thickness(GuideKit.RowInset, 12, GuideKit.RowInset, 12),
            Child = grid,
        });
    }

    /// <summary>Galerist: a square, ink red when done. Miami: a teal ring, filled when done.</summary>
    private static FrameworkElement StepNumber(int number, bool done)
    {
        var flat = Theme.IsFlat;
        var digit = GuideKit.Text($"{number}", flat ? 15 : 18,
                                  done ? nameof(ThemePalette.OnMagenta) : flat ? nameof(ThemePalette.Ice) : nameof(ThemePalette.Teal),
                                  flat ? FontWeights.Medium : FontWeights.Bold, flat ? "Mono" : "Display");
        digit.HorizontalAlignment = HorizontalAlignment.Center;
        digit.VerticalAlignment = VerticalAlignment.Center;
        var mark = new Border
        {
            Width = 34,
            Height = 34,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(flat ? 0 : 17),
            VerticalAlignment = VerticalAlignment.Center,
            Child = digit,
        };
        var accent = flat ? "Synth.Magenta" : "Synth.Teal";
        mark.SetResourceReference(Border.BorderBrushProperty, done || !flat ? accent : "Synth.Ice");
        if (done) mark.SetResourceReference(Border.BackgroundProperty, accent);
        return mark;
    }
}

/// <summary>
/// The guide's window ("Welcome to Lumerist"). Opens by itself at the first
/// launch, until the user finishes or skips it; reopened from General and
/// the tray menu. Closing it without finishing brings it back next launch.
/// </summary>
internal sealed class GuideWindow : Window
{
    private static GuideWindow? open;

    private readonly AppController controller;

    /// <summary>The guide is on screen: no "Stay in touch" meanwhile.</summary>
    public static bool IsOpen => open is not null;

    public static void ShowOrActivate(AppController controller)
    {
        open ??= new GuideWindow(controller);
        open.Show();
        if (open.WindowState == WindowState.Minimized) open.WindowState = WindowState.Normal;
        open.Activate();
    }

    private GuideWindow(AppController controller)
    {
        this.controller = controller;
        SetResourceReference(StyleProperty, "Synth.Window");
        Title = "Welcome to Lumerist";
        Content = new GuideView(controller, Finish);
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.CanMinimize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        UpdateBackground();
        Theme.Changed += UpdateBackground;
        SourceInitialized += (_, _) => Theme.ApplyTitleBar(this);
        Closed += (_, _) =>
        {
            Theme.Changed -= UpdateBackground;
            open = null;
            // The main window is usually in front now: a chance for "Stay in touch".
            GuideHost.GuideClosed();
        };
    }

    private void UpdateBackground() => Background = GuideKit.WindowBackground();

    private void Finish()
    {
        controller.Settings.GuideCompleted = true;
        controller.SaveSettings();
        Close();
    }
}
