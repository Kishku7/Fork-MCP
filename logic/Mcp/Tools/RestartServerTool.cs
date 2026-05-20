using System;
using System.ComponentModel;
using Fork.Logic.Manager;
using Fork.Logic.Model;
using ModelContextProtocol.Server;

namespace Fork.Logic.Mcp.Tools;

[McpServerToolType]
public class RestartServerTool
{
    [McpServerTool, Description(
        "Restart a Fork-managed server. Fire-and-forget — returns immediately. " +
        "Use get_status to poll progress.")]
    public static string RestartServer(
        [Description("The server name exactly as shown in Fork.")] string serverName)
    {
        var vm = StartServerTool.FindServer(serverName);
        if (vm is null) return $"Server '{serverName}' not found. Use list_servers to see available servers.";
        if (vm.CurrentStatus == ServerStatus.STARTING)
            return $"'{serverName}' is currently starting — wait for it to be RUNNING before restarting.";

        _ = ServerManager.Instance.RestartServerAsync(vm);
        return $"Restart signal sent to '{serverName}'. Use get_status to poll progress.";
    }
}
