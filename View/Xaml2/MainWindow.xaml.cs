using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Fork.Logic.Manager;
using Fork.Logic.Model;
using Fork.logic.Utils;
using Fork.ViewModel;
using Application = System.Windows.Application;

namespace Fork.View.Xaml2;

public partial class MainWindow : Window
{
    private bool createOpen;
    private bool importOpen;
    private object lastSelected;
    private readonly MainViewModel viewModel;

    public MainWindow()
    {
        InitializeComponent();
        InitializeSystemTrayNotifyIcon();
        StateChanged += OnMainWindowStateChange;
        Closing += OnMainWindowClose;
        viewModel = ApplicationManager.Instance.MainViewModel;
        DataContext = viewModel;

        // Surface the window to the foreground on launch. When Fork is started by a
        // scheduled task it would otherwise come up unfocused / behind, so the user sees
        // no window; bring it to the front here.
        Loaded += (_, _) =>
        {
            if (WindowState == WindowState.Minimized)
                WindowState = WindowState.Normal;
            Activate();
            Topmost = true;
            Topmost = false;
        };
    }

    private void OpenAppSettings_Click(object sender, RoutedEventArgs e)
    {
        OpenAppSettings();
    }

    private void CreateServer_Click(object sender, RoutedEventArgs e)
    {
        if (CreatePage.Visibility == Visibility.Hidden)
        {
            OpenCreateServer();
        }
        else
        {
            CloseCreateServer();
        }
    }

    private void ImportServer_Click(object sender, RoutedEventArgs e)
    {
        if (ImportPage.Visibility == Visibility.Hidden)
        {
            OpenImportServer();
        }
        else
        {
            CloseImportServer();
        }
    }

    private void DeleteOpen_Click(object sender, RoutedEventArgs e)
    {
        if (viewModel.SelectedEntity is ServerViewModel)
        {
            DeleteServerOverlay.Visibility = Visibility.Visible;
        }
        else
        {
            DeleteNetworkOverlay.Visibility = Visibility.Visible;
        }
    }

    private void RenameOpen_Click(object sender, RoutedEventArgs e)
    {
        RenameServerOverlay.InputText = viewModel.SelectedEntity.Name;
        RenameNetworkOverlay.InputText = viewModel.SelectedEntity.Name;
        if (viewModel.SelectedEntity is ServerViewModel)
        {
            RenameServerOverlay.Visibility = Visibility.Visible;
        }
        else
        {
            RenameNetworkOverlay.Visibility = Visibility.Visible;
        }
    }

    private void CloneOpen_Click(object sender, RoutedEventArgs e)
    {
        if (viewModel.SelectedEntity is ServerViewModel serverViewModel)
        {
            if (serverViewModel.CurrentStatus == ServerStatus.STOPPED)
            {
                CloneServer_Confirmed(sender, e);
                return;
            }

            CloneServerOverlay.Visibility = Visibility.Visible;
        }
        else if (viewModel.SelectedEntity is NetworkViewModel networkViewModel)
        {
            if (networkViewModel.CurrentStatus == ServerStatus.STOPPED)
            {
                CloneNetwork_Confirmed(sender, e);
                return;
            }

            CloneNetworkOverlay.Visibility = Visibility.Visible;
        }
    }

    private void DiscordOpen_Click(object sender, RoutedEventArgs e)
    {
        string url = "https://discord.fork.gg";
        ForkUtils.OpenUrl(url);
    }

    private void KoFiOpen_Click(object sender, RoutedEventArgs e)
    {
        string url = "https://ko-fi.com/forkgg";
        ForkUtils.OpenUrl(url);
    }

    private void OpenCreateServer()
    {
        if (AppSettingsPage.Visibility == Visibility.Visible)
        {
            CloseAppSettings();
        }

        lastSelected = ServerList.SelectedItem;
        ServerList.UnselectAll();

        //Open createServer Frame
        ServerPage.Visibility = Visibility.Hidden;
        CreatePage.Visibility = Visibility.Visible;

        //Change Buttons
        DeleteButton.IsEnabled = false;
        ImportButton.IsEnabled = false;
        CreateButton.Content = "Cancel";
        CreateButton.Style = (Style)Application.Current.FindResource("RoundedTextButtonRed");
        createOpen = true;
    }

    private void CloseCreateServer()
    {
        if (!createOpen)
        {
            return;
        }

        createOpen = false;

        if (ServerList.SelectedItems.Count == 0)
        {
            ServerList.SelectedItem = lastSelected;
        }

        //Close createServer Frame
        ServerPage.Visibility = Visibility.Visible;
        CreatePage.Visibility = Visibility.Hidden;

        //Change Buttons
        DeleteButton.IsEnabled = true;
        ImportButton.IsEnabled = true;
        CreateButton.Content = "New Server";
        CreateButton.Style = (Style)Application.Current.FindResource("RoundedTextButtonGreen");
    }

