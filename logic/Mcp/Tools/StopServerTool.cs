using System;
using System.ComponentModel;
using System.Linq;
using Fork.Logic.Manager;
using Fork.Logic.Model;
using Fork.ViewModel;
using ModelContextProtocol.Server;

namespace Fork.Logic.Mcp.Tools;

[McpServerToolType]
public class StopServerTool
{
    [McpServerTool, Description(
        "Stop a running Fork-managed server. Sends the stop signal; the server may take a few seconds to shut down.")]
    public static string StopServer(
        [Description("The server name exactly as shown in Fork.")] string serverName)
    {
        var vm = StartServerTool.FindServer(serverName);
        if (vm is null) return $"Server '{serverName}' not found. Use list_servers to see available servers.";
        if (vm.CurrentStatus == ServerStatus.STOPPED)
            return $"'{serverName}' is already stopped.";

        ServerManager.Instance.StopServer(vm);
        return $"Stop signal sent to '{serverName}'.";
    }
}
