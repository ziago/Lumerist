namespace Lumerist.Core;

/// <summary>What the engine needs to know about Lightroom at the time of an event.</summary>
/// <param name="Module">
/// <c>LrApplicationView.getCurrentModuleName()</c>: "develop", "library", …,
/// or <see cref="ImportModule"/>.
/// </param>
/// <param name="Tool"><c>LrDevelopController.getSelectedTool()</c>: "masking", "crop", "" …</param>
public sealed record EngineContext(bool IsLightroomFrontmost = true, string Module = "develop", string Tool = "")
{
    /// <summary>
    /// The module while Lightroom's Import window is in front. Lightroom never
    /// reports it (its module stays "library"): the app sets it when it
    /// recognises the window (<see cref="ImportWindowRule"/>).
    /// </summary>
    public const string ImportModule = "import";

    /// <summary>
    /// The keymap scope for this module; other modules only get System Keys,
    /// and the Import window only the Import keys and the pause toggle.
    /// </summary>
    public ParameterScope? Scope => Module switch
    {
        "develop" => ParameterScope.Develop,
        "library" => ParameterScope.Library,
        _ => null,
    };

    public bool IsMasking => Tool == "masking";

    /// <summary>Lightroom's Import window is in front.</summary>
    public bool IsImportWindow => Module == ImportModule;
}

public enum MouseButtonKind
{
    Left,
    Right,
    Middle,
    Back,
    Forward,
    Other,
}

/// <summary>
/// A mouse button. Numbers follow the usual order: 0 left, 1 right, 2 middle,
/// 3 back (XBUTTON1 on Windows), 4 forward (XBUTTON2).
/// </summary>
public readonly record struct MouseButton
{
    private MouseButton(MouseButtonKind kind, int number)
    {
        Kind = kind;
        Number = number;
    }

    public MouseButtonKind Kind { get; }

    /// <summary>The button number (see the type summary); for <see cref="Other"/> the number it was made with.</summary>
    public int Number { get; }

    public static MouseButton Left { get; } = new(MouseButtonKind.Left, 0);
    public static MouseButton Right { get; } = new(MouseButtonKind.Right, 1);
    public static MouseButton Middle { get; } = new(MouseButtonKind.Middle, 2);
    public static MouseButton Back { get; } = new(MouseButtonKind.Back, 3);
    public static MouseButton Forward { get; } = new(MouseButtonKind.Forward, 4);

    public static MouseButton Other(int number) => new(MouseButtonKind.Other, number);

    /// <summary>0 left, 1 right, 2 middle, 3 back, 4 forward; anything else is <see cref="Other"/>.</summary>
    public static MouseButton FromButtonNumber(int buttonNumber) => buttonNumber switch
    {
        0 => Left,
        1 => Right,
        2 => Middle,
        3 => Back,
        4 => Forward,
        _ => Other(buttonNumber),
    };

    /// <summary>The X button a Windows mouse message names: XBUTTON1 (1) is back, XBUTTON2 (2) is forward.</summary>
    public static MouseButton FromXButton(int xButton) => FromButtonNumber(xButton + 2);

    public override string ToString() => Kind == MouseButtonKind.Other ? $"Other({Number})" : Kind.ToString();
}

/// <summary>
/// Decides, event by event, what a key, wheel or mouse event means:
/// <list type="bullet">
/// <item><b>Dial Key</b> (hold): the key is held back; scrolling, dragging, or ←/→
/// while it is down moves its slider. Released without a gesture, the key
/// is replayed to Lightroom (or runs the tap Command Key on the same chord).</item>
/// <item><b>Nudge Key</b> (tap): one step down / up; Shift selects the large step.</item>
/// <item><b>Command Key</b> (tap).</item>
/// <item><b>Combos</b>: with the Combo Key held, the next key is looked up as a
/// combo ("Tab + K"), a Command Key or a Nudge Key of its own. The Combo
/// Key tapped alone, or followed by a key that is no combo, still reaches
/// Lightroom in order.</item>
/// <item><b>System Keys</b>: Palette (hold), Key Reference and pause toggles.</item>
/// <item><b>Mouse</b>: double / middle click resets the held slider, back / forward
/// buttons change photo.</item>
/// <item><b>Import keys</b>: in Lightroom's Import window (module "import") only
/// they and the pause toggle act. Each press sends Lightroom's own Import
/// shortcut (P, X, `), then → with "Move to the next photo".
/// Every other key, the wheel, the trackpad and the mouse reach Lightroom
/// untouched.</item>
/// </list>
/// Typing stays safe: held keys are replayed in order when another key
/// arrives, nothing is captured in text fields, and Ctrl/Alt chords belong to
/// Windows and Lightroom unless a binding names them. With a mask selected
/// (tool "masking") Develop keys move the mask's own sliders; keys without a
/// mask equivalent do nothing rather than touch the whole photo.
/// <para>
/// <typeparamref name="TEvent"/> is the platform event type; tests use plain
/// values. The engine keeps its own copy of the keymap.
/// </para>
/// </summary>
public sealed class KeyGestureEngine<TEvent>
{
    public abstract record Decision
    {
        private Decision() { }

