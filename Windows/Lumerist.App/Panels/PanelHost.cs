using System.Windows;
using System.Windows.Threading;
using Lumerist.App.Design;
using Lumerist.App.Services;
using Lumerist.App.Views;
using Lumerist.Core;
using static Lumerist.App.Interop.Native;

namespace Lumerist.App.Panels;

/// <summary>
/// The panels that float over Lightroom (the Mac's FloatingPanels and the
/// panel side of CommandStore): the Value HUD, the Palette and the Key
/// Reference. Everything runs on the UI thread. It also offers the Floating
/// panels settings page what it needs (<see cref="IPanelSettings"/>).
/// </summary>
internal sealed class PanelHost : IPanelHost, IPanelSettings
{
    private const int VK_SHIFT = 0x10, VK_MENU = 0x12;
    /// <summary>How often the held modifiers are read while the Palette is up over a selected mask.</summary>
    private static readonly TimeSpan ModifierPollInterval = TimeSpan.FromMilliseconds(50);

    private readonly AppController controller;
    private readonly ValueHud hud;
    private FloatingPanel? palette;
    private FloatingPanel? keyReference;
    private KeyReferenceView? keyReferenceView;
    /// <summary>The Palette Key holds it open, or a tap left it open (the Mac's store.isPaletteOpen).</summary>
    private bool isPaletteOpen;
    private bool isPreviewingPalette;
    private bool isKeyReferenceVisible;
    /// <summary>What a click would do with the modifiers held right now; drives the highlight only.</summary>
    private PaletteMaskMode heldMode = PaletteMaskMode.New;
    private DispatcherTimer? modifierPoll;

    public PanelHost(AppController controller)
    {
        this.controller = controller;
        hud = new ValueHud(Dispatcher.CurrentDispatcher);
        hud.PlacingChanged += () => StateChanged?.Invoke();
        // Miami ↔ Galerist: the panels are built again in the new look at once,
        // even while they are on screen.
        Theme.Changed += RebuildForTheme;
        controller.KeymapChanged += () =>
        {
            if (palette?.IsVisible == true) RebuildPalette();
            if (keyReference?.IsVisible == true) RebuildKeyReference();
        };
    }

    /// <summary>
    /// Set by the app: tells the gesture engine that the Palette was closed
    /// here (× clicked, an item picked, Lightroom left the mask), as the Mac's
    /// store.closePalette() calls monitor.paletteDidClose(). Without it the
    /// next press of the Palette Key would only close a Palette already gone.
    /// </summary>
    public Action? PaletteDidClose { get; set; }

    /// <summary>
    /// The HUD's sample started or stopped being placed, or the Key Reference
    /// appeared or went (the settings page's buttons, the tray's "Show Key Reference").
    /// </summary>
    public event Action? StateChanged;

    public bool IsKeyReferenceVisible => isKeyReferenceVisible;

    // IPanelHost ------------------------------------------------------------------------

    public void OpenPalette()
    {
        isPaletteOpen = true;
        Sync();
    }

    public void ClosePalette()
    {
        // The engine closed it (and AppController tells the engine itself).
        isPaletteOpen = false;
        Sync();
    }

    public void ToggleKeyReference() => SetKeyReferenceVisible(!isKeyReferenceVisible);

    public void ShowNotice(string text) => hud.Show(HudReadout.Notice(text), controller.Settings.Overlay);

    public void ShowAck(LightroomAck ack)
    {
        if (HudReadout.ForAck(ack) is { } readout) hud.Show(readout, controller.Settings.Overlay);
    }

    public void ContextChanged(LightroomContext context)
    {
        // Leaving a mask (or the module) closes the Palette, as on the Mac.
        if (!context.IsMasking && isPaletteOpen) StoreClosePalette();
        if (palette?.IsVisible == true) RebuildPalette();
        if (keyReference?.IsVisible == true) RebuildKeyReference();
        UpdateModifierPoll();
    }

    // For the settings page and the tray -------------------------------------------------

    /// <summary>
    /// Shows the HUD's sample with the overlay settings as they are now, then
    /// hides it as the HUD normally would; while placing, restyles the sample.
    /// </summary>
    public void PreviewHud() => hud.Preview(controller.Settings.Overlay);

