using System.Text.Json;
using System.Windows.Threading;
using Microsoft.Win32;
using Lumerist.App.Input;
using Lumerist.App.Panels;
using Lumerist.App.Services;
using Lumerist.Core;

namespace Lumerist.App;

/// <summary>
/// Connects the pieces (the Mac's CommandStore): the bridge to the plug-in,
/// the input hook, the intent dispatcher, the keymap and the plug-in install.
/// Everything here runs on the UI thread.
/// </summary>
internal sealed class AppController : IDisposable
{
    private readonly Dispatcher ui;
    private readonly AppSettings settings;
    private Keymap keymap;
    private readonly PluginInstaller installer = new();
    private readonly LightroomCommandClient client;
    private readonly IntentDispatcher dispatcher;
    private InputHook? hook;
    private bool isConnected;
    private DispatcherTimer? lightroomWatch;
    private bool lightroomWasRunning;
    private IDisposable? pendingQuit;
    private LightroomContext context = new("", "");

    /// <param name="forSnapshots">
    /// For <c>--render-snapshots</c>: the standard layout and default
    /// settings, never the user's files, and nothing is started.
    /// </param>
    public AppController(Dispatcher ui, bool forSnapshots = false)
    {
        this.ui = ui;
        settings = forSnapshots ? new AppSettings() : AppSettings.Load(AppPaths.SettingsFile);
        Log.IsEnabled = settings.DebugLog && !forSnapshots;
        keymap = forSnapshots ? Keymap.StandardLayout() : LoadKeymap();
        dispatcher = new IntentDispatcher(new IntentDispatcherHooks
        {
            Send = Send,
            PostShortcut = PostShortcut,
            Schedule = Schedule,
            FallbackShortcut = command => FallbackShortcuts.For(command, KeyboardLayout.Table(KeyboardLayout.OfLightroom())),
            AppCommandShortcut = name => AppCommands.Shortcut(name, KeyboardLayout.Table(KeyboardLayout.OfLightroom())),
            UpdateRateHz = () => settings.UpdateRateHz,
            Notice = ShowNotice,
            OpenPalette = () => Panels?.OpenPalette(),
            ClosePalette = () =>
            {
                Panels?.ClosePalette();
                hook?.PaletteDidClose();
            },
            ToggleKeyReference = () => Panels?.ToggleKeyReference(),
            Log = Log.Write,
        });
        // The client calls back on a background thread, in order; BeginInvoke keeps the order.
        client = new LightroomCommandClient(
            state => ui.BeginInvoke(() => OnState(state)),
            message => ui.BeginInvoke(() => OnMessage(message)));
    }

    /// <summary>Raised on the UI thread whenever something the tray shows changed.</summary>
    public event Action? Changed;

    public string Status { get; private set; } = "Connecting to the Lumerist plug-in…";

    public bool IsConnected => isConnected;

    public bool ShortcutsEnabled => hook is not null;

    public bool IsPaused => hook?.IsPaused ?? false;

    public bool DebugLogEnabled => settings.DebugLog;

    public string PluginSummary => installer.Status() switch
    {
        null => installer.BundleProblem ?? "Plug-in status unknown",
        PluginInstallStatus.UpToDate(var version) => $"Plug-in {version} installed",
        PluginInstallStatus.NewerInstalled(var installed, _) => $"Plug-in {installed} installed (newer)",
        PluginInstallStatus.NotInstalled => "Plug-in not installed",
        PluginInstallStatus.Outdated(var installed, var bundled) => $"Plug-in {installed?.ToString() ?? "(unknown)"} → {bundled} available",
        PluginInstallStatus.Modified(var version) => $"Plug-in {version} was changed on disk",
        _ => "Plug-in status unknown",
    };

    public bool PluginNeedsAction => installer.Status()?.NeedsAction ?? false;

    public PluginInstallStatus? PluginStatus => installer.Status();

    // For the windows and panels -------------------------------------------------

    /// <summary>The floating panels (Value HUD, Palette, Key Reference); set once at startup.</summary>
    public IPanelHost? Panels { get; set; }

    /// <summary>The keymap in use. Change it only through <see cref="UpdateKeymap"/>.</summary>
    public Keymap Keymap => keymap;

    /// <summary>Raised on the UI thread after <see cref="UpdateKeymap"/> put a new keymap in use.</summary>
    public event Action? KeymapChanged;

    /// <summary>Preferences; after changing one, call <see cref="SaveSettings"/>.</summary>
    public AppSettings Settings => settings;

