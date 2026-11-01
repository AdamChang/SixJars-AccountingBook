using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using SixJars.Tests.Shared;
using Xunit;

namespace SixJars.Api.Tests.Mcp;

/// <summary>MCP tool 測試共用：以 Bearer token 連上 /mcp 的 McpClient，與呼叫 tool 並解析 JSON 結果。</summary>
internal static class McpToolClient
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static async Task<McpClient> ConnectAsync(ApiFactory factory, string subject = ApiFactory.DefaultSubject, string scope = "ledger:read")
    {
        var (_, token) = await McpTestTokens.IssueAsync(factory, Ct, subject, scope: scope);
        var http = McpTestTokens.CreateBearerClient(factory, token);
        var transport = new HttpClientTransport(
            new HttpClientTransportOptions { Endpoint = new Uri("https://localhost/mcp"), TransportMode = HttpTransportMode.StreamableHttp },
            http, NullLoggerFactory.Instance, ownsHttpClient: true);
        return await McpClient.CreateAsync(transport, cancellationToken: Ct);
    }

    public static async Task<JsonElement> CallAsync(McpClient client, string tool, Dictionary<string, object?> arguments)
    {
        var result = await client.CallToolAsync(tool, arguments, cancellationToken: Ct);
        result.IsError.Should().NotBe(true, Text(result));
        return JsonDocument.Parse(Text(result)).RootElement;
    }

    public static string Text(CallToolResult result) => string.Concat(result.Content.OfType<TextContentBlock>().Select(c => c.Text));
}
