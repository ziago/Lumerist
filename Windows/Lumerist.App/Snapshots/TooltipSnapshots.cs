using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using Lumerist.App.Design;
using Lumerist.App.Design.Controls;
using Lumerist.Core;

namespace Lumerist.App.Snapshots;

/// <summary>
/// A tooltip in each theme (<c>tooltip-galerist-light.png</c>, <c>-galerist-dark</c>,
/// <c>-miami</c>): the Dial Key column header with its explanation below, in
/// the very look every ToolTip gets (Synth.ToolTip.Look in Design/Styles.xaml).
/// A real tooltip opens in a window of its own, which a render can't catch.
/// </summary>
internal static class TooltipSnapshots
{
    [ModuleInitializer]
    internal static void Register() => SnapshotRegistry.Add("tooltip", 480, 160, _ => Build());

    private static FrameworkElement Build()
    {
        var look = new ContentControl
        {
            Style = (Style)Application.Current.FindResource("Synth.ToolTip.Look"),
            Content = KeyTypeHelp.Dial(ParameterScope.Develop),
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        return new Border
        {
            Padding = new Thickness(Theme.Space.L),
            Child = Layout.VStack(Theme.Space.XS, SynthText.Label("Dial Key", 10), look),
        };
    }
}
