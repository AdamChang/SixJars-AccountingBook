using MediatR;
using SixJars.Application.Auditing;

namespace SixJars.Api.Endpoints;

internal static class AuditEndpoints
{
    /// <summary>某一筆資料的修改歷史（唯讀）；掛在 <c>/books/{bookId}</c> 群組底下。</summary>
    public static RouteGroupBuilder MapAuditEndpoints(this RouteGroupBuilder book)
    {
        // 帳本 Id 取自路由，只查得到這本帳的記錄。
        book.MapGet("/audit", (Guid bookId, Guid entityId, ISender sender, CancellationToken ct) =>
            sender.Send(new GetAuditHistory(bookId, entityId), ct));
        return book;
    }
}
