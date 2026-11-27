using System.Diagnostics;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media.Effects;
using Lumerist.App.Design;
using Lumerist.App.Input;
using Lumerist.App.Services;
using Lumerist.Core;

namespace Lumerist.App.Guide;

/// <summary>
/// One showing of the update window: the version found, its highlights, and
/// what "Update now" is doing. UI thread only.
/// </summary>
internal sealed class UpdateModel
{
    public abstract record Phase
    {
        private Phase() { }

        /// <summary>"Lumerist X is out": Update now, Release page, Later, Skip this version.</summary>
        public sealed record Offer() : Phase;

        public sealed record Working(UpdateStep Step, DownloadProgress Progress) : Phase;

        /// <summary>The installer is checked and starting; Lumerist quits next.</summary>
        public sealed record Launching() : Phase;

        /// <param name="Message">The headline: "This release can't be installed automatically." or why the last update didn't finish.</param>
        /// <param name="Detail">Why, in a short sentence.</param>
        public sealed record Failed(string Message, string? Detail) : Phase;

        /// <summary>A portable copy (the zip): only the release page.</summary>
        public sealed record NotInstalled() : Phase;

        /// <summary>After an update with Lightroom open: its plug-in is still the old one.</summary>
        public sealed record PluginWaits() : Phase;

        public sealed record QuittingLightroom() : Phase;

        public sealed record Done(string Message) : Phase;
    }

    public const string CantInstall = "This release can't be installed automatically.";
    public const string NotInstalledMessage = "This copy of Lumerist isn't installed; download the update from the release page.";

    /// <summary>How long "Quit Lightroom &amp; update" waits for Lightroom (it may back up its catalog first).</summary>
    private static readonly TimeSpan LightroomQuitWait = TimeSpan.FromMinutes(2);

    private readonly AppController? controller;
    private readonly UpdateChecker? checker;
    private readonly UpdateRelease? release;
    private readonly bool isPreview;
    private CancellationTokenSource? work;

    /// <summary>"Later" or "Skip this version" is already saved: closing adds nothing.</summary>
    private bool decided;

    /// <summary>The automatic check or "Check for updates" found a newer version.</summary>
    public UpdateModel(AppController controller, UpdateChecker checker, UpdateCheckState.Available available)
    {
        this.controller = controller;
        this.checker = checker;
        release = available.Release;
        Version = available.Version;
        Current = UpdateChecker.CurrentVersion;
        Page = UpdateCheck.IsTrustedReleasePage(available.Page) ? available.Page : SupportLinks.Releases;
        Highlights = ReleaseHighlights.Parse(release?.Body);
    }

    /// <summary>After "Update now": the new version runs, or the last update didn't finish.</summary>
    private UpdateModel(AppController controller, UpdateChecker checker, string version, Phase phase)
    {
        this.controller = controller;
        this.checker = checker;
        Version = version;
        Current = AppController.AppVersion;
        Page = SupportLinks.Releases;
        Highlights = [];
        State = phase;
        IsAfterUpdate = true;
    }

    /// <summary>Renders only: a window in a given state; it never downloads or saves.</summary>
    private UpdateModel(Phase phase, string version, string current, IReadOnlyList<string> highlights, bool afterUpdate)
    {
        State = phase;
        Version = version;
        Current = current;
        Highlights = highlights;
        Page = SupportLinks.Releases;
        IsAfterUpdate = afterUpdate;
        isPreview = true;
    }

    public static UpdateModel Preview(Phase phase, IReadOnlyList<string>? highlights = null, bool afterUpdate = false) =>
        new(phase, "0.9.3", "0.9.2", highlights ?? [], afterUpdate);

    /// <summary>The new version runs but Lightroom still has the old plug-in.</summary>
    public static UpdateModel PluginWaiting(AppController controller, UpdateChecker checker, string version) =>
        new(controller, checker, version, new Phase.PluginWaits());

    /// <summary>The installer started but this is still the old version.</summary>
    public static UpdateModel DidNotFinish(AppController controller, UpdateChecker checker, string version) =>
        new(controller, checker, version, new Phase.Failed($"The update to Lumerist {version} didn't finish.",
            $"Lumerist {AppController.AppVersion} is still installed. The installer's log, update-installer.log, is in Lumerist's log folder."));

