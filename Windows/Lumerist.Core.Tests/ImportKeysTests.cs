using Xunit;
using static Lumerist.Core.Tests.KeymapTestSupport;

namespace Lumerist.Core.Tests;

// Import keys: what the shared cases (gestures/import-keys.json and the
// keymap-rules cases) cannot express. The Windows rule for the Import window
// (ImportWindowRuleTests.swift is the Mac's) uses the shapes measured in
// Lightroom Classic 16.0.1 on Windows 11, 27 Nov 2026.

public class ImportWindowRuleTests
{
    private const uint Popup = 0x9400_0000;             // WS_POPUP | WS_VISIBLE | WS_CLIPSIBLINGS
    private const uint DisabledPopup = 0x9C00_0000;     // the same, with WS_DISABLED
    private const string MainFrame = ImportWindowRule.MainFrameClass;

    private static WindowShape OwnedByMainWindow(uint style, int width, int height) =>
        new(style, width, height, MainFrame, 2500, 1332);

    [Fact]
    public void TheImportWindowIsRecognised()
    {
        // The Import window, and the first Assisted Culling guide over it (a
        // transparent popup of the same shape, the main window disabled meanwhile).
        Assert.True(ImportWindowRule.IsImportWindow(OwnedByMainWindow(Popup, 2484, 1293)));
        // The Import window itself while the guide disables it.
        Assert.True(ImportWindowRule.IsImportWindow(OwnedByMainWindow(DisabledPopup, 2484, 1293)));
        // A smaller main window: the ratios count, not the pixels.
        Assert.True(ImportWindowRule.IsImportWindow(new WindowShape(Popup, 1250, 700, MainFrame, 1300, 760)));
    }

    [Fact]
    public void TheMainWindowAndItsDialogsAreNotImport()
    {
        // The main window has no owner.
        Assert.False(ImportWindowRule.IsImportWindow(new WindowShape(0x16CF_4000, 2500, 1332, null, 0, 0)));
        Assert.False(ImportWindowRule.IsImportWindow(null));
        // Export and Preferences: title bar, modal, smaller.
        Assert.False(ImportWindowRule.IsImportWindow(OwnedByMainWindow(0x96CD_0000, 1066, 610)));
        Assert.False(ImportWindowRule.IsImportWindow(OwnedByMainWindow(0x96C8_0000, 1166, 901)));
        // A dialog with a title bar never counts, however large.
        Assert.False(ImportWindowRule.IsImportWindow(OwnedByMainWindow(0x96CD_0000, 2500, 1332)));
        // The guide's card, the bezel and the tooltips: popups, but small.
        Assert.False(ImportWindowRule.IsImportWindow(OwnedByMainWindow(0x9600_0000, 500, 545)));
        Assert.False(ImportWindowRule.IsImportWindow(OwnedByMainWindow(Popup, 806, 531)));
        Assert.False(ImportWindowRule.IsImportWindow(OwnedByMainWindow(0x9400_0003, 195, 50)));
    }

    [Fact]
    public void EveryConditionMatters()
    {
        // Too narrow, too short.
        Assert.False(ImportWindowRule.IsImportWindow(OwnedByMainWindow(Popup, 2200, 1293)));
        Assert.False(ImportWindowRule.IsImportWindow(OwnedByMainWindow(Popup, 2484, 1000)));
        // Not a popup, or only a frame of a title bar.
        Assert.False(ImportWindowRule.IsImportWindow(OwnedByMainWindow(0x1400_0000, 2484, 1293)));
        Assert.False(ImportWindowRule.IsImportWindow(OwnedByMainWindow(Popup | 0x0040_0000, 2484, 1293)));
        // Owned by another window (a tooltip of the Import window), or by a main window of no size.
        Assert.False(ImportWindowRule.IsImportWindow(new WindowShape(Popup, 2484, 1293, "Afx:0000000140000000:0", 2484, 1293)));
        Assert.False(ImportWindowRule.IsImportWindow(new WindowShape(Popup, 2484, 1293, MainFrame, 0, 1332)));
    }
}

public class ImportShortcutsTests
{
    [Fact]
    public void TheStandardKeysAreLightroomsUsKeys()
    {
        var standard = ImportShortcuts.Standard;
        Assert.Equal(new KeyChord(ScanCodes.P), standard.ChordFor(ImportKeyRole.Include));
        Assert.Equal(new KeyChord(ScanCodes.X), standard.ChordFor(ImportKeyRole.Exclude));
        Assert.Equal(new KeyChord(0x29), standard.ChordFor(ImportKeyRole.Toggle));
        Assert.Equal(new KeyChord(0xE04D), standard.NextPhoto);
    }

    /// <summary>
    /// A layout as the app's lookup sees it: the key typing each character
    /// alone, never through Shift, AltGr or a dead key.
    /// </summary>
    private static Func<char, int?> Layout(params (char Character, int ScanCode)[] keys) =>
        character => keys.Where(key => key.Character == character).Select(key => (int?)key.ScanCode).FirstOrDefault();

