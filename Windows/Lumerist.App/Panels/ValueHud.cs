using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Lumerist.App.Design;
using Lumerist.App.Input;
using Lumerist.App.Services;
using Lumerist.Core;

namespace Lumerist.App.Panels;

/// <summary>
/// The Value HUD (the Mac's ValueHUD): a small instrument read-out that shows
/// the slider being adjusted. It never takes the focus or the mouse, so
/// Lightroom stays in front and the keys keep working.
/// </summary>
/// <remarks>
/// "Place the HUD" in the settings puts a sample on screen that the mouse can
/// drag anywhere; each drop is stored as a band of the screen (bottom,
/// centre, top) plus offsets in device-independent pixels.
/// </remarks>
internal sealed class ValueHud
{
    private readonly Dispatcher dispatcher;
    private HudWindow? window;
    private int hideGeneration;
    /// <summary>The window shows the preview sample rather than a real adjustment.</summary>
    private bool showsSample;
    private PlacingSession? placing;
    /// <summary>What is on screen, so a theme change can draw it again in place.</summary>
    private (HudReadout Content, OverlaySettings Settings)? lastShown;
    /// <summary>Lightroom's window the last time it was in front.</summary>
    private nint lightroomWindow;

    private sealed class PlacingSession(OverlaySettings settings, Action<HudPosition> onPlaced, PanelNative.Monitor screen)
    {
        public OverlaySettings Settings { get; set; } = settings;

        public Action<HudPosition> OnPlaced { get; } = onPlaced;

        /// <summary>
        /// Where the sample is shown: the monitor under the pointer at the
        /// start (where the settings are), then the one it was dropped on.
        /// Offsets are relative, so they carry over to the HUD's own monitor.
        /// </summary>
        public PanelNative.Monitor Screen { get; set; } = screen;
    }

    /// <summary>The HUD's own window: also click-through, except while it is placed.</summary>
    private sealed class HudWindow : NonActivatingWindow
    {
        public HudWindow()
        {
            Title = "Lumerist Value HUD";
            SourceInitialized += (_, _) => PanelNative.SetExtendedStyle(this, PanelNative.WS_EX_TRANSPARENT, 0);
        }
    }

    public ValueHud(Dispatcher dispatcher) => this.dispatcher = dispatcher;

    /// <summary>True while the sample is on screen to be dragged into place.</summary>
    public bool IsPlacing => placing is not null;

    /// <summary>Placing started or ended (the settings screen's button follows it).</summary>
    public event Action? PlacingChanged;

    /// <summary>Shows a read-out. Does nothing when the HUD is off or while it is being placed.</summary>
    public void Show(HudReadout content, OverlaySettings settings)
    {
        if (!settings.IsEnabled || IsPlacing) return;
        showsSample = false;
        Present(content, settings);
    }

    /// <summary>
    /// Shows a sample adjustment so style and position can be judged while
    /// they change. Works even when the HUD is turned off. While the HUD is
    /// being placed, it restyles the draggable sample instead.
    /// </summary>
    public void Preview(OverlaySettings settings)
    {
        if (placing is { } session)
        {
            session.Settings = Copy(settings);
            PresentPlacing();
            return;
        }
        showsSample = true;
        Present(HudReadout.Sample, settings, forceHide: !settings.IsEnabled);
    }

    /// <summary>Hides the preview sample; a real read-out stays.</summary>
    public void HidePreview()
    {
        if (!showsSample) return;
        Hide();
    }

    public void Hide()
    {
        if (IsPlacing) FinishPlacing();
        hideGeneration++;
        showsSample = false;
        window?.Hide();
    }

    // Placing ---------------------------------------------------------------------

    /// <summary>
    /// Puts the sample on screen, draggable, until <see cref="EndPlacing"/> or
    /// its own Done button. Each drop reports the new position.
    /// </summary>
    public void BeginPlacing(OverlaySettings settings, Action<HudPosition> onPlaced)
    {
        placing = new PlacingSession(Copy(settings), onPlaced, PanelNative.PointerMonitor());
        PlacingChanged?.Invoke();
        PresentPlacing();
    }

    /// <summary>Stops placing: the sample shows once more at its new spot, then hides as it normally would.</summary>
    public void EndPlacing()
    {
        if (placing is not { } session) return;
        FinishPlacing();
        Preview(session.Settings);
    }

    private void FinishPlacing()
    {
        placing = null;
        if (window is not null) PanelNative.SetExtendedStyle(window, PanelNative.WS_EX_TRANSPARENT, 0);
        PlacingChanged?.Invoke();
    }

