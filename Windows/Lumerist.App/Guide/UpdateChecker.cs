using Lumerist.App.Services;
using Lumerist.Core;

namespace Lumerist.App.Guide;

/// <summary>
/// Looks for a newer Lumerist release (the Mac's UpdateChecker). Nothing is
/// ever downloaded or installed: a newer version is announced in the
/// notification area and only opens its release page. Lumerist checks when
/// it starts, at most once a day, unless the user unticks "Tell me when a new
/// version is out" (first-run guide) or the automatic check in General.
/// UI thread only.
/// </summary>
internal sealed class UpdateChecker
{
    /// <summary>Null in renders: nothing is read from the network or saved.</summary>
    private readonly AppController? controller;
    private readonly UpdateFeedClient client = new();

    /// <param name="controller">Null for a render (snapshots).</param>
    public UpdateChecker(AppController? controller) => this.controller = controller;

    public UpdateCheckState State { get; private set; } = new UpdateCheckState.Idle();

    /// <summary>Raised on the UI thread when <see cref="State"/> or a setting changed.</summary>
    public event Action? Changed;

    public bool IsChecking => State is UpdateCheckState.Checking;

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
        if (!UpdateCheck.IsDue(ChecksAutomatically, LastCheck, DateTimeOffset.Now)) return;
        _ = CheckAsync();
    }

    /// <summary>Asks the release feed once; never throws.</summary>
    public async Task CheckAsync()
    {
        if (controller is null || IsChecking) return;
        SetState(new UpdateCheckState.Checking());
        // Resumes on the UI thread (the dispatcher's synchronization context).
        var (state, counts) = await client.CheckAsync(AppController.AppVersion);
        if (counts)
        {
            controller.Settings.LastUpdateCheck = DateTimeOffset.Now;
            controller.SaveSettings();
        }
        Log.Write($"Update check: {state}");
        SetState(state);
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
}
