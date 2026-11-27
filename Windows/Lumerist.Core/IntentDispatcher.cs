namespace Lumerist.Core;

/// <summary>What <see cref="IntentDispatcher"/> needs from the app.</summary>
public sealed class IntentDispatcherHooks
{
    /// <summary>Sends a request; returns its id (null when not connected).</summary>
    public required Func<LightroomRequest, long?> Send { get; init; }

    /// <summary>Sends one of Lightroom's shortcuts; false when Lightroom is not in front (nothing sent).</summary>
    public required Func<KeyChord, bool> PostShortcut { get; init; }

    /// <summary>Runs an action after a delay, on the dispatcher's thread; disposing cancels it.</summary>
    public required Func<TimeSpan, Action, IDisposable> Schedule { get; init; }

    /// <summary>Shortcut to try when Lightroom refuses a command (see <see cref="FallbackShortcuts"/>).</summary>
    public Func<LightroomCommandName, KeyChord?> FallbackShortcut { get; init; } = command => FallbackShortcuts.For(command);

    /// <summary>The shortcut an app command presses, in Lightroom's layout (see <see cref="AppCommands"/>).</summary>
    public Func<string, KeyChord?> AppCommandShortcut { get; init; } = name => AppCommands.Shortcut(name);

    public Func<DateTimeOffset> Now { get; init; } = () => DateTimeOffset.UtcNow;

    public Func<double> UpdateRateHz { get; init; } = () => IntentCoalescer.DefaultRate;

    public Action<string> Notice { get; init; } = static _ => { };

    public Action OpenPalette { get; init; } = static () => { };

    public Action ClosePalette { get; init; } = static () => { };

    public Action ToggleKeyReference { get; init; } = static () => { };

    public Action<string> Log { get; init; } = static _ => { };
}

/// <summary>
/// Turns the gesture engine's intents into protocol-3 requests (adjustments
/// summed at the chosen update rate), runs macros, sends Lightroom's own
/// shortcuts, and falls back to a shortcut when the SDK refuses a command
/// (IntentDispatcher.swift).
/// </summary>
/// <remarks>Single-threaded: call it from one thread (the app's UI thread), where <see cref="IntentDispatcherHooks.Schedule"/> also calls back.</remarks>
public sealed class IntentDispatcher(IntentDispatcherHooks hooks)
{
    /// <summary>A late answer must not trigger a keystroke.</summary>
    public static readonly TimeSpan FallbackLifetime = TimeSpan.FromSeconds(3);

    private readonly IntentCoalescer coalescer = new();
    private IDisposable? pendingFlush;
    private int flushGeneration;

    /// <summary>Shortcut to send if Lightroom refuses a command, keyed by request id.</summary>
    private readonly Dictionary<long, (KeyChord Chord, DateTimeOffset Sent)> fallbacks = [];

    public void Handle(IEnumerable<KeyIntent> intents)
    {
        foreach (var intent in intents)
        {
            if (coalescer.Add(intent))
            {
                CancelFlush();
                Perform(coalescer.Flush());
            }
            else
            {
                ScheduleFlush();
            }
        }
    }

    /// <summary>Called for every ack; returns true when a rejected command was retried with Lightroom's own shortcut.</summary>
    public bool DidReceive(LightroomAck ack)
    {
        ArgumentNullException.ThrowIfNull(ack);
        if (ack.Id is not { } id || !fallbacks.Remove(id, out var fallback)) return false;
        if (ack.Accepted || hooks.Now() - fallback.Sent >= FallbackLifetime) return false;
        if (!hooks.PostShortcut(fallback.Chord))
        {
            hooks.Log($"Fallback {fallback.Chord.DisplayText} skipped: Lightroom is not in front");
            return false;
        }
        hooks.Log($"Fallback shortcut {fallback.Chord.DisplayText} after: {ack.Message ?? "rejected"}");
        return true;
    }

    /// <summary>The bridge dropped: pending answers will never arrive.</summary>
    public void ConnectionLost() => fallbacks.Clear();