        public sealed record Pass() : Decision;

        public sealed record Consume() : Decision;

        /// <summary>Drop the current event and post these events, in order, instead.</summary>
        public sealed record Replace(IReadOnlyList<TEvent> Events) : Decision
        {
            public bool Equals(Replace? other) => other is not null && Events.SequenceEqual(other.Events);

            public override int GetHashCode() => Events.Aggregate(17, (hash, item) => HashCode.Combine(hash, item));
        }
    }

    public sealed record Outcome(Decision Decision, IReadOnlyList<KeyIntent> Intents)
    {
        public Outcome(Decision decision) : this(decision, []) { }
    }

    /// <summary>
    /// A Palette Key released sooner than this (seconds), without other input,
    /// is a tap: the Palette stays open until the key is pressed again.
    /// </summary>
    public const double TapThreshold = 0.25;

    // State ----------------------------------------------------------------------

    private abstract record Phase
    {
        public sealed record Idle() : Phase;

        /// <param name="Chord">The chord as pressed, to look the key up again when the module changes.</param>
        public sealed record Held(int ScanCode, KeyChord Chord, TEvent KeyDown, ParameterKey Target, CommandAction? TapAction) : Phase;

        /// <param name="Began">gestureBegan was emitted (a click-only reset does not start a gesture).</param>
        public sealed record Adjusting(int ScanCode, KeyChord Chord, ParameterKey Target, bool Began, bool DidDrag) : Phase;

        /// <summary>
        /// A mask is selected and the held key's slider has no mask equivalent:
        /// scrolling is swallowed (never reaching the whole photo), a plain tap
        /// still reaches Lightroom (R still opens Crop).
        /// </summary>
        public sealed record Blocked(int ScanCode, TEvent KeyDown, ParameterKey Key, bool Scrolled) : Phase;

        public sealed record Palette(int ScanCode, TEvent KeyDown, double Start, bool Interacted) : Phase;
    }

    private sealed record ComboKeyHold(int ScanCode, TEvent KeyDown, bool Used);

    /// <summary>
    /// Combos are indexed with their Combo modifier next to plain chords; key
    /// events never carry it, so they are reached only through the Combo Key.
    /// </summary>
    private sealed class ScopeIndex
    {
        public Dictionary<KeyChord, string> Dial { get; } = [];
        public Dictionary<KeyChord, (string Id, double Direction)> Nudge { get; } = [];
        public Dictionary<KeyChord, CommandKey> Actions { get; } = [];
    }

    private static readonly Phase IdlePhase = new Phase.Idle();
    private static readonly Decision PassDecision = new Decision.Pass();
    private static readonly Decision ConsumeDecision = new Decision.Consume();

    private Keymap keymap;
    private Phase phase = IdlePhase;
    private ComboKeyHold? comboKeyHold;
    private bool palettePinned;
    private readonly HashSet<int> consumedKeyUps = [];
    private readonly HashSet<MouseButton> consumedMouseUps = [];
    private bool leftButtonDown;
    private readonly ScrollStepAccumulator scroll = new();
    private double dragResidual;
    private bool suppressesMomentum;
    private readonly Dictionary<ParameterScope, ScopeIndex> indexes = [];

    /// <param name="keymap">Copied: later changes to it do not reach the engine; use <see cref="Update"/>.</param>
    public KeyGestureEngine(Keymap keymap)
    {
        this.keymap = keymap.Clone();
        RebuildIndexes();
    }

    /// <summary>Uses a copy of <paramref name="keymap"/> from now on.</summary>
    public void Update(Keymap keymap)
    {
        this.keymap = keymap.Clone();
        RebuildIndexes();
    }

    /// <summary>A copy of the keymap in use (changing it does not change the engine).</summary>
    public Keymap Keymap => keymap.Clone();

