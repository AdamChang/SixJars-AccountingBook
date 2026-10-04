using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SixJars.Application.Common;

namespace SixJars.Application.Auditing;

/// <summary>某一筆資料的修改歷史（唯讀），由舊到新；只看得到 <see cref="BookId"/> 這本帳的記錄。已刪除的資料也查得到。</summary>
public sealed record GetAuditHistory(Guid BookId, Guid EntityId) : IRequest<IReadOnlyList<AuditEntryDto>>;

/// <summary>稽核記錄的 API 輸出；<see cref="Before"/>／<see cref="After"/> 是 JSON 物件（不是跳脫過的字串），新增時沒有修改前、刪除時沒有修改後。</summary>
public sealed record AuditEntryDto(
    Guid Id,
    DateTimeOffset At,
    string ActorSubject,
    AuditAction Action,
    string EntityType,
    Guid EntityId,
    JsonElement? Before,
    JsonElement? After);

internal sealed class GetAuditHistoryHandler(ISixJarsDbContext db) : IRequestHandler<GetAuditHistory, IReadOnlyList<AuditEntryDto>>
{
    public async Task<IReadOnlyList<AuditEntryDto>> Handle(GetAuditHistory request, CancellationToken cancellationToken)
    {
        // 帳本與實體 Id 一起當查詢條件：只用實體 Id 查詢，就能讀到別本帳的歷史。
        // 同一次 SaveChanges 的記錄 At 相同（例如付款），再依 Id（UUIDv7）排序讓結果穩定。
        var entries = await db.AuditEntries.AsNoTracking()
            .Where(e => e.BookId == request.BookId && e.EntityId == request.EntityId)
            .OrderBy(e => e.At).ThenBy(e => e.Id)
            .ToListAsync(cancellationToken);

        return [.. entries.Select(e => new AuditEntryDto(
            e.Id, e.At, e.ActorSubject, e.Action, e.EntityType, e.EntityId, Parse(e.Before), Parse(e.After)))];
    }

    private static JsonElement? Parse(string? json)
    {
        if (json is null)
        {
            return null;
        }

        // Clone 讓 JsonElement 不依賴已釋放的 JsonDocument。
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