    /// <summary>Hides the sample if it is showing (a real read-out stays); ends placing.</summary>
    public void HideHudPreview() => hud.HidePreview();

    public bool IsPlacingHud => hud.IsPlacing;

    /// <summary>"Place the HUD": a sample the mouse can drag; each drop is written to the overlay settings and saved.</summary>
    public void BeginPlacingHud() => hud.BeginPlacing(controller.Settings.Overlay, position =>
    {
        ValueHud.Apply(position, controller.Settings.Overlay);
        controller.SaveSettings();
    });

    public void EndPlacingHud() => hud.EndPlacing();

    /// <summary>
    /// The Palette as it looks over Lightroom now, with the user's
    /// favourites, for the settings page: its controls do nothing.
    /// </summary>
    public FrameworkElement? CreatePalettePreview()
    {
        var keymap = controller.Keymap;
        var context = controller.Context;
        var frame = PaletteView.Build(new PaletteState(keymap, keymap.Palette, context.Module, context.IsMasking, PaletteMaskMode.New),
            new PaletteHandlers(_ => { }, _ => { }, () => { }, () => { }));
        frame.Root.IsHitTestVisible = false;
        return frame.Root;
    }

    /// <summary>Shows the Palette without its key (a development aid, as on the Mac).</summary>
    public void PreviewPalette()
    {
        isPreviewingPalette = true;
        ShowPalette();
    }

    /// <summary>The Palette's own × (and a picked item when it is not pinned).</summary>
    public void ClosePaletteFromPanel()
    {
        isPreviewingPalette = false;
        StoreClosePalette();
        palette?.Hide();
        Sync();
    }

    public void SetKeyReferenceVisible(bool visible)
    {
        var changed = visible != isKeyReferenceVisible;
        isKeyReferenceVisible = visible;
        Sync();
        if (changed) StateChanged?.Invoke();
    }

    public void ResetPalettePosition()
    {
        controller.Settings.PaletteOrigin = null;
        controller.SaveSettings();
        if (palette?.IsVisible == true) ShowPalette();
    }

    public void ResetKeyReferencePosition()
    {
        controller.Settings.KeyReferenceOrigin = null;
        controller.SaveSettings();
        if (keyReference?.IsVisible == true) ShowKeyReference();
    }

    // Showing and hiding -------------------------------------------------------------------

    /// <summary>The Mac's store.closePalette(): the Palette is no longer open, and the engine is told.</summary>
    private void StoreClosePalette()
    {
        if (!isPaletteOpen) return;
        isPaletteOpen = false;
        PaletteDidClose?.Invoke();
        Sync();
    }

    private void Sync()
    {
        if (isPaletteOpen || isPreviewingPalette)
        {
            if (palette?.IsVisible != true) ShowPalette();
        }
        else
        {
            palette?.Hide();
        }
        if (isKeyReferenceVisible)
        {
            if (keyReference?.IsVisible != true) ShowKeyReference();
        }
        else
        {
            keyReference?.Hide();
        }
        UpdateModifierPoll();
    }

    private void RebuildForTheme()
    {
        hud.ThemeDidChange();
        if (palette?.IsVisible == true) RebuildPalette();
        if (keyReference?.IsVisible == true) RebuildKeyReference(resetSearch: true);
    }

    // Palette ---------------------------------------------------------------------------

    private void ShowPalette()
    {
        palette ??= new FloatingPanel("Lumerist Palette", () => controller.Settings.PaletteOrigin, origin =>
        {
            controller.Settings.PaletteOrigin = origin;
            controller.SaveSettings();
        });
        RebuildPalette();
        // At the pointer, slightly offset so the cursor sits over the first tools.
        var (x, y) = PanelNative.Cursor();
        var scale = (PanelNative.MonitorAt(x, y) ?? PanelNative.PointerMonitor()).Scale;
        palette.Present(((int)Math.Round(x - 40 * scale), (int)Math.Round(y - 30 * scale)));
        UpdateModifierPoll();
    }

    private void RebuildPalette()
    {
        if (palette is null) return;
        var keymap = controller.Keymap;
        var context = controller.Context;
        var state = new PaletteState(keymap, keymap.Palette, context.Module, context.IsMasking, heldMode);
        var frame = PaletteView.Build(state, new PaletteHandlers(RunTool, RunFavourite, TogglePin, ClosePaletteFromPanel));
        palette.SetContent(frame.Root, frame.TitleBar, frame.CornerRadius);
    }