    /// <summary>The keys the Import keys press in Lightroom's Import window.</summary>
    public ImportShortcuts ImportShortcuts { get; private set; } = ImportShortcuts.Standard;

    /// <summary>The app passes Lightroom's Import shortcuts for the active keyboard layout.</summary>
    public void UpdateImportShortcuts(ImportShortcuts shortcuts) => ImportShortcuts = shortcuts;

    public bool IsPaused { get; private set; }

    // Queries for the input hook -------------------------------------------------

    /// <summary>A key, the Combo Key, or the Palette Key is being held.</summary>
    public bool IsEngaged => comboKeyHold is not null || phase is not Phase.Idle;

    public bool NeedsScrollEvents => IsEngaged || suppressesMomentum;

    /// <summary>Mouse moves matter only while a Dial Key is held.</summary>
    public bool NeedsMouseMoves => phase is Phase.Held or Phase.Adjusting;

    public bool IsPaletteOpen => phase is Phase.Palette || palettePinned;

    /// <summary>The parameter a held Dial Key currently controls (for the overlay).</summary>
    public ParameterKey? ActiveParameter => phase switch
    {
        Phase.Held held => held.Target,
        Phase.Adjusting active => active.Target,
        _ => null,
    };

    /// <summary>Forget every gesture (e.g. after the input hook was removed).</summary>
    public void Reset()
    {
        phase = IdlePhase;
        comboKeyHold = null;
        palettePinned = false;
        consumedKeyUps.Clear();
        consumedMouseUps.Clear();
        scroll.Reset();
        dragResidual = 0;
        suppressesMomentum = false;
    }

    /// <summary>The Palette was closed by the app (item chosen, click outside).</summary>
    public void PaletteDidClose()
    {
        palettePinned = false;
        if (phase is Phase.Palette menu) phase = menu with { Interacted = true };
    }

    /// <summary>Key-downs still held back; the app posts them if it stops early.</summary>
    public IReadOnlyList<TEvent> HeldEvents
    {
        get
        {
            var events = new List<TEvent>();
            if (phase is Phase.Held held) events.Add(held.KeyDown);
            if (phase is Phase.Blocked { Scrolled: false } blocked) events.Add(blocked.KeyDown);
            if (phase is Phase.Palette menu) events.Add(menu.KeyDown);
            if (comboKeyHold is { Used: false } hold) events.Add(hold.KeyDown);
            return events;
        }
    }

    // Keys -----------------------------------------------------------------------

