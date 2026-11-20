namespace Lumerist.Core;

/// <summary>
/// Sums slider changes between sends so Lightroom receives at most one change
/// per parameter per tick. Consecutive changes to the same parameter are
/// added; order is kept; totals above the parameter's limit are split; zero
/// totals are dropped. Other intents pass through in order.
/// <para>
/// Usage: <see cref="Add"/> every intent; when it returns true (an intent that
/// is not a slider change), <see cref="Flush"/> now so the order is kept,
/// otherwise flush at the update rate.
/// </para>
/// </summary>
public sealed class IntentCoalescer
{
    /// <summary>Update rates offered to the user, in Hz (how often adjustments are sent).</summary>
    public const double RateMinimum = 10, RateMaximum = 60;

    public const double DefaultRate = 30;

    private readonly List<KeyIntent> queue = [];

    public bool IsEmpty => queue.Count == 0;

    /// <summary>Returns true when the queue should be flushed immediately.</summary>
    public bool Add(KeyIntent intent)
    {
        if (intent is KeyIntent.Adjust adjust &&
            queue.Count > 0 &&
            queue[^1] is KeyIntent.Adjust last &&
            last.Key == adjust.Key)
        {
            queue[^1] = new KeyIntent.Adjust(adjust.Key, last.Delta + adjust.Delta);
            return false;
        }
        queue.Add(intent);
        return intent.IsImmediate;
    }

    public IReadOnlyList<KeyIntent> Flush()
    {
        var result = new List<KeyIntent>();
        foreach (var intent in queue)
        {
            if (intent is not KeyIntent.Adjust adjust)
            {
                result.Add(intent);
                continue;
            }
            var limit = ParameterCatalog.Descriptor(adjust.Key)?.MaximumDelta ?? double.PositiveInfinity;
            var remaining = Round(adjust.Delta);
            while (remaining != 0)
            {
                var part = Math.Max(-limit, Math.Min(limit, remaining));
                result.Add(new KeyIntent.Adjust(adjust.Key, part));
                remaining = Round(remaining - part);
            }
        }
        queue.Clear();
        return result;
    }

    /// <summary>Four decimals, halves away from zero; anything not finite becomes 0.</summary>
    internal static double Round(double value)
    {
        if (!double.IsFinite(value)) return 0;
        return Math.Round(value * 10_000, MidpointRounding.AwayFromZero) / 10_000;
    }
}
