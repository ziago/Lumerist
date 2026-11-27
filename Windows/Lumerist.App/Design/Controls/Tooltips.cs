using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Lumerist.App.Services;
using Lumerist.Core;

namespace Lumerist.App.Design.Controls;

/// <summary>
/// Every tooltip of the app shows after the same delay as the Mac's help tags
/// (<see cref="KeyTypeHelp.InitialShowDelayMilliseconds"/>, 1 s), so a pointer
/// passing over a label never flashes one. Their look follows the theme: the
/// implicit ToolTip style in Design/Styles.xaml.
/// </summary>
internal static class Tooltips
{
    private static bool applied;

    /// <summary>Sets the delay for every element; call once, before the first window.</summary>
    public static void UseAppDelay()
    {
        if (applied) return;
        applied = true;
        try
        {
            ToolTipService.InitialShowDelayProperty.OverrideMetadata(typeof(FrameworkElement),
                new FrameworkPropertyMetadata(KeyTypeHelp.InitialShowDelayMilliseconds));
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException)
        {
            // Too late for some element: tooltips keep Windows' delay, except the
            // explained labels, which set theirs (Explained).
            Log.Write($"Tooltip delay not set for the whole app: {error.Message}");
        }
    }

    /// <summary>
    /// <paramref name="label"/> with an explanation on hover, after the app's
    /// delay whatever <see cref="UseAppDelay"/> managed. The transparent frame
    /// makes the whole label hoverable, gaps between letters included.
    /// </summary>
    public static Border Explained(UIElement label, string text)
    {
        var frame = new Border { Background = Brushes.Transparent, Child = label, ToolTip = text };
        ToolTipService.SetInitialShowDelay(frame, KeyTypeHelp.InitialShowDelayMilliseconds);
        return frame;
    }
}