    /// <param name="isTextInputFocused">
    /// Evaluated at most once, and only when the key would otherwise be
    /// captured (it may need an accessibility query).
    /// </param>
    public Outcome KeyDown(KeyChord chord, TEvent @event, bool isAutoRepeat, double timestamp, EngineContext context,
        Func<bool> isTextInputFocused)
    {
        if (!context.IsLightroomFrontmost) return ReleaseEverything([@event]);
        // A key event is never a combo by itself: only the held Combo Key makes one.
        var pressed = chord.WithoutCombo;

        bool? focusAnswer = null;
        bool TextFocused()
        {
            focusAnswer ??= isTextInputFocused();
            return focusAnswer.Value;
        }

        // Pause works everywhere, even while paused.
        if (keymap.SystemKeys.PauseToggle is { } pause && pressed == pause)
        {
            if (isAutoRepeat) return Consume();
            IsPaused = !IsPaused;
            if (!IsPaused)
            {
                consumedKeyUps.Add(pressed.ScanCode);
                return Consume([new KeyIntent.TogglePause(false)]);
            }
            var released = ReleaseEverything([]);
            consumedKeyUps.Add(pressed.ScanCode);
            var pauseIntents = new List<KeyIntent>(released.Intents) { new KeyIntent.TogglePause(true) };
            return new Outcome(released.Decision is Decision.Pass ? ConsumeDecision : released.Decision, pauseIntents);
        }
        if (IsPaused) return Pass();
        if (context.IsImportWindow) return ImportKeyDown(pressed, @event, isAutoRepeat, TextFocused);

        switch (phase)
        {
            case Phase.Held held:
            {
                if (pressed.ScanCode == held.ScanCode) return Consume();
                if (ArrowDirection(pressed) is { } direction)
                {
                    var intents = FollowContext(context);
                    intents.AddRange(BeginGesture());
                    intents.AddRange(Adjust(direction, IsCoarse(HasShift(pressed.Modifiers))));
                    consumedKeyUps.Add(pressed.ScanCode);
                    return Consume(intents);
                }
                // Another key first: the user is typing. Release the held key ahead
                // of this one so characters stay in order.
                phase = IdlePhase;
                return Replace([held.KeyDown, @event]);
            }
            case Phase.Adjusting active:
            {
                if (pressed.ScanCode == active.ScanCode) return Consume();
                if (ArrowDirection(pressed) is { } direction)
                {
                    var intents = FollowContext(context);
                    intents.AddRange(BeginGesture());
                    intents.AddRange(Adjust(direction, IsCoarse(HasShift(pressed.Modifiers))));
                    consumedKeyUps.Add(pressed.ScanCode);
                    return Consume(intents);
                }
                return Pass();
            }
            case Phase.Blocked blocked:
                if (pressed.ScanCode == blocked.ScanCode) return Consume(); // auto-repeat
                // Another key first: the user is typing; deliver both in order.
                phase = IdlePhase;
                return blocked.Scrolled ? Pass() : Replace([blocked.KeyDown, @event]);
            case Phase.Palette menu:
                if (pressed.ScanCode == menu.ScanCode) return Consume();
                phase = menu with { Interacted = true };
                return Pass();
        }

        // Combo Key held: the next chord is looked up as a combo.
        if (comboKeyHold is { } hold)
        {
            if (pressed.ScanCode == hold.ScanCode) return Consume();
            if (keymap.SystemKeys.KeyReferenceToggle is { IsCombo: true } comboGuide && pressed.AsCombo == comboGuide)
            {
                comboKeyHold = hold with { Used = true };
                consumedKeyUps.Add(pressed.ScanCode);
                return Consume(isAutoRepeat ? [] : [new KeyIntent.ToggleKeyReference()]);
            }
            if (context.Scope is { } comboScope && indexes.TryGetValue(comboScope, out var comboIndex))
            {
                var combo = pressed.AsCombo;
                if (comboIndex.Actions.TryGetValue(combo, out var comboAction))
                {
                    comboKeyHold = hold with { Used = true };
                    consumedKeyUps.Add(pressed.ScanCode);
                    // One action per press, like a plain Command Key.
                    return Consume(isAutoRepeat ? [] : [new KeyIntent.RunAction(comboAction.Action)]);
                }
                if (ChordLookup.TryMatch(combo, comboIndex.Nudge, out var comboNudge, out var comboExtraShift))
                {
                    comboKeyHold = hold with { Used = true };
                    consumedKeyUps.Add(pressed.ScanCode);
                    return Consume(NudgeStep(comboNudge.Id, comboScope, comboNudge.Direction, IsCoarse(comboExtraShift), context));
                }
            }
            // Not a combo: the user is typing.
            comboKeyHold = null;
            return Replace(hold.Used ? [@event] : [hold.KeyDown, @event]);
        }

        // System Keys.
        if (keymap.SystemKeys.KeyReferenceToggle is { } guide && pressed == guide)
        {
            if (isAutoRepeat) return Consume();
            if (TextFocused()) return Pass();
            consumedKeyUps.Add(pressed.ScanCode);
            return Consume([new KeyIntent.ToggleKeyReference()]);
        }
        if (context.Scope == ParameterScope.Develop &&
            keymap.SystemKeys.PaletteKey is { } paletteKey && pressed == paletteKey)
        {
            if (palettePinned)
            {
                palettePinned = false;
                consumedKeyUps.Add(pressed.ScanCode);
                return Consume([new KeyIntent.ClosePalette()]);
            }
            if (isAutoRepeat || TextFocused()) return Pass();
            phase = new Phase.Palette(pressed.ScanCode, @event, timestamp, Interacted: false);
            return Consume([new KeyIntent.OpenPalette()]);
        }
        if (context.Scope is not { } scope || !indexes.TryGetValue(scope, out var index)) return Pass();
        if (keymap.SystemKeys.ComboKey is { } comboKey && pressed == comboKey)
        {
            if (isAutoRepeat || TextFocused()) return Pass();
            comboKeyHold = new ComboKeyHold(pressed.ScanCode, @event, Used: false);
            return Consume();
        }

        // Nudge Keys: one step per press, repeating while held.
        if (ChordLookup.TryMatch(pressed, index.Nudge, out var nudge, out var nudgeExtraShift))
        {
            if (!isAutoRepeat && TextFocused()) return Pass();
            consumedKeyUps.Add(pressed.ScanCode);
            return Consume(NudgeStep(nudge.Id, scope, nudge.Direction, IsCoarse(nudgeExtraShift), context));
        }

        // Dial Keys and tap Command Keys.
        if (isAutoRepeat) return Pass();
        var isDial = ChordLookup.TryMatch(pressed, index.Dial, out var dialId, out _);
        index.Actions.TryGetValue(pressed, out CommandKey? tapAction);
        if (!isDial && tapAction is null) return Pass();
        if (TextFocused()) return Pass();

        if (isDial)
        {
            var key = new ParameterKey(scope, dialId);
            if (Resolve(key, context) is not { } target)
            {
                phase = new Phase.Blocked(pressed.ScanCode, @event, key, Scrolled: false);
                return Consume();
            }
            scroll.Reset();
            dragResidual = 0;
            phase = new Phase.Held(pressed.ScanCode, pressed, @event, target, tapAction?.Action);
            return Consume();
        }
        consumedKeyUps.Add(pressed.ScanCode);
        return Consume([new KeyIntent.RunAction(tapAction!.Action)]);
    }

