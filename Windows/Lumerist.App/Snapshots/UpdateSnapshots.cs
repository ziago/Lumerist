using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using Lumerist.App.Design;
using Lumerist.App.Guide;
using Lumerist.Core;

namespace Lumerist.App.Snapshots;

/// <summary>
/// The update window in each state, as <c>update-&lt;state&gt;-&lt;theme&gt;.png</c>.
/// Previews only: nothing is downloaded, installed or saved.
/// </summary>
internal static class UpdateSnapshots
{
    private static readonly string[] Highlights =
    [
        "Culling keys for Lightroom's Import window: include, exclude and toggle photos with keys you pick.",
        "A note when a key you assign is one Lightroom already uses.",
        "A private connection to Lightroom: the plug-in answers only Lumerist.",
        "From now on, a new version installs with one click.",
    ];

    [ModuleInitializer]
    internal static void Register()
    {
        Add("offer", 400, UpdateModel.Preview(new UpdateModel.Phase.Offer(), Highlights));
        Add("offer-no-highlights", 280, UpdateModel.Preview(new UpdateModel.Phase.Offer()));
        Add("checking", 220, UpdateModel.Preview(new UpdateModel.Phase.Working(UpdateStep.Checking, default)));
        Add("downloading", 250, UpdateModel.Preview(new UpdateModel.Phase.Working(UpdateStep.Downloading,
            new DownloadProgress(35_860_480, 74_448_896))));
        Add("launching", 200, UpdateModel.Preview(new UpdateModel.Phase.Launching()));
        Add("failed", 260, UpdateModel.Preview(new UpdateModel.Phase.Failed(UpdateModel.CantInstall,
            "The release's checksums aren't signed with Lumerist's key.")));
        Add("not-installed", 240, UpdateModel.Preview(new UpdateModel.Phase.NotInstalled()));
        Add("plugin-waits", 260, UpdateModel.Preview(new UpdateModel.Phase.PluginWaits(), afterUpdate: true));
        Add("quitting-lightroom", 200, UpdateModel.Preview(new UpdateModel.Phase.QuittingLightroom(), afterUpdate: true));
        Add("done", 220, UpdateModel.Preview(new UpdateModel.Phase.Done("Plug-in updated. Lightroom Classic is starting again."), afterUpdate: true));
    }

    /// <summary>The view on the window's own background, so a render taller than the content shows no seam.</summary>
    private static void Add(string state, double height, UpdateModel model) =>
        SnapshotRegistry.Add($"update-{state}", UpdateView.ContentWidth, height, _ => new Border
        {
            Background = Theme.Brush(Theme.IsFlat ? nameof(ThemePalette.Panel) : nameof(ThemePalette.Abyss)),
            Child = new UpdateView(model) { VerticalAlignment = VerticalAlignment.Top },
        });
}