    // ` and < alone: US has ` at 0x29 (< is Shift+,); UK the same (VK_OEM_8 there);
    // Italian has no ` and < on the ISO key 0x56; German types ` with Shift as a dead key.
    private static readonly Func<char, int?> Us = Layout(('p', 0x19), ('x', 0x2D), ('`', 0x29));
    private static readonly Func<char, int?> Uk = Layout(('p', 0x19), ('x', 0x2D), ('`', 0x29));
    private static readonly Func<char, int?> Italian = Layout(('p', 0x19), ('x', 0x2D), ('ò', 0x27), ('\\', 0x29), ('<', 0x56));
    private static readonly Func<char, int?> German = Layout(('p', 0x19), ('x', 0x2D), ('^', 0x29), ('<', 0x56));

    [Fact]
    public void PAndXFollowTheLayout()
    {
        var layout = ImportShortcuts.ForLayout(Layout(('p', 0x13), ('x', 0x2C)));
        Assert.Equal(new KeyChord(0x13), layout.Include);
        Assert.Equal(new KeyChord(0x2C), layout.Exclude);
        Assert.Equal(new KeyChord(ScanCodes.RightArrow), layout.NextPhoto);
        // No layout information: the US keys.
        Assert.Equal(ImportShortcuts.Standard, ImportShortcuts.ForLayout(null));
    }

    [Fact]
    public void ToggleIsTheBacktickKeyElseTheLessThanKeyElseNone()
    {
        Assert.Equal(new KeyChord(0x29), ImportShortcuts.ForLayout(Us).Toggle);
        Assert.Equal(new KeyChord(0x29), ImportShortcuts.ForLayout(Uk).Toggle);
        // Italian: the < key left of Z, not \ left of 1 (0x29) and not ò (0x27).
        Assert.Equal(new KeyChord(0x56), ImportShortcuts.ForLayout(Italian).Toggle);
        Assert.Equal(new KeyChord(0x56), ImportShortcuts.ForLayout(German).Toggle);
        // Neither ` nor < alone: no Toggle at all, never the key left of 1.
        var none = ImportShortcuts.ForLayout(Layout(('p', 0x19), ('x', 0x2D)));
        Assert.Null(none.Toggle);
        Assert.Null(none.ChordFor(ImportKeyRole.Toggle));
    }

    [Fact]
    public void AToggleTheLayoutHasNoKeyForSendsNothing()
    {
        var keymap = Keymap.StandardLayout();
        keymap.ImportKeys = new ImportKeys { Toggle = Key(ScanCodes.C), Advance = true };
        var engine = new KeyGestureEngine<string>(keymap);
        engine.UpdateImportShortcuts(ImportShortcuts.ForLayout(Layout(('p', 0x19), ('x', 0x2D))));
        var import = new EngineContext(Module: EngineContext.ImportModule);
        var down = engine.KeyDown(Key(ScanCodes.C), "C↓", isAutoRepeat: false, 0, import, () => false);
        Assert.IsType<KeyGestureEngine<string>.Decision.Consume>(down.Decision);
        Assert.Empty(down.Intents);
        Assert.IsType<KeyGestureEngine<string>.Decision.Consume>(engine.KeyUp(ScanCodes.C, "C↑", 0, import).Decision);

        // On an Italian keyboard it sends the < key.
        engine.UpdateImportShortcuts(ImportShortcuts.ForLayout(Italian));
        Assert.Equal([new KeyIntent.RunAction(new CommandAction.Shortcut(new KeyChord(0x56)))],
            engine.KeyDown(Key(ScanCodes.C), "C↓", isAutoRepeat: false, 0, import, () => false).Intents);
    }

    [Fact]
    public void TheEngineSendsTheShortcutsItWasGiven()
    {
        var keymap = Keymap.StandardLayout();
        keymap.ImportKeys = new ImportKeys { Include = Key(0x2C), Advance = true };
        var engine = new KeyGestureEngine<string>(keymap);
        var shortcuts = ImportShortcuts.Standard with { Include = new KeyChord(0x13) };
        engine.UpdateImportShortcuts(shortcuts);
        var import = new EngineContext(Module: EngineContext.ImportModule);
        var outcome = engine.KeyDown(Key(0x2C), "Z↓", isAutoRepeat: false, 0, import, () => false);
        Assert.Equal(
            [new KeyIntent.RunAction(new CommandAction.Shortcut(new KeyChord(0x13))),
                new KeyIntent.RunAction(new CommandAction.Shortcut(new KeyChord(ScanCodes.RightArrow)))],
            outcome.Intents);
        // A new keymap keeps them.
        engine.Update(keymap);
        Assert.Equal(shortcuts, engine.ImportShortcuts);
    }

    [Fact]
    public void TheImportContextHasNoScope()
    {
        var import = new EngineContext(Module: EngineContext.ImportModule);
        Assert.True(import.IsImportWindow);
        Assert.Null(import.Scope);
        Assert.False(new EngineContext(Module: "library").IsImportWindow);
    }
}