    public Outcome KeyUp(int scanCode, TEvent @event, double timestamp, EngineContext context)
    {
        if (context.IsImportWindow && context.IsLightroomFrontmost && IsInterrupted) return ReleaseEverything([@event]);
        if (consumedKeyUps.Remove(scanCode)) return Consume();

        switch (phase)
        {
            case Phase.Held held when held.ScanCode == scanCode:
                phase = IdlePhase;
                if (held.TapAction is { } tap) return Consume([new KeyIntent.RunAction(tap)]);
                // A tap: Lightroom gets the complete key press it would have had.
                return Replace([held.KeyDown, @event]);

            case Phase.Adjusting active when active.ScanCode == scanCode:
            {
                phase = IdlePhase;
                scroll.Reset();
                dragResidual = 0;
                suppressesMomentum = true;
                var intents = new List<KeyIntent>();
                if (active.Began) intents.Add(new KeyIntent.GestureEnded(active.Target));
                if (active.DidDrag && keymap.Input.ReturnCursorAfterRelease) intents.Add(new KeyIntent.RestorePointer());
                return Consume(intents);
            }

            case Phase.Blocked blocked when blocked.ScanCode == scanCode:
                phase = IdlePhase;
                return blocked.Scrolled ? Consume() : Replace([blocked.KeyDown, @event]);

            case Phase.Palette menu when menu.ScanCode == scanCode:
            {
                phase = IdlePhase;
                // The Palette Key has one job and never reaches Lightroom: a quick
                // tap leaves the Palette open until the next press, a hold closes
                // it on release unless it is pinned.
                var tapped = timestamp - menu.Start < TapThreshold && !menu.Interacted;
                if (tapped || keymap.Palette.KeepVisible)
                {
                    palettePinned = true;
                    return Consume();
                }
                return Consume([new KeyIntent.ClosePalette()]);
            }
        }

        // A Combo Key that made a combo is consumed; tapped alone, it is replayed.
        if (comboKeyHold is { } hold && hold.ScanCode == scanCode)
        {
            comboKeyHold = null;
            return hold.Used ? Consume() : Replace([hold.KeyDown, @event]);
        }
        return Pass();
    }

    // Wheel ----------------------------------------------------------------------

    public Outcome Scroll(double lineDelta, double pointDelta, bool isContinuous, bool isMomentum,
        KeyModifiers modifiers, EngineContext context)
    {
        // Scrolling another app never changes a gesture; key events settle it.
        if (!context.IsLightroomFrontmost || IsPaused) return Pass();
        if (context.IsImportWindow)
        {
            // Lightroom scrolls the Import window as usual. A wheel event is not
            // replayed, so a key still held back is delivered alone.
            suppressesMomentum = false;
            return IsInterrupted ? ReleaseEverything([]) : Pass();
        }

        var intents = FollowContext(context);
        switch (phase)
        {
            case Phase.Idle:
                if (suppressesMomentum)
                {
                    if (isMomentum) return Consume();
                    suppressesMomentum = false;
                }
                return Pass();
            case Phase.Palette:
                return Pass();
            case Phase.Blocked blocked:
            {
                if (isMomentum) return Pass();
                if (lineDelta == 0 && pointDelta == 0) return Pass();
                var first = !blocked.Scrolled;
                phase = blocked with { Scrolled = true };
                return Consume(first ? [new KeyIntent.UnavailableInMask(blocked.Key)] : []);
            }
            case Phase.Held:
                // Inertia from an earlier swipe, or an empty phase event, is not the
                // user scrolling: the key may still turn out to be a tap.
                if (isMomentum) return suppressesMomentum ? Consume() : Pass();
                if (lineDelta == 0 && pointDelta == 0) return Pass();
                intents.AddRange(BeginGesture());
                break;
            case Phase.Adjusting active:
                if (!active.Began && !isMomentum && (lineDelta != 0 || pointDelta != 0)) intents.AddRange(BeginGesture());
                break;
        }

        var steps = scroll.Steps(lineDelta, pointDelta, isContinuous, isMomentum);
        var directed = keymap.Input.InvertWheel ? -steps : steps;
        if (directed != 0) intents.AddRange(Adjust(directed, IsCoarse(HasShift(modifiers))));
        return Consume(intents);
    }

