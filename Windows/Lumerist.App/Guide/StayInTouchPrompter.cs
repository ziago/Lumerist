using System.Windows;
using System.Windows.Threading;
using Lumerist.App.Services;
using Lumerist.App.Views;
using Lumerist.Core;

namespace Lumerist.App.Guide;

/// <summary>
/// Decides when the "Stay in touch" dialog appears over the main window and
/// keeps its schedule (<see cref="StayInTouchSchedule"/>) in the settings (the
/// Mac's StayInTouchPrompter).
/// </summary>
/// <remarks>
/// The dialog only appears while the user is looking at the main window: it is
/// visible, active and not minimised (so Lumerist is in front and Lightroom is
/// not), the setup guide is finished and closed. A window that only flashed
/// at launch never gets it: every chance waits <see cref="SettleDelay"/> and
/// checks again. Nothing is sent until the user types an email and presses
/// "Keep me posted". UI thread only.
/// </remarks>
internal sealed class StayInTouchPrompter
{
    /// <summary>How long the main window must stay in front before a scheduled prompt.</summary>
    public static readonly TimeSpan SettleDelay = TimeSpan.FromMilliseconds(1200);

    /// <summary>
    /// Something in the main window must not be interrupted (a key being
    /// recorded): set by the main window; the prompt then waits for the next chance.
    /// </summary>
    public static Func<bool>? MainWindowIsBusy { get; set; }

    /// <summary>Null in renders: nothing is read, saved or presented.</summary>
    private readonly AppController? controller;
    private StayInTouchSchedule schedule;
    private DispatcherTimer? pendingCheck;
    private Window? watchedWindow;
    private bool isAttached;

    /// <summary>
    /// At most one dialog per launch: once it has been on screen (scheduled or
    /// opened from About &amp; Support), no scheduled prompt follows until the next launch.
    /// </summary>
    private bool shownThisLaunch;

    public StayInTouchPrompter(AppController controller)
    {
        this.controller = controller;
        schedule = controller.Settings.StayInTouch.ToSchedule();
    }

    /// <summary>Renders only: a schedule in a given state; it never saves or presents.</summary>
    private StayInTouchPrompter(StayInTouchSchedule schedule) => this.schedule = schedule;

    public static StayInTouchPrompter Preview(StayInTouchSchedule schedule) => new(schedule);

    /// <summary>The dialog on screen, if any.</summary>
    public StayInTouchModel? Presented { get; private set; }

    /// <summary>A confirmed sign-up's date, the only thing kept about it.</summary>
    public DateTimeOffset? SubscribedAt => schedule.SubscribedAt;

    /// <summary>Raised on the UI thread after a sign-up or when the dialog opens or closes.</summary>
    public event Action? Changed;

    /// <summary>
    /// Listens for chances to ask: Lumerist becomes the active app, the main
    /// window is shown or activated (the setup guide closing is the third).
    /// </summary>
    public void Attach()
    {
        if (controller is null || isAttached || Application.Current is null) return;
        isAttached = true;
        // A main window opened some other way is picked up here too.
        Application.Current.Activated += (_, _) => WatchMainWindow();
        // Program shows the main window from this same event, before this handler runs.
        controller.ShowWindowRequested += WatchMainWindow;
    }

    private void WatchMainWindow()
    {
        if (MainWindowInstance() is { } window && !ReferenceEquals(window, watchedWindow))
        {
            watchedWindow = window;
            window.Activated += (_, _) => ConsiderPrompting();
            window.Closed += (_, _) =>
            {
                if (ReferenceEquals(watchedWindow, window)) watchedWindow = null;
            };
        }
        ConsiderPrompting();
    }

    /// <summary>A chance to ask: checked again after <see cref="SettleDelay"/>.</summary>
    public void ConsiderPrompting()
    {
        if (controller is null || shownThisLaunch || Presented is not null || pendingCheck is not null
            || !schedule.IsDue(DateTimeOffset.Now)) return;
        pendingCheck = new DispatcherTimer { Interval = SettleDelay };
        pendingCheck.Tick += (_, _) =>
        {
            pendingCheck?.Stop();
            pendingCheck = null;
            PromptIfDue();
        };
        pendingCheck.Start();
    }

