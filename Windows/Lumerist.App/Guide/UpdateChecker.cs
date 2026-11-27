using Lumerist.App.Services;
using Lumerist.Core;

namespace Lumerist.App.Guide;

/// <summary>
/// Looks for a newer Lumerist release (the Mac's UpdateChecker). Lumerist
/// checks when it starts, at most once a day, unless the user unticks "Tell
/// me when a new version is out" (first-run guide) or the automatic check in
/// General. A newer version opens the update window (<see cref="UpdateWindow"/>)
/// unless the user chose "Later" or "Skip this version" for it; nothing is
/// downloaded until the user presses "Update now". UI thread only.
/// </summary>
internal sealed class UpdateChecker
{
    /// <summary>Null in renders: nothing is read from the network or saved.</summary>
    private readonly AppController? controller;
    private readonly UpdateFeedClient client = new(feedFile: TestFeed.File);

    /// <param name="controller">Null for a render (snapshots).</param>
    public UpdateChecker(AppController? controller) => this.controller = controller;

    public UpdateCheckState State { get; private set; } = new UpdateCheckState.Idle();

    /// <summary>Raised on the UI thread when <see cref="State"/> or a setting changed.</summary>
    public event Action? Changed;

    /// <summary>The update window should open for this version (raised on the UI thread), by itself or by hand.</summary>
    public event Action<UpdateCheckState.Available, UpdatePrompt.Trigger>? PromptRequested;

    public bool IsChecking => State is UpdateCheckState.Checking;

    /// <summary>
    /// The version updates compare with: the app's, or in a debug build
    /// <c>LUMERIST_UPDATE_PRETEND_VERSION</c> (e.g. "0.9.0", to be offered
    /// the real newest release). A release build ignores the variable.
    /// </summary>
    public static string CurrentVersion => PretendVersion ?? AppController.AppVersion;

#if DEBUG
    private static readonly string? PretendVersion =
        Environment.GetEnvironmentVariable("LUMERIST_UPDATE_PRETEND_VERSION")?.Trim() is { Length: > 0 } pretend ? pretend : null;
#else
    private static readonly string? PretendVersion = null;
#endif

    /// <summary>
    /// A debug build's test hook is set (<c>LUMERIST_UPDATE_PRETEND_VERSION</c>
    /// or <c>LUMERIST_UPDATE_FEED_URL</c>): the check at launch runs at every
    /// launch and its time is never saved.
    /// </summary>
    public static bool IsTesting => PretendVersion is not null || TestFeed.File is not null || TestFeed.IsInvalid;

    public bool ChecksAutomatically
    {
        get => controller?.Settings.CheckForUpdatesAutomatically ?? false;
        set
        {
            if (controller is null || controller.Settings.CheckForUpdatesAutomatically == value) return;
            controller.Settings.CheckForUpdatesAutomatically = value;
            controller.SaveSettings();
            Changed?.Invoke();
        }
    }

    /// <summary>The last time the feed really answered.</summary>
    public DateTimeOffset? LastCheck => controller?.Settings.LastUpdateCheck;

    /// <summary>At launch: the daily check, unless the user turned it off.</summary>
    public void CheckIfDue()
    {
        if (controller is null || IsChecking) return;
        if (IsTesting ? !ChecksAutomatically : !UpdateCheck.IsDue(ChecksAutomatically, LastCheck, DateTimeOffset.Now)) return;
        _ = CheckAsync(UpdatePrompt.Trigger.Launch);
    }

    /// <summary>Asks the release feed once; never throws. By hand ("Check for updates"), a newer version always opens the window.</summary>
    public async Task CheckAsync(UpdatePrompt.Trigger trigger = UpdatePrompt.Trigger.Manual)
    {
        if (controller is null || IsChecking) return;
        SetState(new UpdateCheckState.Checking());
        if (TestFeed.IsInvalid)
        {
            SetState(new UpdateCheckState.Failed("LUMERIST_UPDATE_FEED_URL must be a file:// address of a feed on this PC."));
            return;
        }
        // Resumes on the UI thread (the dispatcher's synchronization context).
        var (state, counts) = await client.CheckAsync(CurrentVersion);
        if (counts && !IsTesting)
        {
            controller.Settings.LastUpdateCheck = DateTimeOffset.Now;
            controller.SaveSettings();
        }
        Log.Write($"Update check: {state}{(IsTesting ? $" (test: version {CurrentVersion}, feed {TestFeed.File ?? "GitHub"})" : "")}");
        SetState(state);
        if (state is UpdateCheckState.Available available && Prompt.ShouldShow(available.Version, CurrentVersion, trigger, DateTimeOffset.Now))
            PromptRequested?.Invoke(available, trigger);
    }

    /// <summary>The menu line and General's button: the window for the version found, as by hand.</summary>
    public void ShowPrompt()
    {
        if (State is UpdateCheckState.Available available && Prompt.ShouldShow(available.Version, CurrentVersion, UpdatePrompt.Trigger.Manual, DateTimeOffset.Now))
            PromptRequested?.Invoke(available, UpdatePrompt.Trigger.Manual);
        else OpenReleasePage();
    }

    /// <summary>"Later" and "Release page": not by itself again for a day.</summary>
    public void Later()
    {
        if (controller is null) return;
        Prompt = Prompt.Later(DateTimeOffset.Now);
    }

    /// <summary>"Skip this version": not by itself again for this version.</summary>
    public void Skip(string version)
    {
        if (controller is null) return;
        Prompt = Prompt.Skip(version);
    }

    private UpdatePrompt Prompt
    {
        get => controller?.Settings.UpdatePrompt ?? new UpdatePrompt();
        set
        {
            if (controller is null) return;
            controller.Settings.UpdatePrompt = value;
            controller.SaveSettings();
        }
    }

    /// <summary>The newer release's page when it is one of this project's on github.com; otherwise the list of releases.</summary>
    public void OpenReleasePage() =>
        GuideKit.Open(State is UpdateCheckState.Available available && UpdateCheck.IsTrustedReleasePage(available.Page)
            ? available.Page
            : SupportLinks.Releases);

    private void SetState(UpdateCheckState state)
    {
        State = state;
        Changed?.Invoke();
    }

    /// <summary>
    /// <c>LUMERIST_UPDATE_FEED_URL</c>, debug builds only: a feed file on this
    /// PC (file://) read instead of GitHub, whose files may then be local too
    /// (https downloads keep every rule). Any other value refuses to check
    /// rather than fall back to GitHub. A release build ignores the variable.
    /// </summary>
    internal static class TestFeed
    {
#if DEBUG
        private static readonly string? Value = Environment.GetEnvironmentVariable("LUMERIST_UPDATE_FEED_URL")?.Trim() is { Length: > 0 } text ? text : null;
#else
        private static readonly string? Value = null;
#endif

        /// <summary>The feed file to read, or null for GitHub.</summary>
        public static string? File { get; } =
            Value is not null && Uri.TryCreate(Value, UriKind.Absolute, out var url) && url.IsFile && !url.IsUnc ? url.LocalPath : null;

        /// <summary>Set, but not a local file: nothing is checked.</summary>
        public static bool IsInvalid => Value is not null && File is null;

        /// <summary>Release files may be local files (only with a test feed).</summary>
        public static bool AllowsLocalFiles => File is not null;
    }
}
