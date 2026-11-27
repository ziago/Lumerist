using System.Windows;
using System.Windows.Threading;
using Lumerist.App.Input;
using Lumerist.App.Services;
using Lumerist.Core;

namespace Lumerist.App.Guide;

/// <summary>
/// The first-run guide, "Stay in touch", About &amp; Support and the update
/// check: what the rest of the app calls. UI thread only.
/// </summary>
internal static class GuideHost
{
    private static AppController? controller;
    private static UpdateChecker? updates;
    private static StayInTouchPrompter? stayInTouch;

    /// <summary>
    /// At startup, after the controller started: the guide on the first run
    /// (until finished or skipped, even when started hidden or by Lightroom,
    /// as on the Mac), the chances for "Stay in touch", and the daily update
    /// check unless the user turned it off.
    /// </summary>
    public static void OnStarted(AppController controller)
    {
        Ensure(controller);
        stayInTouch!.Attach();
        UpdateInstaller.RemoveOldDownloads();
        FinishUpdate(controller);
        // Once the rest of the startup is done, so the guide opens on top of
        // the main window when that opens too.
        if (!controller.Settings.GuideCompleted)
            Dispatcher.CurrentDispatcher.BeginInvoke(() => ShowGuide(controller), DispatcherPriority.ApplicationIdle);
        updates!.CheckIfDue();
    }

    /// <summary>The setup guide (General → "Open guide…", tray → "Setup guide…").</summary>
    public static void ShowGuide(AppController controller)
    {
        Ensure(controller);
        GuideWindow.ShowOrActivate(controller);
    }

    /// <summary>For the main window's General screen: the Mac's General → Updates.</summary>
    public static FrameworkElement CreateUpdatesSection(AppController controller)
    {
        Ensure(controller);
        return new UpdatesSection(updates!);
    }

    /// <summary>For the main window's General screen: the Mac's General → About &amp; Support.</summary>
    public static FrameworkElement CreateAboutSupportSection(AppController controller)
    {
        Ensure(controller);
        return new AboutSupportSection(stayInTouch!);
    }

    /// <summary>The update check, for the tray's line about a new version.</summary>
    public static UpdateChecker Updates(AppController controller)
    {
        Ensure(controller);
        return updates!;
    }

    /// <summary>A line for the notification area (title, text), e.g. after an update; the tray shows it.</summary>
    public static event Action<string, string>? Notice;

    /// <summary>
    /// The first start after "Update now" (settings.json pendingUpdate): when
    /// this is the new version, a notice, or, with Lightroom open and its
    /// plug-in still the old one, the update window's "Quit Lightroom &amp;
    /// update"; when it is still the old one, why the update didn't finish.
    /// </summary>
    private static void FinishUpdate(AppController controller)
    {
        if (controller.Settings.PendingUpdate is not { } pending) return;
        controller.Settings.PendingUpdate = null;
        controller.SaveSettings();
        var now = DateTimeOffset.Now;
        if (now - pending.StartedAt > TimeSpan.FromDays(1) || pending.StartedAt - now > TimeSpan.FromHours(1)) return;
        if (UpdateCheck.CurrentVersion(AppController.AppVersion) is not { } running || UpdateCheck.CurrentVersion(pending.Version) is not { } wanted) return;
        if (running.Equals(wanted))
        {
            Log.Write($"Updated from {pending.From} to {pending.Version}");
            if (controller.PluginNeedsAction && LightroomWindow.IsRunning)
                UpdateWindow.Present(UpdateModel.PluginWaiting(controller, updates!, pending.Version), activate: false);
            else
                Notice?.Invoke($"Lumerist {pending.Version} is installed", "Your keys and settings stay as they are.");
        }
        else if (running < wanted)
        {
            Log.Write($"The update to {pending.Version} didn't finish: this is still {AppController.AppVersion}");
            UpdateWindow.Present(UpdateModel.DidNotFinish(controller, updates!, pending.Version), activate: false);
        }
    }

    /// <summary>The guide closed: the main window is usually in front now, a chance for "Stay in touch".</summary>
    internal static void GuideClosed() => stayInTouch?.ConsiderPrompting();

    private static void Ensure(AppController current)
    {
        if (ReferenceEquals(controller, current)) return;
        controller = current;
        var checker = new UpdateChecker(current);
        // By itself it opens in front without taking the keyboard from Lightroom; by hand it takes it.
        checker.PromptRequested += (available, trigger) =>
            UpdateWindow.Present(new UpdateModel(current, checker, available), activate: trigger == UpdatePrompt.Trigger.Manual);
        updates = checker;
        stayInTouch = new StayInTouchPrompter(current);
    }
}
