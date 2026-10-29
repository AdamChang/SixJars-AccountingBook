using System.Text.Json.Serialization;

namespace SixJars.Api.Mcp.OAuth;

/// <summary>
/// 內建 OAuth 授權伺服器的端點（ADR 0013）。不在 /api 底下：Claude 是外部 client，不會帶 cookie 或 XSRF token。
/// </summary>
internal static class OAuthEndpoints
{
    public const string AuthorizePath = "/oauth/authorize";
    public const string TokenPath = "/oauth/token";

    public static void MapOAuthEndpoints(this IEndpointRouteBuilder app)
    {
        // RFC 8414。client_id_metadata_document_supported 與 "none" 兩者都要有，Claude 才會用 CIMD（否則改找 registration_endpoint）。
        app.MapGet("/.well-known/oauth-authorization-server", (HttpRequest request) =>
        {
            var issuer = McpOAuth.Issuer(request);
            return Results.Json(new AuthorizationServerMetadata(
                issuer,
                issuer + AuthorizePath,
                issuer + TokenPath,
                [McpOAuth.Scope],
                ["code"],
                ["authorization_code", "refresh_token"],
                ["none"],
                ["S256"],
                ClientIdMetadataDocumentSupported: true,
                AuthorizationResponseIssParameterSupported: true));
        });
    }

    private sealed record AuthorizationServerMetadata(
        [property: JsonPropertyName("issuer")] string Issuer,
        [property: JsonPropertyName("authorization_endpoint")] string AuthorizationEndpoint,
        [property: JsonPropertyName("token_endpoint")] string TokenEndpoint,
        [property: JsonPropertyName("scopes_supported")] string[] ScopesSupported,
        [property: JsonPropertyName("response_types_supported")] string[] ResponseTypesSupported,
        [property: JsonPropertyName("grant_types_supported")] string[] GrantTypesSupported,
        [property: JsonPropertyName("token_endpoint_auth_methods_supported")] string[] TokenEndpointAuthMethodsSupported,
        [property: JsonPropertyName("code_challenge_methods_supported")] string[] CodeChallengeMethodsSupported,
        [property: JsonPropertyName("client_id_metadata_document_supported")] bool ClientIdMetadataDocumentSupported,
        [property: JsonPropertyName("authorization_response_iss_parameter_supported")] bool AuthorizationResponseIssParameterSupported);
}
