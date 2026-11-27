using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using Lumerist.Core;

namespace Lumerist.App.Design.Controls;

/// <summary>
/// One key box (the Mac's KeySlot): a <see cref="KeyCap"/> that records a
/// chord on click, with a clear button when assigned. States: assigned,
/// empty "+", recording, conflict (⚠ and the message as tooltip). Combos
/// read with the current Combo Key ("Tab + K").
/// </summary>
internal sealed class KeySlot : ContentControl
{
    private readonly string id;
    private readonly KeyChord? chord;
    private readonly string? conflict;
    private readonly double minWidth;
    private readonly string accessibilityName;
    private readonly KeyChord? comboKey;
    private readonly Action<KeyChord> onSet;
    private readonly Action onClear;
    private Button? clearButton;
    private bool isHovering;
    private bool shownRecording;

    /// <param name="id">Unique per box, so only one records at a time.</param>
    /// <param name="comboKey">The keymap's Combo Key: combos read "Tab + K", and holding it records a combo.</param>
    public KeySlot(string id, KeyChord? chord, string? conflict, double minWidth, string accessibilityName, KeyChord? comboKey,
        Action<KeyChord> onSet, Action onClear)
    {
        this.id = id;
        this.chord = chord;
        this.conflict = conflict;
        this.minWidth = minWidth;
        this.accessibilityName = accessibilityName;
        this.comboKey = comboKey;
        this.onSet = onSet;
        this.onClear = onClear;
        Focusable = false;
        IsTabStop = false;
        Loaded += (_, _) => ChordRecorder.Shared.Changed += OnRecorderChanged;
        Unloaded += (_, _) => ChordRecorder.Shared.Changed -= OnRecorderChanged;
        MouseEnter += (_, _) => SetHover(true);
        MouseLeave += (_, _) => SetHover(false);
        Refresh();
    }

    private void SetHover(bool hovering)
    {
        isHovering = hovering;
        if (clearButton?.Content is TextBlock glyph)
        {
            // Red only while the pointer is on the × itself (ClearButton).
            glyph.Foreground = clearButton.IsMouseOver ? Ink.Error : hovering ? Ink.Ice : Ink.Steel;
            clearButton.Opacity = hovering ? 1 : 0.55;
        }
    }

    /// <summary>Only the box that starts or stops recording draws again.</summary>
    private void OnRecorderChanged()
    {
        if (shownRecording || ChordRecorder.Shared.IsRecording(id)) Refresh();
    }

    private void Refresh()
    {
        var recorder = ChordRecorder.Shared;
        var recording = recorder.IsRecording(id);
        shownRecording = recording;
        var state = recording ? KeyCapState.Recording
            : chord is null ? KeyCapState.Empty
            : conflict is not null ? KeyCapState.Conflict
            : KeyCapState.Assigned;
        var text = chord?.DisplayTextWith(comboKey);

        var cap = SynthControls.Plain(KeyCap.Create(recording ? RecordingPrompt(recorder) : text ?? "+", state, minWidth), () =>
        {
            if (ChordRecorder.Shared.IsRecording(id)) ChordRecorder.Shared.Stop();
            else ChordRecorder.Shared.Start(id, comboKey, onSet, onClear);
        });
        cap.HorizontalAlignment = HorizontalAlignment.Left;
        cap.ToolTip = conflict ?? (chord is null ? "Click, then press a key (Esc cancels)" : "Click to record another key; Delete clears");
        AutomationProperties.SetName(cap, accessibilityName);
        AutomationProperties.SetItemStatus(cap, recording ? "Recording, press a key" : text ?? "Not assigned");
        AutomationProperties.SetHelpText(cap, conflict ?? "Records a new key");

        UIElement trailing;
        if (chord is not null && !recording)
        {
            clearButton = SynthControls.ClearButton(8, onClear, $"Clear {accessibilityName}", () => isHovering ? Ink.Ice : Ink.Steel);
            clearButton.Width = 14;
            clearButton.Height = 14;
            trailing = clearButton;
            SetHover(isHovering);
        }
        else
        {
            clearButton = null;
            trailing = new Border { Width = 14, Height = 14, Background = Brushes.Transparent };
        }
        Content = Layout.HStack(2, cap, trailing);
    }

    /// <summary>"PRESS", or "Tab + …" while the Combo Key is held during recording.</summary>
    private static string RecordingPrompt(ChordRecorder recorder) =>
        recorder.IsComboKeyDown && recorder.ComboKey is { } combo ? $"{combo.DisplayText} + …" : "PRESS";
}
