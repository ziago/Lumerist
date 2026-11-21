using Xunit;

namespace Lumerist.Core.Tests;

public class IntentDispatcherTests
{
    private static readonly ParameterKey Exposure = new(ParameterScope.Develop, "Exposure");
    private static readonly ParameterKey LibraryExposure = new(ParameterScope.Library, "Exposure");

    /// <summary>A clock and timers that move only when the test says so.</summary>
    private sealed class ManualTime
    {
        private readonly List<(DateTimeOffset Due, Action Action, Handle Handle)> timers = [];

        public DateTimeOffset Now { get; private set; } = new(2026, 11, 19, 12, 0, 0, TimeSpan.Zero);

        public int Pending => timers.Count(timer => !timer.Handle.Cancelled);

        public IDisposable Schedule(TimeSpan delay, Action action)
        {
            var handle = new Handle();
            timers.Add((Now + delay, action, handle));
            return handle;
        }

        public void Advance(TimeSpan by)
        {
            Now += by;
            while (timers.Where(timer => timer.Due <= Now).OrderBy(timer => timer.Due).FirstOrDefault() is { Action: not null } due)
            {
                timers.Remove(due);
                if (!due.Handle.Cancelled) due.Action();
            }
        }

        public sealed class Handle : IDisposable
        {
            public bool Cancelled { get; private set; }

            public void Dispose() => Cancelled = true;
        }
    }

    private sealed class Harness
    {
        public ManualTime Time { get; } = new();
        public List<LightroomRequest> Sent { get; } = [];
        public List<KeyChord> Shortcuts { get; } = [];
        public List<string> Notices { get; } = [];
        public List<string> Log { get; } = [];
        public bool Connected { get; set; } = true;
        public bool LightroomInFront { get; set; } = true;
        public long NextId { get; set; } = 1;
        public IntentDispatcher Dispatcher { get; }

        public Harness()
        {
            Dispatcher = new IntentDispatcher(new IntentDispatcherHooks
            {
                Send = request =>
                {
                    if (!Connected) return null;
                    Sent.Add(request);
                    return NextId++;
                },
                PostShortcut = chord =>
                {
                    if (!LightroomInFront) return false;
                    Shortcuts.Add(chord);
                    return true;
                },
                Schedule = Time.Schedule,
                Now = () => Time.Now,
                UpdateRateHz = () => 20,
                Notice = Notices.Add,
                Log = Log.Add,
            });
        }
    }

    [Fact]
    public void AdjustmentsAreSummedUntilTheNextTick()
    {
        var harness = new Harness();
        harness.Dispatcher.Handle([new KeyIntent.Adjust(Exposure, 0.05), new KeyIntent.Adjust(Exposure, 0.05)]);
        Assert.Empty(harness.Sent);
        Assert.Equal(1, harness.Time.Pending);

        harness.Time.Advance(TimeSpan.FromMilliseconds(49));
        Assert.Empty(harness.Sent);
        harness.Time.Advance(TimeSpan.FromMilliseconds(1)); // 20 Hz
        Assert.Equal([LightroomRequest.Adjust(ParameterScope.Develop, "Exposure", 0.1)], harness.Sent);
    }

    [Fact]
    public void AnImmediateIntentFlushesInOrderAndCancelsTheTick()
    {
        var harness = new Harness();
        harness.Dispatcher.Handle([
            new KeyIntent.GestureBegan(Exposure),
            new KeyIntent.Adjust(Exposure, 0.2),
            new KeyIntent.GestureEnded(Exposure),
        ]);
        Assert.Equal([
            LightroomRequest.Gesture(GesturePhase.Begin, "Exposure"),
            LightroomRequest.Adjust(ParameterScope.Develop, "Exposure", 0.2),
            LightroomRequest.Gesture(GesturePhase.End, "Exposure"),
        ], harness.Sent);
        harness.Time.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(3, harness.Sent.Count);
    }

    [Fact]
    public void GesturesAreSentForDevelopOnly()
    {
        var harness = new Harness();
        harness.Dispatcher.Handle([new KeyIntent.GestureBegan(LibraryExposure), new KeyIntent.Adjust(LibraryExposure, 0.33), new KeyIntent.GestureEnded(LibraryExposure)]);
        Assert.Equal([LightroomRequest.Adjust(ParameterScope.Library, "Exposure", 0.33)], harness.Sent);
    }

    [Fact]
    public void ARefusedCommandIsRetriedWithLightroomsShortcut()
    {
        var harness = new Harness();
        harness.Dispatcher.Handle([new KeyIntent.RunAction(new CommandAction.Command("autoTone"))]);
        Assert.Equal([LightroomRequest.Command(LightroomCommandName.AutoTone)], harness.Sent);

        Assert.True(harness.Dispatcher.DidReceive(new LightroomAck(1, false, "not in Library")));
        Assert.Equal([new KeyChord(0x16, KeyModifiers.Control)], harness.Shortcuts);
        // Only once.
        Assert.False(harness.Dispatcher.DidReceive(new LightroomAck(1, false, "again")));
    }

