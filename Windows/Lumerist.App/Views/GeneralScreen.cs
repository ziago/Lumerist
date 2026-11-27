using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Microsoft.Win32;
using Lumerist.App.Design;
using Lumerist.App.Design.Controls;
using Lumerist.App.Guide;
using Lumerist.App.Input;
using Lumerist.App.Services;
using Lumerist.Core;

namespace Lumerist.App.Views;

/// <summary>
/// Appearance, the shortcuts, startup, System Keys, the Lightroom link and
/// options, the plug-in, the profile file, diagnostics, updates and support
/// (GeneralScreen.swift and the sections it shows).
/// </summary>
internal sealed class GeneralScreen(ScreenContext context) : Screen(context)
{
    private string? loginError;
    private string? profileError;
    private bool checkedCatalog;
    private (string Text, PhaseKind Kind)? pluginPhase;

    private enum PhaseKind
    {
        Finished,
        Failed,
    }

    /// <summary>The link and the plug-in state are on this page.</summary>
    public override bool ShowsStatus => true;

    /// <summary>Whether Lightroom Classic runs, read once per build (it lists processes).</summary>
    private bool lightroomRunning;

    protected override FrameworkElement Build()
    {
        lightroomRunning = !Context.IsSnapshot && LightroomWindow.IsRunning;
        return Layout.VStack(Theme.Space.XL,
            Surfaces.ScreenHeader("General", "appearance · permission · startup · keys · lightroom · profile"),
            AppearanceSection.Build(Controller),
            PermissionSection(),
            StartupSection(),
            SystemKeysSection(),
            LightroomSection(),
            PluginSection(),
            ProfileSection(),
            DiagnosticsSection(),
            GuideHost.CreateUpdatesSection(Controller),
            GuideHost.CreateAboutSupportSection(Controller));
    }

    // Permission -----------------------------------------------------------------

    /// <summary>
    /// The Mac's "macOS permission": Windows needs none, so only the
    /// shortcuts switch is left, with a line saying so.
    /// </summary>
    private FrameworkElement PermissionSection()
    {
        var on = Controller.ShortcutsEnabled;
        return Surfaces.SettingsSection("Windows permission", Icons.Shield,
            "Windows needs no permission for this: the shortcuts just work.",
            Surfaces.SettingRow("Shortcuts",
                on
                    ? "Your keys, mouse and trackpad drive Lightroom's sliders whenever Lightroom Classic is in front."
                    : "Every key goes straight to Lightroom, as if Lumerist were not there.",
                SynthControls.Toggle(on, wanted =>
                {
                    if (wanted != Controller.ShortcutsEnabled) Controller.ToggleShortcuts();
                }, "Shortcuts")));
    }

    // Startup --------------------------------------------------------------------

    private FrameworkElement StartupSection()
    {
        var settings = Controller.Settings;
        var quits = settings.QuitWithLightroom;
        var opensAtLogin = !Context.IsSnapshot && StartupRegistration.IsEnabled;
        var rows = new List<UIElement?>
        {
            Surfaces.SettingRow("Open at Login", loginError ?? "Starts Lumerist each time you log in to this PC.",
                SynthControls.Toggle(opensAtLogin, value =>
                {
                    loginError = StartupRegistration.Set(value);
                    Refresh();
                }, "Open at Login")),
            Surfaces.SettingRow("Start with the window hidden",
                "Lumerist opens straight to the notification area. Click its icon there when you want this window.",
                SynthControls.Toggle(settings.StartHidden, value =>
                {
                    Controller.Settings.StartHidden = value;
                    Controller.SaveSettings();
                }, "Start with the window hidden")),
            Surfaces.SettingRow("Open Lumerist when Lightroom starts",
                "The plug-in launches Lumerist quietly, with no window. Applies from Lightroom's next start.",
                SynthControls.Toggle(settings.OpenWithLightroom, value =>
                {
                    Controller.Settings.OpenWithLightroom = value;
                    Controller.SaveSettings();
                }, "Open Lumerist when Lightroom starts")),
            Surfaces.SettingRow("When Lightroom quits",
                quits
                    ? "Lumerist has nothing to do without Lightroom Classic, so it closes too, a moment later."
                    : "Lumerist stays in the notification area until you quit it.",
                SynthControls.Segmented(SettingsChoices.AllQuitChoices, quits ? QuitChoice.QuitAfterPause : QuitChoice.KeepRunning,
                    SettingsChoices.Title, choice =>
                    {
                        Controller.Settings.QuitWithLightroom = choice == QuitChoice.QuitAfterPause;
                        Controller.SaveSettings();
                    }, name: "When Lightroom quits")),
        };
        if (quits)
        {
            rows.Add(Surfaces.SettingRow("Pause length", "Seconds between Lightroom closing and Lumerist closing.",
                SynthControls.Stepper(settings.QuitDelaySeconds, AppSettings.QuitDelayRange.Minimum, AppSettings.QuitDelayRange.Maximum, 1,
                    value => $"{(int)value} s", value =>
                    {
                        Controller.Settings.QuitDelaySeconds = value;
                        Controller.SaveSettings();
                    }, "Pause length in seconds")));
        }
        return Surfaces.SettingsSection("Startup", Icons.Power, null, [.. rows]);
    }

