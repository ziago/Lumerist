using Lumerist.App.Interop;
using Lumerist.Core;
using Lumerist.Core.Input;
using static Lumerist.App.Interop.Native;

namespace Lumerist.App.Input;

/// <summary>Posts keyboard and mouse events with SendInput, marked so the hook lets them through.</summary>
internal static unsafe class InputSender
{
    /// <summary>"LKRP", the same marker as the Mac app's replayed events.</summary>
    public const nuint Marker = 0x4C4B_5250;

    /// <summary>Scan code and extended flag of each left/right modifier, for lifting and pressing them.</summary>
    private static readonly Dictionary<int, (int ScanCode, bool Extended)> ModifierScanCodes = new()
    {
        [VirtualKeys.LeftShift] = (0x2A, false),
        [VirtualKeys.RightShift] = (0x36, false),
        [VirtualKeys.LeftControl] = (0x1D, false),
        [VirtualKeys.RightControl] = (0x1D, true),
        [VirtualKeys.LeftMenu] = (0x38, false),
        [VirtualKeys.RightMenu] = (0x38, true),
    };

    /// <summary>
    /// Posts events the gesture engine held back or passed on, in order. Each
    /// key goes with the modifiers held when it was pressed; afterwards the
    /// modifiers held now (<paramref name="heldMask"/>) are back.
    /// </summary>
    public static bool Replay(IReadOnlyList<InputEvent> events, int heldMask)
    {
        var inputs = new List<INPUT>(events.Count);
        var state = heldMask;
        foreach (var item in events)
        {
            if (item.IsMouse)
            {
                if (MouseInput(item) is { } mouse) inputs.Add(mouse);
                continue;
            }
            AddTransition(inputs, state, item.ModifierMask);
            state = item.ModifierMask;
            inputs.Add(Key(item.VirtualKey, item.ScanCode, item.IsUp));
        }
        AddTransition(inputs, state, heldMask);
        return Send(inputs);
    }

    /// <summary>A key nobody uses, so releasing Alt opens no menu and releasing Win opens no Start.</summary>
    public static bool SendMenuMask() => Send([
        Key(VirtualKeys.MenuMask, 0, isUp: false),
        Key(VirtualKeys.MenuMask, 0, isUp: true),
    ]);

    /// <summary>
    /// Types a chord with exactly its modifiers: held modifiers it does not
    /// use are lifted for the moment and pressed again afterwards, so a held
    /// Shift cannot turn Ctrl+Z into Ctrl+Shift+Z.
    /// </summary>
    /// <param name="heldMask">Modifier keys held now (<see cref="ModifierPlan"/>).</param>
    /// <param name="layout">The keyboard layout of Lightroom's window.</param>
    public static bool Chord(KeyChord chord, int heldMask, nint layout)
    {
        var virtualKey = (int)MapVirtualKeyExW((uint)chord.ScanCode, MAPVK_VSC_TO_VK_EX, layout);
        if (virtualKey == 0) return false;
        var target = ModifierPlan.ForChord(chord.Modifiers, heldMask);
        var inputs = new List<INPUT>();
        AddTransition(inputs, heldMask, target);
        inputs.Add(Key(virtualKey, chord.ScanCode, isUp: false));
        inputs.Add(Key(virtualKey, chord.ScanCode, isUp: true));
        AddTransition(inputs, target, heldMask);
        return Send(inputs);
    }

    private static void AddTransition(List<INPUT> inputs, int from, int to)
    {
        foreach (var (virtualKey, isUp) in ModifierPlan.Transition(from, to))
        {
            if (virtualKey == VirtualKeys.MenuMask)
            {
                inputs.Add(Key(virtualKey, 0, isUp));
            }
            else
            {
                var (scanCode, extended) = ModifierScanCodes[virtualKey];
                inputs.Add(Key(virtualKey, extended ? scanCode | HookScanCodes.ExtendedPrefix : scanCode, isUp));
            }
        }
    }

    private static INPUT Key(int virtualKey, int chordScanCode, bool isUp)
    {
        var flags = 0u;
        if (HookScanCodes.IsExtended(chordScanCode)) flags |= KEYEVENTF_EXTENDEDKEY;
        if (isUp) flags |= KEYEVENTF_KEYUP;
        var input = new INPUT { type = INPUT_KEYBOARD };
        input.u.ki = new KEYBDINPUT
        {
            wVk = (ushort)virtualKey,
            wScan = (ushort)HookScanCodes.Low(chordScanCode),
            dwFlags = flags,
            dwExtraInfo = Marker,
        };
        return input;
    }

    private static INPUT? MouseInput(InputEvent item)
    {
        uint flags;
        uint data = 0;
        switch (item.MouseMessage)
        {
            case WM_LBUTTONDOWN: flags = MOUSEEVENTF_LEFTDOWN; break;
            case WM_LBUTTONUP: flags = MOUSEEVENTF_LEFTUP; break;
            case WM_RBUTTONDOWN: flags = MOUSEEVENTF_RIGHTDOWN; break;
            case WM_RBUTTONUP: flags = MOUSEEVENTF_RIGHTUP; break;
            case WM_MBUTTONDOWN: flags = MOUSEEVENTF_MIDDLEDOWN; break;
            case WM_MBUTTONUP: flags = MOUSEEVENTF_MIDDLEUP; break;
            case WM_XBUTTONDOWN: flags = MOUSEEVENTF_XDOWN; data = item.MouseData >> 16; break;
            case WM_XBUTTONUP: flags = MOUSEEVENTF_XUP; data = item.MouseData >> 16; break;
            case WM_MOUSEWHEEL: flags = MOUSEEVENTF_WHEEL; data = item.MouseData >> 16; break;
            case WM_MOUSEHWHEEL: flags = MOUSEEVENTF_HWHEEL; data = item.MouseData >> 16; break;
            default: return null; // Moves are never replayed.
        }
        // Wheel data is a signed 16-bit delta; SendInput reads it as a signed 32-bit value.
        if (flags is MOUSEEVENTF_WHEEL or MOUSEEVENTF_HWHEEL) data = unchecked((uint)(short)data);
        var input = new INPUT { type = INPUT_MOUSE };
        input.u.mi = new MOUSEINPUT { mouseData = data, dwFlags = flags, dwExtraInfo = Marker };
        return input;
    }

    private static bool Send(IReadOnlyList<INPUT> inputs)
    {
        if (inputs.Count == 0) return true;
        var buffer = inputs.ToArray();
        fixed (INPUT* pointer = buffer)
        {
            // Fewer events than asked: blocked by UIPI (Lightroom runs as administrator) or by the desktop.
            return SendInput((uint)buffer.Length, pointer, SizeOfInput) == buffer.Length;
        }
    }
}
