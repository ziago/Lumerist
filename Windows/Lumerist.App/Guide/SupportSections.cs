using System.Windows;
using System.Windows.Controls;
using Lumerist.App.Design;
using Lumerist.App.Design.Controls;
using Lumerist.Core;

namespace Lumerist.App.Guide;

/// <summary>
/// "Updates" (the Mac's General → Updates): the manual check and the optional
/// daily check at launch, on unless turned off.
/// </summary>
internal sealed class UpdatesSection : LiveView
{
    private readonly UpdateChecker checker;
    private readonly Action changed;

    public UpdatesSection(UpdateChecker checker)
    {
        this.checker = checker;
        changed = Rebuild;
        Rebuild();
    }

    protected override void StartListening() => checker.Changed += changed;

    protected override void StopListening() => checker.Changed -= changed;

    protected override UIElement Build()
    {
        var buttons = new List<UIElement>();
        if (checker.State is UpdateCheckState.Available)
            buttons.Add(GuideKit.Button("Download…", GuideButtonKind.Primary, checker.OpenReleasePage));
        var check = GuideKit.Button(checker.IsChecking ? "Checking…" : "Check for updates", GuideButtonKind.Secondary,
                                    () => _ = checker.CheckAsync());
        check.IsEnabled = !checker.IsChecking;
        buttons.Add(check);

        // The same switch as every other setting (the Mac's .neon toggle style).
        var daily = SynthControls.Toggle(checker.ChecksAutomatically, on => checker.ChecksAutomatically = on,
            "Check automatically once a day");

        return GuideKit.Section("Updates", GuideIcon.ArrowDownCircle,
            "Lumerist never downloads or installs anything by itself. Checking asks GitHub for the list of releases; with automatic checks off, Lumerist talks to nothing but Lightroom on this PC, unless you leave your email under Stay in touch.",
            GuideKit.Row($"Version {AppController.AppVersion}", StateDetail, GuideKit.Buttons([.. buttons])),
            GuideKit.Row("Check automatically once a day", "When Lumerist starts. A new version shows in the notification area.", daily));
    }

    private string StateDetail => checker.State switch
    {
        UpdateCheckState.Checking => "Asking GitHub…",
        UpdateCheckState.UpToDate(var version) => $"Lumerist {version} is the newest release.",
        UpdateCheckState.Available(var version, _) => $"Lumerist {version} is available on GitHub.",
        UpdateCheckState.NoReleases => "No release has been published yet.",
        UpdateCheckState.Failed(var message) => message,
        _ => checker.LastCheck is { } last
            ? $"Last checked {DisplayDates.RelativeNamed(last.ToLocalTime(), DateTimeOffset.Now)}."
            : "Not checked yet.",
    };
}

/// <summary>
/// "About &amp; Support" (the Mac's General → About &amp; Support): project links,
/// the maker's credit line and Galerist link, donations, "Stay in touch", licences.
/// </summary>
internal sealed class AboutSupportSection : LiveView
{
    private readonly StayInTouchPrompter stayInTouch;
    private readonly Action changed;

    public AboutSupportSection(StayInTouchPrompter stayInTouch)
    {
        this.stayInTouch = stayInTouch;
        changed = Rebuild;
        Rebuild();
    }

    protected override void StartListening() => stayInTouch.Changed += changed;

    protected override void StopListening() => stayInTouch.Changed -= changed;

    protected override UIElement Build()
    {
        var galerist = Link("Visit Galerist", SupportLinks.GaleristFromSettings);
        galerist.ToolTip = SupportLinks.GaleristFromSettings.AbsoluteUri;

        return GuideKit.Section("About & Support", GuideIcon.Heart,
            "Lumerist is free and open source (GPL-3.0). Donations are gifts, never payments: every feature stays free. Adobe, Lightroom and Lightroom Classic are trademarks of Adobe; Lumerist is not affiliated with Adobe.",
            GuideKit.Row($"Lumerist {AppController.AppVersion}",
                         "Made by a photographer who wanted faster editing in Lightroom Classic. Ideas and bug reports are welcome.",
                         GuideKit.Buttons(Link("GitHub", SupportLinks.Repository), Link("Report a bug", SupportLinks.Issues),
                                          Link("Ideas", SupportLinks.Discussions))),
            GuideKit.Row("Galerist", GaleristOptIn.Copy.Credit, galerist),
            Donations(),
            // The same dialog as the occasional prompt, opened on request.
            GuideKit.Row(GaleristOptIn.Copy.Title, GaleristOptIn.Copy.AboutDetail, StayInTouchControl()),
            GuideKit.Row("Licences",
                         "Lumerist: GNU GPL v3. Fonts Chakra Petch, Outfit, Inter and JetBrains Mono: SIL Open Font License 1.1 (included in the app).",
                         Link("GPL-3.0", SupportLinks.License)));
    }

    private UIElement StayInTouchControl()
    {
        if (stayInTouch.SubscribedAt is not { } date)
            return GuideKit.Button(GaleristOptIn.Copy.AboutButton, GuideButtonKind.Secondary, stayInTouch.PresentFromAbout);
        var since = GuideKit.Text($"Since {DisplayDates.Abbreviated(date.ToLocalTime())}", 11, nameof(ThemePalette.Steel));
        since.HorizontalAlignment = HorizontalAlignment.Right;
        since.Margin = new Thickness(0, 4, 0, 0);
        var stack = new StackPanel { Children = { GuideKit.Pill(GaleristOptIn.Copy.AboutSubscribed), since } };
        return stack;
    }

    /// <summary>Text above, buttons below: several buttons beside the text would squeeze it.</summary>
    private static UIElement Donations()
    {
        var stack = new StackPanel();
        stack.Children.Add(GuideKit.Text("Buy me a coffee — or a pizza", 13, nameof(ThemePalette.Ice), FontWeights.SemiBold));
        var line = GuideKit.Text("If Lumerist saves you time, a small gift keeps it growing.", 12, nameof(ThemePalette.Steel));
        line.Margin = new Thickness(0, Theme.Space.S, 0, 0);
        stack.Children.Add(line);
        var buttons = new WrapPanel { Margin = new Thickness(0, Theme.Space.S + 2, 0, 0) };
        foreach (var donation in SupportLinks.AvailableDonations)
        {
            var url = donation.Url!;
            var button = GuideKit.Button(donation.Title, GuideButtonKind.Secondary, () => GuideKit.Open(url), IconFor(donation.Id));
            button.ToolTip = donation.Detail;
            button.Margin = new Thickness(0, 0, Theme.Space.S, Theme.Space.XS);
            buttons.Children.Add(button);
        }
        stack.Children.Add(buttons);
        return GuideKit.RowFrame(stack, 12);
    }

    private static GuideIcon IconFor(string donation) => donation switch
    {
        "coffee" => GuideIcon.Cup,
        "kofi" => GuideIcon.Heart,
        "paypal" => GuideIcon.Gift,
        _ => GuideIcon.Star,
    };

    private static Button Link(string title, Uri url) => GuideKit.Button(title, GuideButtonKind.Quiet, () => GuideKit.Open(url));
}
