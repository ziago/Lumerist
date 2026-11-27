using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Threading;
using Lumerist.Core;
using Lumerist.Core.Input;

namespace Lumerist.App.Design.Controls;

/// <summary>
/// Records the next key press in a Lumerist window for one key box at a
/// time (the Mac's ChordRecorder; the rules are in <see cref="ChordRecording"/>).
/// </summary>
/// <remarks>
/// While recording, every key message of the UI thread is read before WPF
/// sees it (<see cref="ComponentDispatcher.ThreadFilterMessage"/> runs before
/// the windows' own keyboard processing, which hangs off
/// ThreadPreprocessMessage): key downs are swallowed, so they never type
/// into a field, move the focus (Tab) or press a button; key ups go on. The
/// scan code comes from the message's lParam, so the chord names the
/// physical key whatever the layout. The input hook leaves these keys alone:
/// it only acts while Lightroom is in front.
/// <para>
/// With an input method on, Windows delivers keys as VK_PROCESSKEY; the real
/// key comes from ImmGetVirtualKey. Input-method keys (半角/全角, 変換, 한/영…)
/// are refused, and modifiers are recognised by scan code as well.
/// </para>
/// </remarks>
internal sealed partial class ChordRecorder
{
    private const int WM_KEYDOWN = 0x0100, WM_KEYUP = 0x0101, WM_SYSKEYDOWN = 0x0104, WM_SYSKEYUP = 0x0105;
    private const int VK_SHIFT = 0x10, VK_CONTROL = 0x11, VK_MENU = 0x12, VK_CAPITAL = 0x14, VK_NUMLOCK = 0x90, VK_SCROLL = 0x91;
    private const int VK_LWIN = 0x5B, VK_RWIN = 0x5C, VK_LSHIFT = 0xA0, VK_RSHIFT = 0xA1;
    private const int VK_LCONTROL = 0xA2, VK_RCONTROL = 0xA3, VK_LMENU = 0xA4, VK_RMENU = 0xA5;
    private const int VK_PROCESSKEY = 0xE5;

    /// <summary>Shift, Ctrl, Alt, Win and Caps Lock by scan code, for keys an input method hides behind VK_PROCESSKEY.</summary>
    private static readonly HashSet<int> ModifierScanCodes = [0x2A, 0x36, 0x1D, 0xE01D, 0x38, 0xE038, 0xE05B, 0xE05C, 0x3A];

    public static ChordRecorder Shared { get; } = new();

    private ChordRecording? recording;
    private Action<KeyChord>? onChord;
    private Action? onClear;
    private bool isListening;

    /// <summary>The key box recording now, or null.</summary>
    public string? RecordingId { get; private set; }

    /// <summary>The Combo Key is held: the next key makes a combo.</summary>
    public bool IsComboKeyDown => recording?.IsComboKeyDown ?? false;

    /// <summary>The Combo Key of the recording in progress.</summary>
    public KeyChord? ComboKey { get; private set; }

    /// <summary>The last key pressed switches the input method and was not recorded.</summary>
    public bool LastKeyRefused { get; private set; }

    /// <summary>Raised on the UI thread when recording starts, stops, or the Combo Key goes down.</summary>
    public event Action? Changed;

    public bool IsRecording(string id) => RecordingId == id;

    public void Start(string id, KeyChord? comboKey, Action<KeyChord> chord, Action clear)
    {
        StopSilently();
        RecordingId = id;
        ComboKey = comboKey;
        recording = new ChordRecording(comboKey);
        onChord = chord;
        onClear = clear;
        if (!isListening)
        {
            ComponentDispatcher.ThreadFilterMessage += OnMessage;
            isListening = true;
        }
        Changed?.Invoke();
    }

    public void Stop()
    {
        if (RecordingId is null) return;
        StopSilently();
        Changed?.Invoke();
    }

    private void StopSilently()
    {
        if (isListening)
        {
            ComponentDispatcher.ThreadFilterMessage -= OnMessage;
            isListening = false;
        }
        RecordingId = null;
        ComboKey = null;
        LastKeyRefused = false;
        recording = null;
        onChord = null;
        onClear = null;
    }

