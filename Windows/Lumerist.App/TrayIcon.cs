using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Lumerist.App.Design;
using Lumerist.App.Guide;
using Lumerist.App.Services;
using Lumerist.Core;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace Lumerist.App;

/// <summary>
/// The icon in the notification area (the Mac's menu bar item). A left click
/// opens the same menu as a right click. The icon follows the theme: the app's
/// icon in Galerist, the neon one in Miami (as the Mac's Dock icon does).
/// </summary>
internal sealed class TrayIcon : IDisposable
{
    private readonly AppController controller;
    private readonly TrayMenu menu;
    private readonly Forms.NotifyIcon icon;
    private Drawing.Icon image;

    public TrayIcon(AppController controller, Action quit)
    {
        this.controller = controller;
        menu = new TrayMenu(controller, quit);
        image = TrayImage.Create();
        icon = new Forms.NotifyIcon { Icon = image, ContextMenuStrip = menu.Strip, Visible = true };
        // A left click opens the same menu, as a click on the Mac's menu bar item does.
        icon.MouseClick += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left) ShowMenu();
        };
        controller.Changed += Refresh;
        Theme.Changed += OnThemeChanged;
        updates = GuideHost.Updates(controller);
        updates.Changed += OnUpdatesChanged;
        icon.BalloonTipClicked += (_, _) => updates.OpenReleasePage();
        Refresh();
    }

    private readonly UpdateChecker updates;

    /// <summary>
    /// A newer version: a line in the menu for as long as it is newer, and a
    /// Windows notification once per version (the Mac shows the line in its
    /// menu bar item). Clicking either opens the release page; nothing is
    /// downloaded by Lumerist.
    /// </summary>
    private void OnUpdatesChanged()
    {
        var available = updates.State as UpdateCheckState.Available;
        menu.ShowUpdate(available?.Version, updates.OpenReleasePage);
        if (available is null || controller.Settings.LastAnnouncedVersion == available.Version) return;
        controller.Settings.LastAnnouncedVersion = available.Version;
        controller.SaveSettings();
        icon.ShowBalloonTip(10_000, $"Lumerist {available.Version} is available",
            "Click to open its download page. Your keys and settings stay as they are.", Forms.ToolTipIcon.Info);
    }

    /// <summary>
    /// NotifyIcon opens its menu by itself only on a right click. Its own
    /// (private) way of opening it brings the menu to the front first, so a
    /// click anywhere else closes it again; a plain Show at the pointer would
    /// leave it open. The plain Show is only the fallback.
    /// </summary>
    private static readonly MethodInfo? ShowContextMenuMethod = typeof(Forms.NotifyIcon).GetMethod(
        "ShowContextMenu", BindingFlags.Instance | BindingFlags.NonPublic, Type.EmptyTypes);

    private void ShowMenu()
    {
        if (ShowContextMenuMethod is not null) ShowContextMenuMethod.Invoke(icon, null);
        else icon.ContextMenuStrip?.Show(Forms.Cursor.Position);
    }

    private void Refresh()
    {
        menu.Refresh();
        // Tooltips are limited to 127 characters.
        var tip = $"Lumerist — {(controller.IsPaused ? "paused" : controller.IsConnected ? "connected" : "not connected")}";
        icon.Text = tip.Length > 127 ? tip[..127] : tip;
    }

    private void OnThemeChanged()
    {
        var previous = image;
        image = TrayImage.Create();
        icon.Icon = image;
        previous.Dispose();
        menu.Strip.Invalidate();
    }

    public void Dispose()
    {
        controller.Changed -= Refresh;
        Theme.Changed -= OnThemeChanged;
        updates.Changed -= OnUpdatesChanged;
        icon.Visible = false;
        icon.Dispose();
        menu.Dispose();
        image.Dispose();
    }
}

