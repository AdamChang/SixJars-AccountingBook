using Microsoft.AspNetCore.Authentication;
using ModelContextProtocol.AspNetCore.Authentication;
using SixJars.Api.Mcp.OAuth;

namespace SixJars.Api.Mcp;

/// <summary>
/// 遠端 MCP（唯讀）與保護它的內建 OAuth 授權伺服器（ADR 0013）。
/// </summary>
/// <remarks>
/// 兩個 scheme 分工：<see cref="McpBearerHandler"/> 負責 authenticate；SDK 的 MCP scheme 負責 challenge（401 加上
/// <c>resource_metadata</c>）並提供 <c>/.well-known/oauth-protected-resource/mcp</c>。SDK 的 handler 自己永遠回 NoResult，不能拿來驗證 token。
/// /mcp 的 policy 明確只用 <see cref="McpBearerHandler.SchemeName"/>：預設 scheme 是 cookie（測試是 Test），不指定的話
/// 瀏覽器帶著 cookie 就能打 /mcp。反過來 /api 只用預設 scheme，bearer token 也打不進去。
/// </remarks>
internal static class McpSetup
{
    public const string Path = "/mcp";
    public const string PolicyName = "Mcp";

    public static IServiceCollection AddSixJarsMcp(this IServiceCollection services)
    {
        services.AddSingleton<McpTokens>();
        services.AddScoped<AuthorizeRequestValidator>();
        services.AddOptions<McpOAuthOptions>().Configure<IConfiguration>((o, configuration) =>
        {
            // 組態有值就整個取代預設值（陣列繫結預設是附加，會把 claude.ai 留在清單裡）。
            var hosts = configuration.GetSection("Mcp:AllowedClientIdHosts").Get<string[]>();
            if (hosts is { Length: > 0 })
            {
                o.AllowedClientIdHosts = hosts;
            }
        });
        services.AddHttpClient<IClientMetadataFetcher, ClientMetadataFetcher>(client => client.Timeout = TimeSpan.FromSeconds(5))
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false });

        services.AddAuthentication()
            .AddScheme<AuthenticationSchemeOptions, McpBearerHandler>(McpBearerHandler.SchemeName,
                o => o.ForwardChallenge = McpAuthenticationDefaults.AuthenticationScheme)
            .AddMcp(o =>
            {
                // 每個請求由這份範本複製；Resource 留空，由 SDK 依請求路徑推得（.../oauth-protected-resource/mcp → {issuer}/mcp）。
                o.ResourceMetadata = new() { ScopesSupported = [McpOAuth.Scope], BearerMethodsSupported = ["header"] };
                // 授權伺服器就是本服務，網址依請求而定（與 McpOAuth.Issuer 同一個來源）。
                o.Events.OnResourceMetadataRequest = context =>
                {
                    context.ResourceMetadata!.AuthorizationServers = [McpOAuth.Issuer(context.Request)];
                    return Task.CompletedTask;
                };
            });

        services.AddAuthorizationBuilder().AddPolicy(PolicyName, policy => policy
            .AddAuthenticationSchemes(McpBearerHandler.SchemeName)
            .RequireAuthenticatedUser()
            .RequireClaim(McpOAuth.ScopeClaim, McpOAuth.Scope));

        services.AddMcpServer(o =>
            {
                o.ServerInfo = new() { Name = "SixJars", Version = "1.0" };
                o.ServerInstructions = ReconcilePrompt.ServerInstructions;
            })
            // stateless：Cloud Run 會換 instance，也不需要伺服器主動推送。
            .WithHttpTransport(o => o.Stateless = true)
            .WithTools<LedgerTools>()
            .WithPrompts<ReconcilePrompt>();
        return services;
    }

    public static WebApplication MapSixJarsMcp(this WebApplication app)
    {
        app.MapOAuthEndpoints();
        app.MapMcp(Path).RequireAuthorization(PolicyName);
        return app;
    }
}
