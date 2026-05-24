using System;
using System.Threading.Tasks;
using System.Windows.Controls;
using Fork.Logic.Model.Settings;
using Fork.Logic.Service;
using Fork.ViewModel;

namespace Fork.View.Xaml2.Pages.Settings;

public partial class ForkNetworkSettingsPage : Page, ISettingsPage
{
    private readonly NetworkViewModel networkViewModel;
    private SettingsViewModel viewModel;

    public ForkNetworkSettingsPage(SettingsViewModel viewModel)
    {
        InitializeComponent();
        SettingsFile = new SettingsFile(FileName);
        this.viewModel = viewModel;
        networkViewModel = viewModel.EntityViewModel as NetworkViewModel;

        DataContext = networkViewModel ?? throw new Exception("ForkNetworkSettings was created for Server");

        // Wire up the Java version ComboBox from the discovery service
        JavaVersionCombo.ItemsSource = JavaDiscoveryService.Instance.KnownJavaVersions;
    }

    public SettingsFile SettingsFile { get; set; }
    public string FileName => "Settings";
    public string FileExtension => "";

    public async Task SaveSettings()
    {
        // settings auto-save via binding
    }
}
