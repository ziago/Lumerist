using System.Windows;
using System.Windows.Automation;
using Lumerist.App.Design;
using Lumerist.App.Design.Controls;
using Lumerist.App.Input;
using Lumerist.Core;

namespace Lumerist.App.Views;

/// <summary>
/// The Import page (ImportScreen.swift): keys for culling in Lightroom's
/// Import window (Include, Exclude, Toggle), "Move to the next photo", and
/// what Lumerist looks at to recognise the window. No key is assigned until
/// the user picks one.
/// </summary>
internal sealed class ImportScreen(ScreenContext context) : Screen(context)
{
    protected override FrameworkElement Build()
    {
        var keymap = Controller.Keymap;
        // The keys Lumerist presses, named as Lightroom's layout prints them.
        var layout = Context.IsSnapshot ? (nint?)null : KeyboardLayout.OfLightroom();
        var sends = layout is { } active ? KeyboardLayout.ImportShortcutsFor(active) : ImportShortcuts.Standard;
        var warnings = Warnings(keymap);

        var description = SynthText.Body(
            "These keys work only while Lightroom's Import window is in front. There the Develop and Library keys pause, and the mouse wheel and trackpad scroll Lightroom as usual.",
            Theme.IsFlat ? 14 : 12, color: Ink.Steel);
        if (Theme.IsFlat)
        {
            description.LineHeight = 14 * 1.25 + 3;
            description.MaxWidth = 640;
            description.HorizontalAlignment = HorizontalAlignment.Left;
        }

        var rows = ImportKeyRoles.All.Select(role =>
        {
            // Toggle shows what its key types ("<" on Italian keyboards, "`" on US ones).
            var lightroomKey = sends.ChordFor(role) is not { } chord ? null
                : role == ImportKeyRole.Toggle && layout is { } typing ? KeyboardLayout.Typed(chord.ScanCode, typing) ?? chord.DisplayText
                : chord.DisplayText;
            var sent = SynthText.Body(lightroomKey is null ? "Sends nothing" : $"Sends {lightroomKey}", 12, color: Ink.Steel);
            sent.VerticalAlignment = VerticalAlignment.Center;
            AutomationProperties.SetName(sent, lightroomKey is null ? "Sends nothing" : $"Sends Lightroom's {lightroomKey} key");
            KeySlot? slot = null;
            slot = new KeySlot($"import.{role.WireName()}", keymap.ImportKeys.ChordFor(role), warnings.GetValueOrDefault(role), 64,
                $"{role.Title()} key", keymap.SystemKeys.ComboKey,
                chord => KeymapEditor.SetImport(Controller, role, chord, slot),
                () => KeymapEditor.SetImport(Controller, role, null, slot));
            return (UIElement?)Surfaces.SettingRow(role.Title(), role.Detail(), Layout.HStack(Theme.Space.M, sent, slot));
        }).ToArray();

        const string advanceTitle = "Move to the next photo";
        var advance = Surfaces.SettingRow(advanceTitle,
            "After Include or Exclude, Lumerist also presses → so the next photo is selected.",
            SynthControls.Toggle(keymap.ImportKeys.Advance, value =>
            {
                if (value == Controller.Keymap.ImportKeys.Advance) return;
                KeymapEditor.Edit(Controller, edited => edited.ImportKeys.Advance = value);
            }, advanceTitle));

        return Layout.VStack(Theme.Space.XL,
            Layout.VStack(Theme.Space.L,
                Surfaces.ScreenHeader("Import", "Culling in Lightroom's Import window"),
                description),
            Surfaces.SettingsSection("Import keys", Icons.CheckboxChecked,
                "Pick keys under one hand, for example Z, X and C. Click a box and press the key: Esc cancels, Delete clears.",
                rows),
            KeymapEditor.LastError is { } error ? SynthText.ErrorLine(error) : null,
            sends.Toggle is null ? SynthText.WarningLine("Toggle isn't available with this keyboard layout.") : null,
            // Lightroom keys the Import keys take over: never blocking, shown while they last.
            warnings.Count == 0
                ? null
                : Layout.VStack(Theme.Space.S, [.. ImportKeyRoles.All.Where(warnings.ContainsKey).Select(role => SynthText.WarningLine(warnings[role]))]),
            Surfaces.SettingsSection("Options", Icons.Forward, null, advance),
            SynthText.Body(
                "Lumerist recognises the Import window by the type and size of Lightroom's windows, and reads no text from Lightroom.",
                12, color: Ink.Steel));
    }

    /// <summary>
    /// What each key box warns about: one of Lightroom's own keys in the
    /// Import window. Errors never get here, since a keymap with errors is
    /// never saved.
    /// </summary>
    private static Dictionary<ImportKeyRole, string> Warnings(Keymap keymap)
    {
        var result = new Dictionary<ImportKeyRole, string>();
        foreach (var conflict in keymap.Conflicts())
        {
            foreach (var usage in conflict.Usages.OfType<KeyUsage.ImportKey>()) result.TryAdd(usage.Role, conflict.Message);
        }
        return result;
    }
}
