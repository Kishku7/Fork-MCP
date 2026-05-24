using System.Collections.ObjectModel;
using System.Windows.Controls;
using Fork.Logic.Model;
using Fork.Logic.Service;

namespace Fork.View.Xaml2.Pages.Settings;

public partial class JavaNetworkingSection : UserControl
{
    public JavaNetworkingSection()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Exposes the discovery service's known-major list to XAML via ElementName binding.
    /// The ComboBox binds ItemsSource="{Binding KnownJavaVersions, ElementName=ThisControl}".
    /// </summary>
    public ObservableCollection<KnownJavaMajor> KnownJavaVersions
        => JavaDiscoveryService.Instance.KnownJavaVersions;
}
