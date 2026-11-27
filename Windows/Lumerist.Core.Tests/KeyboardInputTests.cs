using Xunit;
using static Lumerist.Core.Tests.KeymapTestSupport;

namespace Lumerist.Core.Tests;

/// <summary>AltGr as a modifier of its own, input-method keys, the app commands' dispatch, key names, unreadable keymaps.</summary>
public class AltGrTests
{
    private static KeyGestureEngine<string>.Outcome Down(KeyGestureEngine<string> engine, KeyChord chord, EngineContext? context = null) =>
        engine.KeyDown(chord, "↓", isAutoRepeat: false, 0, context ?? new EngineContext(), () => false);

    [Fact]
    public void AltGrAndAPlainKeyIsNotThatKey()
    {
        // Q is the Exposure Dial Key; AltGr+Q types @ on German keyboards and passes.
        var keymap = Keymap.StandardLayout();
        keymap.CommandKeys.Add(CommandKey.Create(CommandScope.Develop, Key(ScanCodes.H), "Undo", new CommandAction.Command("undo")));
        var engine = new KeyGestureEngine<string>(keymap);
        Assert.IsType<KeyGestureEngine<string>.Decision.Pass>(Down(engine, Key(ScanCodes.Q, KeyModifiers.AltGr)).Decision);
        Assert.IsType<KeyGestureEngine<string>.Decision.Pass>(Down(engine, Key(ScanCodes.H, KeyModifiers.AltGr)).Decision);
        // Shift+AltGr is not Shift for the large step either.
        Assert.IsType<KeyGestureEngine<string>.Decision.Pass>(Down(engine, Key(ScanCodes.Q, KeyModifiers.AltGr | KeyModifiers.Shift)).Decision);
        // The plain keys still work.
        Assert.Equal([new KeyIntent.RunAction(new CommandAction.Command("undo"))], Down(engine, Key(ScanCodes.H)).Intents);
    }

    [Fact]
    public void AnImportKeyWithAltGrPasses()
    {
        var keymap = Keymap.StandardLayout();
        keymap.ImportKeys = new ImportKeys { Include = Key(0x2C) };
        var engine = new KeyGestureEngine<string>(keymap);
        var import = new EngineContext(Module: EngineContext.ImportModule);
        Assert.IsType<KeyGestureEngine<string>.Decision.Pass>(Down(engine, Key(0x2C, KeyModifiers.AltGr), import).Decision);
    }

    [Fact]
    public void AKeyRecordedWithAltGrMatchesOnlyWithAltGr()
    {
        var keymap = Keymap.StandardLayout();
        keymap.CommandKeys.Add(CommandKey.Create(CommandScope.Develop, Key(ScanCodes.H, KeyModifiers.AltGr), "Redo", new CommandAction.Command("redo")));
        keymap.Validate();
        var engine = new KeyGestureEngine<string>(keymap);
        Assert.Equal([new KeyIntent.RunAction(new CommandAction.Command("redo"))], Down(engine, Key(ScanCodes.H, KeyModifiers.AltGr)).Intents);
        Assert.IsType<KeyGestureEngine<string>.Decision.Pass>(Down(engine, Key(ScanCodes.H)).Decision);
    }

    [Fact]
    public void AltGrChordsReadAndStore()
    {
        Assert.Equal("AltGr+Q", Key(ScanCodes.Q, KeyModifiers.AltGr).DisplayText);
        Assert.Equal("Ctrl+AltGr+Shift+Q", Key(ScanCodes.Q, KeyModifiers.Control | KeyModifiers.AltGr | KeyModifiers.Shift).DisplayText);
        Assert.True(Key(ScanCodes.Q, KeyModifiers.AltGr).UsesReservedModifiers);
        var keymap = Keymap.StandardLayout();
        keymap.CommandKeys.Add(CommandKey.Create(CommandScope.Develop, Key(ScanCodes.H, KeyModifiers.AltGr), "Redo", new CommandAction.Command("redo")));
        var text = KeymapStore.Encode(keymap);
        Assert.Contains("\"AltGr\"", text);
        Assert.Equal(Key(ScanCodes.H, KeyModifiers.AltGr), KeymapStore.Decode(text).CommandKeys[^1].Chord);
        // An AltGr chord types a character: the usual warning.
        Assert.Contains(keymap.Conflicts(), conflict => conflict.Chord == Key(ScanCodes.H, KeyModifiers.AltGr)
            && conflict.Message == "AltGr+H is also used for typing or by Lightroom and Windows.");
    }
}

