using System;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Linq;
using System.Text;
using Fork.Logic.Manager;
using Fork.Logic.Model;
using Fork.ViewModel;
using ModelContextProtocol.Server;

namespace Fork.Logic.Mcp.Tools;

[McpServerToolType]
public class GetStatusTool
{
    // Tracks the last known status and when it changed, per server name.
    // Updated lazily on each get_status call.
    private static readonly ConcurrentDictionary<string, (ServerStatus Status, DateTime ChangedAt)> StatusTracker = new(StringComparer.OrdinalIgnoreCase);

    [McpServerTool, Description(
        "Get the current status, performance metrics, and player count for a Fork-managed server. " +
        "Includes elapsed time in the current status — useful for polling start/stop progress.")]
    public static string GetStatus(
        [Description("The server name exactly as shown in Fork.")] string serverName)
    {
        var vm = ServerManager.Instance.Entities
            .FirstOrDefault(e => e.Name.Equals(serverName, StringComparison.OrdinalIgnoreCase));

        if (vm is null) return $"Server '{serverName}' not found. Use list_servers to see available servers.";

        var current = vm.CurrentStatus;
        var now = DateTime.UtcNow;

        // Update tracker if status changed since last poll
        var entry = StatusTracker.AddOrUpdate(
            serverName,
            _ => (current, now),
            (_, existing) => existing.Status == current ? existing : (current, now));

        var elapsed = now - entry.ChangedAt;

        var sb = new StringBuilder();
        sb.AppendLine($"Server:  {vm.Name}");
        sb.AppendLine($"Status:  {FormatStatus(current)}");
        sb.AppendLine($"Time:    {FormatElapsed(elapsed)}");
        sb.AppendLine($"CPU:     {vm.CPUValue}");
        sb.AppendLine($"Memory:  {vm.MemValue}");
        sb.AppendLine($"Disk:    {vm.DiskValue}");

        if (vm is ServerViewModel sv)
        {
            sb.AppendLine($"Type:    {sv.Server.Version.Type}");
            sb.AppendLine($"Version: {sv.Server.Version.Version}");
            int online  = sv.PlayerList.Count(p => p.IsOnline);
            int offline = sv.PlayerList.Count(p => !p.IsOnline);
            int total   = sv.PlayerList.Count;
            sb.AppendLine($"Players: {online} online, {offline} offline, {total} total");
            sb.AppendLine($"Port:    {sv.Server.ServerSettings.ServerPort}");
        }

        return sb.ToString().TrimEnd();
    }

    private static string FormatStatus(ServerStatus status) => status switch
    {
        ServerStatus.RUNNING  => "Running",
        ServerStatus.STARTING => "Starting server",
        ServerStatus.STOPPED  => "Stopped",
        _                     => status.ToString()
    };

    private static string FormatElapsed(TimeSpan ts)
    {
        if (ts.TotalSeconds < 60)
            return $"{(int)ts.TotalSeconds} seconds";
        if (ts.TotalMinutes < 60)
            return $"{(int)ts.TotalMinutes} min, {ts.Seconds} seconds";
        return $"{(int)ts.TotalHours} hours, {ts.Minutes} min, {ts.Seconds} seconds";
    }
}
