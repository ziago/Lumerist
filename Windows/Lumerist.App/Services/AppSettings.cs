using System.Text.Json;
using System.Text.Json.Serialization;
using Lumerist.App.Design;
using Lumerist.Core;

namespace Lumerist.App.Services;

/// <summary>
/// App-wide preferences in %APPDATA%\Lumerist\settings.json (the Mac keeps
/// them in UserDefaults). Unknown or bad values fall back to the defaults.
/// </summary>
/// <remarks>
/// Partial: each part of the interface keeps its own preferences in its own
/// file (AppSettings.Views.cs, AppSettings.Panels.cs, AppSettings.Guide.cs),
/// in this one class.
/// </remarks>
internal sealed partial class AppSettings
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    /// <summary>The keyboard and mouse hooks run (no permission is needed on Windows).</summary>
    public bool ShortcutsEnabled { get; set; } = true;

    /// <summary>The plug-in opens Lumerist (in the background) when Lightroom Classic starts.</summary>
    public bool OpenWithLightroom { get; set; } = true;

    /// <summary>How often Dial Key changes are sent to Lightroom (they are summed in between).</summary>
    public double UpdateRateHz { get; set; } = IntentCoalescer.DefaultRate;

    public bool DebugLog { get; set; }

    /// <summary>Launch with only the tray icon; the window opens from there.</summary>
    public bool StartHidden { get; set; }

    /// <summary>
    /// Lumerist only works inside Lightroom Classic, so by default it follows
    /// Lightroom: opened by the plug-in, closed a few seconds after it.
    /// </summary>
    public bool QuitWithLightroom { get; set; } = true;

    public static readonly (double Minimum, double Maximum) QuitDelayRange = (0, 60);

    private const double DefaultQuitDelaySeconds = 3;

    public double QuitDelaySeconds { get; set; } = DefaultQuitDelaySeconds;

    /// <summary>The Value HUD.</summary>
    public OverlaySettings Overlay { get; set; } = new();

    /// <summary>The look: Galerist (default) or Miami.</summary>
    public AppTheme Theme { get; set; } = AppTheme.Galerist;

    /// <summary>Galerist's light/dark choice; Miami is always dark.</summary>
    public ThemeAppearance ThemeAppearance { get; set; } = ThemeAppearance.System;

    public static AppSettings Load(string path)
    {
        AppSettings? settings = null;
        try
        {
            if (File.Exists(path)) settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), Options);
        }
        catch (Exception error) when (error is JsonException or IOException or UnauthorizedAccessException)
        {
            // Defaults below.
        }
        settings ??= new AppSettings();
        settings.UpdateRateHz = double.IsFinite(settings.UpdateRateHz)
            ? Math.Clamp(settings.UpdateRateHz, IntentCoalescer.RateMinimum, IntentCoalescer.RateMaximum)
            : IntentCoalescer.DefaultRate;
        settings.QuitDelaySeconds = double.IsFinite(settings.QuitDelaySeconds)
            ? Math.Clamp(settings.QuitDelaySeconds, QuitDelayRange.Minimum, QuitDelayRange.Maximum)
            : DefaultQuitDelaySeconds;
        settings.Overlay ??= new OverlaySettings();
        return settings;
    }

    /// <summary>Written to a temporary file first, so a crash never leaves half a file.</summary>
    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(this, Options));
        File.Move(temporary, path, overwrite: true);
    }
}
