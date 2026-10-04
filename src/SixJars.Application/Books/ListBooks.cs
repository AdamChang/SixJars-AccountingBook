using MediatR;
using Microsoft.EntityFrameworkCore;
using SixJars.Application.Common;

namespace SixJars.Application.Books;

/// <summary>列出目前使用者所屬的帳本（ADR 0005）。</summary>
public sealed record ListBooks : IRequest<IReadOnlyList<BookSummaryDto>>;

internal sealed class ListBooksHandler(ISixJarsDbContext db, ICurrentUser user) : IRequestHandler<ListBooks, IReadOnlyList<BookSummaryDto>>
{
    public Task<IReadOnlyList<BookSummaryDto>> Handle(ListBooks request, CancellationToken cancellationToken) =>
        db.ListMemberBooksAsync(user.Subject, cancellationToken);
}

internal static class MemberBooks
{
    /// <summary>以 Google sub 辨識的使用者所屬的帳本，依名稱排序；<see cref="ListBooks"/> 與 <c>/api/me</c> 共用。</summary>
    public static async Task<IReadOnlyList<BookSummaryDto>> ListMemberBooksAsync(
        this ISixJarsDbContext db, string subject, CancellationToken cancellationToken)
    {
        var owned = db.OwnedBookIds(subject);
        // 先取出強型別 Id 再轉 Guid：BookId 走 value converter，EF 無法翻譯 .Value。
        var books = await db.Books.AsNoTracking()
            .Where(b => owned.Contains(b.Id))
            .OrderBy(b => b.Name)
            .Select(b => new { b.Id, b.Name })
            .ToListAsync(cancellationToken);
        return [.. books.Select(b => new BookSummaryDto(b.Id.Value, b.Name))];
    }
}
