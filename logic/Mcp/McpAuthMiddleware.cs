using System;
using System.Threading.Tasks;
using Fork.Logic.Persistence;
using Microsoft.AspNetCore.Http;

namespace Fork.Logic.Mcp;

/// <summary>
/// Bearer token authentication middleware for the Fork MCP server.
///
/// Currently a pass-through stub. When <see cref="Fork.Logic.Model.AppSettings.McpAuthToken"/>
/// is set, requests to /mcp will require an Authorization: Bearer {token} header.
///
/// To activate:
///   1. Uncomment _app.UseMiddleware&lt;McpAuthMiddleware&gt;() in McpHost.cs
///   2. Uncomment the token check below
///   3. Set AppSettings.McpAuthToken to a strong random value
/// </summary>
public class McpAuthMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext ctx)
    {
        var token = AppSettingsSerializer.Instance.AppSettings.McpAuthToken;

        if (!string.IsNullOrEmpty(token))
        {
            // TODO: enforce bearer token
            //
            // var auth = ctx.Request.Headers.Authorization.ToString();
            // if (!auth.Equals($"Bearer {token}", StringComparison.Ordinal))
            // {
            //     ctx.Response.StatusCode = 401;
            //     ctx.Response.Headers["WWW-Authenticate"] = "Bearer realm=\"Fork-MCP\"";
            //     ctx.Response.ContentType = "application/json";
            //     await ctx.Response.WriteAsJsonAsync(new { error = "unauthorized" });
            //     return;
            // }
        }

        await next(ctx);
    }
}