    // System Keys ----------------------------------------------------------------

    /// <summary>Combo Key, Palette Key, Key Reference toggle and Pause toggle (SystemKeysSection.swift).</summary>
    private FrameworkElement SystemKeysSection()
    {
        var keymap = Controller.Keymap;
        var systemKeys = keymap.SystemKeys;
        // Lightroom's own keys a System Key takes over ("0" left of 1 on Hungarian keyboards): never blocking.
        var warnings = new Dictionary<SystemKeyRole, string>();
        foreach (var conflict in keymap.Conflicts().Where(conflict => conflict.LightroomAction is not null))
        {
            foreach (var usage in conflict.Usages.OfType<KeyUsage.SystemKey>()) warnings.TryAdd(usage.Role, conflict.Message);
        }
        var rows = SystemKeyRoles.All.Select(role =>
        {
            KeySlot? slot = null;
            slot = new KeySlot($"system.{role.WireName()}", systemKeys.ChordFor(role), warnings.GetValueOrDefault(role), 64, role.Title(),
                systemKeys.ComboKey,
                chord => KeymapEditor.SetSystem(Controller, role, chord, slot),
                () => KeymapEditor.SetSystem(Controller, role, null, slot));
            return (UIElement?)Surfaces.SettingRow(role.Title(), role.Detail(systemKeys.ComboKey), slot);
        }).ToArray();
        var section = Surfaces.SettingsSection("System Keys", Icons.Keyboard,
            "Click a box and press the new key: Esc cancels, Delete clears. System Keys work in every module, so no slider or Command Key can share them.",
            rows);
        // Why the standard layout left the Palette Key empty on this keyboard.
        var reason = systemKeys.PaletteKey is null && !Context.IsSnapshot ? KeyboardLayout.PaletteKeyUnavailable() : null;
        var notes = new List<UIElement?>
        {
            reason is { } why ? SynthText.Body(PaletteKeyDefault.Explanation(why, KeyboardLayout.LeftOf1Text()), 12, color: Ink.Steel) : null,
        };
        notes.AddRange(SystemKeyRoles.All.Where(warnings.ContainsKey).Select(role => (UIElement?)SynthText.WarningLine(warnings[role])));
        return notes.All(note => note is null) ? section : Layout.VStack(Theme.Space.S, [section, .. notes]);
    }

    // Lightroom ------------------------------------------------------------------

