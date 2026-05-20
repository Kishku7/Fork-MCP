using System;
using System.ComponentModel;
using Fork.Logic.Manager;
using Fork.Logic.Model;
using ModelContextProtocol.Server;

namespace Fork.Logic.Mcp.Tools;

[McpServerToolType]
public class SendCommandTool
{
    [McpServerTool, Description(
        "Send a console command to a running Minecraft server. " +
        "Do NOT prefix with '/'. Examples: 'say Hello world', 'stop', 'whitelist add Steve', 'op Dave'.")]
    public static string SendCommand(
        [Description("The server name exactly as shown in Fork.")] string serverName,
        [Description("The command to send (no leading slash).")] string command)
    {
        var vm = StartServerTool.FindServer(serverName);
        if (vm is null) return $"Server '{serverName}' not found. Use list_servers to see available servers.";
        if (vm.CurrentStatus != ServerStatus.RUNNING)
            return $"'{serverName}' is not running (status: {vm.CurrentStatus}). Start it first.";

        if (vm.ConsoleReader is null)
            return $"No console reader for '{serverName}'. The server may still be starting.";

        try
        {
            vm.ConsoleReader.Read(command, vm);
            return $"Command sent: {command}";
        }
        catch (Exception ex)
        {
            return $"Failed to send command: {ex.Message}";
        }
    }
}
