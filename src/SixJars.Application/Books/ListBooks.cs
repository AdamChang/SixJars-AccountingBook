using MediatR;
using Microsoft.EntityFrameworkCore;
using SixJars.Application.Common;

namespace SixJars.Application.Books;

/// <summary>列出帳本。T35 之後改為只列出目前使用者所屬的帳本。</summary>
public sealed record ListBooks : IRequest<IReadOnlyList<BookSummaryDto>>;

internal sealed class ListBooksHandler(ISixJarsDbContext db) : IRequestHandler<ListBooks, IReadOnlyList<BookSummaryDto>>
{
    public async Task<IReadOnlyList<BookSummaryDto>> Handle(ListBooks request, CancellationToken cancellationToken)
    {
        // 先取出強型別 Id 再轉 Guid：BookId 走 value converter，EF 無法翻譯 .Value。
        var books = await db.Books.AsNoTracking()
            .OrderBy(b => b.Name)
            .Select(b => new { b.Id, b.Name })
            .ToListAsync(cancellationToken);
        return [.. books.Select(b => new BookSummaryDto(b.Id.Value, b.Name))];
    }
}