/// <summary>
/// The tray menu: the Mac's menu bar window (link state, shortcuts, Key
/// Reference, Open Lumerist, connection, setup guide, support, a quiet
/// Galerist link, quit) as a native Windows menu, plus what Windows has no
/// other place for yet: the pause state, the plug-in and the troubleshooting log.
/// </summary>
internal sealed class TrayMenu : IDisposable
{
    private readonly AppController controller;
    private readonly Forms.ToolStripMenuItem link = Line(TrayRole.Link);
    private readonly Forms.ToolStripMenuItem context = Line(TrayRole.Info);
    private readonly Forms.ToolStripMenuItem status = Line(TrayRole.Info);
    private readonly Forms.ToolStripMenuItem paused = Line(TrayRole.Info);
    private readonly Forms.ToolStripMenuItem plugin = Line(TrayRole.Info);
    private readonly Forms.ToolStripMenuItem install = new("Install the Lightroom plug-in");
    private readonly Forms.ToolStripMenuItem shortcuts = new("Shortcuts");
    private readonly Forms.ToolStripMenuItem keyReference = new("Show Key Reference");
    private readonly Forms.ToolStripMenuItem debugLog = new("Keep a troubleshooting log");
    private readonly Forms.ToolStripMenuItem update = new() { Visible = false };
    private Action? openUpdate;

    /// <summary>"Download Lumerist 0.9.1…" while a newer version exists.</summary>
    public void ShowUpdate(string? version, Action open)
    {
        openUpdate = open;
        update.Visible = version is not null;
        if (version is not null) update.Text = $"Download Lumerist {version}…";
    }

    public Forms.ContextMenuStrip Strip { get; }

    public TrayMenu(AppController controller, Action quit)
    {
        this.controller = controller;
        paused.Text = "Paused (Ctrl+Esc in Lightroom resumes)";
        install.Click += (_, _) => Forms.MessageBox.Show(controller.InstallPlugin(), "Lumerist");
        shortcuts.Click += (_, _) => controller.ToggleShortcuts();
        keyReference.Click += (_, _) => controller.Panels?.ToggleKeyReference();
        debugLog.Click += (_, _) => controller.ToggleDebugLog();
        update.Click += (_, _) => openUpdate?.Invoke();

        Strip = new Forms.ContextMenuStrip { Renderer = new TrayMenuRenderer(), ShowCheckMargin = false, ShowImageMargin = true };
        Strip.Items.AddRange(new Forms.ToolStripItem[]
        {
            link,
            context,
            status,
            paused,
            plugin,
            install,
            update,
            new Forms.ToolStripSeparator(),
            shortcuts,
            new Forms.ToolStripSeparator(),
            keyReference,
            Item("Open Lumerist…", controller.RequestMainWindow),
            Item("Check connection", controller.SendConnectionCheck),
            Item("Setup guide…", () => GuideHost.ShowGuide(controller)),
            // As on the Mac: General, where About & Support is the last section.
            Item("Support Lumerist…", () => Views.MainWindow.ShowSection(controller, Views.MainSection.General)),
            // Opens the browser only when clicked; Lumerist sends nothing.
            Item("Deliver photos to clients — Galerist", () => GuideKit.Open(SupportLinks.GaleristFromMenu), TrayRole.Quiet),
            new Forms.ToolStripSeparator(),
            debugLog,
            Item("Open the log folder", OpenLogFolder),
            new Forms.ToolStripSeparator(),
            Item("Quit Lumerist", quit),
        });
        // The plug-in status reads files: only when the menu opens.
        Strip.Opening += (_, _) =>
        {
            RefreshPlugin();
            Refresh();
        };
        RefreshPlugin();
        Refresh();
    }

    /// <summary>Everything but the plug-in (which reads files).</summary>
    public void Refresh()
    {
        link.Text = StatusMenuText.Link(controller.IsConnected);
        link.Tag = controller.IsConnected ? TrayRole.Linked : TrayRole.Link;
        context.Text = StatusMenuText.Context(controller.IsConnected, controller.Context);
        // Why it is not linked: Lightroom closed, the plug-in not answering, the shortcuts could not start.
        status.Text = controller.Status;
        status.Visible = !controller.IsConnected;
        paused.Visible = controller.IsPaused;
        shortcuts.Checked = controller.ShortcutsEnabled;
        debugLog.Checked = controller.DebugLogEnabled;
        keyReference.Text = controller.Panels?.IsKeyReferenceVisible == true ? "Hide Key Reference" : "Show Key Reference";
    }

