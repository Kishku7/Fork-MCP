using System;
using System.ComponentModel;
using System.Linq;
using Fork.Logic.Manager;
using Fork.Logic.Model;
using Fork.ViewModel;
using ModelContextProtocol.Server;

namespace Fork.Logic.Mcp.Tools;

[McpServerToolType]
public class StartServerTool
{
    [McpServerTool, Description(
        "Start a Fork-managed server by name. Fire-and-forget — returns immediately. " +
        "Use get_status to poll progress.")]
    public static string StartServer(
        [Description("The server name exactly as shown in Fork.")] string serverName)
    {
        var vm = FindServer(serverName);
        if (vm is null) return $"Server '{serverName}' not found. Use list_servers to see available servers.";
        if (vm.CurrentStatus == ServerStatus.RUNNING)
            return $"'{serverName}' is already running.";
        if (vm.CurrentStatus == ServerStatus.STARTING)
            return $"'{serverName}' is already starting.";

        _ = ServerManager.Instance.StartServerAsync(vm);
        return $"Start signal sent to '{serverName}'. Use get_status to poll progress.";
    }

    internal static ServerViewModel? FindServer(string name) =>
        ServerManager.Instance.Entities
            .OfType<ServerViewModel>()
            .FirstOrDefault(e => e.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
}
