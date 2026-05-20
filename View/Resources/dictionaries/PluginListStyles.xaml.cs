using System;
using System.Windows;
using Fork.Logic.Manager;
using Fork.Logic.Model.PluginModels;
using Fork.View.Xaml2.Controls;
using Fork.ViewModel;
using Fork.logic.Utils;

namespace Fork.View.Resources.dictionaries;

public partial class PluginListStyles : ResourceDictionary
{
    private void VisitPlugin_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button b)
        {
            if (b.DataContext is Plugin plugin)
            {
                string pluginUrl = plugin.file.url.Substring(0,
                    plugin.file.url.IndexOf("/download?", StringComparison.Ordinal));
                string url = "https://www.spigotmc.org/" + pluginUrl;
                ForkUtils.OpenUrl(url);
            }
        }
    }

    private async void InstallPlugin_Click(object sender, RoutedEventArgs e)
    {
        if (sender is IconButton iconButton)
        {
            if (iconButton.CommandParameter is PluginViewModel pluginViewModel &&
                iconButton.DataContext is Plugin plugin)
            {
                bool result = await PluginManager.Instance.InstallPluginAsync(plugin, pluginViewModel);
            }
        }
    }
}
