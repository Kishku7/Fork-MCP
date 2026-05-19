using System;
using System.ComponentModel;
using System.Threading.Tasks;
using Fork.Logic.Manager;
using ModelContextProtocol.Server;

namespace Fork.Logic.Mcp.Tools;

[McpServerToolType]
public class SetSettingTool
{
    [McpServerTool, Description(
        "Write a server.properties setting on a Fork-managed server and persist it to disk. " +
        "Key names match server.properties exactly (e.g. 'max-players', 'level-seed', 'difficulty'). " +
        "The server does NOT need to be stopped — changes take effect on next restart.")]
    public static async Task<string> SetSetting(
        [Description("The server name exactly as shown in Fork.")] string serverName,
        [Description("The server.properties key to set (e.g. 'max-players', 'motd', 'difficulty').")] string key,
        [Description("The value to assign.")] string value)
    {
        var vm = StartServerTool.FindServer(serverName);
        if (vm is null) return $"Server '{serverName}' not found. Use list_servers to see available servers.";

        var dict = vm.Server.ServerSettings.SettingsDictionary;

        // Resolve key case-insensitively so callers aren't tripped up by
        // snake-case vs. hyphenated variants.
        string? resolvedKey = null;
        if (dict.ContainsKey(key))
        {
            resolvedKey = key;
        }
        else
        {
            foreach (var k in dict.Keys)
            {
                if (k.Equals(key, StringComparison.OrdinalIgnoreCase))
                {
                    resolvedKey = k;
                    break;
                }
            }
        }

        if (resolvedKey is null)
        {
            // Key doesn't exist yet — add it as-is (server.properties allows custom keys)
            resolvedKey = key;
        }

        var old = dict.ContainsKey(resolvedKey) ? dict[resolvedKey] : "(new)";
        dict[resolvedKey] = value;

        await vm.SaveProperties();

        return $"'{serverName}': {resolvedKey} changed from '{old}' to '{value}' and saved.";
    }
}
