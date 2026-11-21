using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Lumerist.App.Design;
using Lumerist.App.Design.Controls;

namespace Lumerist.App.Views;

/// <summary>
/// General → Appearance (AppearanceSection.swift): the theme (two preview
/// cards) and, for Galerist, light or dark. Both apply at once; the window
/// is rebuilt in the new look.
/// </summary>
internal static class AppearanceSection
{
    public static FrameworkElement Build(AppController controller)
    {
        // The look in effect: in the app it equals the saved choice (choosing applies it);
        // snapshots draw each theme without saving one.
        var theme = Theme.Current;
        var appearance = Theme.Appearance;
        var cards = new System.Windows.Controls.Primitives.UniformGrid { Columns = 2, Rows = 1 };
        foreach (var option in new[] { AppTheme.Galerist, AppTheme.Miami })
        {
            var card = ThemeCard(option, option == theme, () =>
            {
                var settings = controller.Settings;
                if (settings.Theme == option && Theme.Current == option) return;
                settings.Theme = option;
                controller.SaveSettings();
                Theme.Apply(option, settings.ThemeAppearance);
            });
            card.Margin = new Thickness(option == AppTheme.Galerist ? 0 : Theme.Space.L / 2, 0, option == AppTheme.Galerist ? Theme.Space.L / 2 : 0, 0);
            cards.Children.Add(card);
        }
        AutomationProperties.SetName(cards, "Theme");
        var themeRow = new Border
        {
            Padding = new Thickness(Surfaces.RowInset, Theme.IsFlat ? 14 : 12, Surfaces.RowInset, Theme.IsFlat ? 18 : 14),
            BorderBrush = Ink.RowDivider,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = Layout.VStack(Theme.Space.M,
                Surfaces.SettingText("Theme", "The look of this window and of the panels over Lightroom. Your keys and settings stay the same."),
                cards),
        };

        var lightOrDark = SynthControls.Segmented([ThemeAppearance.System, ThemeAppearance.Light, ThemeAppearance.Dark], appearance, Title, value =>
        {
            var settings = controller.Settings;
            if (settings.ThemeAppearance == value) return;
            settings.ThemeAppearance = value;
            controller.SaveSettings();
            Theme.Apply(settings.Theme, value);
        }, isEnabled: theme != AppTheme.Miami, name: "Light or dark");

        return Surfaces.SettingsSection("Appearance", Icons.Palette, null,
            themeRow,
            Surfaces.SettingRow("Light or dark",
                theme == AppTheme.Miami
                    ? "Miami is always dark. Galerist can follow Windows or stay light or dark."
                    : "Galerist follows Windows unless you pick one here. Miami is always dark.",
                lightOrDark));
    }

    /// <summary>"Like Windows", "Light", "Dark".</summary>
    public static string Title(ThemeAppearance appearance) => appearance switch
    {
        ThemeAppearance.System => "Like Windows",
        ThemeAppearance.Light => "Light",
        _ => "Dark",
    };

    private static string Title(AppTheme theme) => theme == AppTheme.Galerist ? "Galerist" : "Miami";

    private static string Readout(AppTheme theme) => theme == AppTheme.Galerist ? "Light · dark" : "Neon · dark only";

    /// <summary>One selectable theme: a small window preview in that theme's own colours, its name and a read-out.</summary>
    private static FrameworkElement ThemeCard(AppTheme theme, bool isSelected, Action select)
    {
        // The selection colour of the theme in use (not of the card).
        var accent = Theme.IsFlat ? Ink.Ice : Ink.Magenta;
        var caption = Layout.Bar(Theme.Space.S,
            [
                SynthText.Body(Title(theme), 14, FontWeights.SemiBold, Ink.Ice),
                isSelected ? SynthText.Icon(Icons.CircleCheck, 12, accent) : null,
            ],
            [SynthText.Label(Readout(theme), 9)]);
        caption.Margin = new Thickness(12, 11, 12, 11);
        var preview = theme == AppTheme.Galerist ? GaleristPreview() : MiamiPreview();
        preview.Height = 128;
        preview.ClipToBounds = true;
        var face = new Border
        {
            Background = Ink.Panel,
            CornerRadius = new CornerRadius(Theme.IsFlat ? 0 : 6),
            BorderBrush = isSelected ? accent : Ink.GridLine,
            BorderThickness = new Thickness(isSelected ? 2 : 1),
            ClipToBounds = true,
            Child = Layout.VStack(0, preview, caption),
        };
        var button = SynthControls.Plain(face, select);
        AutomationProperties.SetName(button, $"{Title(theme)} theme");
        AutomationProperties.SetItemStatus(button, isSelected ? "Selected" : Readout(theme));
        if (!isSelected) AutomationProperties.SetHelpText(button, "Switches Lumerist to this look");
        return button;
    }

