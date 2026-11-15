using System;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Fork.Logic.Manager;
using Fork.Logic.Model;
using ModelContextProtocol.Server;

namespace Fork.Logic.Mcp.Tools;

[McpServerToolType]
public class ExitForkTool
{
    [McpServerTool, Description(
        "Exit the Fork application itself (the 'full restart' routine). Refuses while any server is not STOPPED " +
        "unless force=true (ForkGuard keeps running servers alive, but stop them first for a clean restart). " +
        "Shuts down about 2 seconds after replying, so this call returns before the MCP endpoint goes away. " +
        "Relaunch from the interactive session with the ForkStartup scheduled task.")]
    public static string ExitFork(
        [Description("Exit even if servers are running. Default false.")] bool force = false)
    {
        var running = ServerManager.Instance.Entities
            .Where(e => e.CurrentStatus != ServerStatus.STOPPED)
            .Select(e => $"{e.Name} ({e.CurrentStatus})")
            .ToList();

        if (running.Count > 0 && !force)
            return "Refusing to exit: not stopped: " + string.Join(", ", running) +
                   ". Stop them with stop_server first, or pass force=true.";

        Task.Run(async () =>
        {
            await Task.Delay(2000);
            Application.Current.Dispatcher.Invoke(() => Application.Current.Shutdown());
        });
        return "Fork will exit in about 2 seconds.";
    }
}
