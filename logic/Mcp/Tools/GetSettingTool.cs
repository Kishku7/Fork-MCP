using System;
using System.ComponentModel;
using Fork.Logic.Manager;
using Fork.ViewModel;
using ModelContextProtocol.Server;

namespace Fork.Logic.Mcp.Tools;

[McpServerToolType]
public class GetSettingTool
{
    [McpServerTool, Description(
        "Read a server.properties setting from a Fork-managed server. " +
        "Key names match server.properties exactly (e.g. 'max-players', 'level-seed', 'difficulty').")]
    public static string GetSetting(
        [Description("The server name exactly as shown in Fork.")] string serverName,
        [Description("The server.properties key to read (e.g. 'max-players', 'motd', 'server-port').")] string key)
    {
        var vm = StartServerTool.FindServer(serverName);
        if (vm is null) return $"Server '{serverName}' not found. Use list_servers to see available servers.";

        var dict = vm.Server.ServerSettings.SettingsDictionary;

        if (dict.TryGetValue(key, out var value))
            return $"{key} = {value}";

        // Try case-insensitive fallback
        foreach (var kvp in dict)
        {
            if (kvp.Key.Equals(key, StringComparison.OrdinalIgnoreCase))
                return $"{kvp.Key} = {kvp.Value}";
        }

        return $"Key '{key}' not found in server.properties for '{serverName}'.";
    }
}
