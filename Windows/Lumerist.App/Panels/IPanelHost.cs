using Lumerist.Core;

namespace Lumerist.App.Panels;

/// <summary>
/// The panels that float over Lightroom (the Mac's FloatingPanels): the
/// Value HUD, the Palette and the Key Reference. <see cref="AppController"/>
/// calls these on the UI thread; <see cref="PanelHost"/> implements them.
/// </summary>
internal interface IPanelHost
{
    /// <summary>The Palette Key went down (or was tapped): show the Palette.</summary>
    void OpenPalette();

    /// <summary>The Palette Key went up after a hold: hide the Palette unless it is pinned.</summary>
    void ClosePalette();

    /// <summary>The Key Reference key: show or hide the Key Reference panel.</summary>
    void ToggleKeyReference();

    /// <summary>A short message for the user (a key that does nothing here, Lightroom's reason for a rejection).</summary>
    void ShowNotice(string text);

    /// <summary>Every answer from Lightroom; an accepted adjustment carries the value for the Value HUD.</summary>
    void ShowAck(LightroomAck ack);

    /// <summary>Lightroom's module or Develop tool changed (the Key Reference follows it).</summary>
    void ContextChanged(LightroomContext context);

    /// <summary>Whether the Key Reference panel is on screen (the tray says Show or Hide).</summary>
    bool IsKeyReferenceVisible { get; }
}
