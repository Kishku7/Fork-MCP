using System.ComponentModel;
using System.Linq;
using Fork.Logic.Manager;
using Fork.ViewModel;
using ModelContextProtocol.Server;

namespace Fork.Logic.Mcp.Tools;

[McpServerToolType]
public class ListPlayersTool
{
    [McpServerTool, Description(
        "List currently online players on a running server, including their names and OP status.")]
    public static string ListPlayers(
        [Description("The server name exactly as shown in Fork.")] string serverName)
    {
        var vm = StartServerTool.FindServer(serverName);
        if (vm is null) return $"Server '{serverName}' not found. Use list_servers to see available servers.";

        var online = vm.PlayerList.Where(p => p.IsOnline).ToList();
        if (!online.Any())
            return $"No players currently online on '{serverName}'.";

        var lines = online.Select(p =>
        {
            var name = p.Player?.Name ?? "(unknown)";
            var op   = p.IsOP ? " [OP]" : "";
            return $"{name}{op}";
        });

        return $"Players online ({online.Count}):\n" + string.Join("\n", lines);
    }
}
