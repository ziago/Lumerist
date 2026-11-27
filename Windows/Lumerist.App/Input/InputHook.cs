using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Lumerist.Core;
using Lumerist.Core.Input;
using static Lumerist.App.Interop.Native;

namespace Lumerist.App.Input;

/// <summary>
/// Feeds keyboard, wheel and mouse events to <see cref="KeyGestureEngine{TEvent}"/>
/// and applies its decisions (KeyEngineMonitor.swift). Low-level hooks run on
/// a thread of their own with its own message loop. Lightroom's module and
/// tool come from the plug-in; while <see cref="ImportWindowDetector"/> sees
/// Lightroom's Import window in front, the module is "import" instead.
/// </summary>
/// <remarks>
/// Windows removes a low-level hook without notice when its callback is too
/// slow (LowLevelHooksTimeout), so the callback only asks the engine and
/// posts events; everything else goes to the UI thread. A watchdog puts the
/// hooks back when input arrives but the callbacks stopped.
/// </remarks>
internal sealed unsafe class InputHook : IDisposable
{
    private const uint WatchdogTimerMilliseconds = 1_000;
    /// <summary>Input this much newer than the last callback means the hooks are gone.</summary>
    private const uint SilentHookMilliseconds = 1_500;

    /// <summary>The instance the static callbacks reach (one hook at a time).</summary>
    private static InputHook? active;

    private readonly Lock gate = new();
    private readonly KeyGestureEngine<InputEvent> engine;
    private readonly Action<IReadOnlyList<KeyIntent>> onIntents;
    private readonly Action<string> log;
    private readonly ModifierTracker modifiers = new();
    private readonly KeyRepeatTracker repeats = new();
    private readonly ClickCounter clicks = new();
    private readonly HookSilenceWatch silenceWatch = new(SilentHookMilliseconds);
    private string module = "develop";
    private string tool = "";
    /// <summary>Lightroom's interface language (null: not known, English): it names the Import keys.</summary>
    private string? language;
    private bool isImportWindowOpen;
    /// <summary>The layout the Import shortcuts were taken for, while the Import window is open.</summary>
    private nint importLayout;
    private POINT? pointerAnchor;
    private POINT lastPoint;
    private bool menuMaskSent;

    private Thread? thread;
    private uint threadId;
    private nint keyboardHook;
    private nint mouseHook;
    private long lastCallbackTick;

    public InputHook(Keymap keymap, Action<IReadOnlyList<KeyIntent>> onIntents, Action<string> log)
    {
        engine = new KeyGestureEngine<InputEvent>(keymap);
        this.onIntents = onIntents;
        this.log = log;
    }

    /// <exception cref="InvalidOperationException">Another hook is running, or Windows refused the hooks.</exception>
    public void Start()
    {
        if (thread is not null) return;
        if (Interlocked.CompareExchange(ref active, this, null) is not null)
            throw new InvalidOperationException("Another input hook is already running.");
        using var started = new ManualResetEventSlim();
        Exception? failure = null;
        var hookThread = new Thread(() => Run(started, error => failure = error))
        {
            IsBackground = true,
            Name = "Lumerist input hook",
            // Input waits for this thread: keep it responsive.
            Priority = ThreadPriority.AboveNormal,
        };
        hookThread.Start();
        started.Wait();
        if (failure is not null)
        {
            Interlocked.CompareExchange(ref active, null, this);
            throw new InvalidOperationException($"Windows did not accept the keyboard and mouse hooks: {failure.Message}", failure);
        }
        thread = hookThread;
    }

    /// <summary>Removes the hooks; a key held back still reaches Lightroom.</summary>
    public void Stop()
    {
        if (thread is not { } hookThread) return;
        PostThreadMessageW(threadId, WM_QUIT, 0, 0);
        thread = null;
        if (!hookThread.Join(TimeSpan.FromSeconds(2)))
        {
            // Still inside a callback: keep it registered, so no second pair of hooks can start.
            log("The input hook thread did not stop in time");
            return;
        }
        Interlocked.CompareExchange(ref active, null, this);
        ReleaseHeldKeys();
    }

    public void Dispose() => Stop();

    public void UpdateKeymap(Keymap keymap)
    {
        lock (gate) engine.Update(keymap);
    }

    public void UpdateContext(string newModule, string newTool, string? newLanguage = null)
    {
        lock (gate)
        {
            module = newModule;
            tool = newTool;
            if (newLanguage == language) return;
            language = newLanguage;
            // The Import keys follow the language: taken again at once if the Import window is open.
            if (isImportWindowOpen) engine.UpdateImportShortcuts(KeyboardLayout.ImportShortcutsFor(importLayout, language));
        }
    }