    [Fact]
    public void AcceptedLateOrUnknownAnswersSendNoShortcut()
    {
        var harness = new Harness();
        harness.Dispatcher.Handle([new KeyIntent.RunAction(new CommandAction.Command("undo"))]);
        Assert.False(harness.Dispatcher.DidReceive(new LightroomAck(1, true)));

        harness.Dispatcher.Handle([new KeyIntent.RunAction(new CommandAction.Command("undo"))]);
        harness.Time.Advance(IntentDispatcher.FallbackLifetime);
        Assert.False(harness.Dispatcher.DidReceive(new LightroomAck(2, false)));

        Assert.False(harness.Dispatcher.DidReceive(new LightroomAck(99, false)));
        Assert.False(harness.Dispatcher.DidReceive(new LightroomAck(null, false)));
        Assert.Empty(harness.Shortcuts);
    }

    [Fact]
    public void AFallbackNeverLandsInAnotherApp()
    {
        var harness = new Harness();
        harness.Dispatcher.Handle([new KeyIntent.RunAction(new CommandAction.Command("redo"))]);
        harness.LightroomInFront = false;
        Assert.False(harness.Dispatcher.DidReceive(new LightroomAck(1, false)));
        Assert.Contains(harness.Log, line => line.Contains("skipped"));
    }

    [Fact]
    public void LosingTheConnectionForgetsPendingFallbacks()
    {
        var harness = new Harness();
        harness.Dispatcher.Handle([new KeyIntent.RunAction(new CommandAction.Command("flagPick"))]);
        harness.Dispatcher.ConnectionLost();
        Assert.False(harness.Dispatcher.DidReceive(new LightroomAck(1, false)));
    }

    [Fact]
    public void CommandsWithoutAFallbackOrNotSentKeepNone()
    {
        var harness = new Harness();
        harness.Dispatcher.Handle([new KeyIntent.RunAction(new CommandAction.Command("rating", "3"))]);
        Assert.False(harness.Dispatcher.DidReceive(new LightroomAck(1, false)));

        harness.Connected = false;
        harness.Dispatcher.Handle([new KeyIntent.RunAction(new CommandAction.Command("undo"))]);
        Assert.Empty(harness.Shortcuts);
    }

    [Fact]
    public void UnknownCommandsAndBadRequestsAreLoggedNotSent()
    {
        var harness = new Harness();
        harness.Dispatcher.Handle([
            new KeyIntent.RunAction(new CommandAction.Command("formatDisk")),
            new KeyIntent.RunAction(new CommandAction.Command("rating", "9")),
        ]);
        Assert.Empty(harness.Sent);
        Assert.Contains("Unknown command formatDisk", harness.Log);
        Assert.Contains(harness.Log, line => line.StartsWith("Request not sent:"));
    }

    [Fact]
    public void PresetsAndShortcuts()
    {
        var harness = new Harness();
        harness.Dispatcher.Handle([
            new KeyIntent.RunAction(new CommandAction.Preset("A1-B2", "Warm")),
            new KeyIntent.RunAction(new CommandAction.Shortcut(new KeyChord(ScanCodes.V, KeyModifiers.Control | KeyModifiers.Alt))),
        ]);
        Assert.Equal([LightroomRequest.Command(LightroomCommandName.ApplyPreset, "A1-B2")], harness.Sent);
        Assert.Equal([new KeyChord(ScanCodes.V, KeyModifiers.Control | KeyModifiers.Alt)], harness.Shortcuts);
    }

    [Fact]
    public void MacroStepsWaitForTheirDelay()
    {
        var harness = new Harness();
        harness.Dispatcher.Handle([new KeyIntent.RunAction(new CommandAction.Macro([
            new MacroStep(new CommandAction.Command("undo"), 0.5),
            new MacroStep(new CommandAction.Command("redo")),
            new MacroStep(new CommandAction.Command("flagPick"), 1),
        ]))]);
        Assert.Equal([LightroomRequest.Command(LightroomCommandName.Undo)], harness.Sent);
        harness.Time.Advance(TimeSpan.FromSeconds(0.4));
        Assert.Single(harness.Sent);
        harness.Time.Advance(TimeSpan.FromSeconds(0.1));
        Assert.Equal([
            LightroomRequest.Command(LightroomCommandName.Undo),
            LightroomRequest.Command(LightroomCommandName.Redo),
            LightroomRequest.Command(LightroomCommandName.FlagPick),
        ], harness.Sent);
        Assert.Equal(0, harness.Time.Pending); // No wait after the last step.
    }

    [Fact]
    public void NoticesAndPanels()
    {
        var harness = new Harness();
        var opened = 0;
        var dispatcher = new IntentDispatcher(new IntentDispatcherHooks
        {
            Send = _ => 1,
            PostShortcut = _ => true,
            Schedule = harness.Time.Schedule,
            Notice = harness.Notices.Add,
            OpenPalette = () => opened++,
        });
        dispatcher.Handle([
            new KeyIntent.TogglePause(true),
            new KeyIntent.TogglePause(false),
            new KeyIntent.UnavailableInMask(new ParameterKey(ParameterScope.Develop, "Vibrance")),
            new KeyIntent.OpenPalette(),
            new KeyIntent.RestorePointer(),
        ]);
        Assert.Equal(["Lumerist paused", "Lumerist resumed", "Vibrance is not available in a mask"], harness.Notices);
        Assert.Equal(1, opened);
    }
}