    private void RefreshPlugin()
    {
        plugin.Text = controller.PluginSummary;
        install.Visible = controller.PluginNeedsAction;
    }

    private static Forms.ToolStripMenuItem Line(TrayRole role) => new() { Enabled = false, Tag = role };

    private static Forms.ToolStripMenuItem Item(string text, Action action, TrayRole role = TrayRole.Action) =>
        new(text, null, (_, _) => action()) { Tag = role };

    private static void OpenLogFolder()
    {
        try
        {
            Directory.CreateDirectory(AppPaths.LogFolder);
            Process.Start(new ProcessStartInfo(AppPaths.LogFolder) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            Log.Write($"Log folder not opened: {error.Message}");
        }
    }

    /// <summary>
    /// For <c>--render-snapshots</c>: the menu as it would open now, drawn by
    /// Windows into a picture at the screen's DPI. It is never shown.
    /// </summary>
    public FrameworkElement Picture()
    {
        var size = Strip.GetPreferredSize(Drawing.Size.Empty);
        Strip.AutoSize = false;
        Strip.Size = size;
        Strip.PerformLayout();
        using var bitmap = new Drawing.Bitmap(size.Width, size.Height, Drawing.Imaging.PixelFormat.Format32bppArgb);
        Strip.DrawToBitmap(bitmap, new Drawing.Rectangle(Drawing.Point.Empty, size));
        bitmap.SetResolution(Strip.DeviceDpi, Strip.DeviceDpi);
        using var stream = new MemoryStream();
        bitmap.Save(stream, Drawing.Imaging.ImageFormat.Png);
        stream.Position = 0;
        var picture = new BitmapImage();
        picture.BeginInit();
        picture.CacheOption = BitmapCacheOption.OnLoad;
        picture.StreamSource = stream;
        picture.EndInit();
        picture.Freeze();
        return new Grid
        {
            Children =
            {
                new Image
                {
                    Source = picture,
                    Stretch = Stretch.None,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(Theme.Space.S),
                },
            },
        };
    }

    public void Dispose() => Strip.Dispose();
}

/// <summary>How an item of the tray menu is drawn.</summary>
internal enum TrayRole
{
    Action,
    /// <summary>"Waiting for Lightroom" (amber in Miami, grey in Galerist).</summary>
    Link,
    /// <summary>"Lightroom linked" (teal in Miami, ink in Galerist).</summary>
    Linked,
    /// <summary>A status line.</summary>
    Info,
    /// <summary>The Galerist link: grey, like the Mac's quiet item.</summary>
    Quiet,
}

/// <summary>Draws the tray menu in the theme's colours, as the Mac's menu bar window follows the theme.</summary>
internal sealed class TrayMenuRenderer : Forms.ToolStripProfessionalRenderer
{
    public TrayMenuRenderer() : base(new TrayMenuColors()) => RoundedEdges = false;

    /// <summary>Drawn here for every item: the base class greys out disabled items in the system colour, unreadable on a dark menu.</summary>
    protected override void OnRenderItemText(Forms.ToolStripItemTextRenderEventArgs e)
    {
        var role = e.Item.Tag is TrayRole tag ? tag : TrayRole.Action;
        var color = role switch
        {
            TrayRole.Linked => TrayMenuColors.Token(Theme.IsFlat ? nameof(ThemePalette.Ice) : nameof(ThemePalette.Teal)),
            TrayRole.Link => TrayMenuColors.Token(Theme.IsFlat ? nameof(ThemePalette.Steel) : nameof(ThemePalette.Amber)),
            TrayRole.Info or TrayRole.Quiet => TrayMenuColors.Token(nameof(ThemePalette.Steel)),
            _ => TrayMenuColors.Token(e.Item.Enabled ? nameof(ThemePalette.Ice) : nameof(ThemePalette.Disabled)),
        };
        Forms.TextRenderer.DrawText(e.Graphics, e.Text, e.TextFont, e.TextRectangle, color, e.TextFormat);
    }

