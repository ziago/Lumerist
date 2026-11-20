namespace Lumerist.Core;

/// <summary>
/// When the app may ask for an email ("Stay in touch"). Pure, so the rule is
/// testable and the same as on the Mac:
/// <list type="bullet">
/// <item>never once the user subscribed or chose "Don't ask me again";</item>
/// <item>prompt 1 at the first opportunity (the app decides what that is: its
/// main window in front and focused, never over Lightroom);</item>
/// <item>prompt 2 at least 3 days after prompt 1, prompt 3 at least 14 days
/// after prompt 2;</item>
/// <item>never more than 3 prompts.</item>
/// </list>
/// A prompt counts as shown when it appears (<see cref="RecordShown"/>), so
/// "Not now" and closing it count too. The button in About &amp; Support opens
/// the prompt on request and never counts.
/// </summary>
public sealed record StayInTouchSchedule
{
    public const int MaximumPrompts = 3;

    /// <summary>
    /// The least time between prompt n and prompt n + 1 (index n - 1):
    /// 3 days before the second, 14 days before the third.
    /// </summary>
    public static IReadOnlyList<TimeSpan> MinimumGaps { get; } = [TimeSpan.FromDays(3), TimeSpan.FromDays(14)];

    public int TimesShown { get; set; }
    public DateTimeOffset? LastShown { get; set; }
    public bool DontAskAgain { get; set; }
    public DateTimeOffset? SubscribedAt { get; set; }

    public StayInTouchSchedule(int timesShown = 0, DateTimeOffset? lastShown = null, bool dontAskAgain = false, DateTimeOffset? subscribedAt = null)
    {
        TimesShown = timesShown;
        LastShown = lastShown;
        DontAskAgain = dontAskAgain;
        SubscribedAt = subscribedAt;
    }

    public bool IsSubscribed => SubscribedAt is not null;

    /// <summary>Subscribed, "Don't ask me again", or all three prompts used.</summary>
    public bool IsFinished => IsSubscribed || DontAskAgain || TimesShown >= MaximumPrompts;

    /// <summary>The number of the next scheduled prompt (1–3), or null when the app will never ask again.</summary>
    public int? NextPrompt => IsFinished ? null : Math.Max(TimesShown, 0) + 1;

    /// <summary>
    /// The earliest moment the next prompt may appear; null when never. The
    /// first prompt (or a count without a date) is due at once (<see cref="DateTimeOffset.MinValue"/>).
    /// </summary>
    public DateTimeOffset? NextDate
    {
        get
        {
            if (NextPrompt is not { } next) return null;
            if (next <= 1 || LastShown is not { } last) return DateTimeOffset.MinValue;
            return last + MinimumGaps[next - 2];
        }
    }

    public bool IsDue(DateTimeOffset now) => NextDate is { } date && now >= date;

    /// <summary>The prompt appeared (whatever the user does next).</summary>
    public void RecordShown(DateTimeOffset now)
    {
        if (TimesShown >= MaximumPrompts) return;
        TimesShown = Math.Max(TimesShown, 0) + 1;
        LastShown = now;
    }

    /// <summary>A sign-up the server confirmed: never ask again. The date is all that is kept.</summary>
    public void RecordSubscribed(DateTimeOffset now) => SubscribedAt = now;

    /// <summary>"Don't ask me again".</summary>
    public void StopAsking() => DontAskAgain = true;

    // Storage

    /// <summary>Settings keys (the same names as the Mac app's defaults).</summary>
    public static class Key
    {
        public const string TimesShown = "org.lumerist.stay-in-touch.times-shown";
        public const string LastShown = "org.lumerist.stay-in-touch.last-shown";
        public const string DontAskAgain = "org.lumerist.stay-in-touch.dont-ask-again";

        /// <summary>The key the first sign-up form already wrote: anyone who subscribed with it is never asked.</summary>
        public const string SubscribedAt = GaleristOptIn.SubscribedAtDefaultsKey;

        public static IReadOnlyList<string> All { get; } = [TimesShown, LastShown, DontAskAgain, SubscribedAt];
    }

    /// <summary>
    /// Reads from a key-value store (the app's settings, a dictionary in tests).
    /// Missing or malformed values read as "never shown". The count is an int
    /// (or a long in int range), dates are <see cref="DateTimeOffset"/>, the flag a bool.
    /// </summary>
    public static StayInTouchSchedule Read(Func<string, object?> value)
    {
        var shown = value(Key.TimesShown) switch
        {
            int number => number,
            long number when number is >= int.MinValue and <= int.MaxValue => (int)number,
            _ => 0,
        };
        return new StayInTouchSchedule(
            Math.Clamp(shown, 0, MaximumPrompts),
            value(Key.LastShown) as DateTimeOffset?,
            value(Key.DontAskAgain) as bool? ?? false,
            value(Key.SubscribedAt) as DateTimeOffset?);
    }

    /// <summary>Writes every field; null means "remove the key".</summary>
    public void Write(Action<string, object?> store)
    {
        store(Key.TimesShown, TimesShown);
        store(Key.LastShown, LastShown);
        store(Key.DontAskAgain, DontAskAgain);
        store(Key.SubscribedAt, SubscribedAt);
    }
}
