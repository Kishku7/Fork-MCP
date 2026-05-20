using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Fork.Logic.Manager;
using Fork.Logic.Model;
using Fork.ViewModel;

namespace Fork.View.Xaml2.Pages.Server;

/// <summary>
///     Interaktionslogik für ConsolePage.xaml
/// </summary>
public partial class ConsolePage : Page
{
    private readonly ServerViewModel viewModel;

    public ConsolePage(ServerViewModel viewModel)
    {
        InitializeComponent();
        this.viewModel = viewModel;
        DataContext = this.viewModel;

        PlayerToWhitelist.KeyDown += HandleKeyDownText;

        KeyDown += OnPageKeyDown;
    }

    private void Player_Ban(object sender, RoutedEventArgs e)
    {
        MenuItem item = sender as MenuItem;
        PlayerManager.Instance.BanPlayer(viewModel, item?.CommandParameter as string);
    }

    private void Player_OP(object sender, RoutedEventArgs e)
    {
        MenuItem item = sender as MenuItem;
        PlayerManager.Instance.OpPlayer(viewModel, item?.CommandParameter as string);
    }

    private void Player_Kick(object sender, RoutedEventArgs e)
    {
        MenuItem item = sender as MenuItem;
        PlayerManager.Instance.KickPlayer(viewModel, item?.CommandParameter as string);
    }

    private void Player_Deop(object sender, RoutedEventArgs e)
    {
        MenuItem item = sender as MenuItem;
        PlayerManager.Instance.DeopPlayer(viewModel, item?.CommandParameter as string);
    }

    private void Player_Unwhitelist(object sender, RoutedEventArgs e)
    {
        MenuItem item = sender as MenuItem;
        PlayerManager.Instance.UnWhitelistPlayer(viewModel, item?.CommandParameter as string);
    }

    private void AddToWhiteList_Click(object sender, RoutedEventArgs e)
    {
        AddWhitelistPanel.Visibility = Visibility.Visible;
        PlayerToWhitelist.Focus();
    }

    private void WhitelistAddConfirm_Click(object sender, RoutedEventArgs e)
    {
        AddWhitelistPanel.Visibility = Visibility.Collapsed;
        PlayerManager.Instance.WhitelistPlayer(viewModel, PlayerToWhitelist.Text);
        PlayerToWhitelist.Text = "";
    }

    private void Player_Unban(object sender, RoutedEventArgs e)
    {
        MenuItem item = sender as MenuItem;
        PlayerManager.Instance.UnBanPlayer(viewModel, item?.CommandParameter as string);
    }

    private void HandleKeyDownText(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            WhitelistAddConfirm_Click(sender, e);
        }
    }

    private void OnPageKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F && e.KeyboardDevice.Modifiers == ModifierKeys.Control)
        {
            ToggleConsoleSearch();
        }
        else if (e.Key == Key.Delete && e.KeyboardDevice.Modifiers == ModifierKeys.Control)
        {
            viewModel.ClearConsole();
        }
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        string query = SearchBox.Text;
        viewModel.ApplySearchQueryToConsole(query);
    }

    private void SearchMenuItem_Click(object sender, RoutedEventArgs e)
    {
        ToggleConsoleSearch();
    }

    private void ClearMenuItem_Click(object sender, RoutedEventArgs e)
    {
        viewModel.ClearConsole();
    }

    private void ToggleConsoleSearch()
    {
        SearchBox.Visibility = SearchBox.Visibility == Visibility.Collapsed ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void ButtonStartStop_Click(object sender, RoutedEventArgs e)
    {
        StartStopButton.IsEnabled = false;
        if (viewModel.CurrentStatus == ServerStatus.STOPPED)
        {
            await ServerManager.Instance.StartServerAsync(viewModel);
        }
        else if (viewModel.CurrentStatus == ServerStatus.STARTING)
        {
            await Task.Run(() => ServerManager.Instance.KillEntity(viewModel));
        }
        else if (viewModel.CurrentStatus == ServerStatus.RUNNING)
        {
            await Task.Run(() => ServerManager.Instance.StopServer(viewModel));
        }
        StartStopButton.IsEnabled = true;
    }

    private void RestartButton_Click(object sender, RoutedEventArgs e)
    {
        ServerManager.Instance.RestartServerAsync(viewModel);
    }

    #region autoscrolling

    /// <summary>
    ///     Automatically scrolls the scrollviewer
    /// </summary>
    private bool AutoScroll = true;

    private void ScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        // ScrollChanged is registered as an attached event on the ListBox so it catches
        // events bubbling up from the ScrollViewer inside the ConsoleListBox template.
        // e.OriginalSource is always the ScrollViewer that actually fired the event.
        if (e.OriginalSource is not ScrollViewer scrollViewer) return;

        // User scroll event : set or unset auto-scroll mode
        if (e.ExtentHeightChange == 0)
        {
            // Content unchanged — user scrolled manually
            if (scrollViewer.VerticalOffset == scrollViewer.ScrollableHeight)
            {
                // Scroll bar is at bottom — re-enable auto-scroll
                AutoScroll = true;
            }
            else
            {
                // Scroll bar is not at bottom — user scrolled up, disable auto-scroll
                AutoScroll = false;
            }
        }

        // Content scroll event : auto-scroll if enabled
        if (AutoScroll && e.ExtentHeightChange != 0)
        {
            // New content arrived and auto-scroll is on — scroll to bottom
            scrollViewer.ScrollToVerticalOffset(scrollViewer.ExtentHeight);
        }
    }

    #endregion autoscrolling
}