    public string Version { get; }

    /// <summary>The version this copy compares with (a debug build may pretend).</summary>
    public string Current { get; }

    public Uri Page { get; }

    /// <summary>Plain-text bullets, at most five; empty means <see cref="ReleaseHighlights.Fallback"/>.</summary>
    public IReadOnlyList<string> Highlights { get; }

    /// <summary>Shown by the new version after "Update now" (its title says "installed").</summary>
    public bool IsAfterUpdate { get; }

    public Phase State { get; private set; } = new Phase.Offer();

    public string Title => IsAfterUpdate && State is not Phase.Failed ? $"Lumerist {Version} is installed" : $"Lumerist {Version} is out";

    public bool IsBusy => State is Phase.Working or Phase.Launching or Phase.QuittingLightroom;

    /// <summary>Raised on the UI thread when <see cref="State"/> changed.</summary>
    public event Action? Changed;

    /// <summary>A choice that closes the window.</summary>
    public event Action? CloseRequested;

    // Choices ------------------------------------------------------------------------

    /// <summary>
    /// Only the installed copy; the files the release lists for this PC, the
    /// signed checksums, the download and its checks (Lumerist.Core), then
    /// the installer and quit. Any failure runs nothing and says why.
    /// </summary>
    public async void UpdateNow()
    {
        if (isPreview || controller is null || IsBusy || release is null) return;
        if (!UpdateInstaller.IsInstalledCopy())
        {
            Log.Write("Update: this copy is not the installed one");
            SetState(new Phase.NotInstalled());
            return;
        }
        var allowLocal = UpdateChecker.TestFeed.AllowsLocalFiles;
        var choice = UpdateAssets.Choose(release, Current, UpdateInstaller.Architecture, allowLocal);
        if (choice is not UpdateChoice.Ready(var package))
        {
            var reason = ((UpdateChoice.Refused)choice).Reason;
            Log.Write($"Update {Version}: refused ({reason})");
            SetState(new Phase.Failed(CantInstall, Explain(reason)));
            return;
        }
        work = new CancellationTokenSource();
        SetState(new Phase.Working(UpdateStep.Checking, default));
        var installer = new UpdateInstaller(controller);
        try
        {
            // Progress lands on the UI thread (it was created here).
            var progress = new Progress<(UpdateStep Step, DownloadProgress Progress)>(step =>
            {
                if (State is Phase.Working) SetState(new Phase.Working(step.Step, step.Progress));
            });
            var prepared = await installer.PrepareAsync(package, allowLocal, progress, work.Token);
            SetState(new Phase.Launching());
            installer.LaunchAndQuit(prepared, package.Version);
        }
        catch (OperationCanceledException)
        {
            Log.Write($"Update {Version}: cancelled");
            if (State is Phase.Working) SetState(new Phase.Offer());
        }
        catch (UpdateFailure failure)
        {
            Log.Write($"Update {Version}: {failure.Message}");
            SetState(new Phase.Failed(CantInstall, failure.Message));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException
                                      or System.Security.Cryptography.CryptographicException)
        {
            // Not one of the expected failures, but nothing ran either.
            Log.Write($"Update {Version}: {error}");
            SetState(new Phase.Failed(CantInstall, error.Message));
        }
        finally
        {
            work?.Dispose();
            work = null;
        }
    }

    private static string Explain(UpdateRefusal reason) => reason switch
    {
        UpdateRefusal.NotNewer => "This copy's version isn't one an update can replace (a development build).",
        UpdateRefusal.BadTag => "Its version isn't written the way Lumerist reads it.",
        UpdateRefusal.UnsupportedArchitecture => "There is no installer for this PC's processor.",
        UpdateRefusal.MissingFile => "Its installer for this PC, or its signed checksums, are missing.",
        _ => "Its files aren't the ones Lumerist expects.",
    };

    /// <summary>Stops a download (the window stays, back on the offer).</summary>
    public void Cancel() => work?.Cancel();

