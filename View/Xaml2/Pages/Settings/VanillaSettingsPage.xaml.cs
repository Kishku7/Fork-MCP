using System.IO;
using System.Threading.Tasks;
using System.Windows.Controls;
using Fork.Logic.Model.Settings;
using Fork.ViewModel;

namespace Fork.View.Xaml2.Pages.Settings;

public partial class VanillaSettingsPage : Page, ISettingsPage
{
    private readonly ServerViewModel serverViewModel;
    private SettingsViewModel viewModel;

    public VanillaSettingsPage(SettingsViewModel viewModel, SettingsFile settingsFile)
    {
        InitializeComponent();
        this.viewModel = viewModel;
        SettingsFile = settingsFile;
        serverViewModel = viewModel.EntityViewModel as ServerViewModel;
        DataContext = serverViewModel;
    }

    public SettingsFile SettingsFile { get; set; }
    public string FileName => Path.GetFileNameWithoutExtension(SettingsFile.FileInfo.FullName);
    public string FileExtension => Path.GetExtension(SettingsFile.FileInfo.FullName);

    public async Task SaveSettings()
    {
        await serverViewModel.SaveProperties();
    }
}
