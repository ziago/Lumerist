using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Lumerist.App.Design;
using Lumerist.App.Design.Controls;

namespace Lumerist.App.Views;

/// <summary>
/// The header row (wordmark, Lightroom state, shortcut switch) and the
/// section tabs of the main window (ContentView.swift), shared by the
/// window and its snapshots.
/// </summary>
internal static class MainWindowParts
{
    /// <summary>Horizontal padding of a screen: Galerist 32, Miami 24.</summary>
    public static Thickness ScreenPadding => Theme.IsFlat ? new Thickness(32, 28, 32, 28) : new Thickness(24, 20, 24, 20);

    /// <summary>Screens are never wider than this, left-aligned in a wide window.</summary>
    public const double MaximumScreenWidth = 1040;

    /// <summary>
    /// The header row, 52 px: on the Mac it sits in the title bar beside the
    /// window buttons; here it is the top of the window, under the title bar.
    /// </summary>
    public static FrameworkElement Header(AppController controller)
    {
        var bar = Layout.Bar(Theme.Space.XL, [Wordmark()], [Status(controller)]);
        return new Border
        {
            Height = 52,
            Padding = new Thickness(16 + 6, 0, 16 + 4, 0),
            Background = Theme.IsFlat ? Ink.Chrome : Brushes.Transparent,
            BorderBrush = Ink.GridLine,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = bar,
            SnapsToDevicePixels = true,
        };
    }

    /// <summary>Galerist: "Lumerist" with a full stop in the live colour. Miami: glowing teal capitals.</summary>
    private static FrameworkElement Wordmark()
    {
        FrameworkElement mark;
        if (Theme.IsFlat)
        {
            var text = new TextBlock
            {
                FontFamily = Theme.DisplayFont,
                FontSize = 17,
                FontWeight = FontWeights.SemiBold,
                Foreground = Ink.Ice,
            };
            text.Inlines.Add(new Run("Lumerist"));
            text.Inlines.Add(new Run(".") { Foreground = Ink.Live });
            mark = text;
        }
        else
        {
            mark = SynthText.Caps("Lumerist", Theme.DisplayFont, 16, FontWeights.Bold, 2, Ink.Teal);
            mark.Effect = SynthControls.Glow(Ink.Teal.Color, 8, 0.6);
        }
        AutomationProperties.SetName(mark, "Lumerist");
        AutomationProperties.SetHeadingLevel(mark, AutomationHeadingLevel.Level1);
        return mark;
    }

    private static FrameworkElement Status(AppController controller)
    {
        FrameworkElement link = Theme.IsFlat
            ? LinkState(controller.IsConnected)
            : controller.IsConnected
                ? SynthControls.StatusPill("Lightroom · linked", Icons.CheckMark, SynthControls.Tone.Good)
                : SynthControls.StatusPill("Lightroom · waiting", Icons.Hourglass, SynthControls.Tone.Waiting);
        return Layout.HStack(Theme.IsFlat ? 20 : 14, link, ShortcutSwitch(controller));
    }

    /// <summary>Galerist: "Lightroom · linked" with the live dot, or muted "waiting".</summary>
    private static FrameworkElement LinkState(bool isLinked)
    {
        var row = Layout.HStack(Theme.Space.S);
        if (isLinked) row.Children.Add(new System.Windows.Shapes.Ellipse { Width = 7, Height = 7, Fill = Ink.Live, VerticalAlignment = VerticalAlignment.Center });
        row.Children.Add(SynthText.Caps(isLinked ? "Lightroom · linked" : "Lightroom · waiting", Theme.MonoFont, 11, FontWeights.Medium, 1.8,
            isLinked ? Ink.Ice : Ink.Steel));
        var box = new Border
        {
            Height = 28,
            Padding = new Thickness(12, 0, 12, 0),
            BorderBrush = Ink.GridLine,
            BorderThickness = new Thickness(1),
            Child = row,
            SnapsToDevicePixels = true,
        };
        AutomationProperties.SetName(box, isLinked ? "Lightroom linked" : "Waiting for Lightroom");
        return box;
    }

