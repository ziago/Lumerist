using System.Diagnostics.CodeAnalysis;
using Lumerist.Core;

namespace Lumerist.App.Services;

/// <summary>The first-run guide, the update check and "Stay in touch".</summary>
internal sealed partial class AppSettings
{
    /// <summary>The first-run guide was finished or skipped (the Mac's lumerist.onboarding.completed).</summary>
    public bool GuideCompleted { get; set; }

    /// <summary>The daily update check at launch; on unless the user turns it off (lumerist.updates.automatic).</summary>
    public bool CheckForUpdatesAutomatically { get; set; } = true;

    /// <summary>The newer version the tray last announced, so each one is announced once.</summary>
    public string? LastAnnouncedVersion { get; set; }

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
