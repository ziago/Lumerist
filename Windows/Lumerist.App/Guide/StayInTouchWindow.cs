using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using Lumerist.App.Design;
using Lumerist.Core;

namespace Lumerist.App.Guide;

/// <summary>
/// The "Stay in touch" content (the Mac's StayInTouchSheet): one email field,
/// a one-line notice with the Privacy link, "Not now" and "Keep me posted".
/// Enter sends, Esc is "Not now". Nothing is sent before the user acts.
/// </summary>
internal sealed class StayInTouchView : LiveView
{
    private readonly StayInTouchModel model;
    private readonly Action changed;
    private TextBox? emailBox;
    private TextBlock? placeholder;
    private FrameworkElement? errorLine;
    private TextBlock? errorText;
    private FrameworkElement? spinner;
    private Button? keepButton;
    private Button? notNowButton;
    private Button? doneButton;
    private FrameworkElement? formPanel;
    private FrameworkElement? successPanel;
    private bool wasFailed;

    public StayInTouchView(StayInTouchModel model)
    {
        this.model = model;
        changed = UpdateState;
        Rebuild();
    }

    /// <summary>"Not now", "Done", or the success closing by itself.</summary>
    public event Action? CloseRequested;

    protected override void StartListening() => model.Changed += changed;

    protected override void StopListening() => model.Changed -= changed;

    /// <summary>The email field takes the keyboard when the dialog opens and after a failure.</summary>
    public void FocusEmail()
    {
        if (emailBox is { IsEnabled: true, IsVisible: true } box)
        {
            box.Focus();
            Keyboard.Focus(box);
            box.CaretIndex = box.Text.Length;
        }
    }

    protected override UIElement Build()
    {
        // A theme change rebuilds the field: give it the keyboard back.
        var hadFocus = emailBox?.IsKeyboardFocusWithin ?? false;
        var stack = new StackPanel();
        stack.Children.Add(Header());
        formPanel = Form();
        successPanel = Success();
        stack.Children.Add(formPanel);
        stack.Children.Add(successPanel);

        var root = new Border
        {
            Width = 480,
            Padding = new Thickness(28, 26, 28, 24),
            Child = stack,
        };
        root.SetResourceReference(Border.BackgroundProperty, Theme.IsFlat ? "Synth.Panel" : "Synth.Abyss");
        UpdateState();
        if (hadFocus) Dispatcher.BeginInvoke(FocusEmail, DispatcherPriority.Input);
        return root;
    }

    // Parts ------------------------------------------------------------------------

