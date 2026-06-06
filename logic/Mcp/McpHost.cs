using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Fork.Logic.Manager;
using Fork.Logic.Mcp.Tools;
using Fork.Logic.Model;
using Fork.Logic.Persistence;
using Fork.ViewModel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;

namespace Fork.Logic.Mcp;

/// <summary>
/// Hosts an in-process MCP server alongside the Fork WPF application.
/// Uses WebApplication.StartAsync() so the host runs in the background
/// without blocking the WPF dispatcher thread.
///
/// Endpoints:
///   http://{McpIp}:{McpPort}/mcp        — Streamable HTTP MCP transport
///   http://{McpIp}:{McpPort}/mcp/sse    — Legacy SSE MCP transport
///   http://{McpIp}:{McpPort}/health     — Lightweight health check (no handshake required)
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

        // ── Health endpoint ───────────────────────────────────────────────────
        // Plain GET — no MCP handshake, no auth. Safe to poll from monitoring
        // tools or a simple curl. Returns a JSON snapshot of Fork + all servers.
        _app.MapGet("/health", BuildHealthResponse);

        // StartAsync() begins listening without blocking — the host runs in the
        // background managed by its own IHostedService infrastructure.
        await _app.StartAsync();

        Console.WriteLine($"[MCP] Server listening on http://{ip}:{port}/mcp");
        Console.WriteLine($"[MCP] Health endpoint at http://{ip}:{port}/health");
    }

    /// <summary>
    /// Builds a JSON health snapshot on each request. No caching — data is live.
    ///
    /// Top-level fields:
    ///   status        — always "ok" while Fork is responding
    ///   version       — Fork version string (e.g. "1.3.0")
    ///   uptimeSeconds — seconds since Fork process started
    ///   mcpPort       — configured MCP port
    ///   authEnabled   — true when McpAuthToken is set
    ///   forkMemoryMb  — Fork process working set in MB
    ///   servers       — array of per-server snapshots (see below)
    ///
    /// Per-server fields:
    ///   name          — server name as shown in Fork
    ///   status        — "running" | "starting" | "stopped"
    ///   uptimeSeconds — seconds the Java process has been alive (running only; null otherwise)
    ///   memoryUsedMb  — Java process working set in MB (running only; null otherwise)
    ///   memoryMaxMb   — configured -Xmx value in MB
    ///   cpuPercent    — 3-sample rolling CPU average (running only; null otherwise)
    ///   players       — online player count (null for network entities)
    ///   issues        — list of problems found; empty array when all is well
    /// </summary>
    private static IResult BuildHealthResponse()
    {
        // ── Fork process ──────────────────────────────────────────────────────
        var forkProc    = Process.GetCurrentProcess();
        var uptimeSec   = (long)(DateTime.UtcNow - forkProc.StartTime.ToUniversalTime()).TotalSeconds;
        var settings    = AppSettingsSerializer.Instance.AppSettings;
        var forkVersion = ApplicationManager.Initialized
            ? $"{ApplicationManager.Instance.CurrentForkVersion.Major}" +
              $".{ApplicationManager.Instance.CurrentForkVersion.Minor}" +
              $".{ApplicationManager.Instance.CurrentForkVersion.Patch}"
            : "unknown";

        // ── Per-server snapshots ──────────────────────────────────────────────
        var serverSnapshots = new List<object>();

        if (ServerManager.Initialized)
        {
            foreach (var vm in ServerManager.Instance.Entities)
            {
                var issues = new List<string>();

                // ── File system checks (always run, catch problems before start) ──
                string serverDir = Path.Combine(App.ServerPath, vm.Name);
                if (!Directory.Exists(serverDir))
                {
                    issues.Add($"Server directory not found: {serverDir}");
                }
                else
                {
                    if (!File.Exists(Path.Combine(serverDir, "server.jar")))
                        issues.Add("server.jar is missing — server cannot start");
                    if (!File.Exists(Path.Combine(serverDir, "server.properties")))
                        issues.Add("server.properties is missing");
                }

                // ── Java executable check (only meaningful when stopped) ──────
                if (vm.CurrentStatus == ServerStatus.STOPPED)
                {
                    var resolved = Fork.Logic.Service.JavaDiscoveryService.Instance
                        .GetBestForMajor(vm.Entity.JavaSettings.PreferredMajorVersion);
                    if (resolved == null)
                    {
                        issues.Add("No Java installation found � check Java Installations in Fork settings");
                    }
                    else if (!File.Exists(resolved.BinaryPath))
                    {
                        issues.Add($"Java executable not found: {resolved.BinaryPath}");
                    }
                }

                // ── Process uptime (from Java process start time) ─────────────
                long? serverUptimeSec = null;
                if (vm.CurrentStatus == ServerStatus.RUNNING &&
                    ApplicationManager.Initialized &&
                    ApplicationManager.Instance.ActiveEntities.TryGetValue(vm.Entity, out var proc))
                {
                    try
                    {
                        if (!proc.HasExited)
                            serverUptimeSec =
                                (long)(DateTime.UtcNow - proc.StartTime.ToUniversalTime()).TotalSeconds;
                    }
                    catch { /* access denied or process already gone */ }
                }

                // ── Memory (MemValueRaw is % of MaxRam — back-calculate to MB) ─
                bool   isRunning  = vm.CurrentStatus == ServerStatus.RUNNING;
                double memUsedMb  = vm.MemValueRaw / 100.0 * vm.Entity.JavaSettings.MaxRam;
                int    memMaxMb   = vm.Entity.JavaSettings.MaxRam;

                int? onlinePlayers  = vm is ServerViewModel sv1 ? sv1.PlayerList.Count(p => p.IsOnline)  : null;
                int? offlinePlayers = vm is ServerViewModel sv2 ? sv2.PlayerList.Count(p => !p.IsOnline) : null;
                int? totalPlayers   = vm is ServerViewModel sv3 ? sv3.PlayerList.Count                   : null;

                serverSnapshots.Add(new
                {
                    name           = vm.Name,
                    status         = vm.CurrentStatus.ToString().ToLowerInvariant(),
                    uptimeSeconds  = serverUptimeSec,
                    memoryUsedMb   = isRunning ? Math.Round(memUsedMb, 1) : (double?)null,
                    memoryMaxMb    = memMaxMb,
                    cpuPercent     = isRunning ? Math.Round(vm.CPUValueRaw, 1) : (double?)null,
                    playersOnline  = onlinePlayers,
                    playersOffline = offlinePlayers,
                    playersTotal   = totalPlayers,
                    issues,
                });
            }
        }

        return Results.Json(new
        {
            status        = "ok",
            version       = forkVersion,
            uptimeSeconds = uptimeSec,
            mcpPort       = settings.McpPort,
            authEnabled   = settings.McpAuthToken is not null,
            forkMemoryMb  = Math.Round(forkProc.WorkingSet64 / (1024.0 * 1024.0), 1),
            servers       = serverSnapshots,
        });
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