    private void OpenImportServer()
    {
        if (AppSettingsPage.Visibility == Visibility.Visible)
        {
            CloseAppSettings();
        }

        lastSelected = ServerList.SelectedItem;
        ServerList.UnselectAll();

        //Open importServer Frame
        ServerPage.Visibility = Visibility.Hidden;
        ImportPage.Visibility = Visibility.Visible;

        //Change Buttons
        DeleteButton.IsEnabled = false;
        CreateButton.IsEnabled = false;
        ImportButton.Content = "Cancel";
        ImportButton.Style = (Style)Application.Current.FindResource("RoundedTextButtonRed");
        importOpen = true;
    }

    private void CloseImportServer()
    {
        //Check if window is already closed
        if (!importOpen)
        {
            return;
        }

        importOpen = false;

        if (ServerList.SelectedItems.Count == 0)
        {
            ServerList.SelectedItem = lastSelected;
        }

        //Close importServer Frame
        ServerPage.Visibility = Visibility.Visible;
        ImportPage.Visibility = Visibility.Hidden;

        //Change Buttons
        DeleteButton.IsEnabled = true;
        CreateButton.IsEnabled = true;
        ImportButton.Content = "Import Server";
        ImportButton.Style = (Style)Application.Current.FindResource("RoundedTextButton");
    }

    private void OpenAppSettings()
    {
        CloseNonEntityPages();
        lastSelected = ServerList.SelectedItem;
        ServerList.UnselectAll();

        //TODO make loading icon or smth
        viewModel.AppSettingsViewModel.OpenAppSettingsPage();

        //Open importServer Frame
        ServerPage.Visibility = Visibility.Hidden;
        AppSettingsPage.Visibility = Visibility.Visible;

        //Change Buttons
        AppSettingsButton.IsEnabled = false;
    }

    private void CloseAppSettings()
    {
        //Close importServer Frame
        ServerPage.Visibility = Visibility.Visible;
        AppSettingsPage.Visibility = Visibility.Hidden;

        //Save settings:
        viewModel.AppSettingsViewModel.CloseAppSettingsPage();
        viewModel.UpdateInstalledJavaVersion();

        if (ServerList.SelectedItems.Count == 0)
        {
            ServerList.SelectedItem = lastSelected;
        }

        //Change Buttons
        AppSettingsButton.IsEnabled = true;
    }

