using Lumerist.Core;
using Xunit;

namespace Lumerist.Core.Tests;

/// <summary>
/// When the "Stay in touch" prompt may appear: never once subscribed or after
/// "Don't ask me again"; #1 at once; #2 ≥ 3 days after #1; #3 ≥ 14 days after
/// #2; never more than 3. Port of StayInTouchScheduleTests.swift.
/// </summary>
public class StayInTouchScheduleTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);

    private static DateTimeOffset At(double days) => Start.AddDays(days);

    [Fact]
    public void AsksAtTheFirstOpportunity()
    {
        var schedule = new StayInTouchSchedule();
        Assert.Equal(1, schedule.NextPrompt);
        Assert.True(schedule.IsDue(Start));
        Assert.True(schedule.IsDue(DateTimeOffset.MinValue));
    }

    [Fact]
    public void WaitsThreeDaysBeforeTheSecondPrompt()
    {
        var schedule = new StayInTouchSchedule();
        schedule.RecordShown(Start);
        Assert.Equal(1, schedule.TimesShown);
        Assert.Equal(Start, schedule.LastShown);
        Assert.Equal(2, schedule.NextPrompt);
        Assert.False(schedule.IsDue(Start));
        Assert.False(schedule.IsDue(At(3).AddSeconds(-1)));
        Assert.True(schedule.IsDue(At(3)));
        Assert.Equal(At(3), schedule.NextDate);
    }

    [Fact]
    public void WaitsFourteenDaysAfterTheSecondPromptBeforeTheThird()
    {
        var schedule = new StayInTouchSchedule();
        schedule.RecordShown(Start);
        schedule.RecordShown(At(5));
        Assert.Equal(3, schedule.NextPrompt);
        Assert.False(schedule.IsDue(At(5 + 14).AddSeconds(-1)));
        Assert.True(schedule.IsDue(At(5 + 14)));
        Assert.Equal(At(19), schedule.NextDate);
    }

    [Fact]
    public void NeverAsksMoreThanThreeTimes()
    {
        var schedule = new StayInTouchSchedule();
        schedule.RecordShown(Start);
        schedule.RecordShown(At(3));
        schedule.RecordShown(At(17));
        Assert.Equal(3, schedule.TimesShown);
        Assert.Null(schedule.NextPrompt);
        Assert.Null(schedule.NextDate);
        Assert.False(schedule.IsDue(At(10_000)));
        // A fourth showing is not recorded.
        schedule.RecordShown(At(100));
        Assert.Equal(3, schedule.TimesShown);
        Assert.Equal(At(17), schedule.LastShown);
    }

    [Fact]
    public void NeverAsksASubscribedUser()
    {
        var schedule = new StayInTouchSchedule();
        schedule.RecordSubscribed(Start);
        Assert.True(schedule.IsSubscribed);
        Assert.Null(schedule.NextPrompt);
        Assert.False(schedule.IsDue(At(365)));
        // Subscribed after the first prompt: no second one.
        var later = new StayInTouchSchedule();
        later.RecordShown(Start);
        later.RecordSubscribed(At(1));
        Assert.False(later.IsDue(At(30)));
    }

    [Fact]
    public void DontAskAgainMeansNever()
    {
        var schedule = new StayInTouchSchedule();
        schedule.RecordShown(Start);
        schedule.StopAsking();
        Assert.True(schedule.DontAskAgain);
        Assert.Null(schedule.NextPrompt);
        Assert.False(schedule.IsDue(At(365)));
    }

    [Fact]
    public void ACountWithoutADateIsDue()
    {
        // Settings edited by hand: the count still caps the prompts.
        var schedule = new StayInTouchSchedule(timesShown: 1, lastShown: null);
        Assert.Equal(2, schedule.NextPrompt);
        Assert.True(schedule.IsDue(Start));
    }

    // Storage

    [Fact]
    public void UsesTheMacKeysAndTheExistingSubscribedKey()
    {
        Assert.Equal("org.lumerist.stay-in-touch.times-shown", StayInTouchSchedule.Key.TimesShown);
        Assert.Equal("org.lumerist.stay-in-touch.last-shown", StayInTouchSchedule.Key.LastShown);
        Assert.Equal("org.lumerist.stay-in-touch.dont-ask-again", StayInTouchSchedule.Key.DontAskAgain);
        // The date the first sign-up form wrote: whoever subscribed then is never asked.
        Assert.Equal("lumerist.galerist.subscribed-at", StayInTouchSchedule.Key.SubscribedAt);
        Assert.Equal(GaleristOptIn.SubscribedAtDefaultsKey, StayInTouchSchedule.Key.SubscribedAt);
    }

    [Fact]
    public void ReadsNothingAsNeverShown()
    {
        var schedule = StayInTouchSchedule.Read(_ => null);
        Assert.Equal(new StayInTouchSchedule(), schedule);
        Assert.True(schedule.IsDue(Start));
    }

    [Fact]
    public void AUserWhoSubscribedWithTheFirstFormIsNeverAsked()
    {
        var stored = new Dictionary<string, object?> { ["lumerist.galerist.subscribed-at"] = Start };
        var schedule = StayInTouchSchedule.Read(key => stored.GetValueOrDefault(key));
        Assert.Equal(Start, schedule.SubscribedAt);
        Assert.False(schedule.IsDue(At(1)));
    }

    /// <summary>A store where writing null removes the key, like the app's settings.</summary>
    private static void Store(Dictionary<string, object?> stored, string key, object? value)
    {
        if (value is null) stored.Remove(key);
        else stored[key] = value;
    }

    [Fact]
    public void WritesAndReadsBackEveryField()
    {
        var stored = new Dictionary<string, object?>();
        var schedule = new StayInTouchSchedule();
        schedule.RecordShown(Start);
        schedule.StopAsking();
        schedule.Write((key, value) => Store(stored, key, value));
        Assert.Equal(StayInTouchSchedule.Key.All.Except([StayInTouchSchedule.Key.SubscribedAt]).Order(), stored.Keys.Order());
        Assert.Equal(schedule, StayInTouchSchedule.Read(key => stored.GetValueOrDefault(key)));

        schedule.RecordSubscribed(At(2));
        schedule.Write((key, value) => Store(stored, key, value));
        Assert.Equal(At(2), stored[StayInTouchSchedule.Key.SubscribedAt] as DateTimeOffset?);
        Assert.Equal(schedule, StayInTouchSchedule.Read(key => stored.GetValueOrDefault(key)));
    }

    [Fact]
    public void ToleratesMalformedValues()
    {
        var stored = new Dictionary<string, object?>
        {
            [StayInTouchSchedule.Key.TimesShown] = 99,
            [StayInTouchSchedule.Key.LastShown] = "yesterday",
            [StayInTouchSchedule.Key.DontAskAgain] = "yes",
        };
        var schedule = StayInTouchSchedule.Read(key => stored.GetValueOrDefault(key));
        Assert.Equal(StayInTouchSchedule.MaximumPrompts, schedule.TimesShown);
        Assert.Null(schedule.LastShown);
        Assert.False(schedule.DontAskAgain);
        Assert.False(schedule.IsDue(Start));
        var negative = StayInTouchSchedule.Read(key => key == StayInTouchSchedule.Key.TimesShown ? -4 : null);
        Assert.Equal(0, negative.TimesShown);
        Assert.Equal(1, negative.NextPrompt);
        // A count stored as a long (JSON numbers often are) reads the same.
        Assert.Equal(2, StayInTouchSchedule.Read(key => key == StayInTouchSchedule.Key.TimesShown ? 2L : null).TimesShown);
    }
}
