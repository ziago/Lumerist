namespace Lumerist.Core.Input;

// Arithmetic for Windows low-level hook events, kept free of Windows APIs so
// it is tested on every system. The app feeds it the fields of
// KBDLLHOOKSTRUCT / MSLLHOOKSTRUCT.

/// <summary>Virtual-key codes the hook needs (winuser.h).</summary>
public static class VirtualKeys
{
    public const int Shift = 0x10, Control = 0x11, Menu = 0x12;
    public const int LeftWin = 0x5B, RightWin = 0x5C;
    public const int LeftShift = 0xA0, RightShift = 0xA1;
    public const int LeftControl = 0xA2, RightControl = 0xA3;
    public const int LeftMenu = 0xA4, RightMenu = 0xA5;

    /// <summary>
    /// An unassigned key (0xE8). Sent once after Lumerist uses a key while Alt
    /// or Win is held, so releasing Alt does not open Lightroom's menu and
    /// releasing Win does not open Start.
    /// </summary>
    public const int MenuMask = 0xE8;

    /// <summary>A character typed by software (on-screen keyboard, dictation): the scan code field holds the character.</summary>
    public const int Packet = 0xE7;

    /// <summary>A key the input method (IME) is composing with.</summary>
    public const int ProcessKey = 0xE5;

    public static bool IsModifier(int virtualKey) => virtualKey is Shift or Control or Menu or LeftWin or RightWin
        or (>= LeftShift and <= RightMenu);
}

/// <summary>Scan codes as hook events carry them.</summary>
public static class HookScanCodes
{
    public const int ExtendedPrefix = 0xE000;

    /// <summary>
    /// The chord scan code of a hook event: the Set 1 code, plus 0xE000 for
    /// extended keys (→ is 0xE04D). Zero when the event names no physical key
    /// (some injected events).
    /// </summary>
    public static int FromHook(int scanCode, bool isExtended)
    {
        var code = scanCode & 0xFF;
        if (code == 0) return 0;
        return isExtended ? code | ExtendedPrefix : code;
    }

    public static bool IsExtended(int chordScanCode) => (chordScanCode & ExtendedPrefix) == ExtendedPrefix;

    /// <summary>The 8-bit code a KEYBDINPUT carries (the extended flag travels separately).</summary>
    public static int Low(int chordScanCode) => chordScanCode & 0xFF;
}

/// <summary>
/// Which modifier keys are down, followed from the hook's own events: a
/// low-level hook runs before Windows updates its key state, so asking
/// Windows would be one event late.
/// </summary>
/// <remarks>
/// AltGr (Italian, German, French… layouts) arrives as a fake left Ctrl with
/// scan code 0x21D plus right Alt. It types characters such as @ and €, so it
/// counts as neither Ctrl nor Alt. Not thread-safe.
/// </remarks>
public sealed class ModifierTracker
{
    public const int AltGrFakeControlScanCode = 0x21D;

    private readonly HashSet<int> down = [];
    private bool altGrControlDown;
    private bool rightAltIsAltGr;

    /// <summary>
    /// Records a key event. Returns true when the key is a modifier: the hook
    /// passes it on without asking the gesture engine.
    /// </summary>
    public bool Observe(int virtualKey, int scanCode, bool isExtended, bool isUp)
    {
        if (!VirtualKeys.IsModifier(virtualKey)) return false;
        if (virtualKey is VirtualKeys.LeftControl or VirtualKeys.Control && scanCode == AltGrFakeControlScanCode)
        {
            altGrControlDown = !isUp;
            if (!isUp && down.Contains(VirtualKeys.RightMenu)) rightAltIsAltGr = true;
            return true;
        }
        var key = Specific(virtualKey, isExtended);
        if (isUp) down.Remove(key);
        else down.Add(key);
        if (key == VirtualKeys.RightMenu) rightAltIsAltGr = !isUp && altGrControlDown;
        return true;
    }

    public KeyModifiers Current
    {
        get
        {
            var modifiers = KeyModifiers.None;
            if (down.Contains(VirtualKeys.LeftShift) || down.Contains(VirtualKeys.RightShift)) modifiers |= KeyModifiers.Shift;
            if (down.Contains(VirtualKeys.LeftControl) || down.Contains(VirtualKeys.RightControl)) modifiers |= KeyModifiers.Control;
            if (down.Contains(VirtualKeys.LeftMenu) || (down.Contains(VirtualKeys.RightMenu) && !rightAltIsAltGr)) modifiers |= KeyModifiers.Alt;
            return modifiers;
        }
    }

    /// <summary>Win is not a Lumerist modifier: while it is held, keys belong to Windows.</summary>
    public bool IsWinDown => down.Contains(VirtualKeys.LeftWin) || down.Contains(VirtualKeys.RightWin);