    private void PromptIfDue()
    {
        var now = DateTimeOffset.Now;
        if (shownThisLaunch || Presented is not null || !schedule.IsDue(now) || schedule.NextPrompt is not { } number
            || !UserIsLookingAtMainWindow()) return;
        // Counted as soon as it appears: "Not now" and closing count too.
        schedule.RecordShown(now);
        Save();
        shownThisLaunch = true;
        Present(new StayInTouchModel(new GaleristPrompt.Scheduled(number), this));
    }

    /// <summary>
    /// The main window is visible, active and not minimised (so Lumerist is the
    /// app in front, never Lightroom); the setup guide is done and closed; and
    /// nothing else is going on in the window (a dialog over it, a key being recorded).
    /// </summary>
    private bool UserIsLookingAtMainWindow()
    {
        if (controller is null || !controller.Settings.GuideCompleted || GuideWindow.IsOpen) return false;
        if (MainWindowInstance() is not { } window) return false;
        return window.IsVisible && window.IsActive && window.WindowState != WindowState.Minimized
            && !window.OwnedWindows.Cast<Window>().Any(owned => owned.IsVisible)
            && MainWindowIsBusy?.Invoke() != true;
    }

    /// <summary>About &amp; Support: the same dialog, on request. Never counted.</summary>
    public void PresentFromAbout()
    {
        if (controller is null) return;
        if (Presented is not null) return;
        pendingCheck?.Stop();
        pendingCheck = null;
        shownThisLaunch = true;
        Present(new StayInTouchModel(new GaleristPrompt.About(), this));
    }

    /// <summary>Modal over the main window (or the window in front), like the Mac's sheet.</summary>
    private void Present(StayInTouchModel model)
    {
        StayInTouchWindow? dialog = null;
        try
        {
            dialog = new StayInTouchWindow(model);
            var owner = OwnerWindow();
            dialog.Owner = owner;
            dialog.WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner;
            Presented = model;
            Changed?.Invoke();
            dialog.ShowDialog();
        }
        catch (InvalidOperationException error)
        {
            // The app is quitting or the owner is closing: the dialog never appeared.
            Log.Write($"Stay in touch not shown: {error.Message}");
            if (ReferenceEquals(Presented, model))
            {
                Presented = null;
                Changed?.Invoke();
            }
            // Its Closed handler never ran: close it so it lets go of its subscriptions.
            if (dialog is { IsLoaded: false }) dialog.Close();
        }
    }

    /// <summary>The window the user is in (the main window for a scheduled prompt, About &amp; Support for a request).</summary>
    private static Window? OwnerWindow()
    {
        var windows = Application.Current?.Windows.OfType<Window>().ToList() ?? [];
        return windows.FirstOrDefault(window => window.IsActive && window.IsVisible)
            ?? (MainWindowInstance() is { IsVisible: true } main ? main : null)
            ?? windows.FirstOrDefault(window => window.IsVisible && window.ShowInTaskbar);
    }

    private static MainWindow? MainWindowInstance() => Application.Current?.Windows.OfType<MainWindow>().FirstOrDefault();

    /// <summary>A sign-up the server confirmed: only the date is kept, never the email.</summary>
    public void DidSubscribe(DateTimeOffset date)
    {
        schedule.RecordSubscribed(date);
        Save();
        Changed?.Invoke();
    }

    /// <summary>
    /// The dialog closed, however it closed ("Not now", Esc, the close box).
    /// "Don't ask me again" ticked on a scheduled prompt means never again.
    /// </summary>
    public void Closed(StayInTouchModel model)
    {
        if (model.DontAskAgain && model.OffersDontAskAgain && !model.IsSubscribed)
        {
            schedule.StopAsking();
            Save();
        }
        if (ReferenceEquals(Presented, model)) Presented = null;
        Changed?.Invoke();
    }

    private void Save()
    {
        if (controller is null) return;
        controller.Settings.StayInTouch.Store(schedule);
        controller.SaveSettings();
    }
}

/// <summary>One showing of the dialog: what the user typed, and the request's state (the Mac's StayInTouchModel).</summary>
internal sealed class StayInTouchModel
{
    public abstract record Phase
    {
        private Phase() { }

