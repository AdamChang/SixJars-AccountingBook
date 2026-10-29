using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SixJars.Api.Mcp.OAuth;

/// <summary>CIMD（Client ID Metadata Document）：client_id 本身是 HTTPS URL，文件內容由 client 自己宣稱。</summary>
internal sealed record ClientMetadata(
    [property: JsonPropertyName("client_id")] string ClientId,
    [property: JsonPropertyName("client_name")] string? ClientName,
    [property: JsonPropertyName("redirect_uris")] string[] RedirectUris);

internal interface IClientMetadataFetcher
{
    /// <summary>抓不到、逾時、太大或格式不對都回傳 null；呼叫端一律視為不可信的 client。</summary>
    Task<ClientMetadata?> FetchAsync(Uri clientId, CancellationToken cancellationToken);
}

/// <summary>
/// 對外抓 CIMD 文件。host 白名單在呼叫前已檢查（<see cref="AuthorizeRequestValidator"/>），這裡再收緊：
/// 不跟隨 redirect（避免白名單 host 把請求轉到內網）、5 秒逾時、最多讀 64 KB。
/// </summary>
internal sealed class ClientMetadataFetcher(HttpClient http, ILogger<ClientMetadataFetcher> logger) : IClientMetadataFetcher
{
    public const string HttpClientName = "mcp-client-metadata";
    private const int MaxBytes = 64 * 1024;

    public async Task<ClientMetadata?> FetchAsync(Uri clientId, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, clientId);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > MaxBytes)
            {
                logger.LogWarning("CIMD 文件 {ClientId} 回應 {Status}", clientId, (int)response.StatusCode);
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var buffer = new byte[MaxBytes + 1];
            var length = 0;
            int read;
            while (length <= MaxBytes && (read = await stream.ReadAsync(buffer.AsMemory(length), cancellationToken)) > 0)
            {
                length += read;
            }

            return length > MaxBytes ? null : JsonSerializer.Deserialize<ClientMetadata>(buffer.AsSpan(0, length));
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException)
        {
            logger.LogWarning(e, "無法取得 CIMD 文件 {ClientId}", clientId);
            return null;
        }
    }
}
