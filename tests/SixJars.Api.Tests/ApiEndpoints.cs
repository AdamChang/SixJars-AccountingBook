using System.Net.Http.Json;
using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using SixJars.Tests.Shared;

namespace SixJars.Api.Tests;

/// <summary>
/// 列舉實際註冊的 endpoint，組出可以直接送出的請求；供成員授權、登入、XSRF 等慣例測試共用。
/// 以列舉而不是手寫清單：日後新增的 endpoint（例如匯出）會自動被涵蓋。
/// </summary>
internal static partial class ApiEndpoints
{
    /// <summary>路由樣板以 <paramref name="prefix"/> 開頭的所有 endpoint。</summary>
    public static List<RouteEndpoint> Under(ApiFactory factory, string prefix) =>
        All(factory).Where(e => e.RoutePattern.RawText!.StartsWith(prefix, StringComparison.Ordinal)).ToList();

    public static IEnumerable<RouteEndpoint> All(ApiFactory factory) =>
        factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>();

    /// <summary>每個 endpoint 的每個 HTTP method 各一筆，路由與必填的 query 參數都已代入（見 <see cref="Url"/>）。</summary>
    public static IEnumerable<(string Method, string Url)> Calls(IEnumerable<RouteEndpoint> endpoints, Guid bookId) =>
        endpoints.SelectMany(e => e.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods.Select(method => (method, Url(e, bookId))));

    /// <summary>
    /// 非 GET 帶空的 JSON body：參數綁定在 endpoint filter 與 MediatR 之前執行，沒有 body 會先回綁定的 400，
    /// 測不到後面的授權與 XSRF 檢查。空 body 的 validation 則在 <c>BookAccessBehavior</c> 之後，不影響 404。
    /// </summary>
    public static HttpRequestMessage Request(string method, string url)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), url);
        if (method != HttpMethods.Get)
        {
            request.Content = JsonContent.Create(new { });
        }

        return request;
    }

    /// <summary>
    /// 路由參數：<c>{bookId}</c> 代入指定的帳本，<c>:int</c> 約束的（預算的歸屬月份）代入 202601，其他代入新的 Guid。
    /// handler 必填的 query 參數（例如 DELETE 的 <c>version</c>、稽核的 <c>entityId</c>）也要帶假值，
    /// 否則 minimal API 會在進入 MediatR 之前就因為綁定失敗回 400，測不到授權。
    /// </summary>
    public static string Url(RouteEndpoint endpoint, Guid bookId)
    {
        var path = RouteParameter().Replace(endpoint.RoutePattern.RawText!,
            m => m.Groups["name"].Value == "bookId" ? bookId.ToString()
                : m.Value.EndsWith(":int}", StringComparison.Ordinal) ? "202601"
                : Guid.NewGuid().ToString());
        var routeNames = endpoint.RoutePattern.Parameters.Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var handler = endpoint.Metadata.GetMetadata<MethodInfo>()
            ?? throw new InvalidOperationException($"{endpoint.RoutePattern.RawText} 沒有 handler 的 MethodInfo。");
        var query = handler.GetParameters()
            .Where(p => !routeNames.Contains(p.Name!) && p.ParameterType.IsValueType
                && p.ParameterType != typeof(CancellationToken) && Nullable.GetUnderlyingType(p.ParameterType) is null)
            .Select(p => $"{p.Name}={DummyValue(p.ParameterType, endpoint)}")
            .ToList();
        return query.Count == 0 ? path : $"{path}?{string.Join('&', query)}";
    }

    private static string DummyValue(Type type, RouteEndpoint endpoint) =>
        type == typeof(Guid) ? Guid.NewGuid().ToString()
        : type == typeof(int) ? "202601"
        : type == typeof(uint) ? "1"
        : type == typeof(DateOnly) ? "2026-01-01"
        : throw new InvalidOperationException($"{endpoint.RoutePattern.RawText} 有必填的 {type.Name} query 參數，請在測試中補上假值。");

    [GeneratedRegex(@"\{(?<name>\w+)(:[^}]*)?\}")]
    private static partial Regex RouteParameter();
}
