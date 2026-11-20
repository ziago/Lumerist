using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Lumerist.App.Panels;
using Lumerist.App.Services;
using Lumerist.Core;

namespace Lumerist.App.Snapshots;

/// <summary>
/// The Value HUD and the floating panels, as the Mac's HUDSnapshots.swift
/// renders them: every HUD style with an exposure, a contrast, a temperature
/// and a notice over a mid-grey "photo", the bright-sky case, the sample being
/// placed, and the Palette (plain, and over a selected mask with Shift held)
/// beside the Key Reference over Lightroom's backdrop.
/// </summary>
internal static class PanelsSnapshots
{
    /// <summary>Mid-grey behind the HUD (the Mac's Color(white: 0.55)).</summary>
    private static readonly Color Photo = Color.FromRgb(0x8C, 0x8C, 0x8C);

    /// <summary>A bright sky (white: 0.92).</summary>
    private static readonly Color Sky = Color.FromRgb(0xEB, 0xEB, 0xEB);

    /// <summary>Lightroom's backdrop around the photo (white: 0.17).</summary>
    private static readonly Color Backdrop = Color.FromRgb(0x2B, 0x2B, 0x2B);

    [ModuleInitializer]
    internal static void Register()
    {
        // The read-out plus its 18 px glow margin on each side; the extra room stays grey.
        const double ruler = 290 + 36, compact = 240 + 36;
        SnapshotRegistry.Add("hud-ruler", ruler, 172, _ => Hud(Exposure(), OverlaySettings.HudStyle.Ruler));
        SnapshotRegistry.Add("hud-compact", compact, 108, _ => Hud(Exposure(), OverlaySettings.HudStyle.Compact));
        SnapshotRegistry.Add("ruler-contrast", ruler, 172, _ => Hud(Contrast(), OverlaySettings.HudStyle.Ruler));
        SnapshotRegistry.Add("compact-contrast", compact, 108, _ => Hud(Contrast(), OverlaySettings.HudStyle.Compact));
        SnapshotRegistry.Add("ruler-temperature", ruler, 172, _ => Hud(Temperature(), OverlaySettings.HudStyle.Ruler));
        SnapshotRegistry.Add("compact-temperature", compact, 108, _ => Hud(Temperature(), OverlaySettings.HudStyle.Compact));
        SnapshotRegistry.Add("ruler-notice", ruler, 110, _ => Hud(Notice(), OverlaySettings.HudStyle.Ruler));
        SnapshotRegistry.Add("compact-notice", compact, 80, _ => Hud(Notice(), OverlaySettings.HudStyle.Compact));
        // Over a bright sky, at the lowest opacity people tend to pick.
        SnapshotRegistry.Add("ruler-exposure-bright", ruler, 172,
            _ => Hud(Exposure(), OverlaySettings.HudStyle.Ruler, opacity: 0.5, background: Sky));
        // The draggable sample, as "Place the HUD" shows it.
        SnapshotRegistry.Add("placing-ruler", ruler, 172 + HudView.PlacingCaptionHeight,
            _ => Hud(Exposure(), OverlaySettings.HudStyle.Ruler, placing: true));
        SnapshotRegistry.Add("placing-compact", compact, 108 + HudView.PlacingCaptionHeight,
            _ => Hud(Exposure(), OverlaySettings.HudStyle.Compact, placing: true));

        SnapshotRegistry.Add("panels", 32 + PaletteView.Width + 28 + PaletteView.Width + 28 + KeyReferenceView.Width + 32, 680, Panels);
        SnapshotRegistry.Add("panels-palette", PaletteView.Width + 64, 480, controller => Backdropped(PlainPalette(controller)));
        SnapshotRegistry.Add("panels-palette-mask", PaletteView.Width + 64, 520, controller => Backdropped(MaskPalette(controller)));
        SnapshotRegistry.Add("panels-key-reference", KeyReferenceView.Width + 64, 680, controller => Backdropped(KeyReference(controller)));
    }

    private static HudReadout Sample(string title, double value, string text, string unit, double minimum, double maximum) => new()
    {
        Title = title,
        ValueText = text,
        Unit = unit,
        Accent = HudAccent.Teal,
        Bar = HudBar.Create(value, minimum, maximum, unit),
        KeyLabel = "Q",
        StepLabel = "Normal",
    };

    private static HudReadout Exposure() => Sample("Exposure", 0.35, "+0.35", "EV", -5, 5);

    private static HudReadout Contrast() => Sample("Contrast", 12, "+12", "pt", -100, 100);

    private static HudReadout Temperature() => Sample("Temperature", 5_600, "5600", "K", 2_000, 50_000);

    private static HudReadout Notice() => HudReadout.Notice("Vibrance is not available in a mask") with { Title = "Vibrance" };

    private static FrameworkElement Hud(HudReadout content, OverlaySettings.HudStyle style, double opacity = 0.88,
        Color? background = null, bool placing = false)
    {
        var view = HudView.Build(content, style, opacity, placing ? new HudPlacingHandlers(_ => { }, () => { }) : null);
        return new Grid { Background = new SolidColorBrush(background ?? Photo), Children = { view } };
    }

    private static readonly IReadOnlyList<PaletteEntry> Favourites =
    [
        new(PaletteEntryKind.Preset, "preview-portrait", null, "Portrait · soft light"),
        new(PaletteEntryKind.Preset, "preview-bw", null, "B&W · contrast"),
        new(PaletteEntryKind.Tool, "createMask", "aiSelection/landscape", "Landscape"),
    ];

    private static readonly PaletteHandlers NoHandlers = new(_ => { }, _ => { }, () => { }, () => { });

    private static FrameworkElement PlainPalette(AppController controller) => PaletteView.Build(
        new PaletteState(controller.Keymap, new PaletteOptions { Favourites = [.. Favourites] }, "develop", false, PaletteMaskMode.New),
        NoHandlers).Root;

    /// <summary>Over a selected mask with Shift held, pinned.</summary>
    private static FrameworkElement MaskPalette(AppController controller) => PaletteView.Build(
        new PaletteState(controller.Keymap, new PaletteOptions { KeepVisible = true, Favourites = [.. Favourites] }, "develop", true,
            PaletteMaskMode.Add),
        NoHandlers).Root;

    private static FrameworkElement KeyReference(AppController controller) =>
        new KeyReferenceView(controller.Keymap, controller.Context, () => { }).Build().Root;

    private static FrameworkElement Panels(AppController controller)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(32) };
        foreach (var panel in new[] { PlainPalette(controller), MaskPalette(controller), KeyReference(controller) })
        {
            panel.VerticalAlignment = VerticalAlignment.Top;
            if (row.Children.Count > 0) panel.Margin = new Thickness(28, 0, 0, 0);
            row.Children.Add(panel);
        }
        return new Grid { Background = new SolidColorBrush(Backdrop), Children = { row } };
    }

    private static FrameworkElement Backdropped(FrameworkElement panel)
    {
        panel.Margin = new Thickness(32);
        panel.VerticalAlignment = VerticalAlignment.Top;
        panel.HorizontalAlignment = HorizontalAlignment.Left;
        return new Grid { Background = new SolidColorBrush(Backdrop), Children = { panel } };
    }
}