    public void PaletteDidClose()
    {
        lock (gate) engine.PaletteDidClose();
    }

    public bool IsPaused
    {
        get
        {
            lock (gate) return engine.IsPaused;
        }
    }

    /// <summary>Modifier keys held now (<see cref="ModifierPlan"/> mask), to lift them around a shortcut.</summary>
    public int HeldModifierMask
    {
        get
        {
            lock (gate)
            {
                modifiers.Retain(IsKeyDown);
                return modifiers.HeldMask;
            }
        }
    }

    /// <summary>
    /// Forgets every gesture and every key: after the session was locked or
    /// switched, key-ups happened where the hook could not see them. Nothing
    /// is replayed (the keys may belong to the lock screen).
    /// </summary>
    public void ResetInputState()
    {
        lock (gate)
        {
            engine.Reset();
            modifiers.Reset();
            repeats.Reset();
            clicks.Reset();
            pointerAnchor = null;
            menuMaskSent = false;
        }
    }

    // Hook thread -------------------------------------------------------------

    private void Run(ManualResetEventSlim started, Action<Exception> fail)
    {
        threadId = GetCurrentThreadId();
        try
        {
            Install();
        }
        catch (Exception error)
        {
            fail(error);
            started.Set();
            return;
        }
        // Make sure the thread has a message queue before Stop can post WM_QUIT to it.
        PeekMessageW(out _, 0, 0, 0, PM_NOREMOVE);
        started.Set();
        SetTimer(0, 0, WatchdogTimerMilliseconds, 0);
        while (GetMessageW(out var message, 0, 0, 0) > 0)
        {
            if (message.message != WM_TIMER) continue;
            try
            {
                CheckHooks();
            }
            catch (Exception error)
            {
                // An unhandled exception here would end the process and the hooks with it.
                log($"Input hook check failed: {error.Message}");
            }
        }
        Uninstall();
    }

    private void Install()
    {
        var instance = GetModuleHandle(null);
        keyboardHook = SetWindowsHookExW(WH_KEYBOARD_LL, &KeyboardCallback, instance, 0);
        if (keyboardHook == 0) throw new InvalidOperationException($"keyboard hook error {Marshal.GetLastPInvokeError()}");
        mouseHook = SetWindowsHookExW(WH_MOUSE_LL, &MouseCallback, instance, 0);
        if (mouseHook == 0)
        {
            UnhookWindowsHookEx(keyboardHook);
            keyboardHook = 0;
            throw new InvalidOperationException($"mouse hook error {Marshal.GetLastPInvokeError()}");
        }
        Volatile.Write(ref lastCallbackTick, Environment.TickCount64);
    }

    private void Uninstall()
    {
        if (keyboardHook != 0) UnhookWindowsHookEx(keyboardHook);
        if (mouseHook != 0) UnhookWindowsHookEx(mouseHook);
        keyboardHook = 0;
        mouseHook = 0;
    }

