using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Lumerist.App.Design;

namespace Lumerist.App.Snapshots;

/// <summary>
/// PNG renders of every screen, to set beside the Mac's (the Fase 3
/// criterion): <c>Lumerist.exe --render-snapshots &lt;folder&gt;</c> writes
/// <c>&lt;name&gt;-&lt;theme&gt;.png</c> at 2× for Galerist light, Galerist dark
/// and Miami, then quits. Nothing is started: no hooks, no connection.
/// </summary>
/// <remarks>
/// Each part of the interface registers its screens from its own file with
/// a module initializer:
/// <code>
/// [ModuleInitializer]
/// internal static void Register() =&gt;
///     SnapshotRegistry.Add("hud-ruler", 420, 120, controller =&gt; new ValueHudView(...));
/// </code>
/// The builder returns the screen's content (not a Window): it is measured
/// at the given size in device-independent pixels.
/// </remarks>
internal static class SnapshotRegistry
{
    public sealed record Snapshot(string Name, double Width, double Height, Func<AppController, FrameworkElement> Build);

    private static readonly List<Snapshot> Snapshots = [];

    public static IReadOnlyList<Snapshot> All => Snapshots;

    public static void Add(string name, double width, double height, Func<AppController, FrameworkElement> build) =>
        Snapshots.Add(new Snapshot(name, width, height, build));

    /// <summary>Renders every snapshot in every theme; returns how many failed.</summary>
    public static int RenderAll(string folder, AppController controller)
    {
        Directory.CreateDirectory(folder);
        var failures = 0;
        var variants = new (string Name, AppTheme Theme, ThemeAppearance Appearance)[]
        {
            ("galerist-light", AppTheme.Galerist, ThemeAppearance.Light),
            ("galerist-dark", AppTheme.Galerist, ThemeAppearance.Dark),
            ("miami", AppTheme.Miami, ThemeAppearance.Dark),
        };
        foreach (var variant in variants)
        {
            Theme.Apply(variant.Theme, variant.Appearance, notify: false);
            foreach (var snapshot in Snapshots.OrderBy(item => item.Name, StringComparer.Ordinal))
            {
                var path = Path.Combine(folder, $"{snapshot.Name}-{variant.Name}.png");
                try
                {
                    Render(snapshot.Build(controller), snapshot.Width, snapshot.Height, path);
                    Console.WriteLine(path);
                }
                catch (Exception error)
                {
                    failures++;
                    Console.Error.WriteLine($"{snapshot.Name} ({variant.Name}): {error}");
                }
            }
        }
        return failures;
    }

    private static void Render(FrameworkElement element, double width, double height, string path)
    {
        const double scale = 2;
        element.Width = width;
        element.Height = height;
        element.Measure(new Size(width, height));
        element.Arrange(new Rect(0, 0, width, height));
        element.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)(width * scale), (int)(height * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        var background = new DrawingVisual();
        using (var context = background.RenderOpen())
        {
            context.DrawRectangle(Theme.Brush(nameof(ThemePalette.Abyss)), null, new Rect(0, 0, width, height));
        }
        bitmap.Render(background);
        bitmap.Render(element);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path);
        encoder.Save(file);
    }
}
