using System;
using System.ComponentModel;
using System.Linq;
using System.Text;
using Fork.Logic.Manager;
using Fork.ViewModel;
using ModelContextProtocol.Server;

namespace Fork.Logic.Mcp.Tools;

[McpServerToolType]
public class GetStatusTool
{
    [McpServerTool, Description(
        "Get the current status, performance metrics, and player count for a Fork-managed server.")]
    public static string GetStatus(
        [Description("The server name exactly as shown in Fork.")] string serverName)
    {
        var vm = ServerManager.Instance.Entities
            .FirstOrDefault(e => e.Name.Equals(serverName, StringComparison.OrdinalIgnoreCase));

        if (vm is null) return $"Server '{serverName}' not found. Use list_servers to see available servers.";

        var sb = new StringBuilder();
        sb.AppendLine($"Server:  {vm.Name}");
        sb.AppendLine($"Status:  {vm.CurrentStatus}");
        sb.AppendLine($"CPU:     {vm.CPUValue}");
        sb.AppendLine($"Memory:  {vm.MemValue}");
        sb.AppendLine($"Disk:    {vm.DiskValue}");

        if (vm is ServerViewModel sv)
        {
            sb.AppendLine($"Type:    {sv.Server.Version.Type}");
            sb.AppendLine($"Version: {sv.Server.Version.Version}");
            sb.AppendLine($"Players: {sv.PlayerList.Count} online");
            sb.AppendLine($"Port:    {sv.Server.ServerSettings.ServerPort}");
        }

        return sb.ToString().TrimEnd();
    }
}
