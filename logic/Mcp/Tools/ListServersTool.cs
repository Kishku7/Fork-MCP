using System.ComponentModel;
using System.Linq;
using System.Text;
using Fork.Logic.Manager;
using Fork.ViewModel;
using ModelContextProtocol.Server;

namespace Fork.Logic.Mcp.Tools;

[McpServerToolType]
public class ListServersTool
{
    [McpServerTool, Description(
        "List all servers and networks managed by Fork, including their current status and version.")]
    public static string ListServers()
    {
        var entities = ServerManager.Instance.Entities;
        if (!entities.Any())
            return "No servers are configured in Fork.";

        var sb = new StringBuilder();
        sb.AppendLine("Name | Status | Type | Version");
        sb.AppendLine(new string('-', 60));

        foreach (var e in entities)
        {
            if (e is ServerViewModel sv)
                sb.AppendLine($"{sv.Name} | {sv.CurrentStatus} | {sv.Server.Version.Type} | {sv.Server.Version.Version}");
            else
                sb.AppendLine($"{e.Name} | {e.CurrentStatus} | Network | —");
        }

        return sb.ToString().TrimEnd();
    }
}