    // Previews: fixed colours and type, they show each theme as it is ------------------

    private static SolidColorBrush Hex(uint hex) => Ink.Faded(new SolidColorBrush(Theme.FromHex(hex)), 1);

    private static FrameworkElement GaleristPreview()
    {
        var ink = Hex(0x0A0A0A);
        var line = Hex(0xE7E7E7);
        var wordmark = new TextBlock { FontFamily = Theme.Outfit, FontSize = 10, FontWeight = FontWeights.SemiBold, Foreground = ink, VerticalAlignment = VerticalAlignment.Center };
        wordmark.Inlines.Add(new Run("Lumerist"));
        wordmark.Inlines.Add(new Run(".") { Foreground = Hex(0xE11D2A) });
        var top = new Border
        {
            Height = 24,
            Padding = new Thickness(10, 0, 10, 0),
            Background = Hex(0xFAFAFA),
            BorderBrush = line,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = wordmark,
        };
        FrameworkElement Row(string key, bool filled)
        {
            var cap = new Border
            {
                Width = 24,
                Height = 18,
                Background = filled ? ink : Brushes.White,
                BorderBrush = ink,
                BorderThickness = new Thickness(1),
                Child = new TextBlock
                {
                    Text = key,
                    FontFamily = Theme.MonoFont,
                    FontSize = 9,
                    FontWeight = FontWeights.Medium,
                    Foreground = filled ? Brushes.White : ink,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            };
            var bar = Layout.Bar(6, [cap, new Border { Height = 1, Background = line }], []);
            Stretch(bar);
            return bar;
        }
        var body = new Border
        {
            Padding = new Thickness(12),
            Background = Brushes.White,
            Child = Layout.VStack(8,
                new TextBlock { Text = "Develop", FontFamily = Theme.Outfit, FontSize = 16, FontWeight = FontWeights.SemiBold, Foreground = ink },
                Row("Q", false),
                Row("W", true)),
        };
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(top);
        Grid.SetRow(body, 1);
        grid.Children.Add(body);
        return new Border { BorderBrush = line, BorderThickness = new Thickness(0, 0, 0, 1), Child = grid };
    }

    private static FrameworkElement MiamiPreview()
    {
        var palette = ThemePalette.Miami;
        var teal = Hex(palette.Teal);
        var top = new Border
        {
            Height = 24,
            Padding = new Thickness(10, 0, 10, 0),
            BorderBrush = Hex(palette.Outline),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = new SpacedText { Text = "LUMERIST", FontFamily = Theme.ChakraPetch, FontSize = 10, FontWeight = FontWeights.Bold, Tracking = 1.5, Foreground = teal, VerticalAlignment = VerticalAlignment.Center },
        };
        FrameworkElement Row(string key, bool filled)
        {
            var magenta = Hex(palette.Magenta);
            var color = filled ? magenta : teal;
            var cap = new Border
            {
                Width = 24,
                Height = 18,
                CornerRadius = new CornerRadius(3),
                Background = filled ? magenta : Brushes.Transparent,
                BorderBrush = color,
                BorderThickness = new Thickness(1),
                Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = color.Color, BlurRadius = 8, ShadowDepth = 0, Opacity = 0.5 },
                Child = new TextBlock
                {
                    Text = key,
                    FontFamily = Theme.MonoFont,
                    FontSize = 9,
                    FontWeight = FontWeights.Bold,
                    Foreground = filled ? Hex(palette.OnMagenta) : teal,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            };
            var bar = Layout.Bar(6, [cap, new Border { Height = 1, Background = Hex(palette.GridLine) }], []);
            Stretch(bar);
            return bar;
        }
        var body = Layout.VStack(8,
            new SpacedText { Text = "DEVELOP", FontFamily = Theme.ChakraPetch, FontSize = 13, FontWeight = FontWeights.Bold, Tracking = 1, Foreground = Hex(palette.Ice) },
            Row("Q", false),
            Row("W", true));
        body.Margin = new Thickness(12);
        var fill = new LinearGradientBrush(Theme.FromHex(palette.Abyss), Theme.FromHex(palette.Dusk), 90);
        fill.Freeze();
        var grid = new Grid { Background = fill };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(top);
        Grid.SetRow(body, 1);
        grid.Children.Add(body);
        return grid;
    }

    /// <summary>The line after the key takes the rest of the row.</summary>
    private static void Stretch(Grid bar)
    {
        bar.ColumnDefinitions[1].Width = new GridLength(1, GridUnitType.Star);
        bar.ColumnDefinitions[2].Width = new GridLength(0);
    }
}