public class ImportKeyEditingTests
{
    [Fact]
    public void ImportKeysHaveTheirOwnNamesAndTexts()
    {
        Assert.Equal("the Exclude key of the Import window", new KeyUsage.ImportKey(ImportKeyRole.Exclude).Label());
        Assert.Equal(["Include", "Exclude", "Toggle"], ImportKeyRoles.All.Select(role => role.Title()));
        Assert.Equal(["include", "exclude", "toggle"], ImportKeyRoles.All.Select(role => role.WireName()));
        Assert.Equal(ImportKeyRole.Toggle, ImportKeyRoles.FromWireName("toggle"));
        Assert.Null(ImportKeyRoles.FromWireName("next"));
        Assert.Equal("Ticks the photo, so Lightroom imports it.", ImportKeyRole.Include.Detail());
    }

    [Fact]
    public void AnImportKeyMayReuseAModuleKeyAndClearingItIsCommitted()
    {
        // Q is Exposure's Dial Key: Import keys act in another context.
        var edit = KeymapEditing.PlanImportKey(Keymap.StandardLayout(), ImportKeyRole.Include, Key(ScanCodes.Q));
        var commit = Assert.IsType<KeymapEdit.Commit>(edit);
        Assert.Equal(Key(ScanCodes.Q), commit.Keymap.ImportKeys.Include);
        Assert.Equal(Key(ScanCodes.Q), commit.Keymap.Slot(Dev("Exposure"))?.Dial);

        var cleared = KeymapEditing.PlanImportKey(commit.Keymap, ImportKeyRole.Include, null);
        Assert.True(Assert.IsType<KeymapEdit.Commit>(cleared).Keymap.ImportKeys.IsDefault);
    }

    [Fact]
    public void AKeyOfAnotherImportKeyMoves()
    {
        var current = Keymap.StandardLayout();
        current.ImportKeys.Include = Key(0x2C);
        var edit = KeymapEditing.PlanImportKey(current, ImportKeyRole.Exclude, Key(0x2C));
        var ask = Assert.IsType<KeymapEdit.AskToMove>(edit);
        Assert.Equal("Z is already in use", ask.Title);
        Assert.Equal("Z is the Include key of the Import window. Move it here?", ask.Message);
        Assert.True(ask.CanMove);
        Assert.Null(ask.Moved!.ImportKeys.Include);
        Assert.Equal(Key(0x2C), ask.Moved.ImportKeys.Exclude);
        Assert.Equal(Key(0x2C), current.ImportKeys.Include); // the keymap in use is left alone
    }

    [Fact]
    public void ThePauseToggleAndCombosAreRefused()
    {
        var pause = KeymapEditing.PlanImportKey(Keymap.StandardLayout(), ImportKeyRole.Toggle, KeyChord.ControlEscape);
        var ask = Assert.IsType<KeymapEdit.AskToMove>(pause);
        Assert.False(ask.CanMove);
        Assert.Equal("Ctrl+Esc is the Pause toggle. Change that system key first (General → System Keys).", ask.Message);

        var combo = KeymapEditing.PlanImportKey(Keymap.StandardLayout(), ImportKeyRole.Include, Key(0x2C, KeyModifiers.Combo));
        Assert.Equal(new ComboMisuse.ImportKey(ImportKeyRole.Include).Message, Assert.IsType<KeymapEdit.Refuse>(combo).Message);
    }

    [Fact]
    public void AnImportKeyInTheWayOfASystemKeyMoves()
    {
        var current = Keymap.StandardLayout();
        current.ImportKeys.Include = Key(0x2C);
        var ask = Assert.IsType<KeymapEdit.AskToMove>(KeymapEditing.PlanSystemKey(current, SystemKeyRole.PauseToggle, Key(0x2C)));
        Assert.True(ask.CanMove);
        Assert.Equal("Z is the Include key of the Import window. Move it here?", ask.Message);
        Assert.Null(ask.Moved!.ImportKeys.Include);
        Assert.Equal(Key(0x2C), ask.Moved.SystemKeys.PauseToggle);
    }

    [Fact]
    public void RestoringTheStandardLayoutClearsTheKeysAndKeepsMoveToTheNextPhoto()
    {
        var current = Keymap.StandardLayout();
        current.ImportKeys = new ImportKeys { Include = Key(0x2C), Exclude = Key(ScanCodes.X), Advance = true };
        var restored = KeymapEditing.StandardLayoutKeeping(current);
        Assert.Equal(new ImportKeys { Advance = true }, restored.ImportKeys);
        Assert.Equal(Key(0x2C), current.ImportKeys.Include);
    }

    [Fact]
    public void ClonesDoNotShareImportKeys()
    {
        var keymap = Keymap.StandardLayout();
        var copy = keymap.Clone();
        copy.ImportKeys.Include = Key(0x2C);
        Assert.Null(keymap.ImportKeys.Include);
    }
}
