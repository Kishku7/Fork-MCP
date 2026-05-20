using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using Fork.ViewModel;

namespace Fork.View.Xaml2.Pages.Server;

/// <summary>
///     Interaktionslogik für ServerPage.xaml
/// </summary>
public partial class ServerPage : Page
{
    private readonly HashSet<Frame> subPages = new();
    private readonly ServerViewModel viewModel;

    public ServerPage(ServerViewModel viewModel)
    {
        InitializeComponent();
        this.viewModel = viewModel;
        DataContext = this.viewModel;

        subPages.Add(terminalPage);
        subPages.Add(settingsPage);
        subPages.Add(worldsPage);
        subPages.Add(pluginsPage);
    }

    public void OpenTerminal()
    {
        TerminalTab.IsChecked = true;
        SelectTerminal(this, new RoutedEventArgs());
    }

    private void SelectTerminal(object sender, RoutedEventArgs e)
    {
        HideAllPages();
        terminalPage.Visibility = Visibility.Visible;
    }

    private void SelectSettings(object sender, RoutedEventArgs e)
    {
        HideAllPages();
        settingsPage.Visibility = Visibility.Visible;
    }

    private void SelectWorlds(object sender, RoutedEventArgs e)
    {
        HideAllPages();
        worldsPage.Visibility = Visibility.Visible;
    }

    private void SelectPlugins(object sender, RoutedEventArgs e)
    {
        HideAllPages();
        pluginsPage.Visibility = Visibility.Visible;
    }

    private void HideAllPages()
    {
        //Save settings, if settings is closed
        if (settingsPage.Visibility == Visibility.Visible)
        {
            viewModel.SaveSettings();
        }

        foreach (Frame frame in subPages) frame.Visibility = Visibility.Hidden;
    }

}