public class InputMethodKeyTests
{
    [Fact]
    public void TheRecorderRefusesInputMethodKeys()
    {
        var recording = new ChordRecording(KeyChord.Tab);
        // 한/영, 한자, 変換, 無変換, かな by scan code.
        foreach (var scanCode in new[] { 0xF2, 0xF1, 0x79, 0x7B, 0x70 })
            Assert.Equal(ChordRecording.Outcome.Refused, recording.KeyDown(Key(scanCode), isRepeat: false));
        // 半角/全角 shares the scan code of the key left of 1: the app says so from the virtual key.
        Assert.Equal(ChordRecording.Outcome.Refused, recording.KeyDown(Key(ScanCodes.LeftOf1), isRepeat: false, isInputMethodKey: true));
        Assert.Null(recording.Chord);
        // Still recording: the next key is taken.
        Assert.Equal(ChordRecording.Outcome.Finish, recording.KeyDown(Key(ScanCodes.LeftOf1), isRepeat: false));
        Assert.Equal(Key(ScanCodes.LeftOf1), recording.Chord);
    }

    [Fact]
    public void InputMethodAndJisKeysHaveNames()
    {
        Assert.Equal("Han/Yeong", KeyNames.Name(0xF2, null));
        Assert.Equal("Hanja", KeyNames.Name(0xF1, null));
        Assert.Equal("Henkan", KeyNames.Name(0x79, null));
        // Windows' own name comes first for them.
        Assert.Equal("変換", KeyNames.Name(0x79, code => code == 0x79 ? "変換" : null));
        // The ABNT / JIS keys take the layout's character: / on Brazilian keyboards.
        Assert.Contains(0x73, KeyNames.LayoutDependentScanCodes);
        Assert.Contains(0x7D, KeyNames.LayoutDependentScanCodes);
        Assert.Contains(0x7E, KeyNames.LayoutDependentScanCodes);
        Assert.Equal("/", KeyNames.Name(0x73, code => code == 0x73 ? "/" : null));
        Assert.Equal("Ro", KeyNames.Name(0x73, null));
        // A key that types nothing gets the system's name (半角/全角 left of 1 on Japanese keyboards).
        Assert.Equal("半角/全角", KeyNames.Name(ScanCodes.LeftOf1, code => code == ScanCodes.LeftOf1 ? "半角/全角" : null));
        // Keys without a US name ask the layout too, else read "Key 5A".
        Assert.Equal("F13", KeyNames.Name(0x64, code => code == 0x64 ? "F13" : null));
        Assert.Equal("Key 5A", KeyNames.Name(0x5A, null));
    }
}

public class AppCommandDispatchTests
{
    private static (IntentDispatcher Dispatcher, List<LightroomRequest> Sent, List<KeyChord> Shortcuts, List<string> Log) Make(
        Func<string, KeyChord?>? appShortcut = null)
    {
        var sent = new List<LightroomRequest>();
        var shortcuts = new List<KeyChord>();
        var log = new List<string>();
        var dispatcher = new IntentDispatcher(new IntentDispatcherHooks
        {
            Send = request =>
            {
                sent.Add(request);
                return 1;
            },
            PostShortcut = chord =>
            {
                shortcuts.Add(chord);
                return true;
            },
            Schedule = (_, _) => new NoTimer(),
            AppCommandShortcut = appShortcut ?? (name => AppCommands.Shortcut(name)),
            Log = log.Add,
        });
        return (dispatcher, sent, shortcuts, log);
    }

