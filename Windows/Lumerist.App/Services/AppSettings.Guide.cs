using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using Lumerist.Core;

namespace Lumerist.App.Services;

/// <summary>The first-run guide, the update check and "Stay in touch".</summary>
internal sealed partial class AppSettings
{
    /// <summary>The first-run guide was finished or skipped (the Mac's lumerist.onboarding.completed).</summary>
    public bool GuideCompleted { get; set; }

    /// <summary>The daily update check at launch; on unless the user turns it off (lumerist.updates.automatic).</summary>
    public bool CheckForUpdatesAutomatically { get; set; } = true;

    /// <summary>"Skip this version" in the update window (settings.json skippedUpdateVersion; the Mac's lumerist.updates.skipped-version).</summary>
    public string? SkippedUpdateVersion { get; set; }

    /// <summary>
    /// "Later", "Release page" or closing the update window: not by itself
    /// again before (settings.json updateLaterUntil; the Mac's lumerist.updates.later-until).
    /// </summary>
    public DateTimeOffset? UpdateLaterUntil { get; set; }

    /// <summary>
    /// "Update now" started the installer of this version: the next start
    /// says whether it worked. Cleared once read.
    /// </summary>
    public PendingUpdate? PendingUpdate { get; set; }

    /// <summary>When the update window may open by itself (<see cref="Core.UpdatePrompt"/>), stored as the two values above.</summary>
    [JsonIgnore]
    public UpdatePrompt UpdatePrompt
    {
        get => new(SkippedUpdateVersion, UpdateLaterUntil);
        set
        {
            SkippedUpdateVersion = value.SkippedVersion;
            UpdateLaterUntil = value.LaterUntil;
        }
    }

    /// <summary>The last check the release feed really answered (lumerist.updates.last-check).</summary>
    public DateTimeOffset? LastUpdateCheck { get; set; }

    /// <summary>When "Stay in touch" may ask. Never the email: only the date of a confirmed sign-up.</summary>
    [AllowNull]
    public StayInTouchSettings StayInTouch
    {
        get => stayInTouch;
        set => stayInTouch = value ?? new StayInTouchSettings();
    }

    private StayInTouchSettings stayInTouch = new();
}

/// <summary>An installer "Update now" started: its version, the version it replaced and when.</summary>
internal sealed record PendingUpdate(string Version, string From, DateTimeOffset StartedAt);

/// <summary>
/// The stored <see cref="StayInTouchSchedule"/> (the Mac keeps the same four
/// values in UserDefaults). Read through the schedule, so a bad count is clamped.
/// </summary>
internal sealed class StayInTouchSettings
{
    public int TimesShown { get; set; }

    public DateTimeOffset? LastShown { get; set; }

    public bool DontAskAgain { get; set; }

    public DateTimeOffset? SubscribedAt { get; set; }

    public StayInTouchSchedule ToSchedule() => StayInTouchSchedule.Read(key => key switch
    {
        StayInTouchSchedule.Key.TimesShown => TimesShown,
        StayInTouchSchedule.Key.LastShown => LastShown,
        StayInTouchSchedule.Key.DontAskAgain => DontAskAgain,
        StayInTouchSchedule.Key.SubscribedAt => SubscribedAt,
        _ => null,
    });

    public void Store(StayInTouchSchedule schedule)
    {
        TimesShown = schedule.TimesShown;
        LastShown = schedule.LastShown;
        DontAskAgain = schedule.DontAskAgain;
        SubscribedAt = schedule.SubscribedAt;
    }
}
