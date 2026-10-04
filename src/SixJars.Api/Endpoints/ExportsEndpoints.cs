using MediatR;
using SixJars.Application.Backup;
using SixJars.Application.Exports;

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

        // 交易明細（spec §8.3）：?from=&to= 為交易日期範圍（含兩端），皆可省略。
        book.MapGet("/export/transactions.csv", (Guid bookId, DateOnly? from, DateOnly? to, ISender sender, CancellationToken ct) =>
            ExportTransactionsAsync(sender, new ExportTransactions(bookId, from, to, TransactionExportFormat.Csv), ct));
        book.MapGet("/export/transactions.xlsx", (Guid bookId, DateOnly? from, DateOnly? to, ISender sender, CancellationToken ct) =>
            ExportTransactionsAsync(sender, new ExportTransactions(bookId, from, to, TransactionExportFormat.Xlsx), ct));
        return book;
    }

    private static async Task<IResult> ExportTransactionsAsync(ISender sender, ExportTransactions query, CancellationToken ct)
    {
        var file = await sender.Send(query, ct);
        return Results.File(file.Content, file.ContentType, file.FileName);
    }
}
