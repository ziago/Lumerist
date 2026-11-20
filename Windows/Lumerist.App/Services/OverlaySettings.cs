using System.Text.Json.Serialization;

namespace Lumerist.App.Services;

/// <summary>
/// User preferences for the on-screen Value HUD (the Mac's OverlaySettings in
/// ValueHUD.swift). Shared by the HUD (Panels) and its settings screen (Views).
/// </summary>
internal sealed class OverlaySettings
{
    /// <summary>
    /// The band of the screen the HUD is measured from. Set by dragging the
    /// HUD (the nearest band wins), so a spot near the top stays near the top
    /// on a smaller display.
    /// </summary>
    public enum PlacementBand
    {
        Bottom,
        Center,
        Top,
    }

    /// <summary>Ruler: name, value and a scale with numbers. Compact: one slim line.</summary>
    public enum HudStyle
    {
        Ruler,
        Compact,
    }

    public static readonly (double Minimum, double Maximum) DurationRange = (0.4, 4);

    /// <summary>Sanity limits only: the HUD is always kept inside the visible screen.</summary>
    public static readonly (double Minimum, double Maximum) VerticalOffsetRange = (-4_000, 4_000);

    public static readonly (double Minimum, double Maximum) HorizontalOffsetRange = (-8_000, 8_000);

    /// <summary>88 % keeps text ≥ 4.5:1 over bright skies; lower values let more of the photo through.</summary>
    public const double DefaultBackgroundOpacity = 0.88;

    public bool IsEnabled { get; set; } = true;

    public PlacementBand Placement { get; set; } = PlacementBand.Bottom;

    /// <summary>Pixels (device-independent) above (positive) or below (negative) the placement's position.</summary>
    public double VerticalOffset { get; set; }

    /// <summary>Pixels right (positive) or left (negative) of the screen's centre.</summary>
    public double HorizontalOffset { get; set; }

    /// <summary>Seconds the HUD stays visible after the last change.</summary>
    public double Duration { get; set; } = 1.2;

    public HudStyle Style { get; set; } = HudStyle.Ruler;

    /// <summary>0…1 opacity of the dark panel behind the read-out.</summary>
    public double BackgroundOpacity { get; set; } = DefaultBackgroundOpacity;

    /// <summary>The HUD stays on screen after a change ("Hide after: Never").</summary>
    public bool KeepVisible { get; set; }

    /// <summary>The display of Lightroom's window (true) or of the pointer (false).</summary>
    public bool FollowsLightroomWindow { get; set; } = true;

    [JsonIgnore]
    public double ClampedDuration => double.IsFinite(Duration) ? Math.Clamp(Duration, DurationRange.Minimum, DurationRange.Maximum) : 1.2;

    [JsonIgnore]
    public double ClampedVerticalOffset =>
        double.IsFinite(VerticalOffset) ? Math.Clamp(VerticalOffset, VerticalOffsetRange.Minimum, VerticalOffsetRange.Maximum) : 0;

    [JsonIgnore]
    public double ClampedHorizontalOffset =>
        double.IsFinite(HorizontalOffset) ? Math.Clamp(HorizontalOffset, HorizontalOffsetRange.Minimum, HorizontalOffsetRange.Maximum) : 0;

    [JsonIgnore]
    public double ClampedBackgroundOpacity => double.IsFinite(BackgroundOpacity) ? Math.Clamp(BackgroundOpacity, 0, 1) : DefaultBackgroundOpacity;

    public static string StyleName(HudStyle style) => style switch
    {
        HudStyle.Compact => "Compact",
        _ => "Ruler",
    };

    public static string StyleDetail(HudStyle style) => style switch
    {
        HudStyle.Compact => "Name, value and a thin bar on one slim line",
        _ => "Name and value above a numbered scale",
    };

    /// <summary>The Mac's Screen choice: "With Lightroom" or "With the pointer".</summary>
    public static string ScreenName(bool followsLightroomWindow) =>
        followsLightroomWindow ? "With Lightroom" : "With the pointer";
}