    /// <summary>True while releasing a held key could open a menu (Alt) or Start (Win).</summary>
    public bool IsMenuKeyDown => (Current & KeyModifiers.Alt) != 0 || IsWinDown;

    /// <summary>The left/right-specific virtual keys held now (Win excluded), e.g. to lift them around a shortcut.</summary>
    public IReadOnlyList<int> HeldModifierKeys =>
        down.Where(key => key is not (VirtualKeys.LeftWin or VirtualKeys.RightWin))
            .Where(key => !(key == VirtualKeys.RightMenu && rightAltIsAltGr))
            .Order().ToList();

    /// <summary>
    /// The held keys as a <see cref="ModifierPlan"/> mask, recorded with each
    /// key Lumerist may hold back, so a replay can restore them.
    /// </summary>
    public int HeldMask => ModifierPlan.Mask(HeldModifierKeys);

    /// <summary>
    /// Forgets keys Windows says are up. Key-ups never reach the hook while
    /// another desktop is active (Win+L, Ctrl+Alt+Del, UAC), so a modifier
    /// could otherwise stay "down" for good.
    /// </summary>
    /// <returns>True when something was forgotten.</returns>
    public bool Retain(Func<int, bool> isDown)
    {
        var forgotten = down.RemoveWhere(key => !isDown(key)) > 0;
        if (!down.Contains(VirtualKeys.RightMenu)) rightAltIsAltGr = false;
        if (altGrControlDown && !isDown(VirtualKeys.LeftControl)) altGrControlDown = false;
        return forgotten;
    }

    public void Reset()
    {
        down.Clear();
        altGrControlDown = false;
        rightAltIsAltGr = false;
    }

    /// <summary>Hook events name the side; injected events may use the generic code.</summary>
    private static int Specific(int virtualKey, bool isExtended) => virtualKey switch
    {
        VirtualKeys.Shift => VirtualKeys.LeftShift,
        VirtualKeys.Control => isExtended ? VirtualKeys.RightControl : VirtualKeys.LeftControl,
        VirtualKeys.Menu => isExtended ? VirtualKeys.RightMenu : VirtualKeys.LeftMenu,
        _ => virtualKey,
    };
}

/// <summary>
/// Plans the modifier key events that change what Lightroom thinks is held:
/// around a replayed key (it is sent with the modifiers it was pressed with,
/// not those held now) and around a shortcut (exactly its own modifiers).
/// </summary>
public static class ModifierPlan
{
    /// <summary>The six left/right modifier keys, one bit each.</summary>
    private static readonly int[] Order =
    [
        VirtualKeys.LeftShift, VirtualKeys.RightShift,
        VirtualKeys.LeftControl, VirtualKeys.RightControl,
        VirtualKeys.LeftMenu, VirtualKeys.RightMenu,
    ];

    public static int Mask(IEnumerable<int> keys)
    {
        var mask = 0;
        foreach (var key in keys)
        {
            var bit = Array.IndexOf(Order, key);
            if (bit >= 0) mask |= 1 << bit;
        }
        return mask;
    }

    public static IReadOnlyList<int> Keys(int mask) =>
        Order.Where((_, bit) => (mask & (1 << bit)) != 0).ToList();

    /// <summary>
    /// The mask with exactly a chord's modifiers: held keys of the right
    /// family are kept (a held right Ctrl serves Ctrl+Z), missing ones are
    /// pressed on the left.
    /// </summary>
    public static int ForChord(KeyModifiers modifiers, int held)
    {
        var mask = 0;
        foreach (var (family, left, right) in new[]
        {
            (KeyModifiers.Shift, VirtualKeys.LeftShift, VirtualKeys.RightShift),
            (KeyModifiers.Control, VirtualKeys.LeftControl, VirtualKeys.RightControl),
            (KeyModifiers.Alt, VirtualKeys.LeftMenu, VirtualKeys.RightMenu),
        })
        {
            if ((modifiers & family) == 0) continue;
            var heldOfFamily = held & Mask([left, right]);
            mask |= heldOfFamily != 0 ? heldOfFamily : Mask([left]);
        }
        return mask;
    }

    /// <summary>
    /// Key events (virtual key, up?) that take Lightroom from <paramref name="from"/>
    /// to <paramref name="to"/>: releases first, then presses. An Alt pressed
    /// and released with nothing in between opens the menu bar, so an unused
    /// key (<see cref="VirtualKeys.MenuMask"/>) is tapped before any Alt goes
    /// up and after any Alt goes down.
    /// </summary>
    public static IReadOnlyList<(int VirtualKey, bool IsUp)> Transition(int from, int to)
    {
        var events = new List<(int, bool)>();
        var released = Keys(from & ~to);
        var pressed = Keys(to & ~from);
        if (released.Any(IsAlt)) AddMask(events);
        events.AddRange(released.Select(key => (key, true)));
        events.AddRange(pressed.Select(key => (key, false)));
        if (pressed.Any(IsAlt)) AddMask(events);
        return events;
    }