    private FrameworkElement Header()
    {
        var badge = new Border
        {
            Width = 40,
            Height = 40,
            BorderThickness = new Thickness(1),
            Child = GuideKit.Icon(GuideIcon.Envelope, 16, nameof(ThemePalette.Teal)),
            VerticalAlignment = VerticalAlignment.Center,
        };
        ((FrameworkElement)badge.Child).HorizontalAlignment = HorizontalAlignment.Center;
        if (Theme.IsFlat)
        {
            badge.SetResourceReference(Border.BorderBrushProperty, "Synth.GridLine");
        }
        else
        {
            // An envelope in a teal ring with a soft glow (decoration only).
            badge.CornerRadius = new CornerRadius(20);
            badge.BorderBrush = GuideKit.Translucent(nameof(ThemePalette.Teal), 0.8);
            badge.Effect = new DropShadowEffect { Color = Theme.ColorOf(nameof(ThemePalette.Teal)), BlurRadius = 16, ShadowDepth = 0, Opacity = 0.35 };
        }
        var title = GuideKit.Title(GaleristOptIn.Copy.Title, Theme.IsFlat ? 24 : 20);
        title.Margin = new Thickness(14, 0, 0, 0);
        title.VerticalAlignment = VerticalAlignment.Center;
        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 14),
            Children = { badge, title },
        };
    }

    private FrameworkElement Form()
    {
        var form = new StackPanel();
        var message = GuideKit.Text(GaleristOptIn.Copy.Message, 13.5);
        message.Margin = new Thickness(0, 0, 0, Theme.Space.L);
        form.Children.Add(message);
        form.Children.Add(EmailField());

        errorText = GuideKit.Text("", 12, nameof(ThemePalette.Error));
        errorText.VerticalAlignment = VerticalAlignment.Center;
        var errorIcon = GuideKit.Icon(GuideIcon.ExclamationCircle, 13, nameof(ThemePalette.Error));
        errorIcon.VerticalAlignment = VerticalAlignment.Top;
        errorIcon.Margin = new Thickness(0, 1, 6, 0);
        var error = new DockPanel { Margin = new Thickness(0, Theme.Space.S, 0, 0), Children = { errorIcon, errorText } };
        DockPanel.SetDock(errorIcon, Dock.Left);
        // Read out when it appears.
        AutomationProperties.SetLiveSetting(errorText, AutomationLiveSetting.Assertive);
        errorLine = error;
        form.Children.Add(error);

        form.Children.Add(Notice());
        form.Children.Add(Actions());
        return form;
    }

    private FrameworkElement EmailField()
    {
        var box = new TextBox
        {
            Style = GuideKit.StyleNamed("Guide.EmailBox"),
            Text = model.Email,
            InputScope = new InputScope { Names = { new InputScopeName(InputScopeNameValue.EmailSmtpAddress) } },
        };
        box.SetResourceReference(BackgroundProperty, Theme.IsFlat ? "Synth.PanelRaised" : "Synth.Panel");
        AutomationProperties.SetName(box, "Email address");
        box.TextChanged += (_, _) => model.Email = box.Text;
        // Enter sends, even with an address that does not look right: the
        // answer then says so (the button alone stays disabled).
        box.PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            e.Handled = true;
            model.Submit();
        };
        emailBox = box;

        placeholder = GuideKit.Text(GaleristOptIn.Copy.EmailPlaceholder, 14, nameof(ThemePalette.Disabled));
        placeholder.TextWrapping = TextWrapping.NoWrap;
        placeholder.IsHitTestVisible = false;
        placeholder.VerticalAlignment = VerticalAlignment.Center;
        placeholder.Margin = new Thickness(13, 0, 13, 0);
        return new Grid { Children = { box, placeholder } };
    }

    /// <summary>The notice, then the Privacy link (opened in the browser).</summary>
    private TextBlock Notice()
    {
        var notice = GuideKit.Text("", 11.5, nameof(ThemePalette.Steel));
        notice.Margin = new Thickness(0, 10, 0, 0);
        notice.Inlines.Clear();
        notice.Inlines.Add(new Run(GaleristOptIn.Copy.Notice + " "));
        var link = new Hyperlink(new Run(GaleristOptIn.Copy.PrivacyLink)) { NavigateUri = SupportLinks.GaleristPrivacy };
        link.SetResourceReference(TextElement.ForegroundProperty, "Synth.Teal");
        link.RequestNavigate += (_, e) =>
        {
            GuideKit.Open(e.Uri);
            e.Handled = true;
        };
        notice.Inlines.Add(link);
        return notice;
    }

    private FrameworkElement Actions()
    {
        var row = new Grid { Margin = new Thickness(0, 22, 0, 0) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = Theme.Space.S });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        if (model.OffersDontAskAgain)
        {
            var dontAsk = new CheckBox
            {
                Content = GaleristOptIn.Copy.DontAskAgain,
                IsChecked = model.DontAskAgain,
                Style = GuideKit.StyleNamed("Guide.CheckBox"),
                VerticalAlignment = VerticalAlignment.Center,
            };
            if (Theme.IsFlat)
            {
                dontAsk.SetResourceReference(BackgroundProperty, "Synth.Panel");
                dontAsk.SetResourceReference(BorderBrushProperty, "Synth.Steel");
            }
            else
            {
                dontAsk.SetResourceReference(BackgroundProperty, "Synth.Abyss");
                dontAsk.BorderBrush = GuideKit.Translucent(nameof(ThemePalette.Teal), 0.8);
            }
            dontAsk.Checked += (_, _) => model.DontAskAgain = true;
            dontAsk.Unchecked += (_, _) => model.DontAskAgain = false;
            row.Children.Add(dontAsk);
        }

        spinner = GuideKit.Spinner();
        spinner.Margin = new Thickness(0, 0, Theme.Space.M, 0);
        Grid.SetColumn(spinner, 2);
        row.Children.Add(spinner);

        notNowButton = GuideKit.Button(GaleristOptIn.Copy.NotNow, GuideButtonKind.Secondary, () => CloseRequested?.Invoke());
        notNowButton.Margin = new Thickness(0, 0, Theme.Space.M, 0);
        Grid.SetColumn(notNowButton, 3);
        row.Children.Add(notNowButton);

        keepButton = GuideKit.Button(GaleristOptIn.Copy.Button, GuideButtonKind.Primary, model.Submit);
        Grid.SetColumn(keepButton, 4);
        row.Children.Add(keepButton);
        return row;
    }

    private FrameworkElement Success()
    {
        var icon = GuideKit.Icon(GuideIcon.CheckCircleFill, 18, nameof(ThemePalette.Success));
        var text = GuideKit.Text(GaleristOptIn.Copy.Success, 15, nameof(ThemePalette.Ice), FontWeights.SemiBold);
        text.Margin = new Thickness(10, 0, 0, 0);
        text.VerticalAlignment = VerticalAlignment.Center;
        doneButton = GuideKit.Button(GaleristOptIn.Copy.Done, GuideButtonKind.Primary, () => CloseRequested?.Invoke());
        doneButton.HorizontalAlignment = HorizontalAlignment.Right;
        doneButton.Margin = new Thickness(0, 22, 0, 0);
        return new StackPanel
        {
            Children =
            {
                new StackPanel { Orientation = Orientation.Horizontal, Children = { icon, text } },
                doneButton,
            },
        };
    }

    // State ------------------------------------------------------------------------

    private void UpdateState()
    {
        if (emailBox is null || placeholder is null || errorLine is null || errorText is null || spinner is null
            || keepButton is null || notNowButton is null || doneButton is null || formPanel is null || successPanel is null) return;
        var subscribed = model.IsSubscribed;
        formPanel.Visibility = subscribed ? Visibility.Collapsed : Visibility.Visible;
        successPanel.Visibility = subscribed ? Visibility.Visible : Visibility.Collapsed;
        // One default (Enter) and one cancel (Esc) button at a time.
        keepButton.IsDefault = !subscribed;
        notNowButton.IsCancel = !subscribed;
        doneButton.IsDefault = subscribed;
        doneButton.IsCancel = subscribed;

        if (emailBox.Text != model.Email) emailBox.Text = model.Email;
        placeholder.Visibility = model.Email.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        emailBox.IsEnabled = !model.IsSending;
        keepButton.IsEnabled = model.CanSubmit;
        spinner.Visibility = model.IsSending ? Visibility.Visible : Visibility.Collapsed;

        var failed = model.State is StayInTouchModel.Phase.Failed;
        errorText.Text = model.State is StayInTouchModel.Phase.Failed failure ? failure.Message : "";
        errorLine.Visibility = failed ? Visibility.Visible : Visibility.Collapsed;
        // Back to the field after a failure (it was disabled while sending).
        if (failed && !wasFailed && IsLoaded) Dispatcher.BeginInvoke(FocusEmail, DispatcherPriority.Input);
        wasFailed = failed;
    }
}