    private void PresentPlacing()
    {
        if (placing is not { } session) return;
        var settings = session.Settings;
        var hud = MakeWindowIfNeeded();
        // Deferred: both replace the view that is handling the click.
        var view = HudView.Build(HudReadout.Sample, settings.Style, settings.ClampedBackgroundOpacity, new HudPlacingHandlers(
            handle => WindowDrag.Begin(hud, handle, () => dispatcher.BeginInvoke(PlacingDropped)),
            () => dispatcher.BeginInvoke(EndPlacing)));
        hud.Content = view;
        var size = hud.MeasureContent();
        // The caption hangs under the read-out, which sits exactly where the
        // real HUD appears on that monitor.
        var (x, y) = PixelOrigin(new Size(size.Width, size.Height - HudView.PlacingCaptionHeight), settings, session.Screen);
        PanelNative.SetExtendedStyle(hud, 0, PanelNative.WS_EX_TRANSPARENT);
        var wasHidden = Prepare(hud);
        Place(hud, x, y, size, session.Screen);
        showsSample = true;
        hideGeneration++; // No fade while placing.
        Reveal(hud, wasHidden, size);
    }

    /// <summary>
    /// The sample was dropped: store where it is, then settle it where the
    /// real HUD will appear (kept inside the screen).
    /// </summary>
    private void PlacingDropped()
    {
        if (window is null || placing is not { } session) return;
        var frame = PanelNative.Frame(window);
        var scale = PanelNative.WindowMonitor(PanelNative.Handle(window)).Scale;
        var readout = new Rect(frame.X, frame.Y, frame.Width, Math.Max(0, frame.Height - HudView.PlacingCaptionHeight * scale));
        var monitor = PanelNative.MonitorFor(readout);
        var dips = new HudRect(readout.X / monitor.Scale, readout.Y / monitor.Scale, readout.Width / monitor.Scale,
            readout.Height / monitor.Scale);
        var position = HudPlacement.Position(dips, ToHudRect(monitor.WorkAreaDips));
        Apply(position, session.Settings);
        session.Screen = monitor;
        session.OnPlaced(position);
        PresentPlacing();
    }

    // Showing ---------------------------------------------------------------------

    /// <summary>Draws what is on screen again in the new theme, where it is, without changing when it hides.</summary>
    public void ThemeDidChange()
    {
        if (window is not { IsVisible: true } hud) return;
        if (placing is not null)
        {
            PresentPlacing();
            return;
        }
        if (lastShown is not { } shown) return;
        hud.Content = HudView.Build(shown.Content, shown.Settings.Style, shown.Settings.ClampedBackgroundOpacity);
        var size = hud.MeasureContent();
        var monitor = ScreenFor(shown.Settings);
        var (x, y) = PixelOrigin(size, shown.Settings, monitor);
        Place(hud, x, y, size, monitor);
    }

    private void Present(HudReadout content, OverlaySettings settings, bool forceHide = false)
    {
        var hud = MakeWindowIfNeeded();
        lastShown = (content, Copy(settings));
        hud.Content = HudView.Build(content, settings.Style, settings.ClampedBackgroundOpacity);
        var size = hud.MeasureContent();
        var monitor = ScreenFor(settings);
        var (x, y) = PixelOrigin(size, settings, monitor);
        PanelNative.SetExtendedStyle(hud, PanelNative.WS_EX_TRANSPARENT, 0);
        var wasHidden = Prepare(hud);
        Place(hud, x, y, size, monitor);
        Reveal(hud, wasHidden, size);
        if (settings.KeepVisible && !forceHide) hideGeneration++; // Cancel any pending fade.
        else ScheduleHide(settings.ClampedDuration);
    }

    /// <summary>A hidden window is shown fully transparent, so it never flashes at its old place.</summary>
    private static bool Prepare(HudWindow hud)
    {
        if (hud.IsVisible) return false;
        hud.BeginAnimation(UIElement.OpacityProperty, null);
        hud.Opacity = 0;
        hud.ShowWithoutActivating();
        return true;
    }

    private static void Place(HudWindow hud, int x, int y, Size size, PanelNative.Monitor monitor) =>
        PanelNative.Place(hud, x, y, (int)Math.Ceiling(size.Width * monitor.Scale), (int)Math.Ceiling(size.Height * monitor.Scale));

