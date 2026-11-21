using System.IO.Pipes;
using System.Windows;
using System.Windows.Threading;
using Lumerist.App.Design;
using Lumerist.App.Design.Controls;
using Lumerist.App.Guide;
using Lumerist.App.Panels;
using Lumerist.App.Services;
using Lumerist.App.Snapshots;

namespace Lumerist.App;

internal static class Program
{
    /// <summary>One Lumerist per Windows session.</summary>
    private const string InstanceName = @"Local\io.github.ziago.lumerist";

    /// <summary>
    /// A second copy (the plug-in's "Open Lumerist", a double click) hands its
    /// arguments to the running one through this pipe, then exits.
    /// </summary>
    public static readonly string PipeName = CreatePipeName();

    private static string CreatePipeName()
    {
        using var process = System.Diagnostics.Process.GetCurrentProcess();
        return $"io.github.ziago.lumerist-{Environment.UserName}-{process.SessionId}";
    }

    /// <summary>Opens the main window at startup or in the running copy.</summary>
    public const string ShowWindowArgument = "--show-window";

    /// <summary>The plug-in starts Lumerist with Lightroom: in the background, no window.</summary>
    public const string WithLightroomArgument = "--with-lightroom";

    /// <summary>
    /// The user opened Lumerist (Start menu, double click, the plug-in's
    /// "Open Lumerist"), as opposed to the plug-in starting it with Lightroom.
    /// </summary>
    public static bool WantsWindow(IReadOnlyCollection<string> args) =>
        args.Contains(ShowWindowArgument) || !args.Contains(WithLightroomArgument);

    /// <summary><c>--render-snapshots &lt;folder&gt;</c>: PNGs of every screen, then quit.</summary>
    public const string RenderSnapshotsArgument = "--render-snapshots";

    /// <summary>
    /// <c>--install-plugin</c>: installs or updates the Lightroom plug-in and
    /// exits, with no window (the installer runs it after copying the app).
    /// </summary>
    public const string InstallPluginArgument = "--install-plugin";

    [STAThread]
    private static int Main(string[] args)
    {
        if (args is [InstallPluginArgument])
        {
            return (int)new PluginInstaller().InstallFromCommandLine();
        }
        if (args.Length >= 2 && args[0] == RenderSnapshotsArgument)
        {
            return new LumeristApplication(args, snapshotFolder: args[1]).Run();
        }
        using var instance = new Mutex(initiallyOwned: true, InstanceName, out var isFirst);
        if (!isFirst)
        {
            HandToRunningCopy(args);
            return 0;
        }
        return new LumeristApplication(args).Run();
    }

    private static void HandToRunningCopy(string[] args)
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.Out, PipeOptions.CurrentUserOnly);
            pipe.Connect(2_000);
            using var writer = new StreamWriter(pipe);
            // A second start with Lightroom asks for nothing; any other start shows the window.
            writer.WriteLine(WantsWindow(args) ? ShowWindowArgument : "");
        }
        catch (Exception error) when (error is IOException or TimeoutException or UnauthorizedAccessException)
        {
            // The running copy is starting or quitting: nothing to hand over.
        }
    }
}

/// <summary>A WPF application without a main window: the tray icon is the way in.</summary>
internal sealed class LumeristApplication(IReadOnlyList<string> arguments, string? snapshotFolder = null) : Application
{
    private AppController? controller;
    private TrayIcon? tray;
    private CancellationTokenSource? listening;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/Lumerist;component/Design/Styles.xaml"),
        });

        if (snapshotFolder is not null)
        {
            var failures = SnapshotRegistry.RenderAll(snapshotFolder, new AppController(Dispatcher, forSnapshots: true));
            Shutdown(failures == 0 ? 0 : 1);
            return;
        }

        DispatcherUnhandledException += OnUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) => Log.Write($"Unhandled error: {args.ExceptionObject}");

        controller = new AppController(Dispatcher);
        Theme.Apply(controller.Settings.Theme, controller.Settings.ThemeAppearance, notify: false);
        controller.Panels = new PanelHost(controller) { PaletteDidClose = controller.PaletteDidClose };
        controller.ShowWindowRequested += () => Views.MainWindow.ShowOrActivate(controller);
        // Stay in touch never interrupts a key being recorded.
        StayInTouchPrompter.MainWindowIsBusy = () => ChordRecorder.Shared.RecordingId is not null;
        tray = new TrayIcon(controller, Shutdown);
        controller.Start(arguments);
        listening = new CancellationTokenSource();
        _ = ListenForOtherCopiesAsync(listening.Token);
        GuideHost.OnStarted(controller);
        // As on the Mac: the window opens when the user starts Lumerist, unless
        // "Start hidden" is on; never when the plug-in starts it with Lightroom.
        if (arguments.Contains(Program.ShowWindowArgument)
            || (Program.WantsWindow(arguments) && !controller.Settings.StartHidden))
        {
            controller.RequestMainWindow();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        listening?.Cancel();
        tray?.Dispose();
        controller?.Dispose();
        base.OnExit(e);
    }

    /// <summary>Arguments from a second copy: only <c>--show-window</c> does anything.</summary>
    private async Task ListenForOtherCopiesAsync(CancellationToken cancellation)
    {
        while (!cancellation.IsCancellationRequested)
        {
            try
            {
                await using var pipe = new NamedPipeServerStream(Program.PipeName, PipeDirection.In, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(cancellation);
                // A copy that connects and never writes must not block the next one.
                using var patience = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
                patience.CancelAfter(TimeSpan.FromSeconds(2));
                using var reader = new StreamReader(pipe);
                var buffer = new char[256];
                string line;
                try
                {
                    var read = await reader.ReadBlockAsync(buffer.AsMemory(), patience.Token);
                    line = new string(buffer, 0, read);
                }
                catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
                {
                    continue;
                }
                if (line.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Contains(Program.ShowWindowArgument))
                {
                    _ = Dispatcher.BeginInvoke(() => controller?.RequestMainWindow());
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                Log.Write($"Second copy not heard: {error.Message}");
                try
                {
                    await Task.Delay(1_000, cancellation);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }

    /// <summary>A bug in one action must not take the hooks down with the app.</summary>
    private static void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Write($"Unhandled error: {e.Exception}");
        e.Handled = true;
    }
}
