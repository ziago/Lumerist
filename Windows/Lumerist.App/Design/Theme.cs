using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;

namespace Lumerist.App.Design;

/// <summary>The available looks (the Mac's <c>AppTheme</c>).</summary>
internal enum AppTheme
{
    Galerist,
    Miami,
}

/// <summary>Light or dark, for themes that have both (Galerist). Miami is always dark.</summary>
internal enum ThemeAppearance
{
    /// <summary>"Like Windows": the apps light/dark choice in Windows settings.</summary>
    System,
    Light,
    Dark,
}

/// <summary>
/// The look of the app (the Mac's <c>Synth</c> and <c>ThemeController</c>).
/// <see cref="Apply"/> writes every token into the application resources, so
/// XAML uses <c>{DynamicResource Synth.Teal}</c> and code uses
/// <see cref="Brush(string)"/>; both follow a theme change.
/// </summary>
/// <remarks>
/// Resource keys:
/// <list type="bullet">
/// <item><c>Synth.&lt;Token&gt;</c>: a frozen <see cref="SolidColorBrush"/> for each
/// <see cref="ThemePalette"/> token (Synth.Abyss, Synth.Teal…), and
/// <c>Synth.&lt;Token&gt;.Color</c> the <see cref="Color"/>;</item>
/// <item><c>Synth.Font.Display</c>, <c>Synth.Font.Text</c>, <c>Synth.Font.Mono</c>:
/// Chakra Petch / Inter / JetBrains Mono in Miami, Outfit / Outfit / JetBrains
/// Mono in Galerist;</item>
/// <item><c>Synth.Radius.Control</c>, <c>Synth.Radius.Row</c>,
/// <c>Synth.Radius.Container</c>: <see cref="CornerRadius"/> 4 / 3 / 6 in Miami,
/// 0 in Galerist;</item>
/// <item><c>Synth.IsFlat</c>: true in Galerist (no glow, gradients, grid or
/// rounded corners).</item>
/// </list>
/// Windows that build text from code (uppercase titles in Miami) rebuild
/// their content on <see cref="Changed"/>, as the Mac rebuilds its views.
/// </remarks>
internal static partial class Theme
{
    public static AppTheme Current { get; private set; } = AppTheme.Galerist;

    public static ThemeAppearance Appearance { get; private set; } = ThemeAppearance.System;

    /// <summary>Galerist: no glow, gradients, grid or rounded corners.</summary>
    public static bool IsFlat => Current == AppTheme.Galerist;

    /// <summary>Whether the theme in effect is dark (always in Miami).</summary>
    public static bool IsDark => Current == AppTheme.Miami || Appearance switch
    {
        ThemeAppearance.Light => false,
        ThemeAppearance.Dark => true,
        _ => SystemUsesDarkApps(),
    };

    public static ThemePalette Palette => Current == AppTheme.Miami
        ? ThemePalette.Miami
        : IsDark ? ThemePalette.GaleristDark : ThemePalette.GaleristLight;

    /// <summary>Raised on the UI thread after the theme or the light/dark choice changed.</summary>
    public static event Action? Changed;

    /// <summary>4 px grid (the Mac's Synth.Space).</summary>
    public static class Space
    {
        public const double XS = 4, S = 8, M = 12, L = 16, XL = 20, XXL = 24;
    }

    /// <summary>The Value HUD's entrance and exit (the Mac's Synth.Motion).</summary>
    public static readonly TimeSpan OverlayIn = TimeSpan.FromMilliseconds(120);
    public static readonly TimeSpan OverlayOut = TimeSpan.FromMilliseconds(250);

    public static FontFamily DisplayFont => IsFlat ? Outfit : ChakraPetch;
    public static FontFamily TextFont => IsFlat ? Outfit : Inter;
    public static FontFamily MonoFont => JetBrainsMono;

    private static readonly Uri FontBase = new("pack://application:,,,/");
    private static readonly FontFamily Inter = new(FontBase, "./Assets/Fonts/#Inter, Segoe UI");
    private static readonly FontFamily JetBrainsMono = new(FontBase, "./Assets/Fonts/#JetBrains Mono, Consolas");

    /// <summary>Galerist's font, whatever the theme in effect (the Appearance previews draw both looks).</summary>
    public static readonly FontFamily Outfit = new(FontBase, "./Assets/Fonts/#Outfit, Segoe UI");

    /// <summary>Miami's display font, whatever the theme in effect.</summary>
    public static readonly FontFamily ChakraPetch = new(FontBase, "./Assets/Fonts/#Chakra Petch, Segoe UI");

    private static bool listensToSystem;

