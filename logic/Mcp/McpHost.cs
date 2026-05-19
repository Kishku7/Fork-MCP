using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Fork.Logic.Mcp.Tools;
using Fork.Logic.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;

namespace Fork.Logic.Mcp;

/// <summary>
/// Hosts an in-process MCP server alongside the Fork WPF application.
/// Uses WebApplication.StartAsync() so the host runs in the background
/// without blocking the WPF dispatcher thread.
///
/// Endpoint: http://{McpIp}:{McpPort}/mcp  (streamable HTTP transport)
///           http://{McpIp}:{McpPort}/mcp/sse  (legacy SSE transport)
///
/// Configuration: AppSettings.McpEnabled / McpIp / McpPort / McpAuthToken
/// </summary>
public sealed class McpHost : IDisposable
{
    private WebApplication? _app;

    public async Task StartAsync()
    {
        var settings = AppSettingsSerializer.Instance.AppSettings;

        if (!settings.McpEnabled)
        {
            Console.WriteLine("[MCP] Disabled via AppSettings.McpEnabled — skipping.");
            return;
        }

        var ip   = IPAddress.Parse(settings.McpIp);
        var port = settings.McpPort;

        var builder = WebApplication.CreateBuilder(Array.Empty<string>());

        builder.WebHost.ConfigureKestrel(k => k.Listen(ip, port));

        builder.Services
            .AddMcpServer()
            .WithHttpTransport()
            .WithTools<ListServersTool>()
            .WithTools<StartServerTool>()
            .WithTools<StopServerTool>()
            .WithTools<RestartServerTool>()
            .WithTools<SendCommandTool>()
            .WithTools<GetConsoleTool>()
            .WithTools<GetStatusTool>()
            .WithTools<ListPlayersTool>()
            .WithTools<GetSettingTool>()
            .WithTools<SetSettingTool>()
            .WithTools<ListPluginsTool>()
            .WithTools<InstallPluginTool>()
            .WithTools<EnablePluginTool>()
            .WithTools<DisablePluginTool>();

        _app = builder.Build();

        // ── Auth middleware ───────────────────────────────────────────────────
        // McpAuthMiddleware is a pass-through stub. Uncomment when McpAuthToken
        // enforcement is implemented:
        // _app.UseMiddleware<McpAuthMiddleware>();

        _app.MapMcp("/mcp");

        // StartAsync() begins listening without blocking — the host runs in the
        // background managed by its own IHostedService infrastructure.
        await _app.StartAsync();

        Console.WriteLine($"[MCP] Server listening on http://{ip}:{port}/mcp");
    }

    public async Task StopAsync()
    {
        if (_app is null) return;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try { await _app.StopAsync(cts.Token); }
        catch { /* shutting down */ }
        Console.WriteLine("[MCP] Server stopped.");
    }

    public void Dispose()
    {
        try { _app?.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(5)); }
        catch { /* shutting down */ }
    }
}