    /// <summary>"SHORTCUTS [ON]": magenta when on. Windows needs no permission, so there is no waiting state.</summary>
    private static FrameworkElement ShortcutSwitch(AppController controller)
    {
        var on = controller.ShortcutsEnabled;
        UIElement face;
        if (Theme.IsFlat)
        {
            face = new Border
            {
                Height = 28,
                Background = Brushes.Transparent,
                Child = Layout.HStack(10, SynthText.Caps("Shortcuts", Theme.MonoFont, 11, FontWeights.Medium, 1.8, Ink.Steel),
                    SynthControls.FlatSwitch(on)),
            };
        }
        else
        {
            var color = on ? Ink.Magenta : Ink.Steel;
            var state = new Border
            {
                Height = 20,
                Padding = new Thickness(8, 0, 8, 0),
                CornerRadius = new CornerRadius(2),
                Background = color,
                Child = SynthText.Caps(on ? "On" : "Off", Theme.MonoFont, 11, FontWeights.Bold, 1, on ? Ink.OnMagenta : Ink.Abyss),
            };
            var box = new Border
            {
                Height = 28,
                Padding = new Thickness(10, 0, 4, 0),
                CornerRadius = new CornerRadius(4),
                Background = on ? Ink.Faded(Ink.Magenta, 0.10) : Brushes.Transparent,
                BorderBrush = color,
                BorderThickness = new Thickness(1),
                Child = Layout.HStack(Theme.Space.S,
                    SynthText.Caps("Shortcuts", Theme.MonoFont, 11, FontWeights.SemiBold, 1, on ? Ink.MagentaLight : Ink.Steel), state),
            };
            if (on) box.Effect = SynthControls.Glow(Ink.Magenta.Color, 8, 0.45);
            face = box;
        }
        var button = SynthControls.Plain(face, controller.ToggleShortcuts);
        button.ToolTip = on ? "Turn keyboard + scroll shortcuts off" : "Turn keyboard + scroll shortcuts on";
        AutomationProperties.SetName(button, "Shortcuts");
        AutomationProperties.SetItemStatus(button, on ? "On" : "Off");
        return button;
    }

    /// <summary>
    /// Uppercase tabs: module pages left of the divider, settings on the
    /// right. Galerist: mono small caps with their icons on the header band,
    /// the current one ink with a 2 px underline. Miami: the current one
    /// outlined in teal.
    /// </summary>
    public static FrameworkElement SectionTabs(MainSection selection, Action<MainSection> select)
    {
        var modules = MainSections.All.Where(section => section.IsModulePage());
        var settings = MainSections.All.Where(section => !section.IsModulePage());
        if (Theme.IsFlat)
        {
            var row = Layout.HStack(26);
            row.VerticalAlignment = VerticalAlignment.Stretch;
            foreach (var section in modules) row.Children.Add(FlatTab(section, section == selection, select));
            row.Children.Add(new Border { Width = 1, Height = 18, Background = Ink.GridLine, VerticalAlignment = VerticalAlignment.Center });
            foreach (var section in settings) row.Children.Add(FlatTab(section, section == selection, select));
            // The line under the tabs; the current tab's underline covers it.
            var band = new Grid { Height = 46, Background = Ink.Chrome };
            band.Children.Add(new Border { Height = 1, Background = Ink.GridLine, VerticalAlignment = VerticalAlignment.Bottom });
            row.Margin = new Thickness(32, 0, 32, 0);
            band.Children.Add(row);
            return band;
        }
        var neon = Layout.HStack(2);
        foreach (var section in modules) neon.Children.Add(NeonTab(section, section == selection, select));
        neon.Children.Add(new Border
        {
            Width = 1,
            Height = 18,
            Background = Ink.GridLine,
            Margin = new Thickness(6, 0, 6, 10),
            VerticalAlignment = VerticalAlignment.Bottom,
        });
        foreach (var section in settings) neon.Children.Add(NeonTab(section, section == selection, select));
        neon.Margin = new Thickness(14, 10, 14, 0);
        var strip = new Grid();
        strip.Children.Add(new Border { Height = 1, Background = Ink.GridLine, VerticalAlignment = VerticalAlignment.Bottom });
        strip.Children.Add(neon);
        return strip;
    }