    /// <summary>Raised on the UI thread after <see cref="SaveSettings"/>.</summary>
    public event Action? SettingsChanged;

    /// <summary>Lightroom's module and Develop tool, as last reported by the plug-in.</summary>
    public LightroomContext Context => context;

    /// <summary>Develop presets from the last <see cref="RequestPresets"/>.</summary>
    public IReadOnlyList<PresetItem> Presets { get; private set; } = [];

    public event Action? PresetsChanged;

    /// <summary>The last catalog check: parameters this Lightroom does not support, and those it skipped.</summary>
    public LightroomResult.Catalog? CatalogCheck { get; private set; }

    public event Action? CatalogCheckChanged;

    /// <summary>Every answer from Lightroom (the Value HUD reads the value it reports).</summary>
    public event Action<LightroomAck>? AckReceived;

    /// <summary>The plug-in or a second copy of Lumerist asked for the main window.</summary>
    public event Action? ShowWindowRequested;

    /// <summary>
    /// Validates, saves and uses a new keymap. Returns null, or why it was not
    /// saved (the keymap in use is then unchanged).
    /// </summary>
    public string? UpdateKeymap(Keymap newKeymap)
    {
        try
        {
            KeymapFile.Save(newKeymap);
        }
        catch (Exception error) when (error is KeymapException or IOException or UnauthorizedAccessException)
        {
            Log.Write($"Keymap not saved: {error.Message}");
            return error.Message;
        }
        keymap = newKeymap;
        hook?.UpdateKeymap(newKeymap);
        KeymapChanged?.Invoke();
        Changed?.Invoke();
        return null;
    }

    /// <summary>The user closed the Palette (× or a pick): the next Palette Key press opens it again.</summary>
    public void PaletteDidClose() => hook?.PaletteDidClose();

    /// <summary>Runs a Command Key action as if its key had been pressed (Palette, menus).</summary>
    public void Run(CommandAction action) => dispatcher.Handle([new KeyIntent.RunAction(action)]);

    public void RequestPresets() => Send(LightroomRequest.Query(LightroomQuery.Presets));

    /// <summary>A ping: the plug-in's answer shows the link works end to end.</summary>
    public void SendConnectionCheck() => Send(LightroomRequest.Ping);

    /// <summary>+0.1 EV on the selected photo in Develop (General → Lightroom, after a confirmation).</summary>
    public void SendTestExposure() => Send(LightroomRequest.Adjust(ParameterScope.Develop, "Exposure", 0.1));

    public void RequestCatalogCheck() => Send(LightroomRequest.Query(LightroomQuery.Catalog));

    /// <summary>Opens the main window (tray, plug-in's "Open Lumerist", a second copy).</summary>
    public void RequestMainWindow() => ShowWindowRequested?.Invoke();

    public void Start(IReadOnlyList<string> arguments)
    {
        Log.Write($"Lumerist {AppVersion} started{(arguments.Count > 0 ? " " + string.Join(' ', arguments) : "")}");
        KeyNames.LayoutName = scanCode => KeyboardLayout.Name(scanCode, KeyboardLayout.OfLightroom());
        WritePluginLink();
        Log.Write(PluginSummary);
        UpdatePluginIfOutdated();
        client.Start();
        if (settings.ShortcutsEnabled) StartShortcuts();
        TextFocusProbe.WarmUp();
        SystemEvents.SessionSwitch += OnSessionSwitch;
        WatchLightroom();
    }

    // Following Lightroom (the Mac's LightroomLifecycle) ------------------------

    /// <summary>How often Lumerist looks whether Lightroom Classic is still running.</summary>
    private static readonly TimeSpan LightroomWatchInterval = TimeSpan.FromSeconds(2);

    private void WatchLightroom()
    {
        lightroomWasRunning = LightroomWindow.IsRunning;
        lightroomWatch = new DispatcherTimer(DispatcherPriority.Background, ui) { Interval = LightroomWatchInterval };
        lightroomWatch.Tick += (_, _) =>
        {
            var isRunning = LightroomWindow.IsRunning;
            if (isRunning == lightroomWasRunning) return;
            lightroomWasRunning = isRunning;
            if (isRunning) LightroomLaunched();
            else LightroomQuit();
        };
        lightroomWatch.Start();
    }

