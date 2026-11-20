using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;

namespace Lumerist.App.Design.Controls;

/// <summary>
/// Small modal questions in the theme (the Mac's alerts and confirmation
/// dialogs): a title, a message and named buttons, never "Yes / No".
/// </summary>
internal static class SynthDialog
{
    /// <summary>
    /// Asks before doing something; true when <paramref name="actionTitle"/>
    /// was chosen. Esc and the close box cancel.
    /// </summary>
    public static bool Confirm(DependencyObject? from, string title, string? message, string actionTitle, bool isDestructive = false,
        string cancelTitle = "Cancel")
    {
        var chosen = false;
        Show(from, title, message, [(cancelTitle, false), (actionTitle, true)], isDestructive, index => chosen = index == 1);
        return chosen;
    }

    /// <summary>Tells something, with one OK button.</summary>
    public static void Inform(DependencyObject? from, string title, string? message, string okTitle = "OK") =>
        Show(from, title, message, [(okTitle, true)], isDestructive: false, _ => { });

    private static void Show(DependencyObject? from, string title, string? message, IReadOnlyList<(string Title, bool IsAction)> buttons,
        bool isDestructive, Action<int> chose)
    {
        var owner = from is null ? null : Window.GetWindow(from);
        var window = new Window
        {
            Title = "Lumerist",
            Width = 440,
            SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner,
            Owner = owner,
        };
        window.SetResourceReference(FrameworkElement.StyleProperty, "Synth.Window");
        window.Resources.MergedDictionaries.Add(new ControlStyles());
        window.SourceInitialized += (_, _) => Theme.ApplyTitleBar(window);

        var row = Layout.HStack(Theme.Space.S);
        row.HorizontalAlignment = HorizontalAlignment.Right;
        for (var index = 0; index < buttons.Count; index++)
        {
            var (buttonTitle, isAction) = buttons[index];
            var choice = index;
            var kind = !isAction ? SynthControls.Kind.Secondary
                : isDestructive ? SynthControls.Kind.Destructive
                : SynthControls.Kind.Primary;
            var button = SynthControls.Button(buttonTitle, kind, () =>
            {
                chose(choice);
                window.Close();
            });
            button.IsDefault = isAction;
            button.IsCancel = !isAction || buttons.Count == 1;
            row.Children.Add(button);
        }

        var heading = SynthText.Title(title, 16);
        AutomationProperties.SetHeadingLevel(heading, AutomationHeadingLevel.Level1);
        var content = Layout.VStack(Theme.Space.M,
            heading,
            message is null ? null : SynthText.Body(message, 13, color: Ink.Body),
            row);
        row.Margin = new Thickness(0, Theme.Space.S, 0, 0);
        window.Content = new Border { Padding = new Thickness(Theme.Space.XXL), Child = content };
        window.PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            window.Close();
            e.Handled = true;
        };
        window.ShowDialog();
    }
}
