using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Windows.Input;
using UserControl = System.Windows.Controls.UserControl;
using Fork.Logic.Persistence;
using Path = System.IO.Path;

namespace Fork.View.Xaml2.Pages.Settings;

public partial class JavaNetworkingSection : UserControl
{
    public JavaNetworkingSection()
    {
        InitializeComponent();
    }

    private void JavaPath_MouseDown(object sender, MouseButtonEventArgs e)
    {
        OpenFileDialog ofd = new()
            { Multiselect = false, Filter = "Java executable|java.exe", Title = "Select a java.exe" };
        if (!ServerJavaPath.Text.Equals("java.exe") &&
            new DirectoryInfo(ServerJavaPath.Text.Replace(@"\java.exe", "")).Exists)
        {
            ofd.InitialDirectory = ServerJavaPath.Text.Replace(@"\java.exe", "");
        }
        else
        {
            ofd.InitialDirectory = @"C:\Program Files";
        }

        DialogResult result = ofd.ShowDialog();
        if (result == DialogResult.OK && !string.IsNullOrWhiteSpace(ofd.FileName))
        {
            ServerJavaPath.Text = ofd.FileName;
        }
    }

    private void DefaultJavaDirReset_Click(object sender, RoutedEventArgs e)
    {
        ServerJavaPath.Text = AppSettingsSerializer.Instance.AppSettings.DefaultJavaPath;
    }
}