    private void ScheduleFlush()
    {
        if (pendingFlush is not null) return;
        var interval = TimeSpan.FromSeconds(1 / Math.Max(1, hooks.UpdateRateHz()));
        var generation = ++flushGeneration;
        pendingFlush = hooks.Schedule(interval, () =>
        {
            // A flush cancelled after it was already queued does nothing.
            if (generation != flushGeneration || pendingFlush is null) return;
            pendingFlush = null;
            Perform(coalescer.Flush());
        });
    }

    private void CancelFlush()
    {
        flushGeneration++;
        pendingFlush?.Dispose();
        pendingFlush = null;
    }

    private void Perform(IReadOnlyList<KeyIntent> intents)
    {
        foreach (var intent in intents)
        {
            switch (intent)
            {
                case KeyIntent.Adjust(var key, var delta):
                    Send(() => LightroomRequest.Adjust(key.Scope, key.Id, delta));
                    break;
                case KeyIntent.Reset(var key):
                    Send(() => LightroomRequest.Reset(key.Scope, key.Id));
                    break;
                case KeyIntent.GestureBegan(var key):
                    if (key.Scope == ParameterScope.Develop) Send(() => LightroomRequest.Gesture(GesturePhase.Begin, key.Id));
                    break;
                case KeyIntent.GestureEnded(var key):
                    if (key.Scope == ParameterScope.Develop) Send(() => LightroomRequest.Gesture(GesturePhase.End, key.Id));
                    break;
                case KeyIntent.RunAction(var action):
                    Run(action);
                    break;
                case KeyIntent.RevealPanel(var key):
                    Send(() => LightroomRequest.Command(LightroomCommandName.RevealPanel, key.Id));
                    break;
                case KeyIntent.OpenPalette:
                    hooks.Log("Palette open");
                    hooks.OpenPalette();
                    break;
                case KeyIntent.ClosePalette:
                    hooks.Log("Palette closed");
                    hooks.ClosePalette();
                    break;
                case KeyIntent.ToggleKeyReference:
                    hooks.ToggleKeyReference();
                    break;
                case KeyIntent.TogglePause(var isPaused):
                    hooks.Notice(isPaused ? "Lumerist paused" : "Lumerist resumed");
                    break;
                case KeyIntent.UnavailableInMask(var key):
                    hooks.Notice($"{ParameterCatalog.Descriptor(key)?.Label ?? key.Id} is not available in a mask");
                    break;
                case KeyIntent.RestorePointer:
                    break; // Handled by the input hook, which knows where the gesture began.
            }
        }
    }

    private void Run(CommandAction action)
    {
        switch (action)
        {
            case CommandAction.Command(var name, null) when AppCommands.IsAppCommand(name):
                // Never through the plug-in: Lightroom's own shortcut is the only way. (With an argument it is not an app command.)
                if (hooks.AppCommandShortcut(name) is { } shortcut) hooks.PostShortcut(shortcut);
                break;
            case CommandAction.Command(var name, var argument):
                if (LightroomCommandNames.FromWireName(name) is not { } command)
                {
                    hooks.Log($"Unknown command {name}");
                    return;
                }
                var id = Send(() => LightroomRequest.Command(command, argument));
                if (id is { } sent && hooks.FallbackShortcut(command) is { } fallback) fallbacks[sent] = (fallback, hooks.Now());
                break;
            case CommandAction.Preset(var uuid, _):
                Send(() => LightroomRequest.Command(LightroomCommandName.ApplyPreset, uuid));
                break;
            case CommandAction.Shortcut(var chord):
                hooks.PostShortcut(chord);
                break;
            case CommandAction.Macro(var steps):
                RunMacro(steps, 0);
                break;
        }
    }

    /// <summary>Runs the steps in order, waiting after each one for its delay.</summary>
    private void RunMacro(IReadOnlyList<MacroStep> steps, int start)
    {
        for (var index = start; index < steps.Count; index++)
        {
            Run(steps[index].Action);
            if (steps[index].Delay > 0 && index + 1 < steps.Count)
            {
                var next = index + 1;
                hooks.Schedule(TimeSpan.FromSeconds(steps[index].Delay), () => RunMacro(steps, next));
                return;
            }
        }
    }

    private long? Send(Func<LightroomRequest> make)
    {
        try
        {
            return hooks.Send(make());
        }
        catch (Exception error)
        {
            hooks.Log($"Request not sent: {error.Message}");
            return null;
        }
    }
}
