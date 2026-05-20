using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows;
using System.Windows.Forms;
using Fork.Logic.Model;
using Fork.Logic.Persistence;
using Application = System.Windows.Application;

namespace Fork.View.Xaml2;

public partial class MainWindow : Window
{
    private NotifyIcon systemTrayIcon;

    private void InitializeSystemTrayNotifyIcon()
    {
        systemTrayIcon = new NotifyIcon();
        systemTrayIcon.Icon =
            new Icon(Application.GetResourceStream(new Uri("pack://application:,,,/icon.ico")).Stream);
        systemTrayIcon.Visible = false;
        systemTrayIcon.DoubleClick += delegate { ShowApplication(); };
        systemTrayIcon.Text = "Fork";

        ContextMenuStrip contextMenuStrip = new();
        contextMenuStrip.Items.Add("Open", null, delegate { ShowApplication(); });
        contextMenuStrip.Items.Add("Close", null, delegate { Application.Current.Shutdown(); });

        systemTrayIcon.ContextMenuStrip = contextMenuStrip;
    }

    private void ShowApplication()
    {
        Show();
        WindowState = WindowState.Normal;
        systemTrayIcon.Visible = false;
    }

    private void OnMainWindowClose(object sender, CancelEventArgs e)
    {
        if (AppSettingsSerializer.Instance.AppSettings.SystemTrayOptions == SystemTrayOptions.WhenClose ||
            AppSettingsSerializer.Instance.AppSettings.SystemTrayOptions ==
            SystemTrayOptions.WhenMinimizeOrClose)
        {
            e.Cancel = true;
            Hide();
            systemTrayIcon.Visible = true;
            return;
        }

        Application.Current.Shutdown();
    }

    private void OnMainWindowStateChange(object sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized &&
            (AppSettingsSerializer.Instance.AppSettings.SystemTrayOptions == SystemTrayOptions.WhenMinimize ||
             AppSettingsSerializer.Instance.AppSettings.SystemTrayOptions ==
             SystemTrayOptions.WhenMinimizeOrClose))
        {
            Hide();
            systemTrayIcon.Visible = true;
        }
    }
}