        public sealed record Editing() : Phase;

        public sealed record Sending() : Phase;

        public sealed record Failed(string Message) : Phase;

        public sealed record Subscribed(DateTimeOffset At) : Phase;
    }

    private readonly StayInTouchPrompter? prompter;
    private readonly bool isPreview;

    public StayInTouchModel(GaleristPrompt prompt, StayInTouchPrompter prompter)
    {
        Prompt = prompt;
        this.prompter = prompter;
    }

    /// <summary>Renders only: a dialog in a given state; it never sends or saves.</summary>
    private StayInTouchModel(Phase phase, GaleristPrompt prompt, string email, bool dontAskAgain)
    {
        State = phase;
        Prompt = prompt;
        Form.Email = email;
        DontAskAgain = dontAskAgain;
        isPreview = true;
    }

    public static StayInTouchModel Preview(Phase phase, GaleristPrompt? prompt = null, string email = "", bool dontAskAgain = false) =>
        new(phase, prompt ?? new GaleristPrompt.Scheduled(1), email, dontAskAgain);

    public GaleristPrompt Prompt { get; }

    /// <summary>What the user typed; emptied after a confirmed sign-up.</summary>
    public GaleristSignUpForm Form { get; } = new();

    /// <summary>Unticked when the dialog opens.</summary>
    public bool DontAskAgain { get; set; }

    public Phase State { get; private set; } = new Phase.Editing();

    /// <summary>Raised on the UI thread when the state or what can be done changed.</summary>
    public event Action? Changed;

    /// <summary>A result for screen readers (the dialog changes under them).</summary>
    public event Action<string>? Announcement;

    public bool IsPreview => isPreview;

    public bool IsSending => State is Phase.Sending;

    public bool IsSubscribed => State is Phase.Subscribed;

    public bool CanSubmit => Form.EmailLooksValid && !IsSending && !IsSubscribed;

    /// <summary>"Don't ask me again" belongs to the scheduled prompts, not to the dialog opened from About &amp; Support.</summary>
    public bool OffersDontAskAgain => Prompt is GaleristPrompt.Scheduled;

    /// <summary>Typing clears a failure.</summary>
    public string Email
    {
        get => Form.Email;
        set
        {
            if (Form.Email == value) return;
            Form.Email = value;
            if (State is Phase.Failed) State = new Phase.Editing();
            Changed?.Invoke();
        }
    }

    /// <summary>The one network call, from "Keep me posted" or Enter in the field.</summary>
    public void Submit()
    {
        if (isPreview || IsSending || IsSubscribed) return;
        switch (Form.Prepare(GaleristOptIn.ProcessEnvironment(), GuideNative.PreferredLanguages(), AppController.AppVersion, Prompt))
        {
            case GaleristSignUpForm.Submission.Refuse refuse:
                Fail(refuse.Outcome);
                break;
            case GaleristSignUpForm.Submission.Send send:
                SetState(new Phase.Sending());
                _ = SendAsync(new GaleristSubscriptionClient(send.BaseUrl, AppController.AppVersion), send.Subscription);
                break;
        }
    }

    private async Task SendAsync(GaleristSubscriptionClient client, GaleristSubscription subscription)
    {
        // Resumes on the UI thread; SubscribeAsync never throws.
        var outcome = await client.SubscribeAsync(subscription);
        Log.Write($"Stay in touch: {outcome}");
        if (outcome == GaleristSubscriptionOutcome.Subscribed)
        {
            var now = DateTimeOffset.Now;
            // Recorded even if the dialog was closed meanwhile.
            prompter?.DidSubscribe(now);
            Form.Reset();
            SetState(new Phase.Subscribed(now));
            Announcement?.Invoke(outcome.Message());
        }
        else
        {
            Fail(outcome);
        }
    }

    /// <summary>The dialog closed.</summary>
    public void DialogClosed()
    {
        if (!isPreview) prompter?.Closed(this);
    }

    private void Fail(GaleristSubscriptionOutcome outcome)
    {
        SetState(new Phase.Failed(outcome.Message()));
        Announcement?.Invoke(outcome.Message());
    }

    private void SetState(Phase phase)
    {
        State = phase;
        Changed?.Invoke();
    }
}