    private static Button FlatTab(MainSection section, bool selected, Action<MainSection> select)
    {
        var color = selected ? Ink.Ice : Ink.Steel;
        var label = Layout.HStack(7, SynthText.Icon(section.Icon(), 11.5, color),
            SynthText.Caps(section.Title(), Theme.MonoFont, 11, FontWeights.Medium, 1.8, color));
        var face = new Grid { Background = Brushes.Transparent };
        face.Children.Add(label);
        face.Children.Add(new Border
        {
            Height = 2,
            Background = selected ? Ink.Magenta : Brushes.Transparent,
            VerticalAlignment = VerticalAlignment.Bottom,
        });
        var button = Tab(face, section, selected, select);
        // Full height, so the underline sits on the band's bottom line.
        button.VerticalAlignment = VerticalAlignment.Stretch;
        return button;
    }

    private static Button NeonTab(MainSection section, bool selected, Action<MainSection> select)
    {
        var text = SynthText.Caps(section.Title(), Theme.DisplayFont, 12, FontWeights.SemiBold, 0.8, selected ? Brushes.White : Ink.Steel);
        var face = new Border
        {
            Padding = new Thickness(10, 9, 10, 10),
            CornerRadius = new CornerRadius(6, 6, 0, 0),
            Child = text,
            VerticalAlignment = VerticalAlignment.Bottom,
            BorderThickness = selected ? new Thickness(1, 1, 1, 0) : new Thickness(0),
            BorderBrush = selected ? Ink.Teal : Brushes.Transparent,
        };
        if (selected)
        {
            var fill = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
            fill.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(255 * 0.18), Ink.Teal.Color.R, Ink.Teal.Color.G, Ink.Teal.Color.B), 0));
            fill.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(255 * 0.02), Ink.Teal.Color.R, Ink.Teal.Color.G, Ink.Teal.Color.B), 1));
            fill.Freeze();
            face.Background = fill;
            face.Effect = SynthControls.Glow(Ink.Teal.Color, 7, 0.25);
        }
        else
        {
            face.Background = Brushes.Transparent;
        }
        var button = Tab(face, section, selected, select);
        button.VerticalAlignment = VerticalAlignment.Bottom;
        return button;
    }

    private static Button Tab(UIElement face, MainSection section, bool selected, Action<MainSection> select)
    {
        var button = SynthControls.Plain(face, () => select(section));
        AutomationProperties.SetName(button, section.Title());
        AutomationProperties.SetItemStatus(button, selected ? "Selected" : "");
        return button;
    }

    /// <summary>
    /// The window drawn without the window around it (header, tabs and one
    /// screen at full length), for the snapshots: the Mac's MainWindowSnapshot.
    /// </summary>
    public static FrameworkElement Compose(AppController controller, MainSection section, FrameworkElement screen)
    {
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var background = new SynthBackground();
        Grid.SetRowSpan(background, 3);
        root.Children.Add(background);
        var header = Header(controller);
        root.Children.Add(header);
        var tabs = SectionTabs(section, _ => { });
        Grid.SetRow(tabs, 1);
        root.Children.Add(tabs);
        var column = new LeadingColumn
        {
            MaximumWidth = MaximumScreenWidth,
            Margin = ScreenPadding,
            VerticalAlignment = VerticalAlignment.Top,
            Child = screen,
        };
        Grid.SetRow(column, 2);
        root.Children.Add(column);
        root.SetValue(TextElement.FontFamilyProperty, Theme.TextFont);
        return root;
    }
}
