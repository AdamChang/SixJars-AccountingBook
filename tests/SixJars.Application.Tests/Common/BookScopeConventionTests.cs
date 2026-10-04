using System.Reflection;
using System.Runtime.CompilerServices;
using FluentAssertions;
using MediatR;
using SixJars.Application.Common;
using Xunit;

namespace SixJars.Application.Tests.Common;

public class BookScopeConventionTests
{
    private static readonly Assembly Application = typeof(IBookScoped).Assembly;

    private static List<Type> Requests() => Application.GetTypes()
        .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IBaseRequest).IsAssignableFrom(t))
        .ToList();

    /// <summary>
    /// 日後新增 request 時若忘了實作 <see cref="IBookScoped"/>，就會略過成員授權（ADR 0005）；以反射把關。
    /// 唯一的例外是明確標示 <see cref="ICliOnlyRequest"/> 的管理用 request；兩者不可同時實作，
    /// 否則 CLI（不是任何帳本的成員）送出時一律 404。
    /// </summary>
    [Fact]
    public void Every_request_with_BookId_is_book_scoped_or_cli_only()
    {
        var requests = Requests()
            .Where(t => t.GetProperty(nameof(IBookScoped.BookId))?.PropertyType == typeof(Guid))
            .ToList();

        // 至少要掃到帳本設定、交易與預定支出的 request，避免掃錯組件而空轉通過。
        requests.Should().HaveCountGreaterThanOrEqualTo(15);
        requests.Where(t => typeof(IBookScoped).IsAssignableFrom(t) == typeof(ICliOnlyRequest).IsAssignableFrom(t))
            .Select(t => t.FullName)
            .Should().BeEmpty("帶 BookId 的 request 必須恰好是 IBookScoped（經過 BookAccessBehavior）或 ICliOnlyRequest 其中之一");
    }

    /// <summary>
    /// CLI 專用的 request 一律不是 public：Application 的 internal 只開放給 CLI 與測試（見下一個測試），
    /// API 因此無法參考，也就不可能誤把它們接上 endpoint。
    /// </summary>
    [Fact]
    public void Cli_only_requests_are_not_public()
    {
        var cliOnly = Requests().Where(t => typeof(ICliOnlyRequest).IsAssignableFrom(t)).ToList();

        cliOnly.Should().NotBeEmpty();
        cliOnly.Where(t => t.IsVisible).Select(t => t.FullName)
            .Should().BeEmpty("public 的 CLI 專用 request 可以被 SixJars.Api 參考");
    }

    [Fact]
    public void Application_internals_are_not_visible_to_the_api()
    {
        var friends = Application.GetCustomAttributes<InternalsVisibleToAttribute>().Select(a => a.AssemblyName).ToList();

        friends.Should().Contain("SixJars.Cli");
        friends.Should().OnlyContain(name => name == "SixJars.Cli" || name.EndsWith(".Tests", StringComparison.Ordinal),
            "SixJars.Api 看得到 internal 就能送出 CLI 專用的 request（ICliOnlyRequest）");
    }
}
