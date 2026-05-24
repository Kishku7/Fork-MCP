using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using Fork.Logic.Manager;
using Fork.ViewModel;
using ModelContextProtocol.Server;

namespace Fork.Logic.Mcp.Tools;

[McpServerToolType]
public class ListPlayersTool
{
    [McpServerTool, Description(
        "List all known players for a server — both currently online and offline — with their OP status. " +
        "Online players are listed first. Use this instead of checking only who is currently online.")]
    public static string ListPlayers(
        [Description("The server name exactly as shown in Fork.")] string serverName)
    {
        var vm = StartServerTool.FindServer(serverName);
        if (vm is null) return $"Server '{serverName}' not found. Use list_servers to see available servers.";

        var all = vm.PlayerList.ToList();
        if (!all.Any())
            return $"No known players for '{serverName}'. Players are discovered when they connect or when the server loads.";

        var online  = all.Where(p => p.IsOnline).OrderBy(p => p.Player?.Name).ToList();
        var offline = all.Where(p => !p.IsOnline).OrderBy(p => p.Player?.Name).ToList();

        var sb = new StringBuilder();

        if (online.Any())
        {
            sb.AppendLine($"Online ({online.Count}):");
            foreach (var p in online)
            {
                var name = p.Player?.Name ?? "(unknown)";
                var op   = p.IsOP ? " [OP]" : "";
                sb.AppendLine($"  {name}{op}");
            }
        }

        if (offline.Any())
        {
            if (online.Any()) sb.AppendLine();
            sb.AppendLine($"Known offline ({offline.Count}):");
            foreach (var p in offline)
            {
                var name = p.Player?.Name ?? "(unknown)";
                var op   = p.IsOP ? " [OP]" : "";
                sb.AppendLine($"  {name}{op}");
            }
        }

        return sb.ToString().TrimEnd();
    }
}
