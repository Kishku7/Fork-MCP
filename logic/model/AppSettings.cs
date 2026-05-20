using System.IO;

namespace Fork.Logic.Model;

public class AppSettings
{
    public string ServerPath { get; set; } = Path.Combine(App.ApplicationPath, "servers");
    public int MaxConsoleLines { get; set; } = 100_000;
    public int MaxConsoleLinesPerSecond { get; set; } = 10;
    public string DefaultJavaPath { get; set; } = "java.exe";
    public bool EnableDiscordBot { get; set; } = false;
    public string DiscordBotToken { get; set; }
    public bool ConsoleThrottling { get; set; } = true;
    public bool RichPresence { get; set; } = true;
    public bool SendTelemetry { get; set; } = false;
    public SystemTrayOptions SystemTrayOptions { get; set; } = SystemTrayOptions.None;

    // ── MCP Server ────────────────────────────────────────────────────────────
    /// <summary>Enable the built-in MCP server. Defaults to true.</summary>
    public bool McpEnabled { get; set; } = true;
    /// <summary>IP address for the MCP server to bind to. 0.0.0.0 = all interfaces.</summary>
    public string McpIp { get; set; } = "0.0.0.0";
    /// <summary>Port for the MCP server. Default 19475.</summary>
    public int McpPort { get; set; } = 19475;
    /// <summary>
    /// When non-null, MCP requests must supply this value as a Bearer token.
    /// Null = no authentication (default). Set via the Fork settings UI or AppSettings.json.
    /// </summary>
    public string? McpAuthToken { get; set; } = null;

    // ── Re-attach polling ─────────────────────────────────────────────────────
    /// <summary>
    /// Minutes between automatic state-sync cycles when re-attached to a running
    /// server via ForkGuard (save-all + role/player list refresh).
    /// Set to 0 to disable periodic polling. Default: 15.
    /// </summary>
    public int ReattachPollIntervalMinutes { get; set; } = 15;
}
