using System.ComponentModel;
using System.Linq;
using System.Text;
using Fork.Logic.Manager;
using Fork.ViewModel;
using ModelContextProtocol.Server;

namespace Fork.Logic.Mcp.Tools;

[McpServerToolType]
public class ListPluginsTool
{
    [McpServerTool, Description(
        "List all installed plugins on a Fork-managed server, including enabled/disabled state and version.")]
    public static string ListPlugins(
        [Description("The server name exactly as shown in Fork.")] string serverName)
    {
        var vm = StartServerTool.FindServer(serverName);
        if (vm is null) return $"Server '{serverName}' not found. Use list_servers to see available servers.";

        var pluginVm = new PluginViewModel(vm);
        var plugins  = pluginVm.InstalledPlugins;

        if (!plugins.Any())
            return $"No plugins installed on '{serverName}'.";

        var sb = new StringBuilder();
        sb.AppendLine($"Installed plugins on '{serverName}' ({plugins.Count}):");
        sb.AppendLine(new string('-', 50));

        foreach (var p in plugins.OrderBy(p => p.Name))
        {
            var state   = p.IsEnabled ? "enabled" : "DISABLED";
            var version = p.InstalledVersion > 0 ? $"v{p.InstalledVersion}" : "unknown";
            var update  = p.LatestVersion > p.InstalledVersion && p.LatestVersion > 0 ? " [update available]" : "";
            sb.AppendLine($"{p.Name} | {state} | {version}{update}");
        }

        return sb.ToString().TrimEnd();
    }
}
