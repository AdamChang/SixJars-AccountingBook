using FluentAssertions;
using MediatR;
using SixJars.Application.Common;
using Xunit;

namespace SixJars.Application.Tests.Common;

public class BookScopeConventionTests
{
    /// <summary>日後新增 request 時若忘了實作 <see cref="IBookScoped"/>，就會略過成員授權（ADR 0005）；以反射把關。</summary>
    [Fact]
    public void Every_request_with_BookId_is_book_scoped()
    {
        var requests = typeof(IBookScoped).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IBaseRequest).IsAssignableFrom(t))
            .Where(t => t.GetProperty(nameof(IBookScoped.BookId))?.PropertyType == typeof(Guid))
            .ToList();

        // 至少要掃到帳本設定、交易與預定支出的 request，避免掃錯組件而空轉通過。
        requests.Should().HaveCountGreaterThanOrEqualTo(15);
        requests.Where(t => !typeof(IBookScoped).IsAssignableFrom(t)).Select(t => t.FullName)
            .Should().BeEmpty("帶 BookId 的 request 都必須經過 BookAccessBehavior");
    }
}
