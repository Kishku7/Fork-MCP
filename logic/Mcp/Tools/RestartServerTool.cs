using System;
using System.ComponentModel;
using System.Threading.Tasks;
using Fork.Logic.Manager;
using ModelContextProtocol.Server;

namespace Fork.Logic.Mcp.Tools;

[McpServerToolType]
public class RestartServerTool
{
    [McpServerTool, Description(
        "Restart a Fork-managed server. If the server is stopped, it will be started. " +
        "If running, it will be stopped then started.")]
    public static async Task<string> RestartServer(
        [Description("The server name exactly as shown in Fork.")] string serverName)
    {
        var vm = StartServerTool.FindServer(serverName);
        if (vm is null) return $"Server '{serverName}' not found. Use list_servers to see available servers.";

        var ok = await ServerManager.Instance.RestartServerAsync(vm);
        return ok ? $"'{serverName}' restarted successfully." : $"Failed to restart '{serverName}'.";
    }
}