    // Mouse ----------------------------------------------------------------------

    public Outcome MouseDown(MouseButton button, int clickCount, TEvent @event, EngineContext context)
    {
        if (button == MouseButton.Left) leftButtonDown = true;
        if (!context.IsLightroomFrontmost || IsPaused) return Pass();
        if (context.IsImportWindow) return IsInterrupted ? ReleaseEverything([@event]) : Pass();

        if ((button == MouseButton.Back || button == MouseButton.Forward) &&
            keymap.Input.MouseBackForward && context.Scope is not null)
        {
            consumedMouseUps.Add(button);
            var name = button == MouseButton.Back ? "previousPhoto" : "nextPhoto";
            return Consume([new KeyIntent.RunAction(new CommandAction.Command(name))]);
        }

        if (ActiveParameter is null) return Pass();
        var followed = FollowContext(context);
        if (ActiveParameter is not { } target) return new Outcome(PassDecision, followed);
        if (button == MouseButton.Left)
        {
            consumedMouseUps.Add(MouseButton.Left);
            EnterAdjustingSilently();
            if (clickCount >= 2 && keymap.Input.DoubleClickReset) followed.Add(new KeyIntent.Reset(target));
            return Consume(followed);
        }
        if (button == MouseButton.Middle && keymap.Input.MiddleClickReset)
        {
            consumedMouseUps.Add(MouseButton.Middle);
            EnterAdjustingSilently();
            followed.Add(new KeyIntent.Reset(target));
            return Consume(followed);
        }
        return new Outcome(PassDecision, followed);
    }

    public Outcome MouseUp(MouseButton button, TEvent @event, EngineContext context)
    {
        if (button == MouseButton.Left) leftButtonDown = false;
        return consumedMouseUps.Remove(button) ? Consume() : Pass();
    }

    /// <summary>
    /// Pointer movement (<paramref name="dx"/> in points, positive to the right).
    /// Drags adjust the held Dial Key's slider horizontally.
    /// </summary>
    public Outcome MouseMoved(double dx, double dy, KeyModifiers modifiers, TEvent @event, EngineContext context)
    {
        if (!context.IsLightroomFrontmost || IsPaused || !NeedsMouseMoves) return Pass();
        if (context.IsImportWindow) return ReleaseEverything([@event]);
        var dragging = leftButtonDown;
        if (!dragging && keymap.Input.RequireMouseDown) return Pass();
        if (!double.IsFinite(dx)) return dragging ? Consume() : Pass();

        var intents = FollowContext(context);
        var pointsPerStep = keymap.Input.DragPointsPerStep;
        if (dragResidual != 0 && (dragResidual > 0) != (dx > 0) && dx != 0) dragResidual = 0;
        dragResidual += dx;
        var steps = Math.Truncate(dragResidual / pointsPerStep);
        dragResidual -= steps * pointsPerStep;
        if (dx != 0) MarkDragged();
        if (steps != 0)
        {
            intents.AddRange(BeginGesture());
            intents.AddRange(Adjust(steps, IsCoarse(HasShift(modifiers))));
        }
        return new Outcome(dragging ? ConsumeDecision : PassDecision, intents);
    }

    // Import window -----------------------------------------------------------------

    /// <summary>A gesture, a held key or an open Palette is left over from before the Import window came to the front.</summary>
    private bool IsInterrupted => IsEngaged || IsPaletteOpen;