    /// <summary>
    /// "Quit with Lightroom", except while a Lumerist window is open or the
    /// plug-in still needs installing: on Windows the plug-in is installed
    /// with Lightroom closed, and Lumerist must not vanish meanwhile.
    /// </summary>
    private bool FollowsLightroomNow =>
        settings.QuitWithLightroom && !PluginNeedsAction &&
        !(System.Windows.Application.Current?.Windows.OfType<System.Windows.Window>()
            .Any(window => window.IsVisible && window.ShowInTaskbar) ?? false);

    private void LightroomQuit()
    {
        Log.Write("Lightroom Classic quit");
        UpdatePluginIfOutdated();
        if (!FollowsLightroomNow) return;
        var delay = settings.QuitDelaySeconds;
        Status = $"Lightroom Classic has quit; Lumerist follows in {(int)delay} s";
        Changed?.Invoke();
        pendingQuit?.Dispose();
        pendingQuit = Schedule(TimeSpan.FromSeconds(delay), () =>
        {
            pendingQuit = null;
            if (LightroomWindow.IsRunning || !FollowsLightroomNow) return;
            Log.Write("Quitting with Lightroom Classic");
            System.Windows.Application.Current?.Shutdown();
        });
    }

    /// <summary>
    /// A newer plug-in came with this version: it replaces the installed one
    /// by itself whenever Lightroom is closed, at launch or as soon as
    /// Lightroom quits, keeping the old copy in the backup folder.
    /// </summary>
    private void UpdatePluginIfOutdated()
    {
        if (installer.Status() is not { UpdatesAutomatically: true } || LightroomWindow.IsRunning) return;
        Log.Write("Updating the plug-in by itself");
        installer.Install();
        Changed?.Invoke();
    }

    private void LightroomLaunched()
    {
        Log.Write("Lightroom Classic launched");
        if (pendingQuit is null) return;
        pendingQuit.Dispose();
        pendingQuit = null;
        Status = "Lightroom Classic is back; Lumerist stays open";
        Changed?.Invoke();
    }

    /// <summary>Lock, unlock, user switch: keys went up where the hook could not see them.</summary>
    private void OnSessionSwitch(object? sender, SessionSwitchEventArgs e)
    {
        Log.Write($"Session {e.Reason}: input state reset");
        ui.BeginInvoke(() => hook?.ResetInputState());
    }

    public static string AppVersion =>
        typeof(AppController).Assembly.GetName().Version is { } version ? $"{version.Major}.{version.Minor}.{version.Build}" : "?";

    public void ToggleShortcuts()
    {
        if (hook is null) StartShortcuts();
        else StopShortcuts();
        settings.ShortcutsEnabled = hook is not null;
        SaveSettings();
        Changed?.Invoke();
    }

    public void ToggleDebugLog()
    {
        settings.DebugLog = !settings.DebugLog;
        Log.IsEnabled = settings.DebugLog;
        Log.Write("Troubleshooting log on");
        SaveSettings();
        Changed?.Invoke();
    }

    public void Reconnect() => client.Reconnect();

    public string InstallPlugin()
    {
        var message = installer.Install();
        Changed?.Invoke();
        return message;
    }

    public void Dispose()
    {
        lightroomWatch?.Stop();
        pendingQuit?.Dispose();
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        StopShortcuts();
        client.Dispose();
        Log.Write("Lumerist quit");
    }

    // Keymap and settings ------------------------------------------------------

    private static Keymap LoadKeymap() => KeymapFile.Load();

    public void SaveSettings()
    {
        try
        {
            settings.Save(AppPaths.SettingsFile);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Log.Write($"Settings not saved: {error.Message}");
        }
        // "Open Lumerist when Lightroom starts" lives in the plug-in's link file.
        WritePluginLink();
        SettingsChanged?.Invoke();
    }

    /// <summary>Tells the plug-in where Lumerist.exe is, so it can open it with Lightroom (written at every start).</summary>
    private void WritePluginLink()
    {
        var exe = Environment.ProcessPath;
        if (exe is null) return;
        var link = new PluginLink(exe, settings.OpenWithLightroom);
        if (!link.IsAccepted)
        {
            Log.Write($"The plug-in cannot open Lumerist from {exe}: move it to a folder without \", % or ^ in the path");
            return;
        }
        try
        {
            link.Save(AppPaths.DataFolder);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Log.Write($"plugin-link.json not written: {error.Message}");
        }
    }

    // Shortcuts ----------------------------------------------------------------