    /// <summary>Puts the hooks back if Windows removed them for being slow.</summary>
    private void CheckHooks()
    {
        var input = new LASTINPUTINFO { cbSize = (uint)sizeof(LASTINPUTINFO) };
        if (!GetLastInputInfo(ref input)) return;
        var lastCallback = unchecked((uint)Volatile.Read(ref lastCallbackTick));
        var silence = unchecked(input.dwTime - lastCallback);
        switch (silenceWatch.Check(input.dwTime, lastCallback))
        {
            case HookSilence.None:
                return;
            case HookSilence.FirstUnseenInput:
                log($"Input without a hook callback after {silence} ms; waiting for more");
                return;
        }
        log($"Input hooks went silent ({silence} ms); installing them again");
        Uninstall();
        // Keys may have been released while the hooks were gone.
        ReleaseHeldKeys();
        lock (gate)
        {
            modifiers.Reset();
            repeats.Reset();
        }
        try
        {
            Install();
        }
        catch (Exception error)
        {
            log($"Input hooks could not be installed again: {error.Message}");
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static nint KeyboardCallback(int code, nint wParam, nint lParam)
    {
        if (code == HC_ACTION && active is { } hook)
        {
            try
            {
                if (hook.OnKeyboard(*(KBDLLHOOKSTRUCT*)lParam)) return 1;
            }
            catch (Exception error)
            {
                hook.LogSafely($"Keyboard event not handled: {error.Message}");
            }
        }
        return CallNextHookEx(0, code, wParam, lParam);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static nint MouseCallback(int code, nint wParam, nint lParam)
    {
        if (code == HC_ACTION && active is { } hook)
        {
            try
            {
                if (hook.OnMouse((uint)wParam, *(MSLLHOOKSTRUCT*)lParam)) return 1;
            }
            catch (Exception error)
            {
                hook.LogSafely($"Mouse event not handled: {error.Message}");
            }
        }
        return CallNextHookEx(0, code, wParam, lParam);
    }

    private static double Now => Environment.TickCount64 / 1_000.0;

    /// <summary>Nothing may escape an UnmanagedCallersOnly method: it would end the process.</summary>
    private void LogSafely(string message)
    {
        try
        {
            log(message);
        }
        catch (Exception)
        {
            // Nothing else to do.
        }
    }

    /// <summary>Returns true to swallow the event.</summary>
    private bool OnKeyboard(KBDLLHOOKSTRUCT info)
    {
        Volatile.Write(ref lastCallbackTick, Environment.TickCount64);
        if ((info.flags & LLKHF_INJECTED) != 0 && info.dwExtraInfo == InputSender.Marker) return false;
        // Characters typed by software and keys the IME is composing name no physical key.
        if (info.vkCode is VirtualKeys.Packet or VirtualKeys.ProcessKey) return false;
        var isUp = (info.flags & LLKHF_UP) != 0;
        var isExtended = (info.flags & LLKHF_EXTENDED) != 0;

        lock (gate)
        {
            if (modifiers.Observe((int)info.vkCode, (int)info.scanCode, isExtended, isUp))
            {
                if (!modifiers.IsMenuKeyDown) menuMaskSent = false;
                return false;
            }
            modifiers.Retain(IsKeyDown);
            var scanCode = HookScanCodes.FromHook((int)info.scanCode, isExtended);
            // No physical key, or Win held: Windows' own shortcuts (Win+D, Win+Tab…).
            if (scanCode == 0 || (modifiers.IsWinDown && !isUp)) return false;

            var context = Context(out var lightroomThread);
            var item = InputEvent.Key((int)info.vkCode, scanCode, isUp, modifiers.HeldMask);
            var wasEngaged = engine.IsEngaged;
            KeyGestureEngine<InputEvent>.Outcome outcome;
            if (isUp)
            {
                repeats.KeyUp(scanCode);
                outcome = engine.KeyUp(scanCode, item, Now, context);
            }
            else
            {
                var isRepeat = repeats.KeyDown(scanCode, Now);
                outcome = engine.KeyDown(new KeyChord(scanCode, modifiers.Current), item, isRepeat, Now, context,
                    () => TextFocusProbe.IsTextInputFocused(lightroomThread));
            }
            return Apply(outcome, wasEngaged);
        }
    }

    private bool OnMouse(uint message, MSLLHOOKSTRUCT info)
    {
        Volatile.Write(ref lastCallbackTick, Environment.TickCount64);
        if ((info.flags & LLMHF_INJECTED) != 0 && info.dwExtraInfo == InputSender.Marker) return false;

        lock (gate)
        {
            if (message == WM_MOUSEMOVE)
            {
                // The common case, kept cheap: no gesture wants the pointer.
                if (!engine.NeedsMouseMoves)
                {
                    lastPoint = info.pt;
                    return false;
                }
                var engaged = engine.IsEngaged;
                var moved = engine.MouseMoved(info.pt.X - lastPoint.X, info.pt.Y - lastPoint.Y, modifiers.Current,
                    InputEvent.Mouse(message, info.mouseData), Context());
                // A swallowed move leaves the pointer where it was: the next delta is measured from there.
                var swallowed = Apply(moved, engaged);
                if (!swallowed) lastPoint = info.pt;
                return swallowed;
            }

            if (message is WM_MOUSEWHEEL or WM_MOUSEHWHEEL)
            {
                if (!engine.NeedsScrollEvents) return false;
                var delta = (int)unchecked((short)(info.mouseData >> 16));
                modifiers.Retain(IsKeyDown);
                var current = modifiers.Current;
                // Like the Mac: the horizontal axis counts only with Shift (tilt wheels, touchpads).
                if (message == WM_MOUSEHWHEEL && (current & KeyModifiers.Shift) == 0) delta = 0;
                var (lines, points, isContinuous) = WheelInput.FromDelta(delta);
                var wasEngaged = engine.IsEngaged;
                return Apply(engine.Scroll(lines, points, isContinuous, isMomentum: false, current, Context()), wasEngaged);
            }

            if (Button(message, info.mouseData) is not ({ } button, var isDown)) return false;
            var item = InputEvent.Mouse(message, info.mouseData);
            var engagedBefore = engine.IsEngaged;
            if (!isDown) return Apply(engine.MouseUp(button, item, Context()), engagedBefore);
            var count = clicks.Press(button, info.time, info.pt.X, info.pt.Y, GetDoubleClickTime(),
                GetSystemMetrics(SM_CXDOUBLECLK), GetSystemMetrics(SM_CYDOUBLECLK));
            return Apply(engine.MouseDown(button, count, item, Context()), engagedBefore);
        }
    }

    /// <summary>Call with the lock held.</summary>
    private EngineContext Context() => Context(out _);

    /// <summary>
    /// Lightroom's context for this event; call with the lock held. With
    /// another app in front the Import window state stands until Lightroom is
    /// back (the engine lets every event through meanwhile).
    /// </summary>
    private EngineContext Context(out uint lightroomThread)
    {
        var (isInFront, thread, window) = LightroomWindow.ForegroundWindow();
        lightroomThread = thread;
        if (isInFront) FollowImportWindow(ImportWindowDetector.IsImportWindow(window), thread);
        return new EngineContext(isInFront, isImportWindowOpen ? EngineContext.ImportModule : module, tool);
    }

    /// <summary>
    /// Lightroom's Import window came to the front or went away. While it is
    /// open, its shortcuts follow the keyboard layout of Lightroom's window and
    /// Lightroom's language: taken at the opening and again whenever the layout
    /// (or, in <see cref="UpdateContext"/>, the language) changes (the table
    /// is read once per layout, so the check costs one call). One log line per change, no more.
    /// </summary>
    private void FollowImportWindow(bool open, uint lightroomThread)
    {
        if (open)
        {
            var layout = GetKeyboardLayout(lightroomThread);
            if (!isImportWindowOpen || layout != importLayout)
            {
                importLayout = layout;
                engine.UpdateImportShortcuts(KeyboardLayout.ImportShortcutsFor(layout, language));
            }
        }
        if (open == isImportWindowOpen) return;
        isImportWindowOpen = open;
        LogSafely(open ? "Import window opened" : "Import window closed");
    }

    private static (MouseButton? Button, bool IsDown) Button(uint message, uint mouseData) => message switch
    {
        WM_LBUTTONDOWN => (MouseButton.Left, true),
        WM_LBUTTONUP => (MouseButton.Left, false),
        WM_RBUTTONDOWN => (MouseButton.Right, true),
        WM_RBUTTONUP => (MouseButton.Right, false),
        WM_MBUTTONDOWN => (MouseButton.Middle, true),
        WM_MBUTTONUP => (MouseButton.Middle, false),
        WM_XBUTTONDOWN => (MouseButton.FromXButton((int)(mouseData >> 16)), true),
        WM_XBUTTONUP => (MouseButton.FromXButton((int)(mouseData >> 16)), false),
        _ => (null, false),
    };

    /// <summary>Applies the engine's decision; call with the lock held. Returns true to swallow the event.</summary>
    private bool Apply(KeyGestureEngine<InputEvent>.Outcome outcome, bool wasEngaged)
    {
        // Remember where the pointer was when a gesture started, so "return
        // cursor after release" can put it back.
        if (!wasEngaged && engine.IsEngaged && GetCursorPos(out var start))
        {
            pointerAnchor = start;
            lastPoint = start;
        }
        var intents = outcome.Intents;
        if (intents.Any(intent => intent is KeyIntent.RestorePointer))
        {
            if (pointerAnchor is { } anchor)
            {
                SetCursorPos(anchor.X, anchor.Y);
                lastPoint = anchor;
            }
            intents = intents.Where(intent => intent is not KeyIntent.RestorePointer).ToList();
        }
        if (!engine.IsEngaged) pointerAnchor = null;
        if (intents.Count > 0) onIntents(intents);

        switch (outcome.Decision)
        {
            case KeyGestureEngine<InputEvent>.Decision.Consume:
                MaskMenuKey();
                return true;
            case KeyGestureEngine<InputEvent>.Decision.Replace(var events):
                // Posted right away; Windows delivers them after the current event.
                if (InputSender.Replay(events, modifiers.HeldMask)) return true;
                // Refused (e.g. Lightroom runs as administrator): at least let this event through.
                LogSafely("Replayed keys were refused by Windows");
                return false;
            default:
                return false;
        }
    }

    /// <summary>Alt or Win was held while Lumerist swallowed something: keep their release from opening a menu.</summary>
    private void MaskMenuKey()
    {
        if (menuMaskSent || !modifiers.IsMenuKeyDown) return;
        InputSender.SendMenuMask();
        menuMaskSent = true;
    }

    /// <summary>Key downs held back still reach Lightroom; the gesture state starts clean.</summary>
    private void ReleaseHeldKeys()
    {
        IReadOnlyList<InputEvent> held;
        int heldMask;
        lock (gate)
        {
            held = engine.HeldEvents;
            engine.Reset();
            pointerAnchor = null;
            modifiers.Retain(IsKeyDown);
            heldMask = modifiers.HeldMask;
        }
        if (held.Count > 0) InputSender.Replay(held, heldMask);
    }
}
