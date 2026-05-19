using System;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Fork.Logic.Manager;
using Fork.Logic.Model;
using Fork.ViewModel;
using ModelContextProtocol.Server;

namespace Fork.Logic.Mcp.Tools;

[McpServerToolType]
public class StartServerTool
{
    [McpServerTool, Description(
        "Start a Fork-managed server by name. Has no effect if the server is already running.")]
    public static async Task<string> StartServer(
        [Description("The server name exactly as shown in Fork.")] string serverName)
    {
        var vm = FindServer(serverName);
        if (vm is null) return $"Server '{serverName}' not found. Use list_servers to see available servers.";
        if (vm.CurrentStatus == ServerStatus.RUNNING)
            return $"'{serverName}' is already running.";

        var ok = await ServerManager.Instance.StartServerAsync(vm);
        return ok ? $"'{serverName}' started successfully." : $"Failed to start '{serverName}'.";
    }

    internal static ServerViewModel? FindServer(string name) =>
        ServerManager.Instance.Entities
            .OfType<ServerViewModel>()
            .FirstOrDefault(e => e.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
}