    /// <summary>The mode the modifiers give at the moment of the click.</summary>
    /// <remarks>
    /// The click goes to the Palette, not to Lightroom, so Lightroom saw Alt
    /// down with nothing after it and would open its menu bar when Alt goes
    /// up: a tap of an unused key, sent to Lightroom, breaks that sequence.
    /// </remarks>
    private static PaletteMaskMode CurrentMode(bool masking) =>
        PaletteMaskModes.From(IsKeyDown(VK_SHIFT), IsKeyDown(VK_MENU), masking);

    /// <summary>The mode at a click; with Alt held, also keeps Lightroom's menu bar closed (see <see cref="CurrentMode"/>).</summary>
    private static PaletteMaskMode ModeAtClick(bool masking)
    {
        if (IsKeyDown(VK_MENU)) Input.InputSender.SendMenuMask();
        return CurrentMode(masking);
    }

    private void RunTool(string argument) =>
        Run(PaletteMenu.MaskAction(argument, ModeAtClick(controller.Context.IsMasking)));

    private void RunFavourite(PaletteEntry entry) =>
        Run(PaletteMenu.Action(entry, ModeAtClick(controller.Context.IsMasking)));

    private void Run(CommandAction action)
    {
        controller.Run(action);
        if (!controller.Keymap.Palette.KeepVisible) ClosePaletteFromPanel();
    }

    /// <summary>Deferred: the new keymap rebuilds the Palette, whose pin is handling the click.</summary>
    private void TogglePin() => Dispatcher.CurrentDispatcher.BeginInvoke(() =>
    {
        var keymap = controller.Keymap.Clone();
        keymap.Palette.KeepVisible = !keymap.Palette.KeepVisible;
        if (controller.UpdateKeymap(keymap) is { } error) Log.Write($"Palette pin not saved: {error}");
    });

    /// <summary>Follows the modifiers while the Palette is up over a selected mask.</summary>
    private void UpdateModifierPoll()
    {
        var wanted = isPaletteOpen && controller.Context.IsMasking && palette?.IsVisible == true;
        if (wanted == (modifierPoll is not null)) return;
        modifierPoll?.Stop();
        modifierPoll = null;
        var changed = heldMode != PaletteMaskMode.New;
        heldMode = PaletteMaskMode.New;
        if (wanted)
        {
            modifierPoll = new DispatcherTimer(DispatcherPriority.Input) { Interval = ModifierPollInterval };
            modifierPoll.Tick += (_, _) =>
            {
                var mode = CurrentMode(masking: true);
                if (mode == heldMode) return;
                heldMode = mode;
                RebuildPalette();
            };
            modifierPoll.Start();
        }
        if (changed && palette?.IsVisible == true) RebuildPalette();
    }

    // Key Reference -----------------------------------------------------------------------

    private void ShowKeyReference()
    {
        if (keyReference is null)
        {
            keyReference = new FloatingPanel("Lumerist Key Reference", () => controller.Settings.KeyReferenceOrigin, origin =>
            {
                controller.Settings.KeyReferenceOrigin = origin;
                controller.SaveSettings();
            });
            var panel = keyReference;
            keyReferenceView = new KeyReferenceView(controller.Keymap, controller.Context, () => SetKeyReferenceVisible(false),
                panel.StartTakingFocus);
        }
        keyReferenceView?.Update(controller.Keymap, controller.Context);
        RebuildKeyReference();
        // Near the top right of the screen Lightroom is on.
        var foreground = GetForegroundWindow();
        var monitor = foreground != 0 ? PanelNative.WindowMonitor(foreground) : PanelNative.PrimaryMonitor();
        var work = monitor.WorkArea;
        keyReference.Present(((int)Math.Round(work.Right - 360 * monitor.Scale), (int)Math.Round(work.Top + 40 * monitor.Scale)));
    }

    private void RebuildKeyReference(bool resetSearch = false)
    {
        if (keyReference is null || keyReferenceView is null) return;
        keyReferenceView.Update(controller.Keymap, controller.Context);
        var frame = keyReferenceView.Build(resetSearch);
        keyReference.SetContent(frame.Root, frame.TitleBar, frame.CornerRadius);
    }
}