    /// <summary>The plug-in link and its tests, how often a dial updates Lightroom, and Lightroom's options (LightroomSection.swift).</summary>
    private FrameworkElement LightroomSection()
    {
        var input = Controller.Keymap.Input;
        var connected = Controller.IsConnected;
        var linkDetail = connected
            ? "Linked to the Lumerist plug-in inside Lightroom Classic."
            : lightroomRunning
                ? "Lightroom Classic is open; waiting for the Lumerist plug-in to answer."
                : "Lightroom Classic is closed. Lumerist links up as soon as it opens.";
        var link = Layout.HStack(Theme.Space.S,
            connected
                ? SynthControls.StatusPill("Linked", Icons.CheckMark, SynthControls.Tone.Good)
                : SynthControls.StatusPill("Waiting", Icons.Hourglass, SynthControls.Tone.Waiting),
            SynthControls.Button("Reconnect", SynthControls.Kind.Secondary, Controller.Reconnect));

        var check = SynthControls.Button("Check connection", SynthControls.Kind.Secondary, Controller.SendConnectionCheck);
        check.IsEnabled = !Context.IsSnapshot;
        var exposure = SynthControls.Button("Send test exposure…", SynthControls.Kind.Quiet, () =>
        {
            if (SynthDialog.Confirm(View, "Send a test exposure?",
                    "Lightroom raises the Exposure of the selected photo by 0.1. It works in the Develop module, and Undo takes it back.",
                    "Add +0.1 EV", isDestructive: true))
                Controller.SendTestExposure();
        });
        exposure.IsEnabled = !Context.IsSnapshot;

        var hz = Controller.Settings.UpdateRateHz;
        var smoothness = SynthControls.Segmented(SettingsChoices.AllSmoothness, SettingsChoices.Nearest(hz), SettingsChoices.Title, choice =>
        {
            Controller.Settings.UpdateRateHz = choice.Hz();
            Controller.SaveSettings();
        }, name: "Smoothness");
        AutomationProperties.SetHelpText(smoothness, $"{(int)hz} updates per second");
        var hzLabel = SynthText.Label($"{(int)hz} Hz", 10);
        hzLabel.HorizontalAlignment = HorizontalAlignment.Right;
        var smoothnessControl = Layout.VStack(4, smoothness, hzLabel);

        var unsupported = Controller.CatalogCheck?.Unsupported ?? [];
        var catalogDetail = !checkedCatalog
            ? "Asks Lightroom which sliders it knows. Open Lightroom in Develop first."
            : unsupported.Count == 0
                ? "Lightroom knows every slider Lumerist can move."
                : "Unknown to this Lightroom, so their keys show a warning: " + string.Join(", ", unsupported);
        var checkNow = SynthControls.Button("Check now", SynthControls.Kind.Secondary, () =>
        {
            checkedCatalog = true;
            Controller.RequestCatalogCheck();
            Refresh();
        });
        checkNow.IsEnabled = connected;

        return Surfaces.SettingsSection("Lightroom", Icons.Link, "Lumerist and the plug-in talk only through 127.0.0.1, inside this PC.",
            Surfaces.SettingRow("Link", linkDetail, link),
            Surfaces.SettingRow("Try the link",
                "Check connection waits for the plug-in to answer. The test exposure adds +0.1 EV to the selected photo in Develop.",
                Layout.HStack(Theme.Space.S, check, exposure)),
            Surfaces.SettingRow("Smoothness",
                "How often a turning dial updates Lightroom. Smooth follows your hand more closely; Light gives Lightroom more time to redraw. Steps in between are added up, never lost.",
                smoothnessControl),
            ToggleRow("Follow the selected mask",
                "With a mask selected, Dial Keys and Nudge Keys move that mask's own sliders. A slider masks don't have stays put and shows a notice.",
                input.FollowSelectedMask, (keymap, value) => keymap.Input.FollowSelectedMask = value),
            ToggleRow("Open the slider's panel",
                "As an adjustment starts, Lightroom expands the panel that holds the slider, so you can watch it move.",
                input.RevealPanels, (keymap, value) => keymap.Input.RevealPanels = value),
            Surfaces.SettingRow("Parameter check", catalogDetail, checkNow));
    }

    private Border ToggleRow(string title, string detail, bool value, Action<Keymap, bool> apply) =>
        Surfaces.SettingRow(title, detail, SynthControls.Toggle(value,
            newValue => KeymapEditor.Edit(Controller, keymap => apply(keymap, newValue)), title));