    private void ServerList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.RemovedItems.Count > 0 && e.RemovedItems[0] is EntityViewModel entityViewModel)
        {
            entityViewModel.SaveSettings();
        }

        CloseNonEntityPages();
    }

    private void CloseNonEntityPages()
    {
        if (CreatePage.Visibility == Visibility.Visible)
        {
            CloseCreateServer();
        }

        if (ImportPage.Visibility == Visibility.Visible)
        {
            CloseImportServer();
        }

        if (AppSettingsPage.Visibility == Visibility.Visible)
        {
            CloseAppSettings();
        }
    }

    // ConfirmationOverlay cancel — dismisses whichever overlay is visible
    private void Overlay_Cancelled(object sender, RoutedEventArgs e)
    {
        DeleteServerOverlay.Visibility = Visibility.Collapsed;
        DeleteNetworkOverlay.Visibility = Visibility.Collapsed;
        RenameServerOverlay.Visibility = Visibility.Collapsed;
        RenameNetworkOverlay.Visibility = Visibility.Collapsed;
        CloneServerOverlay.Visibility = Visibility.Collapsed;
        CloneNetworkOverlay.Visibility = Visibility.Collapsed;
    }

    private async void RenameServer_Confirmed(object sender, RoutedEventArgs e)
    {
        if (viewModel.SelectedEntity is not ServerViewModel serverViewModel)
        {
            return;
        }

        RenameServerOverlay.IsConfirmEnabled = false;
        string newName = RenameServerOverlay.InputText;
        //TODO name verifier instead of this
        if (newName.Equals(""))
        {
            newName = "ForkEntity";
        }

        bool success = await ServerManager.Instance.RenameServerAsync(serverViewModel, newName);
        if (success)
        {
            Console.WriteLine("Successfully renamed Server to: " + newName);
        }
        else
        {
            //TODO Show error
            Console.WriteLine("Error renaming Server: " + serverViewModel.Name);
        }

        RenameServerOverlay.IsConfirmEnabled = true;
        RenameServerOverlay.Visibility = Visibility.Collapsed;
    }

    private async void RenameNetwork_Confirmed(object sender, RoutedEventArgs e)
    {
        if (viewModel.SelectedEntity is not NetworkViewModel networkViewModel)
        {
            return;
        }

        RenameNetworkOverlay.IsConfirmEnabled = false;
        string newName = RenameNetworkOverlay.InputText;
        //TODO name verifier instead of this
        if (newName.Equals(""))
        {
            newName = "ForkEntity";
        }

        bool success = await ServerManager.Instance.RenameNetworkAsync(networkViewModel, newName);
        if (success)
        {
            Console.WriteLine("Successfully renamed Network to: " + newName);
        }
        else
        {
            //TODO Show error
            Console.WriteLine("Error renaming Network: " + networkViewModel.Name);
        }

        RenameNetworkOverlay.IsConfirmEnabled = true;
        RenameNetworkOverlay.Visibility = Visibility.Collapsed;
    }

    private async void CloneServer_Confirmed(object sender, RoutedEventArgs e)
    {
        if (viewModel.SelectedEntity is not ServerViewModel serverViewModel)
        {
            return;
        }

        CloneServerOverlay.IsConfirmEnabled = false;

        bool success = await ServerManager.Instance.CloneServerAsync(serverViewModel);
        if (success)
        {
            Console.WriteLine("Successfully cloned Server:" + serverViewModel.Name);
        }
        else
        {
            //TODO Show error
            Console.WriteLine("Error cloning Server: " + serverViewModel.Name);
        }

        CloneServerOverlay.IsConfirmEnabled = true;
        CloneServerOverlay.Visibility = Visibility.Collapsed;
    }

    private async void CloneNetwork_Confirmed(object sender, RoutedEventArgs e)
    {
        if (viewModel.SelectedEntity is not NetworkViewModel networkViewModel)
        {
            return;
        }

        CloneNetworkOverlay.IsConfirmEnabled = false;

        bool success = await ServerManager.Instance.CloneNetworkAsync(networkViewModel);
        if (success)
        {
            Console.WriteLine("Successfully renamed Network to: " + networkViewModel.Name);
        }
        else
        {
            //TODO Show error
            Console.WriteLine("Error renaming Network: " + networkViewModel.Name);
        }

        CloneNetworkOverlay.IsConfirmEnabled = true;
        CloneNetworkOverlay.Visibility = Visibility.Collapsed;
    }

    private async void DeleteServer_Confirmed(object sender, RoutedEventArgs e)
    {
        if (viewModel.SelectedEntity is not ServerViewModel serverToDelete)
        {
            return;
        }

        DeleteServerOverlay.IsConfirmEnabled = false;

        bool success = await ServerManager.Instance.DeleteServerAsync(serverToDelete);
        if (!success)
        {
            Console.WriteLine("Problem while deleting " + serverToDelete);
        }
        else
        {
            Console.WriteLine("Successfully deleted server " + serverToDelete);
            Application.Current.Dispatcher?.Invoke(() => viewModel.Entities.Remove(serverToDelete),
                DispatcherPriority.Background); //This shouldn't be here
            ServerList.SelectedIndex = 0;
        }

        DeleteServerOverlay.IsConfirmEnabled = true;
        DeleteServerOverlay.Visibility = Visibility.Collapsed;
    }

    private async void DeleteNetwork_Confirmed(object sender, RoutedEventArgs e)
    {
        if (viewModel.SelectedEntity is not NetworkViewModel networkToDelete)
        {
            return;
        }

        DeleteNetworkOverlay.IsConfirmEnabled = false;

        bool success = await ServerManager.Instance.DeleteNetworkAsync(networkToDelete);
        if (!success)
        {
            Console.WriteLine("Problem while deleting " + networkToDelete.Network);
        }
        else
        {
            Console.WriteLine("Successfully deleted network " + networkToDelete.Network);
            Application.Current.Dispatcher?.Invoke(() => viewModel.Entities.Remove(networkToDelete),
                DispatcherPriority.Background); //This shouldn't be here
            ServerList.SelectedIndex = 0;
        }

        DeleteNetworkOverlay.IsConfirmEnabled = true;
        DeleteNetworkOverlay.Visibility = Visibility.Collapsed;
    }

    private void EntityMouseUp(object sender, MouseButtonEventArgs e)
    {
        FrameworkElement s = sender as FrameworkElement;
        viewModel.SelectedEntity = s.DataContext as EntityViewModel;
    }

    private void EntityMouseDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
    }

    private void NewVersion_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        string url = viewModel.LatestForkVersion.URL;
        ForkUtils.OpenUrl(url);
    }

    private void TextBlock_Link_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is System.Windows.Controls.TextBlock textBlock)
        {
            string url = textBlock.Text;
            ForkUtils.OpenUrl(url);
        }
    }
}
