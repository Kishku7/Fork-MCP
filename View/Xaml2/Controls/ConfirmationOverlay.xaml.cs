using System.Windows;
using System.Windows.Controls;

namespace Fork.View.Xaml2.Controls;

public partial class ConfirmationOverlay : UserControl
{
    // OverlayTitle
    public static readonly DependencyProperty OverlayTitleProperty =
        DependencyProperty.Register(nameof(OverlayTitle), typeof(string), typeof(ConfirmationOverlay),
            new PropertyMetadata(string.Empty));

    public string OverlayTitle
    {
        get => (string)GetValue(OverlayTitleProperty);
        set => SetValue(OverlayTitleProperty, value);
    }

    // WarningText
    public static readonly DependencyProperty WarningTextProperty =
        DependencyProperty.Register(nameof(WarningText), typeof(string), typeof(ConfirmationOverlay),
            new PropertyMetadata(string.Empty));

    public string WarningText
    {
        get => (string)GetValue(WarningTextProperty);
        set => SetValue(WarningTextProperty, value);
    }

    // ConfirmButtonText
    public static readonly DependencyProperty ConfirmButtonTextProperty =
        DependencyProperty.Register(nameof(ConfirmButtonText), typeof(string), typeof(ConfirmationOverlay),
            new PropertyMetadata("Confirm"));

    public string ConfirmButtonText
    {
        get => (string)GetValue(ConfirmButtonTextProperty);
        set => SetValue(ConfirmButtonTextProperty, value);
    }

    // IsDestructive
    public static readonly DependencyProperty IsDestructiveProperty =
        DependencyProperty.Register(nameof(IsDestructive), typeof(bool), typeof(ConfirmationOverlay),
            new PropertyMetadata(false));

    public bool IsDestructive
    {
        get => (bool)GetValue(IsDestructiveProperty);
        set => SetValue(IsDestructiveProperty, value);
    }

    // ShowInput
    public static readonly DependencyProperty ShowInputProperty =
        DependencyProperty.Register(nameof(ShowInput), typeof(bool), typeof(ConfirmationOverlay),
            new PropertyMetadata(false));

    public bool ShowInput
    {
        get => (bool)GetValue(ShowInputProperty);
        set => SetValue(ShowInputProperty, value);
    }

    // InputText
    public static readonly DependencyProperty InputTextProperty =
        DependencyProperty.Register(nameof(InputText), typeof(string), typeof(ConfirmationOverlay),
            new PropertyMetadata(string.Empty));

    public string InputText
    {
        get => (string)GetValue(InputTextProperty);
        set => SetValue(InputTextProperty, value);
    }

    // IsConfirmEnabled
    public static readonly DependencyProperty IsConfirmEnabledProperty =
        DependencyProperty.Register(nameof(IsConfirmEnabled), typeof(bool), typeof(ConfirmationOverlay),
            new PropertyMetadata(true));

    public bool IsConfirmEnabled
    {
        get => (bool)GetValue(IsConfirmEnabledProperty);
        set => SetValue(IsConfirmEnabledProperty, value);
    }

    // ConfirmClicked event
    public static readonly RoutedEvent ConfirmClickedEvent =
        EventManager.RegisterRoutedEvent(nameof(ConfirmClicked), RoutingStrategy.Bubble,
            typeof(RoutedEventHandler), typeof(ConfirmationOverlay));

    public event RoutedEventHandler ConfirmClicked
    {
        add => AddHandler(ConfirmClickedEvent, value);
        remove => RemoveHandler(ConfirmClickedEvent, value);
    }

    // CancelClicked event
    public static readonly RoutedEvent CancelClickedEvent =
        EventManager.RegisterRoutedEvent(nameof(CancelClicked), RoutingStrategy.Bubble,
            typeof(RoutedEventHandler), typeof(ConfirmationOverlay));

    public event RoutedEventHandler CancelClicked
    {
        add => AddHandler(CancelClickedEvent, value);
        remove => RemoveHandler(CancelClickedEvent, value);
    }

    public ConfirmationOverlay()
    {
        InitializeComponent();
    }

    private void ConfirmBtn_Click(object sender, RoutedEventArgs e)
    {
        RaiseEvent(new RoutedEventArgs(ConfirmClickedEvent));
    }

    private void CancelBtn_Click(object sender, RoutedEventArgs e)
    {
        RaiseEvent(new RoutedEventArgs(CancelClickedEvent));
    }
}
