using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.WebUtilities;

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

        app.MapGet(AuthorizePath, AuthorizeAsync);
        app.MapPost(AuthorizePath, ConsentAsync);
    }

    /// <summary>
    /// 先驗證 client（不可信的 client 不必登入就拒絕），再要求登入：未登入轉到既有的 Google 登入，登入後回到同一個網址
    /// （<c>ReturnUrl</c> 接受站內相對路徑）。是否登入看預設 scheme（cookie）；能讀哪些帳本之後由 BookAccessBehavior 決定。
    /// </summary>
    private static async Task<IResult> AuthorizeAsync(
        HttpContext context, AuthorizeRequestValidator validator, IAntiforgery antiforgery, CancellationToken cancellationToken)
    {
        var parameters = AuthorizeParameters.From(name => context.Request.Query[name].FirstOrDefault());
        var validation = await validator.ValidateAsync(parameters, context.Request, cancellationToken);
        if (validation is not AuthorizeValidation.Valid valid)
        {
            return Respond(context, validation);
        }

        if (context.User.Identity?.IsAuthenticated != true)
        {
            var returnUrl = context.Request.Path + context.Request.QueryString;
            return Results.Redirect($"/auth/login?returnUrl={Uri.EscapeDataString(returnUrl)}");
        }

        return ConsentPage.Render(context, antiforgery, valid, context.User.FindFirst("email")?.Value);
    }

    /// <summary>同意頁送出：驗 antiforgery（防止別的網站替使用者按下「允許」），重新驗證全部參數，同意才發 code。</summary>
    private static async Task<IResult> ConsentAsync(
        HttpContext context, AuthorizeRequestValidator validator, IAntiforgery antiforgery, McpTokens tokens, TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var subject = context.User.FindFirst("sub")?.Value;
        if (subject is null || !context.Request.HasFormContentType || !await antiforgery.IsRequestValidAsync(context))
        {
            return ConsentPage.Error(context, "授權請求已失效，請回到 Claude 重新連線。");
        }

        var form = await context.Request.ReadFormAsync(cancellationToken);
        var validation = await validator.ValidateAsync(AuthorizeParameters.From(name => form[name].FirstOrDefault()), context.Request, cancellationToken);
        if (validation is not AuthorizeValidation.Valid valid)
        {
            return Respond(context, validation);
        }

        if (form["decision"] != "allow")
        {
            return RedirectToClient(context, valid.RedirectUri, new() { ["error"] = "access_denied", ["state"] = valid.State });
        }

        var code = tokens.CreateAuthorizationCode(new AuthorizationCode(Guid.NewGuid(), subject, context.User.FindFirst("email")?.Value,
            valid.ClientId, valid.RedirectUri, valid.CodeChallenge, valid.Scope, valid.Resource,
            clock.GetUtcNow() + McpOAuth.AuthorizationCodeLifetime));
        return RedirectToClient(context, valid.RedirectUri, new() { ["code"] = code, ["state"] = valid.State });
    }

    private static IResult Respond(HttpContext context, AuthorizeValidation validation) => validation switch
    {
        AuthorizeValidation.RedirectError error =>
            RedirectToClient(context, error.RedirectUri, new() { ["error"] = error.Error, ["state"] = error.State }),
        AuthorizeValidation.Rejected rejected => ConsentPage.Error(context, rejected.Message),
        _ => throw new InvalidOperationException($"未預期的驗證結果 {validation}"),
    };

    /// <summary>轉回 client，一律帶 iss（RFC 9207，防止 mix-up 攻擊）；state 為 null 時不帶。</summary>
    private static IResult RedirectToClient(HttpContext context, string redirectUri, Dictionary<string, string?> parameters)
    {
        parameters["iss"] = McpOAuth.Issuer(context.Request);
        return Results.Redirect(QueryHelpers.AddQueryString(redirectUri, parameters.Where(p => p.Value is not null)));
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
