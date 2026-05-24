using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Media;
using Fork.Logic.Logging;
using Fork.Logic.Manager;
using Fork.Logic.Persistence;
using Fork.Logic.Service;
using Fork.logic.Utils;
using Fork.ViewModel;
using Application = System.Windows.Application;
using CheckBox = System.Windows.Controls.CheckBox;
using Clipboard = System.Windows.Clipboard;

namespace Fork.View.Xaml2.Pages;

public partial class AppSettingsPage : Page
{
    private readonly AppSettingsViewModel viewModel;

    public AppSettingsPage(AppSettingsViewModel viewModel)
    {
        InitializeComponent();
        this.viewModel = viewModel;
        DataContext = this.viewModel;
        UpdateJavaDiscoveryStatus();
        // Refresh status when discovery completes in background
        JavaDiscoveryService.Instance.KnownJavaVersions.CollectionChanged += (_, _) => UpdateJavaDiscoveryStatus();
        foreach (var k in JavaDiscoveryService.Instance.KnownJavaVersions)
            k.PropertyChanged += (_, _) => UpdateJavaDiscoveryStatus();
    }

    // ── Server directory ─────────────────────────────────────────────────────

    private void OpenForkServerDir_Click(object sender, RoutedEventArgs e)
    {
        Process.Start("explorer.exe", "-p, " + ForkServerPath.Text);
    }

    private async void ApplyNewServerDir_Click(object sender, RoutedEventArgs e)
    {
        bool result;
        try
        {
            result = await ServerManager.Instance.MoveEntitiesAsync(ForkServerPath.Text);
        }
        catch (Exception ex)
        {
            ServerDirChangeErrorGrid.Visibility = Visibility.Visible;
            ErrorMsgBox.Text = ex is UnauthorizedAccessException
                ? $"Fork can't access \"{ForkServerPath.Text}\"! Please try another directory."
                : ex.Message;
            ErrorLogger.Append(ex);
            return;
        }

        if (!result)
        {
            ServerDirChangeErrorGrid.Visibility = Visibility.Visible;
            ErrorMsgBox.Text = "Unknown error — please report to a Fork developer.";
            return;
        }

        ServerDirChangeErrorGrid.Visibility = Visibility.Collapsed;
        ServerDirChangedGrid.Visibility = Visibility.Collapsed;
        ResetServerDirButton.Visibility = Visibility.Collapsed;
        serverPathBgr.Background = (Brush)Application.Current.FindResource("buttonBgrDefault");
    }

    private void ServerDirPath_MouseDown(object sender, MouseButtonEventArgs e)
    {
        FolderBrowserDialog fbd = new() { SelectedPath = ForkServerPath.Text };
        if (fbd.ShowDialog() == DialogResult.OK && !string.IsNullOrWhiteSpace(fbd.SelectedPath))
        {
            ForkServerPath.Text = fbd.SelectedPath;
            bool changed = !ForkServerPath.Text.Equals(viewModel.AppSettings.ServerPath);
            ServerDirChangedGrid.Visibility = changed ? Visibility.Visible : Visibility.Collapsed;
            ResetServerDirButton.Visibility = changed ? Visibility.Visible : Visibility.Collapsed;
            serverPathBgr.Background = (Brush)Application.Current.FindResource(
                changed ? "tabSelected" : "textBackground");
        }
    }

    private void ResetServerDir_Click(object sender, RoutedEventArgs e)
    {
        ForkServerPath.Text = viewModel.AppSettings.ServerPath;
        ServerDirChangeErrorGrid.Visibility = Visibility.Collapsed;
        ServerDirChangedGrid.Visibility = Visibility.Collapsed;
        ResetServerDirButton.Visibility = Visibility.Collapsed;
        serverPathBgr.Background = (Brush)Application.Current.FindResource("buttonBgrDefault");
    }

    // ── Java installations ────────────────────────────────────────────────────

    private void JavaBaseDir_MouseDown(object sender, MouseButtonEventArgs e)
    {
        FolderBrowserDialog fbd = new()
        {
            Description = "Select the root folder containing Java installations",
            SelectedPath = viewModel.AppSettings.JavaBaseDirectory ?? @"C:\Program Files"
        };
        if (fbd.ShowDialog() == DialogResult.OK && !string.IsNullOrWhiteSpace(fbd.SelectedPath))
        {
            viewModel.AppSettings.JavaBaseDirectory = fbd.SelectedPath;
            ForkJavaBaseDir.Text = fbd.SelectedPath;
        }
    }

    private void BrowseJavaBaseDir_Click(object sender, RoutedEventArgs e)
    {
        FolderBrowserDialog fbd = new()
        {
            Description = "Select the root folder containing Java installations",
            SelectedPath = viewModel.AppSettings.JavaBaseDirectory ?? @"C:\Program Files"
        };
        if (fbd.ShowDialog() == DialogResult.OK && !string.IsNullOrWhiteSpace(fbd.SelectedPath))
        {
            viewModel.AppSettings.JavaBaseDirectory = fbd.SelectedPath;
            ForkJavaBaseDir.Text = fbd.SelectedPath;
        }
    }

    private void ClearJavaBaseDir_Click(object sender, RoutedEventArgs e)
    {
        viewModel.AppSettings.JavaBaseDirectory = null;
        ForkJavaBaseDir.Text = "";
    }

    private void ReloadJavaVersions_Click(object sender, RoutedEventArgs e)
    {
        JavaDiscoveryStatus.Text = "Scanning…";
        Task.Run(() =>
        {
            JavaDiscoveryService.Instance.Reload();
            Application.Current.Dispatcher.Invoke(UpdateJavaDiscoveryStatus);
        });
    }

    private void UpdateJavaDiscoveryStatus()
    {
        var available = JavaDiscoveryService.Instance.KnownJavaVersions
            .Where(k => k.Major > 0 && k.IsAvailable)
            .Select(k => $"Java {k.Major} ({k.Best?.FullVersion})")
            .ToList();

        JavaDiscoveryStatus.Text = available.Count > 0
            ? "Found: " + string.Join(" · ", available)
            : "No Java installations found — check directories above or install Java.";
    }

    // ── Discord ───────────────────────────────────────────────────────────────

    private void BecomeSupporter_Click(object sender, RoutedEventArgs e)
        => ForkUtils.OpenUrl("https://www.ko-fi.com/forkgg");

    private void InviteDiscordBot_Click(object sender, MouseButtonEventArgs e)
        => ForkUtils.OpenUrl("https://bot.fork.gg");

    private async void CopyDiscordToken_Click(object sender, MouseButtonEventArgs e)
    {
        try
        {
            Clipboard.SetText(AppSettingsSerializer.Instance.AppSettings.DiscordBotToken);
            CopiedIndicator.Visibility = Visibility.Visible;
            await Task.Delay(1000);
            CopiedIndicator.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            ErrorLogger.Append(ex);
        }
    }

    private void EnableDisableDiscordBot_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is CheckBox checkBox && checkBox.IsChecked.HasValue)
        {
            if (checkBox.IsChecked.Value) ApplicationManager.StopDiscordWebSocket();
            else                          ApplicationManager.StartDiscordWebSocket();
        }
    }
}