    private void OnMessage(ref MSG message, ref bool handled)
    {
        if (handled || recording is null) return;
        var isDown = message.message is WM_KEYDOWN or WM_SYSKEYDOWN;
        if (!isDown && message.message is not (WM_KEYUP or WM_SYSKEYUP)) return;
        var virtualKey = (int)message.wParam;
        // An input method is composing: ask it which key this is.
        if (virtualKey == VK_PROCESSKEY && Interop.Native.ImmGetVirtualKey(message.hwnd) is var original and not 0 and not VK_PROCESSKEY)
            virtualKey = (int)original;
        // Modifiers on their own, Win and the lock keys never make a chord.
        if (virtualKey is VK_SHIFT or VK_CONTROL or VK_MENU or VK_LSHIFT or VK_RSHIFT or VK_LCONTROL or VK_RCONTROL
            or VK_LMENU or VK_RMENU or VK_LWIN or VK_RWIN or VK_CAPITAL or VK_NUMLOCK or VK_SCROLL) return;
        var lParam = (long)message.lParam;
        var scanCode = HookScanCodes.FromHook((int)((lParam >> 16) & 0xFF), ((lParam >> 24) & 1) != 0);
        if (scanCode == 0 || ModifierScanCodes.Contains(scanCode)) return;

        ChordRecording.Outcome outcome;
        if (isDown)
        {
            handled = true;
            var wasComboDown = recording.IsComboKeyDown;
            outcome = recording.KeyDown(new KeyChord(scanCode, CurrentModifiers()), isRepeat: ((lParam >> 30) & 1) != 0,
                isInputMethodKey: VirtualKeys.IsInputMethodKey(virtualKey));
            var refused = outcome == ChordRecording.Outcome.Refused;
            if (refused != LastKeyRefused || (outcome == ChordRecording.Outcome.Continue && wasComboDown != recording.IsComboKeyDown))
            {
                LastKeyRefused = refused;
                Changed?.Invoke();
            }
        }
        else
        {
            outcome = recording.KeyUp(scanCode);
        }
        Finish(outcome);
    }

    private void Finish(ChordRecording.Outcome outcome)
    {
        switch (outcome)
        {
            case ChordRecording.Outcome.Cancel:
                Stop();
                break;
            case ChordRecording.Outcome.Clear:
                var clear = onClear;
                Stop();
                // After the message: the handler may open a dialog.
                if (clear is not null) Dispatcher.CurrentDispatcher.BeginInvoke(clear);
                break;
            case ChordRecording.Outcome.Finish when recording?.Chord is { } chord:
                var handler = onChord;
                Stop();
                if (handler is not null) Dispatcher.CurrentDispatcher.BeginInvoke(() => handler(chord));
                break;
        }
    }

    /// <summary>
    /// Shift, Ctrl, Alt and AltGr held now. AltGr (left Ctrl with right Alt)
    /// types characters on European layouts, so it is its own modifier, like the
    /// input hook's (<see cref="ModifierTracker"/>), and only on a layout that has
    /// AltGr: on US layouts left Ctrl + right Alt is Ctrl+Alt.
    /// </summary>
    private static KeyModifiers CurrentModifiers()
    {
        static bool Down(int virtualKey) => (GetKeyState(virtualKey) & 0x8000) != 0;
        var leftControl = Down(VK_LCONTROL);
        var rightControl = Down(VK_RCONTROL);
        var leftAlt = Down(VK_LMENU);
        var rightAlt = Down(VK_RMENU);
        var altGr = rightAlt && leftControl && !rightControl && !leftAlt
            && Input.KeyboardLayout.HasAltGr(Interop.Native.GetKeyboardLayout(0));
        var modifiers = KeyModifiers.None;
        if (Down(VK_SHIFT)) modifiers |= KeyModifiers.Shift;
        if (rightControl || (leftControl && !altGr)) modifiers |= KeyModifiers.Control;
        if (leftAlt || (rightAlt && !altGr)) modifiers |= KeyModifiers.Alt;
        if (altGr) modifiers |= KeyModifiers.AltGr;
        return modifiers;
    }

    [LibraryImport("user32.dll")]
    private static partial short GetKeyState(int virtualKey);
}