    /// <summary>A check mark in the accent colour (the default one is black, invisible on a dark menu).</summary>
    protected override void OnRenderItemCheck(Forms.ToolStripItemImageRenderEventArgs e)
    {
        var box = e.ImageRectangle;
        if (box.Width <= 0 || box.Height <= 0) return;
        var color = TrayMenuColors.Token(Theme.IsFlat ? nameof(ThemePalette.Ice) : nameof(ThemePalette.Magenta));
        using var pen = new Drawing.Pen(color, Math.Max(1.5f, box.Height / 9f))
        {
            StartCap = Drawing.Drawing2D.LineCap.Round,
            EndCap = Drawing.Drawing2D.LineCap.Round,
            LineJoin = Drawing.Drawing2D.LineJoin.Round,
        };
        var smoothing = e.Graphics.SmoothingMode;
        e.Graphics.SmoothingMode = Drawing.Drawing2D.SmoothingMode.AntiAlias;
        e.Graphics.DrawLines(pen, new[]
        {
            new Drawing.PointF(box.Left + box.Width * 0.22f, box.Top + box.Height * 0.52f),
            new Drawing.PointF(box.Left + box.Width * 0.42f, box.Top + box.Height * 0.72f),
            new Drawing.PointF(box.Left + box.Width * 0.80f, box.Top + box.Height * 0.30f),
        });
        e.Graphics.SmoothingMode = smoothing;
    }

    protected override void OnRenderSeparator(Forms.ToolStripSeparatorRenderEventArgs e)
    {
        var bounds = new Drawing.Rectangle(Drawing.Point.Empty, e.Item.Size);
        using var pen = new Drawing.Pen(TrayMenuColors.Token(nameof(ThemePalette.GridLine)));
        var y = bounds.Top + bounds.Height / 2;
        e.Graphics.DrawLine(pen, bounds.Left + 8, y, bounds.Right - 8, y);
    }
}

/// <summary>The theme's colours for the tray menu, read when it paints, so a theme change needs no rebuild.</summary>
internal sealed class TrayMenuColors : Forms.ProfessionalColorTable
{
    public TrayMenuColors() => UseSystemColors = false;

    public static Drawing.Color Token(string name)
    {
        var color = Theme.ColorOf(name);
        return Drawing.Color.FromArgb(color.A, color.R, color.G, color.B);
    }

    private static Drawing.Color Blend(Drawing.Color top, Drawing.Color bottom, double alpha) => Drawing.Color.FromArgb(
        (int)Math.Round(top.R * alpha + bottom.R * (1 - alpha)),
        (int)Math.Round(top.G * alpha + bottom.G * (1 - alpha)),
        (int)Math.Round(top.B * alpha + bottom.B * (1 - alpha)));

    /// <summary>Galerist: the panel white (or charcoal); Miami: the abyss.</summary>
    private static Drawing.Color Background => Token(Theme.IsFlat ? nameof(ThemePalette.Panel) : nameof(ThemePalette.Abyss));

    private static Drawing.Color Hover => Theme.IsFlat ? Token(nameof(ThemePalette.GridLine)) : Blend(Token(nameof(ThemePalette.Teal)), Background, 0.18);

    private static Drawing.Color Frame => Theme.IsFlat ? Token(nameof(ThemePalette.GridLine)) : Blend(Token(nameof(ThemePalette.Teal)), Background, 0.7);