    /// <summary>Opens the release page in the browser; like "Later", the window waits a day before it opens by itself again.</summary>
    public void OpenReleasePage()
    {
        if (isPreview) return;
        RememberLater();
        GuideKit.Open(Page);
        CloseRequested?.Invoke();
    }

    /// <summary>Not by itself again for 24 hours ("Later", "Release page", closing the window).</summary>
    public void Later()
    {
        if (isPreview) return;
        RememberLater();
        CloseRequested?.Invoke();
    }

    public void SkipThisVersion()
    {
        if (isPreview) return;
        checker?.Skip(Version);
        decided = true;
        CloseRequested?.Invoke();
    }

    private void RememberLater()
    {
        // The window after an update is about the plug-in, not about a new version.
        if (decided || IsAfterUpdate) return;
        checker?.Later();
        decided = true;
    }

    public void Close() => CloseRequested?.Invoke();

    /// <summary>
    /// After an update with Lightroom open: asks Lightroom Classic to quit
    /// (as its close box does), waits for it, updates the plug-in, then opens
    /// Lightroom again. Lightroom may ask about a catalog backup first.
    /// </summary>
    public async void QuitLightroomAndUpdate()
    {
        if (isPreview || controller is null || IsBusy) return;
        SetState(new Phase.QuittingLightroom());
        var lightroom = LightroomWindow.ClassicProcesses();
        var path = lightroom.Count > 0 ? lightroom[0].Path : null;
        try
        {
            foreach (var (process, _) in lightroom) process.CloseMainWindow();
        }
        catch (InvalidOperationException)
        {
            // It quit meanwhile.
        }
        finally
        {
            foreach (var (process, _) in lightroom) process.Dispose();
        }
        var waited = Stopwatch.StartNew();
        while (LightroomWindow.IsRunning && waited.Elapsed < LightroomQuitWait) await Task.Delay(500);
        if (LightroomWindow.IsRunning)
        {
            SetState(new Phase.Failed("Lightroom Classic is still open.",
                "Quit it yourself: Lumerist updates the plug-in as soon as it closes."));
            return;
        }
        // Lumerist may have updated it already, the moment Lightroom quit.
        var message = controller.PluginNeedsAction ? controller.InstallPlugin() : "Plug-in updated.";
        Log.Write($"After the update: {message}");
        if (controller.PluginNeedsAction)
        {
            SetState(new Phase.Failed("The plug-in wasn't updated.", message));
            return;
        }
        if (path is not null) OpenLightroom(path);
        SetState(new Phase.Done(path is null ? "Plug-in updated. Open Lightroom Classic: Lumerist connects by itself."
            : "Plug-in updated. Lightroom Classic is starting again."));
    }

    private static void OpenLightroom(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            Log.Write($"Lightroom Classic not opened again: {error.Message}");
        }
    }

    /// <summary>
    /// The window closed, however it closed. A download stops; closing
    /// without a choice counts as "Later".
    /// </summary>
    public void WindowClosed()
    {
        work?.Cancel();
        if (!isPreview) RememberLater();
    }

    private void SetState(Phase phase)
    {
        State = phase;
        Changed?.Invoke();
    }
}

/// <summary>
/// The update window's content (the Mac's update sheet), in the theme in
/// effect. Release text is shown as plain text only.
/// </summary>
internal sealed class UpdateView : LiveView
{
    public const double ContentWidth = 500;

    private readonly UpdateModel model;
    private readonly Action changed;

    public UpdateView(UpdateModel model)
    {
        this.model = model;
        changed = Rebuild;
        Rebuild();
    }

    protected override void StartListening() => model.Changed += changed;

    protected override void StopListening() => model.Changed -= changed;

    protected override UIElement Build()
    {
        var stack = new StackPanel();
        stack.Children.Add(Header());
        foreach (var part in Body()) stack.Children.Add(part);
        var root = new Border { Width = ContentWidth, Padding = new Thickness(28, 26, 28, 24), Child = stack };
        root.SetResourceReference(Border.BackgroundProperty, Theme.IsFlat ? "Synth.Panel" : "Synth.Abyss");
        return root;
    }

