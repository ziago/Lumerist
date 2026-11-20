namespace Lumerist.Core;

/// <summary>The band of the screen the Value HUD is measured from.</summary>
public enum HudBand
{
    Bottom,
    Center,
    Top,
}

/// <summary>A rectangle in device-independent pixels, y growing downwards (Windows screen coordinates).</summary>
public readonly record struct HudRect(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;

    public double Bottom => Y + Height;

    public double MidX => X + Width / 2;

    public double MidY => Y + Height / 2;
}

/// <summary>Where the HUD sits: a band of the screen plus offsets (up and right are positive).</summary>
public readonly record struct HudPosition(HudBand Band, double VerticalOffset, double HorizontalOffset);

/// <summary>
/// Where the Value HUD goes on a screen, and the position a dragged sample
/// stands for (the Mac's ValueHUD bandY / origin / position). The Mac counts
/// y upwards; here y grows downwards, so "above" is a smaller y.
/// </summary>
public static class HudPlacement
{
    /// <summary>Room around the read-out for its neon glow.</summary>
    public const double GlowMargin = 18;

    /// <summary>Distance of the bottom and top bands from the screen edge (before the glow margin).</summary>
    public const double EdgeInset = 80;

    /// <summary>A drop this close to the screen's centre line snaps onto it.</summary>
    public const double CentreSnap = 12;

    /// <summary>The top edge of a read-out of this height placed in a band, with no offset.</summary>
    public static double BandTop(HudBand band, double height, HudRect visible) => band switch
    {
        HudBand.Bottom => visible.Bottom - (EdgeInset - GlowMargin) - height,
        HudBand.Center => visible.MidY - height / 2,
        _ => visible.Y + EdgeInset - GlowMargin,
    };

    /// <summary>
    /// The top-left corner of a read-out of this size: centred on the screen,
    /// in its band, moved by the offsets, kept on screen, on whole pixels.
    /// </summary>
    public static (double X, double Y) Origin(double width, double height, HudPosition position, HudRect visible)
    {
        var x = visible.MidX - width / 2 + position.HorizontalOffset;
        var y = BandTop(position.Band, height, visible) - position.VerticalOffset;
        x = Math.Min(Math.Max(x, visible.X), visible.Right - width);
        y = Math.Min(Math.Max(y, visible.Y), visible.Bottom - height);
        // Rounded as the Mac rounds it: the bottom edge's height above the screen's bottom.
        var bottomUp = Math.Round(visible.Bottom - (y + height), MidpointRounding.AwayFromZero);
        return (Math.Round(x, MidpointRounding.AwayFromZero), visible.Bottom - height - bottomUp);
    }

    /// <summary>
    /// The position that puts a read-out at <paramref name="frame"/> on the
    /// screen whose visible area is <paramref name="visible"/>: the band (by
    /// thirds) its centre is in, and the offsets from that band.
    /// </summary>
    public static HudPosition Position(HudRect frame, HudRect visible)
    {
        var third = visible.Height / 3;
        var band = frame.MidY > visible.Bottom - third ? HudBand.Bottom
            : frame.MidY < visible.Y + third ? HudBand.Top
            : HudBand.Center;
        var horizontal = frame.MidX - visible.MidX;
        if (Math.Abs(horizontal) < CentreSnap) horizontal = 0;
        var vertical = BandTop(band, frame.Height, visible) - frame.Y;
        return new HudPosition(band,
            Math.Round(vertical, MidpointRounding.AwayFromZero),
            Math.Round(horizontal, MidpointRounding.AwayFromZero));
    }
}
