using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using Lumerist.App.Guide;
using Lumerist.Core;

namespace Lumerist.App.Snapshots;

/// <summary>
/// The setup guide, the tray menu, "Stay in touch" in each state and the
/// Updates / About &amp; Support sections, named like the Mac's
/// OnboardingSnapshots renders. Nothing is sent, saved or installed: the
/// prompter and the dialogs are previews.
/// </summary>
internal static class GuideSnapshots
{
    [ModuleInitializer]
    internal static void Register()
    {
        SnapshotRegistry.Add("setup-guide", 640, 480, controller => new GuideView(controller, finish: null));
        SnapshotRegistry.Add("status-menu", 360, 520, controller =>
        {
            using var menu = new TrayMenu(controller, quit: () => { });
            return menu.Picture();
        });

        // The Mac's general-support also shows the plug-in section, which the
        // main window's General screen draws on Windows.
        SnapshotRegistry.Add("general-support", 860, 860, controller =>
            Page(GuideHost.SupportPage(new UpdateChecker(null), StayInTouchPrompter.Preview(new StayInTouchSchedule()))));
        SnapshotRegistry.Add("general-support-stay-in-touch", 860, 600, _ =>
            About(StayInTouchPrompter.Preview(new StayInTouchSchedule())));
        SnapshotRegistry.Add("general-support-subscribed", 860, 600, _ =>
            About(StayInTouchPrompter.Preview(new StayInTouchSchedule(timesShown: 1, lastShown: DateTimeOffset.Now, subscribedAt: DateTimeOffset.Now))));

        // "Stay in touch" in each state, as the dialog shows it.
        const string email = "name@example.com";
        AddStayInTouch("editing", 330, StayInTouchModel.Preview(new StayInTouchModel.Phase.Editing()));
        AddStayInTouch("error", 350, StayInTouchModel.Preview(
            new StayInTouchModel.Phase.Failed(GaleristSubscriptionOutcome.Unreachable.Message()), email: email, dontAskAgain: true));
        AddStayInTouch("sending", 330, StayInTouchModel.Preview(new StayInTouchModel.Phase.Sending(), email: email));
        AddStayInTouch("success", 210, StayInTouchModel.Preview(new StayInTouchModel.Phase.Subscribed(DateTimeOffset.Now)));
        AddStayInTouch("about", 330, StayInTouchModel.Preview(new StayInTouchModel.Phase.Editing(), new GaleristPrompt.About(), email));
    }

    private static void AddStayInTouch(string state, double height, StayInTouchModel model) =>
        SnapshotRegistry.Add($"stay-in-touch-{state}", 480, height, _ => new StayInTouchView(model));

    private static FrameworkElement About(StayInTouchPrompter prompter) =>
        Page(new Border { Padding = new Thickness(Design.Theme.Space.XXL), Child = new AboutSupportSection(prompter) });

    /// <summary>
    /// The window background behind a page (Miami's gradient). Heights leave
    /// room for longer text on Windows; the background fills the rest.
    /// </summary>
    private static FrameworkElement Page(FrameworkElement content) =>
        new Border { Background = GuideKit.WindowBackground(), Child = content, VerticalAlignment = VerticalAlignment.Stretch };
}