    private FrameworkElement Header()
    {
        var done = model.State is UpdateModel.Phase.Done;
        var badge = new Border
        {
            Width = 40,
            Height = 40,
            BorderThickness = new Thickness(1),
            Child = GuideKit.Icon(done ? GuideIcon.Check : GuideIcon.ArrowDownCircle, 16, nameof(ThemePalette.Teal)),
            VerticalAlignment = VerticalAlignment.Center,
        };
        ((FrameworkElement)badge.Child).HorizontalAlignment = HorizontalAlignment.Center;
        if (Theme.IsFlat)
        {
            badge.SetResourceReference(Border.BorderBrushProperty, "Synth.GridLine");
        }
        else
        {
            badge.CornerRadius = new CornerRadius(20);
            badge.BorderBrush = GuideKit.Translucent(nameof(ThemePalette.Teal), 0.8);
            badge.Effect = new DropShadowEffect { Color = Theme.ColorOf(nameof(ThemePalette.Teal)), BlurRadius = 16, ShadowDepth = 0, Opacity = 0.35 };
        }
        var title = GuideKit.Title(model.Title, Theme.IsFlat ? 24 : 20);
        AutomationProperties.SetHeadingLevel(title, AutomationHeadingLevel.Level1);
        var texts = new StackPanel { Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Children = { title } };
        if (!model.IsAfterUpdate)
        {
            var current = GuideKit.Text($"You have {model.Current}.", 13, nameof(ThemePalette.Steel));
            current.Margin = new Thickness(0, 2, 0, 0);
            texts.Children.Add(current);
        }
        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 18),
            Children = { badge, texts },
        };
    }

    private IEnumerable<UIElement> Body()
    {
        switch (model.State)
        {
            case UpdateModel.Phase.Offer:
                yield return WhatsNew();
                yield return Skip();
                yield return Actions(
                    (GuideKit.Button("Later", GuideButtonKind.Secondary, model.Later), Role.Cancel),
                    (GuideKit.Button("Release page", GuideButtonKind.Secondary, model.OpenReleasePage), Role.None),
                    (GuideKit.Button("Update now", GuideButtonKind.Primary, model.UpdateNow), Role.Default));
                break;
            case UpdateModel.Phase.Working(var step, var progress):
                yield return Status(step switch
                {
                    UpdateStep.Checking => "Checking the release's signature…",
                    UpdateStep.Downloading => $"Downloading Lumerist {model.Version}…",
                    _ => "Checking the download…",
                }, spinner: step != UpdateStep.Downloading);
                if (step == UpdateStep.Downloading) yield return Progress(progress);
                yield return Actions((GuideKit.Button("Cancel", GuideButtonKind.Secondary, model.Cancel), Role.Cancel));
                break;
            case UpdateModel.Phase.Launching:
                yield return Status("Starting the installer. Lumerist closes and opens again when it's done.", spinner: true);
                break;
            case UpdateModel.Phase.Failed(var message, var detail):
                yield return Problem(message, detail);
                yield return Actions(
                    (GuideKit.Button("Close", GuideButtonKind.Secondary, model.Close), Role.Cancel),
                    (GuideKit.Button("Release page", GuideButtonKind.Primary, model.OpenReleasePage), Role.Default));
                break;
            case UpdateModel.Phase.NotInstalled:
                yield return Problem(UpdateModel.NotInstalledMessage, null);
                yield return Actions(
                    (GuideKit.Button("Close", GuideButtonKind.Secondary, model.Close), Role.Cancel),
                    (GuideKit.Button("Release page", GuideButtonKind.Primary, model.OpenReleasePage), Role.Default));
                break;
            case UpdateModel.Phase.PluginWaits:
                yield return Paragraph("Lightroom Classic still runs the old plug-in. Quit Lightroom to finish: Lumerist updates the plug-in, then opens Lightroom again.");
                yield return Actions(
                    (GuideKit.Button("Later", GuideButtonKind.Secondary, model.Later), Role.Cancel),
                    (GuideKit.Button("Quit Lightroom & update", GuideButtonKind.Primary, model.QuitLightroomAndUpdate), Role.Default));
                break;
            case UpdateModel.Phase.QuittingLightroom:
                yield return Status("Waiting for Lightroom Classic to quit…", spinner: true);
                break;
            case UpdateModel.Phase.Done(var message):
                yield return Paragraph(message);
                yield return Actions((GuideKit.Button("Done", GuideButtonKind.Primary, model.Close), Role.Both));
                break;
        }
    }

    // Parts ------------------------------------------------------------------------

    /// <summary>The highlights as plain lines with a bullet, or the fallback.</summary>
    private FrameworkElement WhatsNew()
    {
        var list = new StackPanel();
        var label = GuideKit.Label("What's new", 10.5);
        label.Margin = new Thickness(0, 0, 0, Theme.Space.S);
        list.Children.Add(label);
        if (model.Highlights.Count == 0)
        {
            list.Children.Add(GuideKit.Text(ReleaseHighlights.Fallback, 13.5));
            return list;
        }
        foreach (var line in model.Highlights)
        {
            var dot = GuideKit.Text("•", 13.5, nameof(ThemePalette.Teal));
            dot.Margin = new Thickness(0, 0, 8, 0);
            dot.VerticalAlignment = VerticalAlignment.Top;
            // Text only: a TextBlock's Text never becomes a link or markup.
            var text = GuideKit.Text(line, 13.5);
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 6), Children = { dot, text } };
            DockPanel.SetDock(dot, Dock.Left);
            list.Children.Add(row);
        }
        return list;
    }

    private FrameworkElement Skip()
    {
        var skip = GuideKit.Button("Skip this version", GuideButtonKind.Quiet, model.SkipThisVersion);
        skip.Padding = new Thickness(0);
        skip.FontSize = Theme.IsFlat ? 10.5 : 11;
        skip.Margin = new Thickness(0, 14, 0, 0);
        skip.ToolTip = $"Lumerist won't open this window for {model.Version} again. A newer version still shows.";
        return skip;
    }

    private static TextBlock Paragraph(string text)
    {
        var paragraph = GuideKit.Text(text, 13.5);
        paragraph.Margin = new Thickness(0, 0, 0, 4);
        return paragraph;
    }

    private static FrameworkElement Status(string text, bool spinner)
    {
        var line = GuideKit.Text(text, 13.5);
        line.VerticalAlignment = VerticalAlignment.Center;
        AutomationProperties.SetLiveSetting(line, AutomationLiveSetting.Polite);
        if (!spinner) return line;
        var arc = GuideKit.Spinner();
        arc.Margin = new Thickness(0, 0, 10, 0);
        var row = new DockPanel { Children = { arc, line } };
        DockPanel.SetDock(arc, Dock.Left);
        return row;
    }

    /// <summary>A bar in the accent colour, and "34.2 MB of 71.0 MB".</summary>
    private static FrameworkElement Progress(DownloadProgress progress)
    {
        var fraction = progress.Total is > 0 and var total ? Math.Clamp((double)progress.Received / total, 0, 1) : 0;
        var bar = new Grid { Height = 6, Margin = new Thickness(0, 12, 0, 6) };
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(fraction, GridUnitType.Star) });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1 - fraction, GridUnitType.Star) });
        var track = new Border();
        track.SetResourceReference(Border.BackgroundProperty, "Synth.GridLine");
        track.SetResourceReference(Border.CornerRadiusProperty, "Synth.Radius.Row");
        Grid.SetColumnSpan(track, 2);
        var fill = new Border();
        fill.SetResourceReference(Border.BackgroundProperty, "Synth.Teal");
        fill.SetResourceReference(Border.CornerRadiusProperty, "Synth.Radius.Row");
        bar.Children.Add(track);
        bar.Children.Add(fill);
        AutomationProperties.SetName(bar, $"{Math.Round(fraction * 100)} percent downloaded");
        static string Megabytes(long bytes) => (bytes / 1048576.0).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
        var amount = GuideKit.Text(progress.Total is { } size ? $"{Megabytes(progress.Received)} MB of {Megabytes(size)} MB" : $"{Megabytes(progress.Received)} MB",
            12, nameof(ThemePalette.Steel), font: "Mono");
        return new StackPanel { Children = { bar, amount } };
    }

    private static FrameworkElement Problem(string message, string? detail)
    {
        var icon = GuideKit.Icon(GuideIcon.ExclamationCircle, 14, nameof(ThemePalette.Error));
        icon.VerticalAlignment = VerticalAlignment.Top;
        icon.Margin = new Thickness(0, 2, 8, 0);
        var text = GuideKit.Text(message, 13.5, nameof(ThemePalette.Ice), FontWeights.SemiBold);
        AutomationProperties.SetLiveSetting(text, AutomationLiveSetting.Assertive);
        var headline = new DockPanel { Children = { icon, text } };
        DockPanel.SetDock(icon, Dock.Left);
        var stack = new StackPanel { Children = { headline } };
        if (detail is not null)
        {
            var reason = GuideKit.Text(detail, 12.5, nameof(ThemePalette.Steel));
            reason.Margin = new Thickness(22, 6, 0, 0);
            stack.Children.Add(reason);
        }
        return stack;
    }

    private enum Role
    {
        None,
        /// <summary>Enter.</summary>
        Default,
        /// <summary>Esc.</summary>
        Cancel,
        Both,
    }

    /// <summary>Buttons at the right, 8 apart; one Enter and one Esc button.</summary>
    private static FrameworkElement Actions(params (Button Button, Role Role)[] buttons)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 22, 0, 0) };
        foreach (var (button, role) in buttons)
        {
            button.IsDefault = role is Role.Default or Role.Both;
            button.IsCancel = role is Role.Cancel or Role.Both;
            if (row.Children.Count > 0) button.Margin = new Thickness(Theme.Space.S, 0, 0, 0);
            row.Children.Add(button);
        }
        return row;
    }
}

