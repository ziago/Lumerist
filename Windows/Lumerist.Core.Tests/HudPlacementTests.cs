using Lumerist.Core;
using Xunit;

namespace Lumerist.Core.Tests;

/// <summary>
/// Where the Value HUD appears and what a dropped sample stands for (the
/// Mac's ValueHUD.bandY / origin / position, with y growing downwards).
/// </summary>
public class HudPlacementTests
{
    // A 1920 × 1080 screen with a 40 px taskbar at the bottom.
    private static readonly HudRect Visible = new(0, 0, 1920, 1040);
    private const double Width = 326, Height = 169;

    private static (double X, double Y) Origin(HudBand band, double vertical = 0, double horizontal = 0) =>
        HudPlacement.Origin(Width, Height, new HudPosition(band, vertical, horizontal), Visible);

    [Fact]
    public void BottomIsCentredEightyPixelsAboveTheEdgeLessTheGlow()
    {
        Assert.Equal((797.0, 809.0), Origin(HudBand.Bottom));
    }

    [Fact]
    public void CentreAndTopBands()
    {
        // 520 − 84.5 = 435.5 from the top: the Mac rounds the bottom edge (435.5 → 436 up from the bottom).
        Assert.Equal((797.0, 435.0), Origin(HudBand.Center));
        Assert.Equal((797.0, 62.0), Origin(HudBand.Top));
    }

    [Fact]
    public void OffsetsMoveUpAndRight()
    {
        Assert.Equal((747.0, 709.0), Origin(HudBand.Bottom, vertical: 100, horizontal: -50));
        Assert.Equal((847.0, 112.0), Origin(HudBand.Top, vertical: -50, horizontal: 50));
    }

    [Fact]
    public void TheHudStaysOnScreen()
    {
        Assert.Equal((1594.0, 0.0), Origin(HudBand.Bottom, vertical: 4_000, horizontal: 8_000));
        Assert.Equal((0.0, 871.0), Origin(HudBand.Top, vertical: -4_000, horizontal: -8_000));
    }

    [Fact]
    public void ScreensAwayFromTheOriginWork()
    {
        var right = new HudRect(1920, -200, 2560, 1400);
        var origin = HudPlacement.Origin(Width, Height, new HudPosition(HudBand.Bottom, 0, 0), right);
        Assert.Equal((1920.0 + 1280 - 163, -200.0 + 1400 - 62 - 169), origin);
    }

    [Fact]
    public void ADropReadsBackAsTheSamePosition()
    {
        var (x, y) = Origin(HudBand.Bottom, vertical: 100, horizontal: -50);
        Assert.Equal(new HudPosition(HudBand.Bottom, 100, -50), HudPlacement.Position(new HudRect(x, y, Width, Height), Visible));
    }

    [Fact]
    public void TheNearestThirdChoosesTheBand()
    {
        Assert.Equal(new HudPosition(HudBand.Top, -38, 0), HudPlacement.Position(new HudRect(797, 100, Width, Height), Visible));
        // 435.5 − 400 = 35.5, rounded away from zero.
        Assert.Equal(new HudPosition(HudBand.Center, 36, 0), HudPlacement.Position(new HudRect(797, 400, Width, Height), Visible));
        Assert.Equal(HudBand.Bottom, HudPlacement.Position(new HudRect(797, 700, Width, Height), Visible).Band);
    }

    [Fact]
    public void ADropNearTheCentreLineSnapsOntoIt()
    {
        Assert.Equal(0, HudPlacement.Position(new HudRect(797 + 11, 809, Width, Height), Visible).HorizontalOffset);
        Assert.Equal(-12, HudPlacement.Position(new HudRect(797 - 12, 809, Width, Height), Visible).HorizontalOffset);
        Assert.Equal(0, HudPlacement.Position(new HudRect(797, 809, Width, Height), Visible).VerticalOffset);
    }
}
