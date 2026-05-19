using System;
using System.ComponentModel;
using System.Net.Http;
using System.Threading.Tasks;
using Fork.Logic.Manager;
using Fork.Logic.Model.PluginModels;
using Fork.ViewModel;
using ModelContextProtocol.Server;
using Newtonsoft.Json;

namespace Fork.Logic.Mcp.Tools;

[McpServerToolType]
public class InstallPluginTool
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };
    private const string SpigetBase = "https://api.spiget.org/v2/resources/";

    [McpServerTool, Description(
        "Install a plugin on a Fork-managed server by Spiget resource ID. " +
        "Find the ID at spiget.org or from the plugin's SpigotMC page URL (e.g. resource/my-plugin.12345 → ID is 12345). " +
        "Only works on servers with plugin support (Paper, Spigot, Purpur).")]
    public static async Task<string> InstallPlugin(
        [Description("The server name exactly as shown in Fork.")] string serverName,
        [Description("The Spiget resource ID (integer). Found in the SpigotMC URL: /resources/plugin-name.XXXXX")] int spigetResourceId)
    {
        var vm = StartServerTool.FindServer(serverName);
        if (vm is null) return $"Server '{serverName}' not found.";

        if (!vm.Server.Version.HasPlugins)
            return $"'{serverName}' runs {vm.Server.Version.Type} which does not support plugins. " +
                   "Switch to Paper, Spigot, or Purpur.";

        // Fetch plugin metadata from Spiget
        Plugin plugin;
        try
        {
            var json = await Http.GetStringAsync($"{SpigetBase}{spigetResourceId}");
            plugin = JsonConvert.DeserializeObject<Plugin>(json)
                ?? throw new InvalidOperationException("Spiget returned null.");
        }
        catch (Exception ex)
        {
            return $"Failed to fetch plugin info from Spiget (ID {spigetResourceId}): {ex.Message}";
        }

        var pluginVm = new PluginViewModel(vm);

        // Check if already installed
        foreach (var installed in pluginVm.InstalledPlugins)
        {
            if (installed.IsSpigetPlugin && installed.SpigetId == spigetResourceId)
                return $"'{plugin.name}' is already installed on '{serverName}'.";
        }

        var ok = await PluginManager.Instance.InstallPluginAsync(plugin, pluginVm);
        return ok
            ? $"'{plugin.name}' (ID {spigetResourceId}) installed on '{serverName}'. Restart the server to load it."
            : $"Failed to install plugin ID {spigetResourceId} on '{serverName}'.";
    }
}