/// <summary>
/// The update window. It opens in front of every window (Lightroom too)
/// without taking the keyboard from it when it opened by itself, and stays
/// in front until the user clicks or types in it. It has a taskbar button,
/// so it can't get lost behind Lightroom, and Lumerist doesn't quit with
/// Lightroom while it is open.
/// </summary>
internal sealed class UpdateWindow : Window
{
    private static UpdateWindow? open;

    private readonly UpdateModel model;

    public static bool IsOpen => open is not null;

    /// <param name="activate">Take the keyboard (the user asked, by hand).</param>
    public static void Present(UpdateModel model, bool activate)
    {
        if (open is { } window)
        {
            window.Topmost = true;
            if (activate) window.Activate();
            return;
        }
        try
        {
            open = new UpdateWindow(model) { ShowActivated = activate };
            open.Show();
        }
        catch (InvalidOperationException error)
        {
            // The app is quitting.
            Log.Write($"Update window not shown: {error.Message}");
            open = null;
        }
    }

    private UpdateWindow(UpdateModel model)
    {
        this.model = model;
        var view = new UpdateView(model);
        SetResourceReference(StyleProperty, "Synth.Window");
        Title = model.Title;
        Content = view;
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = true;
        Topmost = true;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        UpdateBackground();

        model.CloseRequested += Close;
        model.Changed += OnModelChanged;
        Theme.Changed += UpdateBackground;
        SourceInitialized += (_, _) => Theme.ApplyTitleBar(this);
        // In front until the user deals with it; then an ordinary window.
        PreviewMouseDown += (_, _) => Topmost = false;
        PreviewKeyDown += (_, _) => Topmost = false;
        Closing += (_, e) =>
        {
            // The installer is starting: Lumerist quits by itself in a moment.
            if (model.State is UpdateModel.Phase.Launching) e.Cancel = true;
        };
        Closed += (_, _) =>
        {
            model.CloseRequested -= Close;
            model.Changed -= OnModelChanged;
            Theme.Changed -= UpdateBackground;
            model.WindowClosed();
            open = null;
        };
    }

    private void OnModelChanged() => Title = model.Title;

    private void UpdateBackground() => Background = Theme.Brush(Theme.IsFlat ? nameof(ThemePalette.Panel) : nameof(ThemePalette.Abyss));
}