    private static bool IsAlt(int key) => key is VirtualKeys.LeftMenu or VirtualKeys.RightMenu;

    private static void AddMask(List<(int, bool)> events)
    {
        events.Add((VirtualKeys.MenuMask, false));
        events.Add((VirtualKeys.MenuMask, true));
    }
}

/// <summary>Hook events carry no repeat flag: a key down for a key already down is a repeat.</summary>
public sealed class KeyRepeatTracker
{
    private readonly HashSet<int> down = [];

    /// <summary>Records a key down; returns true when it is an auto-repeat.</summary>
    public bool KeyDown(int scanCode) => !down.Add(scanCode);

    public void KeyUp(int scanCode) => down.Remove(scanCode);

    public void Reset() => down.Clear();
}

/// <summary>
/// Hook events carry no click count: a press of the same button, soon enough
/// (the system double-click time) and close enough (the double-click
/// rectangle, centred on the previous press), continues the series.
/// </summary>
public sealed class ClickCounter
{
    private MouseButton? lastButton;
    private uint lastTime;
    private int lastX, lastY;
    private int count;

    /// <param name="time">The event's millisecond tick (wraps after 49 days, like the hook's).</param>
    /// <param name="doubleClickTime">GetDoubleClickTime().</param>
    /// <param name="width">SM_CXDOUBLECLK.</param>
    /// <param name="height">SM_CYDOUBLECLK.</param>
    public int Press(MouseButton button, uint time, int x, int y, uint doubleClickTime, int width, int height)
    {
        var continues = lastButton == button
            && unchecked(time - lastTime) <= doubleClickTime
            && Math.Abs(x - lastX) <= width / 2
            && Math.Abs(y - lastY) <= height / 2;
        count = continues ? count + 1 : 1;
        lastButton = button;
        lastTime = time;
        lastX = x;
        lastY = y;
        return count;
    }

    public void Reset()
    {
        lastButton = null;
        count = 0;
    }
}

/// <summary>
/// Windows removes a low-level hook without notice when a callback is too
/// slow. The watchdog notices it by comparing the last input time
/// (GetLastInputInfo) with the last callback: input much newer than the last
/// callback means the hooks are gone.
/// </summary>
/// <remarks>
/// One input alone is not enough: the first input after some minutes of
/// quiet (for example the mouse move that turns the screen back on) can count
/// as input without reaching the hooks. The hooks look gone only when a
/// further input, at a different time, still has no callback.
/// </remarks>
public sealed class HookSilenceWatch(uint silentMilliseconds)
{
    /// <summary>The first input without a callback, while waiting for a second one.</summary>
    private uint? unseenInput;

    /// <param name="lastInput">GetLastInputInfo's tick.</param>
    /// <param name="lastCallback">The low 32 bits of the last callback's tick.</param>
    public HookSilence Check(uint lastInput, uint lastCallback)
    {
        var silence = unchecked(lastInput - lastCallback);
        // Callbacks are keeping up, or ran after the last input (the difference wrapped).
        if (silence <= silentMilliseconds || silence >= int.MaxValue)
        {
            unseenInput = null;
            return HookSilence.None;
        }
        if (unseenInput is not { } first)
        {
            unseenInput = lastInput;
            return HookSilence.FirstUnseenInput;
        }
        if (first == lastInput) return HookSilence.None;
        unseenInput = null;
        return HookSilence.Silent;
    }

    public void Reset() => unseenInput = null;
}

/// <summary>What <see cref="HookSilenceWatch"/> makes of the last input.</summary>
public enum HookSilence
{
    /// <summary>Nothing to do: callbacks keep up, or the one unseen input is still the last.</summary>
    None,
    /// <summary>An input without a callback: wait for another before acting.</summary>
    FirstUnseenInput,
    /// <summary>A further input still without a callback: the hooks are gone.</summary>
    Silent,
}

/// <summary>Turns WM_MOUSEWHEEL deltas into what the gesture engine reads.</summary>
public static class WheelInput
{
    /// <summary>One notch of a classic wheel (WHEEL_DELTA).</summary>
    public const int NotchDelta = 120;

    /// <summary>Point travel per line, as in the shared fixtures (lines × 10).</summary>
    public const double PointsPerLine = 10;

    /// <summary>
    /// A multiple of 120 is a notched wheel (whole lines). Anything else comes
    /// from a precision touchpad or a high-resolution wheel and is read as
    /// continuous travel.
    /// </summary>
    public static (double Lines, double Points, bool IsContinuous) FromDelta(int delta)
    {
        var lines = delta / (double)NotchDelta;
        return (lines, lines * PointsPerLine, delta % NotchDelta != 0);
    }
}