    /// <summary>
    /// In Lightroom's Import window only the Import keys act. Anything left
    /// over from Develop or Library ends first, as when Lightroom loses focus:
    /// held keys reach Lightroom, a gesture ends, the Palette closes. The
    /// Develop and Library keys mean nothing there, so a held Dial Key does not
    /// keep its slider (Quick Develop would change the catalog's photos behind
    /// the window).
    /// </summary>
    private Outcome ImportKeyDown(KeyChord chord, TEvent @event, bool isAutoRepeat, Func<bool> textFocused)
    {
        var released = IsInterrupted ? ReleaseEverything([]) : Pass();
        var own = ImportKey(chord, isAutoRepeat, textFocused);
        var intents = new List<KeyIntent>(released.Intents);
        intents.AddRange(own.Intents);
        if (released.Decision is not Decision.Replace(var held)) return new Outcome(own.Decision, intents);
        return new Outcome(new Decision.Replace(own.Decision is Decision.Consume ? held : [.. held, @event]), intents);
    }

    /// <summary>
    /// An Import key presses Lightroom's own shortcut, plus → after Include or
    /// Exclude with "Move to the next photo". Auto-repeats are swallowed when
    /// the first press was taken, and pass when it went to a text field. A
    /// Toggle the layout has no key for is taken and sends nothing.
    /// </summary>
    private Outcome ImportKey(KeyChord chord, bool isAutoRepeat, Func<bool> textFocused)
    {
        if (keymap.ImportKeys.RoleFor(chord) is not { } role) return Pass();
        if (isAutoRepeat) return consumedKeyUps.Contains(chord.ScanCode) ? Consume() : Pass();
        if (textFocused()) return Pass();
        consumedKeyUps.Add(chord.ScanCode);
        if (ImportShortcuts.ChordFor(role) is not { } lightroomKey) return Consume();
        var intents = new List<KeyIntent> { new KeyIntent.RunAction(new CommandAction.Shortcut(lightroomKey)) };
        if (keymap.ImportKeys.Advance && role != ImportKeyRole.Toggle)
            intents.Add(new KeyIntent.RunAction(new CommandAction.Shortcut(ImportShortcuts.NextPhoto)));
        return Consume(intents);
    }

    // Helpers --------------------------------------------------------------------

    private static Outcome Pass() => new(PassDecision);

    private static Outcome Consume() => new(ConsumeDecision);

    private static Outcome Consume(IReadOnlyList<KeyIntent> intents) => new(ConsumeDecision, intents);

    private static Outcome Replace(IReadOnlyList<TEvent> events) => new(new Decision.Replace(events));

    private static bool HasShift(KeyModifiers modifiers) => (modifiers & KeyModifiers.Shift) != 0;

    private void RebuildIndexes()
    {
        indexes.Clear();
        foreach (var scope in ParameterScopes.All)
        {
            var index = new ScopeIndex();
            foreach (var (id, slot) in keymap.Slots(scope).OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                if (slot.Dial is { IsCombo: false } dial) index.Dial.TryAdd(dial, id);
                if (slot.NudgeDown is { } down) index.Nudge.TryAdd(down, (id, -1.0));
                if (slot.NudgeUp is { } up) index.Nudge.TryAdd(up, (id, 1.0));
            }
            foreach (var action in keymap.CommandKeysIn(scope)) index.Actions.TryAdd(action.Chord, action);
            indexes[scope] = index;
        }
        scroll.PointsPerStep = keymap.Input.TrackpadPointsPerStep;
    }

    /// <summary>
    /// The parameter a key controls now, or null when a mask is selected and
    /// the slider has no mask equivalent.
    /// </summary>
    private ParameterKey? Resolve(ParameterKey key, EngineContext context)
    {
        if (key.Scope != ParameterScope.Develop || !context.IsMasking || !keymap.Input.FollowSelectedMask) return key;
        if (Keymap.LocalKey(key) is not { } local || ParameterCatalog.Descriptor(local) is null) return null;
        return local;
    }

    private bool IsCoarse(bool shift) => shift != keymap.Input.SwapNormalAndCoarse;

    private double? ArrowDirection(KeyChord chord)
    {
        if (!keymap.Input.ArrowKeysAdjust || chord.UsesReservedModifiers) return null;
        return chord.ScanCode switch
        {
            ScanCodes.LeftArrow => -1,
            ScanCodes.RightArrow => 1,
            _ => null,
        };
    }

    private double? Step(ParameterKey key, bool coarse)
    {
        if (keymap.Steps(key) is not { } steps) return null;
        return coarse ? steps.Coarse : steps.Normal;
    }

