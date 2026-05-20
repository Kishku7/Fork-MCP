using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Fork.logic.Utils;
using Fork.ViewModel;

namespace Fork.View.Xaml2.Controls;

public partial class JavaWarningOverlay : UserControl
{
    public JavaWarningOverlay()
    {
        InitializeComponent();
    }

    private MainViewModel? ViewModel => DataContext as MainViewModel;

    private void Ignore_Click(object sender, RoutedEventArgs e)
    {
        ViewModel?.UpdateInstalledJavaVersion(true);
    }

    private void CheckAgain_Click(object sender, RoutedEventArgs e)
    {
        ViewModel?.UpdateInstalledJavaVersion();
    }

    private void TextBlock_Link_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is TextBlock textBlock)
        {
            string url = textBlock.Text;
            ForkUtils.OpenUrl(url);
        }
    }
}
