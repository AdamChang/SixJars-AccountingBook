using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SixJars.Application.Books;
using SixJars.Application.Members;
using SixJars.Infrastructure.Persistence;
using SixJars.Tests.Shared;
using Xunit;

namespace SixJars.Api.Tests;

/// <summary>ADR 0005：帳本範圍的 API 只開放給該帳本的成員；不是成員時回 404，不透露帳本是否存在。</summary>
public partial class MembershipTests(PostgresFixture postgres)
{
    private const string StrangerSubject = "stranger-sub";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    /// 列舉所有 <c>/api/books/{bookId}</c> 底下的 endpoint，以非成員身分逐一呼叫，全部都要 404。
    /// 用單一 Fact 迴圈：日後新增的 endpoint（例如匯出）會自動被涵蓋。
    /// </summary>
    [Fact]
    public async Task Every_book_endpoint_is_404_for_non_members()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        // 帳本存在、而且有擁有者，404 才是因為登入者不是成員，而不是因為帳本不存在。
        var book = await factory.SeedBookAsync(Ct);
        var client = factory.CreateSignedInClient(StrangerSubject);
        var endpoints = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText!.StartsWith("/api/books/{bookId", StringComparison.Ordinal))
            .ToList();
        endpoints.Should().HaveCountGreaterThanOrEqualTo(15, "至少有帳本設定、交易、預定支出、摘要與稽核的 endpoint，避免列舉錯誤而空轉通過");

        List<string> leaks = [];
        foreach (var endpoint in endpoints)
        {
            var url = Url(endpoint, book.Id.Value);
            foreach (var method in endpoint.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods)
            {
                using var request = new HttpRequestMessage(new HttpMethod(method), url);
                if (method != HttpMethods.Get)
                {
                    // 空 body：BookAccessBehavior 在 Validation 之前執行，所以先回 404 而不是 400。
                    request.Content = JsonContent.Create(new { });
                }

                var response = await client.SendAsync(request, Ct);
                if (response.StatusCode != HttpStatusCode.NotFound)
                {
                    leaks.Add($"{method} {url} → {(int)response.StatusCode}");
                }
            }
        }

        leaks.Should().BeEmpty();
    }

    [Fact]
    public async Task Member_can_read_book()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);

        var response = await factory.CreateSignedInClient().GetAsync($"/api/books/{book.Id.Value}", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Me_lists_only_member_books()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var mine = await factory.SeedBookAsync(Ct);
        await factory.SeedBookAsync(Ct, ownerSubject: "someone-else-sub");
        var client = factory.CreateSignedInClient();

        var me = await client.GetFromJsonAsync<MeDto>("/api/me", ApiJson.Options, Ct);
        var books = await client.GetFromJsonAsync<List<BookSummaryDto>>("/api/books", ApiJson.Options, Ct);

        me!.Subject.Should().Be(ApiFactory.DefaultSubject);
        me.Email.Should().Be(TestAuthHandler.EmailOf(ApiFactory.DefaultSubject));
        me.Books.Select(b => b.Id).Should().Equal(mine.Id.Value);
        books!.Select(b => b.Id).Should().Equal(mine.Id.Value);
    }

    /// <summary>
    /// spec §3.4：P2 只授權擁有者。Domain 建不出其他角色，所以直接寫入資料表模擬日後才會出現的唯讀成員。
    /// </summary>
    [Fact]
    public async Task Non_owner_member_is_treated_as_non_member()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct, ownerSubject: null);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SixJarsDbContext>();
            var bookId = book.Id.Value;
            var email = TestAuthHandler.EmailOf(ApiFactory.DefaultSubject);
            await db.Database.ExecuteSqlAsync(
                $"""
                INSERT INTO "BookMembers" ("Id", "BookId", "Email", "GoogleSubject", "Role", "AddedAt")
                VALUES ({Guid.CreateVersion7()}, {bookId}, {email}, {ApiFactory.DefaultSubject}, 'ReadOnly', {DateTimeOffset.UtcNow})
                """, Ct);
        }

        var client = factory.CreateSignedInClient();

        (await client.GetAsync($"/api/books/{book.Id.Value}", Ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.GetFromJsonAsync<MeDto>("/api/me", ApiJson.Options, Ct))!.Books.Should().BeEmpty();
    }

    /// <summary>
    /// 路由參數：<c>{bookId}</c> 代入 seed 帳本，其他代入新的 Guid。
    /// handler 必填的 query 參數（例如 DELETE 的 <c>version</c>、稽核的 <c>entityId</c>）也要帶假值，
    /// 否則 minimal API 會在進入 MediatR 之前就因為綁定失敗回 400，測不到授權。
    /// </summary>
    private static string Url(RouteEndpoint endpoint, Guid bookId)
    {
        var path = RouteParameter().Replace(endpoint.RoutePattern.RawText!,
            m => m.Groups["name"].Value == "bookId" ? bookId.ToString() : Guid.NewGuid().ToString());
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