    public override Drawing.Color ToolStripDropDownBackground => Background;
    public override Drawing.Color ImageMarginGradientBegin => Background;
    public override Drawing.Color ImageMarginGradientMiddle => Background;
    public override Drawing.Color ImageMarginGradientEnd => Background;
    public override Drawing.Color MenuBorder => Frame;
    public override Drawing.Color MenuItemBorder => Hover;
    public override Drawing.Color MenuItemSelected => Hover;
    public override Drawing.Color MenuItemSelectedGradientBegin => Hover;
    public override Drawing.Color MenuItemSelectedGradientEnd => Hover;
    public override Drawing.Color MenuItemPressedGradientBegin => Hover;
    public override Drawing.Color MenuItemPressedGradientMiddle => Hover;
    public override Drawing.Color MenuItemPressedGradientEnd => Hover;
    public override Drawing.Color CheckBackground => Background;
    public override Drawing.Color CheckSelectedBackground => Hover;
    public override Drawing.Color CheckPressedBackground => Hover;
    public override Drawing.Color SeparatorDark => Token(nameof(ThemePalette.GridLine));
    public override Drawing.Color SeparatorLight => Token(nameof(ThemePalette.GridLine));
}

/// <summary>The tray icon's picture for the theme in effect.</summary>
internal static class TrayImage
{
    public static Drawing.Icon Create()
    {
        var size = Forms.SystemInformation.SmallIconSize;
        if (Theme.Current == AppTheme.Miami) return Miami(Math.Max(16, size.Width));
        using var stream = typeof(TrayIcon).Assembly.GetManifestResourceStream("Lumerist.ico")
            ?? throw new InvalidOperationException("The tray icon resource is missing.");
        return new Drawing.Icon(stream, size);
    }

    /// <summary>
    /// The neon icon's small version (Resources/Icon/icon-miami-small.svg):
    /// night gradient, sunset half disc, a dark screen with a teal frame and
    /// a teal slider line. Drawn at the exact tray size, so it stays sharp.
    /// </summary>
    private static Drawing.Icon Miami(int pixels)
    {
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            // The SVG's 1024 canvas and its transform: translate(100 100) scale(0.919643) translate(-64 -64).
            context.PushTransform(new ScaleTransform(pixels / 1024.0, pixels / 1024.0));
            context.PushTransform(new TranslateTransform(100, 100));
            context.PushTransform(new ScaleTransform(0.919643, 0.919643));
            context.PushTransform(new TranslateTransform(-64, -64));
            context.DrawRoundedRectangle(Vertical((0x140A33, 0), (0x2A0F4A, 0.62), (0x070B18, 1)), null, new Rect(64, 64, 896, 896), 200, 200);
            context.DrawGeometry(Vertical((0xFFB347, 0), (0xFF6A5E, 0.55), (0xFF3EA5, 1)), null, Geometry.Parse("M 262,600 A 250,250 0 0 1 762,600 Z"));
            var teal = new SolidColorBrush(Theme.FromHex(0x2DE2E6));
            context.DrawRoundedRectangle(new SolidColorBrush(Theme.FromHex(0x070B18, 0xD1)), new Pen(teal, 40), new Rect(320, 300, 384, 300), 56, 56);
            context.DrawLine(new Pen(teal, 44) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, new Point(430, 450), new Point(594, 450));
            for (var index = 0; index < 4; index++) context.Pop();
        }
        var render = new RenderTargetBitmap(pixels, pixels, 96, 96, PixelFormats.Pbgra32);
        render.Render(visual);
        var buffer = new byte[pixels * pixels * 4];
        render.CopyPixels(buffer, pixels * 4, 0);

        using var bitmap = new Drawing.Bitmap(pixels, pixels, Drawing.Imaging.PixelFormat.Format32bppPArgb);
        var data = bitmap.LockBits(new Drawing.Rectangle(0, 0, pixels, pixels), Drawing.Imaging.ImageLockMode.WriteOnly,
                                   Drawing.Imaging.PixelFormat.Format32bppPArgb);
        try
        {
            for (var row = 0; row < pixels; row++) Marshal.Copy(buffer, row * pixels * 4, data.Scan0 + row * data.Stride, pixels * 4);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
        var handle = bitmap.GetHicon();
        try
        {
            // The copy owns its own handle; the one GetHicon made is freed here.
            using var borrowed = Drawing.Icon.FromHandle(handle);
            return (Drawing.Icon)borrowed.Clone();
        }
        finally
        {
            GuideNative.DestroyIcon(handle);
        }
    }

    private static LinearGradientBrush Vertical(params (uint Hex, double Offset)[] stops)
    {
        var brush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        foreach (var (hex, offset) in stops) brush.GradientStops.Add(new GradientStop(Theme.FromHex(hex), offset));
        return brush;
    }
}
