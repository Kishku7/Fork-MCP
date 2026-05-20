using System.Windows;
using System.Windows.Controls;

namespace Fork.View.Xaml2.Controls;

public partial class EmptyStateView : UserControl
{
    public static readonly RoutedEvent CreateServerRequestedEvent =
        EventManager.RegisterRoutedEvent(nameof(CreateServerRequested), RoutingStrategy.Bubble,
            typeof(RoutedEventHandler), typeof(EmptyStateView));

    public static readonly RoutedEvent ImportServerRequestedEvent =
        EventManager.RegisterRoutedEvent(nameof(ImportServerRequested), RoutingStrategy.Bubble,
            typeof(RoutedEventHandler), typeof(EmptyStateView));

    public event RoutedEventHandler CreateServerRequested
    {
        add => AddHandler(CreateServerRequestedEvent, value);
        remove => RemoveHandler(CreateServerRequestedEvent, value);
    }

    public event RoutedEventHandler ImportServerRequested
    {
        add => AddHandler(ImportServerRequestedEvent, value);
        remove => RemoveHandler(ImportServerRequestedEvent, value);
    }

    public EmptyStateView()
    {
        InitializeComponent();
    }

    private void CreateServer_Click(object sender, RoutedEventArgs e)
    {
        RaiseEvent(new RoutedEventArgs(CreateServerRequestedEvent));
    }

    private void ImportServer_Click(object sender, RoutedEventArgs e)
    {
        RaiseEvent(new RoutedEventArgs(ImportServerRequestedEvent));
    }
}