    private sealed class NoTimer : IDisposable
    {
        public void Dispose() { }
    }

    [Fact]
    public void AnAppCommandPressesLightroomsShortcutAndSendsNoRequest()
    {
        var (dispatcher, sent, shortcuts, _) = Make();
        dispatcher.Handle([new KeyIntent.RunAction(new CommandAction.Command(AppCommands.PasteSettingsFromPrevious))]);
        Assert.Empty(sent);
        Assert.Equal([new KeyChord(ScanCodes.V, KeyModifiers.Control | KeyModifiers.Alt)], shortcuts);
    }

    [Fact]
    public void TheShortcutComesFromTheAppsLayout()
    {
        var (dispatcher, _, shortcuts, _) = Make(name => AppCommands.Shortcut(name, LayoutTables.Dvorak));
        dispatcher.Handle([new KeyIntent.RunAction(new CommandAction.Command(AppCommands.PasteSettingsFromPrevious))]);
        Assert.Equal([new KeyChord(0x34, KeyModifiers.Control | KeyModifiers.Alt)], shortcuts);
    }

    [Fact]
    public void WithAnArgumentItIsNotTheAppCommand()
    {
        var (dispatcher, sent, shortcuts, log) = Make();
        dispatcher.Handle([new KeyIntent.RunAction(new CommandAction.Command(AppCommands.PasteSettingsFromPrevious, "x"))]);
        Assert.Empty(shortcuts);
        Assert.Empty(sent);
        Assert.Contains(log, line => line.StartsWith("Unknown command", StringComparison.Ordinal));
    }

    [Fact]
    public void InAMacroToo()
    {
        var (dispatcher, sent, shortcuts, _) = Make();
        dispatcher.Handle([new KeyIntent.RunAction(new CommandAction.Macro([
            new MacroStep(new CommandAction.Command("undo")),
            new MacroStep(new CommandAction.Command(AppCommands.PasteSettingsFromPrevious)),
        ]))]);
        Assert.Single(sent);
        Assert.Single(shortcuts);
    }
}

public class UnreadableKeymapTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "lumerist-keymap-" + Guid.NewGuid().ToString("N"));

    public UnreadableKeymapTests() => Directory.CreateDirectory(folder);

    public void Dispose() => Directory.Delete(folder, recursive: true);

    [Fact]
    public void AnUnreadableKeymapIsCopiedOnceBeforeAnythingReplacesIt()
    {
        var path = Path.Combine(folder, "keymap.json");
        File.WriteAllText(path, """{ "format": 99, "develop": {} }""");
        Assert.Throws<KeymapException>(() => KeymapStore.Load(path));

        var now = new DateTimeOffset(2026, 11, 27, 15, 30, 0, TimeSpan.Zero);
        var copy = KeymapStore.PreserveUnreadable(path, now);
        Assert.StartsWith("keymap.unreadable-", Path.GetFileName(copy));
        Assert.EndsWith(".json", copy);
        Assert.Equal(File.ReadAllText(path), File.ReadAllText(copy));
        // The same content is not copied again at the next launch.
        Assert.Equal(copy, KeymapStore.PreserveUnreadable(path, now.AddMinutes(5)));
        Assert.Single(Directory.GetFiles(folder, "keymap.unreadable-*.json"));

        // Other content at the same second gets its own copy.
        File.WriteAllText(path, "not json");
        var second = KeymapStore.PreserveUnreadable(path, now);
        Assert.NotEqual(copy, second);
        Assert.Equal("not json", File.ReadAllText(second));

        // Saving the standard layout over it now loses nothing.
        KeymapStore.Save(Keymap.StandardLayout(), path);
        Assert.NotNull(KeymapStore.Load(path));
        Assert.Equal(2, Directory.GetFiles(folder, "keymap.unreadable-*.json").Length);
    }
}
