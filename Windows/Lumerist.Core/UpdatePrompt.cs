namespace Lumerist.Core;

/// <summary>
/// When the update window appears (the Mac's UpdatePromptSchedule; shared
/// cases: Shared/fixtures/updates).
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>Only for a version strictly newer than the running one (both
/// semantic versions without "v"; a development build never sees it).</item>
/// <item>"Check for updates" pressed by hand always shows it.</item>
/// <item>After the check at launch, not for the version the user skipped
/// ("Skip this version"; a newer one shows again), and not before
/// <see cref="LaterUntil"/> ("Later" or "Release page": 24 hours). A
/// <see cref="LaterUntil"/> more than 24 hours ahead (the clock was moved
/// back) no longer counts.</item>
/// <item>Whether the launch check runs at all is the update check's rule (at
/// most once a day, unless turned off).</item>
/// </list>
/// </remarks>
public sealed record UpdatePrompt(string? SkippedVersion = null, DateTimeOffset? LaterUntil = null)
{
    public static readonly TimeSpan LaterInterval = TimeSpan.FromDays(1);

    public enum Trigger
    {
        /// <summary>The automatic check when Lumerist starts.</summary>
        Launch,
        /// <summary>"Check for updates" (or the menu line) by hand.</summary>
        Manual,
    }

    public bool ShouldShow(string available, string current, Trigger trigger, DateTimeOffset now)
    {
        if (UpdateCheck.CurrentVersion(available) is not { } offered || UpdateCheck.CurrentVersion(current) is not { } running
            || running >= offered) return false;
        if (trigger == Trigger.Manual) return true;
        if (SkippedVersion is not null && UpdateCheck.CurrentVersion(SkippedVersion) is { } skipped && skipped.Equals(offered)) return false;
        if (LaterUntil is { } until && now < until && until - now <= LaterInterval) return false;
        return true;
    }

    /// <summary>"Later" (and "Release page"): not again before a day has passed.</summary>
    public UpdatePrompt Later(DateTimeOffset now) => this with { LaterUntil = now + LaterInterval };

    /// <summary>"Skip this version".</summary>
    public UpdatePrompt Skip(string version) => this with { SkippedVersion = version };
}
