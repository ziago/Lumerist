using System.Windows;
using System.Windows.Controls;
using Lumerist.App.Design.Controls;
using Lumerist.Core;

namespace Lumerist.App.Views;

/// <summary>
/// Everything a screen needs: the controller, the window's remembered state,
/// and a way to go to another section. Shared by the window and the snapshots.
/// </summary>
internal sealed class ScreenContext(AppController controller, Action<MainSection, ModuleTab?, string?> go, bool isSnapshot = false)
{
    private readonly Dictionary<ParameterScope, ModulePageState> modules = new()
    {
        [ParameterScope.Develop] = new ModulePageState(ParameterScope.Develop),
        [ParameterScope.Library] = new ModulePageState(ParameterScope.Library),
    };

    public AppController Controller { get; } = controller;

    /// <summary>Rendering for <c>--render-snapshots</c>: nothing is started or saved.</summary>
    public bool IsSnapshot { get; } = isSnapshot;

    public Keymap Keymap => Controller.Keymap;

    /// <summary>Every key box reads combos with the current Combo Key ("Tab + K").</summary>
    public KeyChord? ComboKey => Controller.Keymap.SystemKeys.ComboKey;

    /// <summary>The floating panels' settings, when the panel host offers them (see <see cref="IPanelSettings"/>).</summary>
    public IPanelSettings? Panels => Controller.Panels as IPanelSettings;

    public ModulePageState Module(ParameterScope scope) => modules[scope];

    /// <summary>Opens a section (a module page on <paramref name="tab"/>, searching <paramref name="search"/>).</summary>
    public void Go(MainSection section, ModuleTab? tab = null, string? search = null) => go(section, tab, search);
}

/// <summary>
/// One screen of the main window. <see cref="View"/> stays the same element;
/// <see cref="Refresh"/> rebuilds what is inside from the current keymap and
/// settings (the Mac's views re-render the same way).
/// </summary>
internal abstract class Screen(ScreenContext context)
{
    protected ScreenContext Context { get; } = context;

    protected AppController Controller => Context.Controller;

    public ContentControl View { get; } = new() { Focusable = false, IsTabStop = false };

    /// <summary>Rebuilds the content.</summary>
    public virtual void Refresh() => View.Content = Build();

    protected abstract FrameworkElement Build();

    /// <summary>The screen shows the link or plug-in state (it refreshes on <see cref="AppController.Changed"/>).</summary>
    public virtual bool ShowsStatus => false;

    /// <summary>The window leaves this screen or closes (end samples and previews).</summary>
    public virtual void Leave() { }
}

/// <summary>
/// Applies key assignments and option changes, asking before moving a key
/// that is already in use (the Mac's KeymapEditor). The keymap is only ever
/// written through <see cref="AppController.UpdateKeymap"/>.
/// </summary>
internal static class KeymapEditor
{
    /// <summary>Why the last edit was not saved, shown under the slider tables.</summary>
    public static string? LastError { get; private set; }

    /// <summary>
    /// A Lightroom key the last assignment took over. It never blocks; the
    /// key's box keeps its own ⚠ while the clash lasts.
    /// </summary>
    public sealed record Warning(KeyUsage Usage, string Message);

    public static Warning? LastWarning { get; private set; }

    /// <summary>Raised when <see cref="LastError"/> changes without a new keymap.</summary>
    public static event Action? Changed;

    /// <param name="usage">The key being assigned, for the warning about Lightroom's own keys (none when clearing).</param>
    public static void Apply(AppController controller, KeymapEdit edit, DependencyObject? from, KeyUsage? usage = null)
    {
        switch (edit)
        {
            case KeymapEdit.Commit commit:
                if (Save(controller, commit.Keymap)) NoteWarning(commit.Keymap, usage);
                break;
            case KeymapEdit.Refuse refuse:
                SetError(refuse.Message);
                SynthDialog.Inform(from, KeymapEditing.RefuseTitle, refuse.Message);
                break;
            case KeymapEdit.AskToMove ask when ask.CanMove:
                if (!SynthDialog.Confirm(from, ask.Title, ask.Message, "Move here")) return;
                if (ask.Moved is { } moved)
                {
                    if (Save(controller, moved)) NoteWarning(moved, usage);
                }
                else
                {
                    SetError(ask.MoveError);
                }
                break;
            case KeymapEdit.AskToMove ask:
                SynthDialog.Inform(from, ask.Title, ask.Message);
                break;
        }
    }

    public static void Assign(AppController controller, KeyChord? chord, SlotRole role, ParameterKey key, DependencyObject? from) =>
        Apply(controller, KeymapEditing.PlanAssign(controller.Keymap, chord, role, key), from, chord is null ? null : role.Usage(key));

    public static void SetSystem(AppController controller, SystemKeyRole role, KeyChord? chord, DependencyObject? from) =>
        Apply(controller, KeymapEditing.PlanSystemKey(controller.Keymap, role, chord), from);

    /// <summary>An Import key (null clears it). A combo is refused with its reason; a key of another Import key can move.</summary>
    public static void SetImport(AppController controller, ImportKeyRole role, KeyChord? chord, DependencyObject? from) =>
        Apply(controller, KeymapEditing.PlanImportKey(controller.Keymap, role, chord), from,
            chord is null ? null : new KeyUsage.ImportKey(role));

    /// <summary>The last warning about a Nudge Key of <paramref name="scope"/>, while <paramref name="keymap"/> still has it.</summary>
    public static string? CurrentWarning(Keymap keymap, ParameterScope scope) =>
        LastWarning is { } warning && warning.Usage.SlotScope() == scope && keymap.LightroomWarning(warning.Usage) == warning.Message
            ? warning.Message
            : null;

    /// <summary>An option or list change (switches, favourites, Command Keys): on a copy, then saved.</summary>
    public static void Edit(AppController controller, Action<Keymap> change)
    {
        var proposed = controller.Keymap.Clone();
        change(proposed);
        Save(controller, proposed);
    }

    /// <summary>True when the keymap was saved and is in use.</summary>
    private static bool Save(AppController controller, Keymap keymap)
    {
        var error = controller.UpdateKeymap(keymap);
        SetError(error);
        return error is null;
    }

    private static void NoteWarning(Keymap saved, KeyUsage? usage) =>
        LastWarning = usage is not null && saved.LightroomWarning(usage) is { } message ? new Warning(usage, message) : null;

    private static void SetError(string? message)
    {
        if (LastError == message) return;
        LastError = message;
        Changed?.Invoke();
    }
}