    private void StartShortcuts()
    {
        if (hook is not null) return;
        var newHook = new InputHook(keymap, intents => ui.BeginInvoke(() => dispatcher.Handle(intents)), Log.Write);
        newHook.UpdateContext(context.Module.Length == 0 ? "develop" : context.Module, context.Tool);
        try
        {
            newHook.Start();
        }
        catch (InvalidOperationException error)
        {
            Status = error.Message;
            Log.Write(error.Message);
            return;
        }
        hook = newHook;
        Log.Write("Keyboard and mouse shortcuts on");
    }

    private void StopShortcuts()
    {
        if (hook is null) return;
        hook.Dispose();
        hook = null;
        Log.Write("Keyboard and mouse shortcuts off");
    }

    /// <summary>Sends one of Lightroom's own shortcuts, only while Lightroom is in front.</summary>
    private bool PostShortcut(KeyChord chord)
    {
        var (isInFront, threadId) = LightroomWindow.Foreground();
        if (!isInFront) return false;
        var layout = Interop.Native.GetKeyboardLayout(threadId);
        if (!InputSender.Chord(chord, hook?.HeldModifierMask ?? 0, layout))
            Log.Write($"Shortcut {chord.DisplayText} not delivered (is Lightroom running as administrator?)");
        return true;
    }

    private IDisposable Schedule(TimeSpan delay, Action action)
    {
        var timer = new DispatcherTimer(DispatcherPriority.Normal, ui) { Interval = delay };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            action();
        };
        timer.Start();
        return new Cancellation(timer.Stop);
    }

    private sealed class Cancellation(Action cancel) : IDisposable
    {
        public void Dispose() => cancel();
    }

    private void ShowNotice(string text)
    {
        Log.Write($"Notice: {text}");
        Panels?.ShowNotice(text);
        Changed?.Invoke();
    }

    // Bridge -------------------------------------------------------------------

    private long? Send(LightroomRequest request)
    {
        Log.Write($"Send {request.Kind}");
        return client.Send(request, error =>
        {
            if (error is not null) ui.BeginInvoke(() => Log.Write($"Request not written: {error.Message}"));
        });
    }

    private void OnState(LightroomCommandClient.State state)
    {
        switch (state)
        {
            case LightroomCommandClient.State.Ready:
                var wasConnected = isConnected;
                isConnected = true;
                Status = $"Connected to the Lumerist plug-in on 127.0.0.1:{Contract.RequestPort}";
                Log.Write("Bridge connected");
                if (!wasConnected) Send(LightroomRequest.Query(LightroomQuery.Context));
                break;
            case LightroomCommandClient.State.Waiting(var reason):
                if (isConnected) Log.Write($"Bridge lost: {reason}");
                isConnected = false;
                dispatcher.ConnectionLost();
                Status = LightroomWindow.IsRunning
                    ? "Waiting for the Lumerist plug-in in Lightroom Classic…"
                    : "Lightroom Classic is closed. Lumerist connects as soon as it opens.";
                break;
        }
        Changed?.Invoke();
    }

    private void OnMessage(LightroomMessage message)
    {
        switch (message)
        {
            case LightroomMessage.Ack(var ack):
                Log.Write($"Lightroom {(ack.Accepted ? "accepted" : "rejected")} #{ack.Id?.ToString() ?? "-"} {ack.Parameter ?? ""}: {ack.Message ?? ""}");
                // A refused command retried with Lightroom's own shortcut shows no HUD (as on the Mac).
                if (!dispatcher.DidReceive(ack)) Panels?.ShowAck(ack);
                AckReceived?.Invoke(ack);
                break;
            case LightroomMessage.Context(var newContext):
                Apply(newContext);
                break;
            case LightroomMessage.Result(LightroomResult.Context(_, var newContext)):
                Apply(newContext);
                break;
            case LightroomMessage.Result(LightroomResult.Presets(_, var items, _)):
                Log.Write($"Presets: {items.Count}");
                Presets = items;
                PresetsChanged?.Invoke();
                break;
            case LightroomMessage.Result(LightroomResult.Catalog(_, var unsupported, var skipped) catalog):
                Log.Write($"Catalog check: unsupported {string.Join(", ", unsupported)}; skipped {string.Join(", ", skipped)}");
                CatalogCheck = catalog;
                CatalogCheckChanged?.Invoke();
                break;
        }
    }

    private void Apply(LightroomContext newContext)
    {
        if (newContext == context) return;
        context = newContext;
        hook?.UpdateContext(newContext.Module, newContext.Tool);
        Log.Write($"Lightroom context: {newContext.Module} {newContext.Tool}");
        Panels?.ContextChanged(newContext);
        Changed?.Invoke();
    }
}