/// <summary>
/// The dialog around <see cref="StayInTouchView"/>, modal over the main
/// window like the Mac's sheet. After a sign-up it closes by itself, unless a
/// screen reader is running: then "Done" closes it.
/// </summary>
internal sealed class StayInTouchWindow : Window
{
    private static readonly TimeSpan AutoCloseDelay = TimeSpan.FromMilliseconds(1500);

    private readonly StayInTouchModel model;
    private readonly StayInTouchView view;
    private DispatcherTimer? autoClose;

    public StayInTouchWindow(StayInTouchModel model)
    {
        this.model = model;
        view = new StayInTouchView(model);
        SetResourceReference(StyleProperty, "Synth.Window");
        Title = GaleristOptIn.Copy.Title;
        Content = view;
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        UpdateBackground();

        view.CloseRequested += Close;
        model.Changed += OnModelChanged;
        model.Announcement += Announce;
        Theme.Changed += UpdateBackground;
        SourceInitialized += (_, _) => Theme.ApplyTitleBar(this);
        ContentRendered += (_, _) => view.FocusEmail();
        Closed += (_, _) =>
        {
            autoClose?.Stop();
            model.Changed -= OnModelChanged;
            model.Announcement -= Announce;
            Theme.Changed -= UpdateBackground;
            model.DialogClosed();
        };
    }

    private void UpdateBackground() => Background = Theme.Brush(Theme.IsFlat ? nameof(ThemePalette.Panel) : nameof(ThemePalette.Abyss));

    private void OnModelChanged()
    {
        if (!model.IsSubscribed || autoClose is not null || GuideNative.IsScreenReaderRunning) return;
        autoClose = new DispatcherTimer { Interval = AutoCloseDelay };
        autoClose.Tick += (_, _) =>
        {
            autoClose.Stop();
            Close();
        };
        autoClose.Start();
    }

    /// <summary>Screen readers hear the result even though the dialog changes under them.</summary>
    private void Announce(string message)
    {
        var peer = UIElementAutomationPeer.FromElement(this) ?? UIElementAutomationPeer.CreatePeerForElement(this);
        peer?.RaiseNotificationEvent(AutomationNotificationKind.ActionCompleted,
            AutomationNotificationProcessing.ImportantMostRecent, message, "LumeristStayInTouch");
    }
}
