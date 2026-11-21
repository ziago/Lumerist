namespace Lumerist.Core;

/// <summary>
/// Converts raw wheel values into whole slider steps.
/// <para>
/// Notched wheels report line deltas that map one-to-one onto steps.
/// Precision touchpads and high-resolution wheels report many small point
/// deltas, which are accumulated so a short swipe does not jump the slider.
/// Momentum (inertia after the fingers lift) is ignored so the slider stops
/// with them.
/// </para>
/// </summary>
internal sealed class ScrollStepAccumulator
{
    public const int MaximumStepsPerEvent = 10;

    private double pointsPerStep = InputOptions.DefaultTrackpadPointsPerStep;
    private double residual;

    /// <summary>
    /// Touchpad travel per step for this accumulator (user sensitivity), kept
    /// within <see cref="InputOptions.TrackpadPointsPerStepMinimum"/> …
    /// <see cref="InputOptions.TrackpadPointsPerStepMaximum"/>.
    /// </summary>
    public double PointsPerStep
    {
        get => pointsPerStep;
        set => pointsPerStep = double.IsNaN(value)
            ? InputOptions.TrackpadPointsPerStepMinimum
            : Math.Clamp(value, InputOptions.TrackpadPointsPerStepMinimum, InputOptions.TrackpadPointsPerStepMaximum);
    }

    public void Reset() => residual = 0;

    public int Steps(double lineDelta, double pointDelta, bool isContinuous, bool isMomentum)
    {
        if (isMomentum) return 0;

        if (!isContinuous)
        {
            if (!double.IsFinite(lineDelta)) return 0;
            var whole = Math.Truncate(lineDelta);
            // Some smooth-scrolling mice report fractional lines: never lose a notch.
            if (whole == 0 && lineDelta != 0) whole = lineDelta > 0 ? 1 : -1;
            return Limited(whole);
        }

        if (!double.IsFinite(pointDelta) || pointDelta == 0) return 0;
        if (residual != 0 && (residual > 0) != (pointDelta > 0))
        {
            residual = 0; // Direction reversed: start from the new direction.
        }
        residual += pointDelta;
        var steps = Math.Truncate(residual / pointsPerStep);
        residual -= steps * pointsPerStep;
        return Limited(steps);
    }

    /// <summary>At most <see cref="MaximumStepsPerEvent"/> steps either way (clamped before the int conversion).</summary>
    private static int Limited(double steps) => (int)Math.Clamp(steps, -MaximumStepsPerEvent, MaximumStepsPerEvent);
}
