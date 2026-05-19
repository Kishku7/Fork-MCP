using System;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Fork.Logic.Manager;
using Fork.ViewModel;
using ModelContextProtocol.Server;

namespace Fork.Logic.Mcp.Tools;

[McpServerToolType]
public class DisablePluginTool
{
    [McpServerTool, Description(
        "Disable a plugin on a Fork-managed server without uninstalling it. " +
        "Moves the JAR to plugins_disabled/. Restart the server for it to take effect.")]
    public static async Task<string> DisablePlugin(
        [Description("The server name exactly as shown in Fork.")] string serverName,
        [Description("The plugin name exactly as shown in list_plugins.")] string pluginName)
    {
        var vm = StartServerTool.FindServer(serverName);
        if (vm is null) return $"Server '{serverName}' not found.";

        var pluginVm = new PluginViewModel(vm);
        var plugin   = pluginVm.InstalledPlugins
            .FirstOrDefault(p => p.Name.Equals(pluginName, StringComparison.OrdinalIgnoreCase));

        if (plugin is null)
            return $"Plugin '{pluginName}' not found on '{serverName}'. Use list_plugins to see installed plugins.";
        if (!plugin.IsEnabled)
            return $"'{pluginName}' is already disabled.";

        var ok = await PluginManager.Instance.DisablePluginAsync(plugin, pluginVm);
        return ok
            ? $"'{pluginName}' disabled on '{serverName}'. Restart the server for it to take effect."
            : $"Failed to disable '{pluginName}'.";
    }
}