    /// <summary><paramref name="direction"/> whole steps on the active gesture's slider.</summary>
    private List<KeyIntent> Adjust(double direction, bool coarse)
    {
        if (ActiveParameter is not { } target || Step(target, coarse) is not { } step) return [];
        var delta = IntentCoalescer.Round(direction * step);
        return delta == 0 ? [] : [new KeyIntent.Adjust(target, delta)];
    }

    private List<KeyIntent> NudgeStep(string id, ParameterScope scope, double direction, bool coarse, EngineContext context)
    {
        var key = new ParameterKey(scope, id);
        if (Resolve(key, context) is not { } target) return [new KeyIntent.UnavailableInMask(key)];
        if (Step(target, coarse) is not { } step) return [];
        var delta = IntentCoalescer.Round(direction * step);
        return delta == 0 ? [] : [new KeyIntent.Adjust(target, delta)];
    }

    /// <summary>Held → adjusting with gestureBegan (and revealPanel when enabled).</summary>
    private List<KeyIntent> BeginGesture()
    {
        switch (phase)
        {
            case Phase.Held held:
                phase = new Phase.Adjusting(held.ScanCode, held.Chord, held.Target, Began: true, DidDrag: false);
                return Began(held.Target);
            case Phase.Adjusting { Began: false } active:
                phase = active with { Began = true };
                return Began(active.Target);
            default:
                return [];
        }
    }

    private List<KeyIntent> Began(ParameterKey target) => keymap.Input.RevealPanels
        ? [new KeyIntent.GestureBegan(target), new KeyIntent.RevealPanel(target)]
        : [new KeyIntent.GestureBegan(target)];

    /// <summary>
    /// The module or the Masking tool changed while a Dial Key is held. The
    /// new context reaches the app a moment after Lightroom switches, often
    /// after the key went down, so the key moves the slider it has now. A
    /// gesture already begun ends on the old slider and begins again on the
    /// new one at the next step. When the key means nothing in the new
    /// context, it keeps its slider and Lightroom says why it cannot move it.
    /// </summary>
    private List<KeyIntent> FollowContext(EngineContext context)
    {
        (KeyChord Chord, ParameterKey Current)? held = phase switch
        {
            Phase.Held h => (h.Chord, h.Target),
            Phase.Adjusting a => (a.Chord, a.Target),
            _ => null,
        };
        if (held is not { } key
            || context.Scope is not { } scope
            || !indexes.TryGetValue(scope, out var index)
            || !ChordLookup.TryMatch(key.Chord, index.Dial, out var dialId, out _)
            || Resolve(new ParameterKey(scope, dialId), context) is not { } target
            || target == key.Current)
        {
            return [];
        }
        switch (phase)
        {
            case Phase.Held h:
                index.Actions.TryGetValue(key.Chord, out var tapAction);
                phase = h with { Target = target, TapAction = tapAction?.Action };
                return [];
            case Phase.Adjusting a:
                List<KeyIntent> ended = a.Began ? [new KeyIntent.GestureEnded(a.Target)] : [];
                phase = a with { Target = target, Began = false };
                return ended;
            default:
                return [];
        }
    }

    /// <summary>
    /// A click while a Dial Key is held: the key is no longer a tap, but no
    /// slider gesture has started yet.
    /// </summary>
    private void EnterAdjustingSilently()
    {
        if (phase is Phase.Held held)
            phase = new Phase.Adjusting(held.ScanCode, held.Chord, held.Target, Began: false, DidDrag: false);
    }

    private void MarkDragged()
    {
        if (phase is Phase.Held held)
            phase = new Phase.Adjusting(held.ScanCode, held.Chord, held.Target, Began: false, DidDrag: true);
        else if (phase is Phase.Adjusting active)
            phase = active with { DidDrag = true };
    }

    /// <summary>
    /// Lightroom lost focus (or pause): deliver every held key rather than
    /// lose it, end gestures, close the Palette.
    /// </summary>
    private Outcome ReleaseEverything(IReadOnlyList<TEvent> then)
    {
        var held = HeldEvents;
        var intents = new List<KeyIntent>();
        if (phase is Phase.Adjusting { Began: true } active) intents.Add(new KeyIntent.GestureEnded(active.Target));
        if (IsPaletteOpen) intents.Add(new KeyIntent.ClosePalette());
        var pausedState = IsPaused;
        Reset();
        IsPaused = pausedState;
        // Nothing held back: the current event (if any) goes through unchanged.
        if (held.Count == 0) return new Outcome(PassDecision, intents);
        return new Outcome(new Decision.Replace([.. held, .. then]), intents);
    }
}
