using System.Windows.Controls;
using System.Windows.Input;
using Fork.logic.Utils;

namespace Fork.View.Xaml2.Pages.Settings;

public partial class ServerAppearanceSection : UserControl
{
    public ServerAppearanceSection()
    {
        InitializeComponent();
    }

    private void OpenMOTDGenerator_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        ForkUtils.OpenUrl("https://minecraft.tools/motd.php");
    }
}