    // Plug-in --------------------------------------------------------------------

    /// <summary>General → "Lightroom plug-in" (the Mac's PluginSection): status, install or update, the guide, the files.</summary>
    private FrameworkElement PluginSection()
    {
        var status = Controller.PluginStatus;
        var lightroom = lightroomRunning ? " Lightroom Classic is open." : "";
        var detail = status switch
        {
            null => Controller.PluginSummary,
            PluginInstallStatus.NotInstalled => "Not installed." + lightroom,
            PluginInstallStatus.Outdated(var installed, var bundled) =>
                $"Installed {installed?.ToString() ?? "unknown"}; this app brings {bundled}." + lightroom,
            PluginInstallStatus.Modified(var version) => $"Version {version}, but the files differ from this app's copy." + lightroom,
            PluginInstallStatus.UpToDate(var version) => $"Version {version} installed.",
            PluginInstallStatus.NewerInstalled(var installed, var bundled) =>
                $"A newer plug-in ({installed}) is installed than this app's ({bundled}).",
            _ => Controller.PluginSummary,
        };
        FrameworkElement? pill = status switch
        {
            PluginInstallStatus.UpToDate or PluginInstallStatus.NewerInstalled =>
                SynthControls.StatusPill("Installed", Icons.CheckMark, SynthControls.Tone.Good),
            PluginInstallStatus.NotInstalled => SynthControls.StatusPill("Missing", Icons.Cancel, SynthControls.Tone.Off),
            PluginInstallStatus.Modified => SynthControls.StatusPill("Changed", Icons.Warning, SynthControls.Tone.Waiting),
            null => null,
            _ => SynthControls.StatusPill("Outdated", Icons.ArrowDown, SynthControls.Tone.Waiting),
        };
        var statusControls = Layout.HStack(Theme.Space.S, pill);
        if (status is { NeedsAction: true })
        {
            var label = status switch
            {
                PluginInstallStatus.NotInstalled => "Install",
                PluginInstallStatus.Modified => "Reinstall",
                _ => "Update",
            };
            // Lightroom keeps its plug-ins in memory: on Windows they are replaced only while it is closed.
            var install = SynthControls.Button(label, SynthControls.Kind.Primary, () =>
            {
                var message = Controller.InstallPlugin();
                pluginPhase = (message, Controller.PluginNeedsAction ? PhaseKind.Failed : PhaseKind.Finished);
                Refresh();
            });
            install.IsEnabled = !lightroomRunning;
            statusControls.Children.Add(install);
        }

        var guide = SynthControls.Button("Open guide…", SynthControls.Kind.Secondary, () => GuideHost.ShowGuide(Controller));
        var plugin = Path.Combine(PluginInstallLayout.ModulesDirectory(AppPaths.RoamingAppData), PluginInstallLayout.BundleName);
        var reveal = SynthControls.Button("Show in File Explorer", SynthControls.Kind.Quiet, () => ShowInExplorer(plugin));
        reveal.IsEnabled = !Context.IsSnapshot && Directory.Exists(plugin);

        var rows = new List<UIElement?>
        {
            Surfaces.SettingRow("Status", detail, statusControls),
            Surfaces.SettingRow("Setup guide", "The three first-run steps, checked live.", guide),
            Surfaces.SettingRow("Plug-in files", plugin, reveal),
        };
        if (pluginPhase is var (text, kind))
        {
            var line = SynthText.Body(text, 12, color: kind == PhaseKind.Finished ? Ink.Teal : Ink.Error);
            line.Margin = new Thickness(Surfaces.RowInset, 10, Surfaces.RowInset, 10);
            rows.Add(line);
        }
        return Surfaces.SettingsSection("Lightroom plug-in", Icons.Puzzle,
            @"The plug-in ships inside Lumerist. Replaced or removed copies are kept in %APPDATA%\Lumerist\plugin-backups.",
            [.. rows]);
    }

