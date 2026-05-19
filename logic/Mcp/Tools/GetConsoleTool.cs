using System;
using System.ComponentModel;
using System.Linq;
using Fork.Logic.Manager;
using ModelContextProtocol.Server;

namespace Fork.Logic.Mcp.Tools;

[McpServerToolType]
public class GetConsoleTool
{
    [McpServerTool, Description(
        "Retrieve recent console output lines from a Fork-managed server.")]
    public static string GetConsole(
        [Description("The server name exactly as shown in Fork.")] string serverName,
        [Description("Number of most recent lines to return. Default 50, max 500.")] int lines = 50)
    {
        var vm = ServerManager.Instance.Entities
            .FirstOrDefault(e => e.Name.Equals(serverName, StringComparison.OrdinalIgnoreCase));

        if (vm is null) return $"Server '{serverName}' not found. Use list_servers to see available servers.";

        lines = Math.Clamp(lines, 1, 500);

        var output = vm.ConsoleOutList
            .TakeLast(lines)
            .Select(m => $"[{m.CreationTime:HH:mm:ss}] {m.Content}");

        var result = string.Join("\n", output);
        return string.IsNullOrEmpty(result) ? $"No console output yet for '{serverName}'." : result;
    }
}