    /// <summary>Puts a theme into effect: resources, then <see cref="Changed"/> (unless <paramref name="notify"/> is false).</summary>
    public static void Apply(AppTheme theme, ThemeAppearance appearance, bool notify = true)
    {
        Current = theme;
        Appearance = appearance;
        if (!listensToSystem)
        {
            listensToSystem = true;
            SystemEvents.UserPreferenceChanged += OnSystemPreferenceChanged;
        }
        WriteResources(Application.Current?.Resources);
        if (Application.Current is { } application)
            foreach (Window window in application.Windows) ApplyTitleBar(window);
        if (notify) Changed?.Invoke();
    }

    /// <summary>The brush of a token for code-built UI, e.g. <c>Theme.Brush(nameof(ThemePalette.Teal))</c>.</summary>
    public static SolidColorBrush Brush(string token) => Frozen(ColorOf(token));

    public static Color ColorOf(string token) =>
        typeof(ThemePalette).GetProperty(token)?.GetValue(Palette) is uint hex
            ? FromHex(hex)
            : throw new ArgumentException($"Unknown theme token {token}", nameof(token));

    public static Color FromHex(uint hex, byte alpha = 0xFF) =>
        Color.FromArgb(alpha, (byte)(hex >> 16), (byte)(hex >> 8), (byte)hex);

    /// <summary>Titles are uppercase in Miami and sentence case in Galerist.</summary>
    public static string TitleText(string text) => IsFlat ? text : text.ToUpperInvariant();

    /// <summary>Writes every token into <paramref name="resources"/> (the application's, or a render's own).</summary>
    public static void WriteResources(ResourceDictionary? resources)
    {
        if (resources is null) return;
        foreach (var property in typeof(ThemePalette).GetProperties())
        {
            if (property.GetValue(Palette) is not uint hex) continue;
            var color = FromHex(hex);
            resources[$"Synth.{property.Name}"] = Frozen(color);
            resources[$"Synth.{property.Name}.Color"] = color;
        }
        resources["Synth.Font.Display"] = DisplayFont;
        resources["Synth.Font.Text"] = TextFont;
        resources["Synth.Font.Mono"] = MonoFont;
        resources["Synth.Radius.Control"] = new CornerRadius(IsFlat ? 0 : 4);
        resources["Synth.Radius.Row"] = new CornerRadius(IsFlat ? 0 : 3);
        resources["Synth.Radius.Container"] = new CornerRadius(IsFlat ? 0 : 6);
        resources["Synth.IsFlat"] = IsFlat;
        // Tooltips (Styles.xaml): Galerist paper and hairline, Miami's raised panel with a neon edge.
        resources["Synth.ToolTip.Background"] = Frozen(ColorOf(IsFlat ? nameof(ThemePalette.Panel) : nameof(ThemePalette.PanelRaised)));
        resources["Synth.ToolTip.Border"] = Frozen(ColorOf(IsFlat ? nameof(ThemePalette.Outline) : nameof(ThemePalette.Teal)));
        resources["Synth.ToolTip.Glow"] = IsFlat ? null : ToolTipGlow(ColorOf(nameof(ThemePalette.Teal)));
        // Room around the frame for the glow, which a tooltip's window would otherwise cut.
        resources["Synth.ToolTip.Margin"] = new Thickness(IsFlat ? 0 : 8);
    }

    private static System.Windows.Media.Effects.DropShadowEffect ToolTipGlow(Color color)
    {
        var glow = new System.Windows.Media.Effects.DropShadowEffect { Color = color, BlurRadius = 10, ShadowDepth = 0, Opacity = 0.45 };
        glow.Freeze();
        return glow;
    }

    /// <summary>A dark or light title bar to match the theme. Call once the window has a handle (SourceInitialized).</summary>
    public static void ApplyTitleBar(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == 0) return;
        var dark = IsDark ? 1 : 0;
        // DWMWA_USE_IMMERSIVE_DARK_MODE (Windows 10 2004 and later); ignored where unsupported.
        _ = DwmSetWindowAttribute(handle, 20, ref dark, sizeof(int));
    }

    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    /// <summary>Windows' "Choose your default app mode": AppsUseLightTheme = 0 means dark.</summary>
    private static bool SystemUsesDarkApps()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int light && light == 0;
        }
        catch (Exception error) when (error is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }

    private static void OnSystemPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category != UserPreferenceCategory.General || Current != AppTheme.Galerist || Appearance != ThemeAppearance.System) return;
        Application.Current?.Dispatcher.BeginInvoke(() => Apply(Current, Appearance));
    }

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmSetWindowAttribute(nint window, int attribute, ref int value, int size);
}