    /// <summary>Opens File Explorer with the item selected.</summary>
    private static void ShowInExplorer(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true })?.Dispose();
        }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Log.Write($"File Explorer did not open: {error.Message}");
        }
    }

    // Profile --------------------------------------------------------------------

    private FrameworkElement ProfileSection()
    {
        var restore = SynthControls.Button("Restore…", SynthControls.Kind.Destructive, () =>
        {
            if (!SynthDialog.Confirm(View, "Restore the standard layout?",
                    "Dial Keys, Nudge Keys, Command Keys, System Keys and steps return to the layout Lumerist ships with, and the Import keys are cleared. Your mouse, trackpad, Import and Lightroom options and the Palette are kept. To keep the current keys, export the profile first.",
                    "Restore", isDestructive: true)) return;
            profileError = Controller.UpdateKeymap(KeymapEditing.StandardLayoutKeeping(Controller.Keymap, KeyboardLayout.StandardPaletteKey()));
            if (profileError is not null) Refresh();
        });
        var rows = new List<UIElement?>
        {
            Surfaces.SettingRow("Profile file", "Save everything to a JSON file, or load one.",
                Layout.HStack(Theme.Space.S,
                    SynthControls.Button("Export…", SynthControls.Kind.Secondary, ExportProfile),
                    SynthControls.Button("Import…", SynthControls.Kind.Secondary, ImportProfile))),
            Surfaces.SettingRow("Standard layout", "Brings back the keys Lumerist ships with. Your options stay as they are.", restore),
        };
        if (profileError is { } error)
        {
            var line = SynthText.ErrorLine(error);
            line.Margin = new Thickness(Surfaces.RowInset, 10, Surfaces.RowInset, 10);
            rows.Add(line);
        }
        return Surfaces.SettingsSection("Profile", Icons.Document,
            "A profile holds Dial, Nudge and Command Keys, System Keys, steps, the Palette, and the mouse, trackpad and Lightroom options.",
            [.. rows]);
    }

    private void ExportProfile()
    {
        var dialog = new SaveFileDialog { Filter = "JSON (*.json)|*.json", FileName = "Lumerist Profile.json", AddExtension = true };
        if (dialog.ShowDialog(Window.GetWindow(View)) != true) return;
        try
        {
            KeymapStore.Save(Controller.Keymap, dialog.FileName);
            profileError = null;
        }
        catch (Exception error) when (error is KeymapException or IOException or UnauthorizedAccessException)
        {
            profileError = error.Message;
        }
        Refresh();
    }

    /// <summary>Accepts a profile exported by Lumerist for Windows.</summary>
    private void ImportProfile()
    {
        var dialog = new OpenFileDialog { Filter = "JSON (*.json)|*.json", Multiselect = false };
        if (dialog.ShowDialog(Window.GetWindow(View)) != true) return;
        try
        {
            var imported = KeymapStore.Load(dialog.FileName) ?? throw new IOException($"{dialog.FileName} was not found.");
            profileError = Controller.UpdateKeymap(imported);
        }
        catch (Exception error) when (error is KeymapException or JsonException or IOException or UnauthorizedAccessException)
        {
            profileError = error.Message;
        }
        Refresh();
    }

    // Diagnostics ----------------------------------------------------------------

    private FrameworkElement DiagnosticsSection()
    {
        var path = Log.FilePath;
        var reveal = SynthControls.Button("Show in File Explorer", SynthControls.Kind.Quiet, () => ShowInExplorer(path));
        reveal.IsEnabled = !Context.IsSnapshot && File.Exists(path);
        var toggle = SynthControls.Toggle(Controller.DebugLogEnabled, wanted =>
        {
            if (wanted != Controller.DebugLogEnabled) Controller.ToggleDebugLog();
        }, "Write a debug log");
        return Surfaces.SettingsSection("Diagnostics", Icons.Diagnostic,
            "The log notes link changes, the commands sent and Lightroom's answers. Never photos or catalog contents.",
            Surfaces.SettingRow("Debug log", path, Layout.HStack(Theme.Space.M, reveal, toggle)));
    }
}
