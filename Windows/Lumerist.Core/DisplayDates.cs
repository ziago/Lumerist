using System.Globalization;

namespace Lumerist.Core;

/// <summary>
/// Dates as the Mac shows them in English (the interface is English on both
/// systems): "Nov 20, 2026" and "2 days ago" / "yesterday".
/// </summary>
public static class DisplayDates
{
    /// <summary>The Mac's <c>date.formatted(date: .abbreviated, time: .omitted)</c>: "Nov 20, 2026".</summary>
    public static string Abbreviated(DateTimeOffset date) =>
        date.ToString("MMM d, yyyy", CultureInfo.InvariantCulture);

    /// <summary>
    /// The Mac's <c>.relative(presentation: .named)</c>: the largest calendar
    /// unit that differs ("3 weeks ago", "in 2 hours"), with the named forms
    /// "now", "yesterday", "tomorrow", "last week", "next month"… Calendar
    /// units are counted on the clock of <paramref name="now"/>; pass local time.
    /// </summary>
    public static string RelativeNamed(DateTimeOffset date, DateTimeOffset now)
    {
        var past = date < now;
        var other = date.ToOffset(now.Offset).DateTime;
        var (start, end) = past ? (other, now.DateTime) : (now.DateTime, other);

        var years = end.Year - start.Year;
        if (years > 0 && start.AddYears(years) > end) years--;
        var cursor = start.AddYears(years);
        var months = (end.Year - cursor.Year) * 12 + end.Month - cursor.Month;
        if (months > 0 && cursor.AddMonths(months) > end) months--;
        cursor = cursor.AddMonths(months);
        var rest = end - cursor;
        var weeks = rest.Days / 7;
        var days = rest.Days % 7;

        (int Value, string Unit, string Previous, string Next)[] units =
        [
            (years, "year", "last year", "next year"),
            (months, "month", "last month", "next month"),
            (weeks, "week", "last week", "next week"),
            (days, "day", "yesterday", "tomorrow"),
            (rest.Hours, "hour", "", ""),
            (rest.Minutes, "minute", "", ""),
            (rest.Seconds, "second", "", ""),
        ];
        foreach (var (value, unit, previous, next) in units)
        {
            if (value <= 0) continue;
            if (value == 1 && previous.Length > 0) return past ? previous : next;
            var amount = value == 1 ? $"1 {unit}" : $"{value} {unit}s";
            return past ? $"{amount} ago" : $"in {amount}";
        }
        return "now";
    }
}
