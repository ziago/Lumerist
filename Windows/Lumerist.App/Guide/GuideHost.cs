using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Lumerist.App.Design;

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

    /// <summary>About &amp; Support with Updates, in a window of its own (tray → "Support Lumerist…").</summary>
    public static void ShowAbout(AppController controller)
    {
        Ensure(controller);
        AboutWindow.ShowOrActivate(updates!, stayInTouch!);
    }

    /// <summary>"Stay in touch" on request (About &amp; Support's "Leave your email…"): never counted as a scheduled prompt.</summary>
    public static void ShowStayInTouch(AppController controller)
    {
        Ensure(controller);
        stayInTouch!.PresentFromAbout();
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

    /// <summary>The Updates and About &amp; Support sections one under the other, with the Mac's page padding.</summary>
    internal static FrameworkElement SupportPage(UpdateChecker updates, StayInTouchPrompter stayInTouch)
    {
        var about = new AboutSupportSection(stayInTouch) { Margin = new Thickness(0, Theme.Space.XL, 0, 0) };
        return new Border
        {
            Padding = new Thickness(Theme.Space.XXL),
            Child = new StackPanel { Children = { new UpdatesSection(updates), about } },
        };
    }

    /// <summary>The update check, for the tray's notice of a new version.</summary>
    public static UpdateChecker Updates(AppController controller)
    {
        Ensure(controller);
        return updates!;
    }

    /// <summary>The guide closed: the main window is usually in front now, a chance for "Stay in touch".</summary>
    internal static void GuideClosed() => stayInTouch?.ConsiderPrompting();

    private static void Ensure(AppController current)
    {
        if (ReferenceEquals(controller, current)) return;
        controller = current;
        updates = new UpdateChecker(current);
        stayInTouch = new StayInTouchPrompter(current);
    }
}