    /// <summary>Brings the window up if it was hidden or is fading.</summary>
    private static void Reveal(HudWindow hud, bool wasHidden, Size size)
    {
        // A fading window reads below 1 (the animated value).
        if (!wasHidden && hud.Opacity >= 1) return;
        PanelNative.BringToTop(hud);
        if (wasHidden && SystemParameters.ClientAreaAnimation && hud.Content is FrameworkElement content)
        {
            hud.BeginAnimation(UIElement.OpacityProperty, null);
            hud.Opacity = 1;
            ScanLineWipe(content, size);
        }
        else
        {
            hud.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1, new Duration(Theme.OverlayIn)));
        }
    }

    /// <summary>The read-out is revealed top to bottom in 120 ms, like a scan line.</summary>
    private static void ScanLineWipe(FrameworkElement content, Size size)
    {
        var mask = new RectangleGeometry(new Rect(0, 0, size.Width, size.Height * 0.02));
        content.Clip = mask;
        var wipe = new RectAnimation(new Rect(0, 0, size.Width, size.Height), new Duration(Theme.OverlayIn))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
        };
        wipe.Completed += (_, _) =>
        {
            if (ReferenceEquals(content.Clip, mask)) content.Clip = null;
        };
        mask.BeginAnimation(RectangleGeometry.RectProperty, wipe);
    }

    private void ScheduleHide(double seconds)
    {
        hideGeneration++;
        var generation = hideGeneration;
        After(TimeSpan.FromSeconds(seconds), () => FadeOut(generation));
    }

    private void FadeOut(int generation)
    {
        if (generation != hideGeneration || window is not { } hud) return;
        hud.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, new Duration(Theme.OverlayOut)));
        After(Theme.OverlayOut + TimeSpan.FromMilliseconds(50), () =>
        {
            // A newer show in the meantime keeps the window on screen.
            if (generation != hideGeneration) return;
            hud.Hide();
            showsSample = false;
        });
    }

    private void After(TimeSpan delay, Action action)
    {
        var timer = new DispatcherTimer(DispatcherPriority.Normal, dispatcher) { Interval = delay };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            action();
        };
        timer.Start();
    }

    private HudWindow MakeWindowIfNeeded()
    {
        if (window is not null) return window;
        window = new HudWindow();
        PanelNative.Handle(window);
        return window;
    }

    // Position --------------------------------------------------------------------

    /// <summary>The monitor chosen in the settings: Lightroom's, or the pointer's.</summary>
    private PanelNative.Monitor ScreenFor(OverlaySettings settings) =>
        (settings.FollowsLightroomWindow ? LightroomMonitor() : null) ?? PanelNative.PointerMonitor();

    /// <summary>
    /// The monitor that holds most of Lightroom Classic's window: the one in
    /// front, else the one that was last in front.
    /// </summary>
    private PanelNative.Monitor? LightroomMonitor()
    {
        if (LightroomWindow.Foreground().IsInFront) lightroomWindow = Interop.Native.GetForegroundWindow();
        return PanelNative.IsUsableWindow(lightroomWindow) ? PanelNative.WindowMonitor(lightroomWindow) : null;
    }

    /// <summary>The top-left corner, in screen pixels, of a read-out of this size placed as the settings say.</summary>
    private static (int X, int Y) PixelOrigin(Size size, OverlaySettings settings, PanelNative.Monitor monitor)
    {
        var (x, y) = HudPlacement.Origin(size.Width, size.Height, PositionOf(settings), ToHudRect(monitor.WorkAreaDips));
        return ((int)Math.Round(x * monitor.Scale), (int)Math.Round(y * monitor.Scale));
    }

    private static HudRect ToHudRect(Rect rect) => new(rect.X, rect.Y, rect.Width, rect.Height);

    private static HudPosition PositionOf(OverlaySettings settings) =>
        new(ToBand(settings.Placement), settings.ClampedVerticalOffset, settings.ClampedHorizontalOffset);

    private static HudBand ToBand(OverlaySettings.PlacementBand band) => band switch
    {
        OverlaySettings.PlacementBand.Center => HudBand.Center,
        OverlaySettings.PlacementBand.Top => HudBand.Top,
        _ => HudBand.Bottom,
    };

    private static OverlaySettings.PlacementBand FromBand(HudBand band) => band switch
    {
        HudBand.Center => OverlaySettings.PlacementBand.Center,
        HudBand.Top => OverlaySettings.PlacementBand.Top,
        _ => OverlaySettings.PlacementBand.Bottom,
    };

    /// <summary>Writes a placed position into the settings (band and offsets).</summary>
    public static void Apply(HudPosition position, OverlaySettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Placement = FromBand(position.Band);
        settings.VerticalOffset = position.VerticalOffset;
        settings.HorizontalOffset = position.HorizontalOffset;
    }

    /// <summary>A copy, so what is on screen does not change under it (the Mac's settings are a value).</summary>
    private static OverlaySettings Copy(OverlaySettings settings) => new()
    {
        IsEnabled = settings.IsEnabled,
        Placement = settings.Placement,
        VerticalOffset = settings.VerticalOffset,
        HorizontalOffset = settings.HorizontalOffset,
        Duration = settings.Duration,
        Style = settings.Style,
        BackgroundOpacity = settings.BackgroundOpacity,
        KeepVisible = settings.KeepVisible,
        FollowsLightroomWindow = settings.FollowsLightroomWindow,
    };
}
