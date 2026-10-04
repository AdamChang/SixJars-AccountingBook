using MediatR;
using SixJars.Application.Backup;

namespace SixJars.Api.Endpoints;

internal static class ExportsEndpoints
{
    /// <summary>備份與匯出的下載；掛在 <c>/books/{bookId}</c> 群組底下。</summary>
    public static RouteGroupBuilder MapExportsEndpoints(this RouteGroupBuilder book)
    {
        // 以 BackupJson 的格式（縮排、中文不跳脫）序列化，不用 API 預設的 JSON 設定；檔名的日期是匯出時間在台北的日期。
        book.MapGet("/export/backup.json", async (Guid bookId, ISender sender, CancellationToken ct) =>
        {
            var backup = await sender.Send(new ExportBackup(bookId), ct);
            return Results.File(BackupJson.SerializeToUtf8Bytes(backup), "application/json", BackupJson.FileName(backup.ExportedAt));
        });
        return book;
    }
}
