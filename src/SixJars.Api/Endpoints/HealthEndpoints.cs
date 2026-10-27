using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using SixJars.Infrastructure.Persistence;

namespace SixJars.Api.Endpoints;

internal static class HealthEndpoints
{
    public static void MapHealthEndpoints(this IEndpointRouteBuilder app)
    {
        // 健康檢查（spec §5）：會實際連到資料庫，連不上時回 503。不需要登入。
        app.MapGet("/health", async (SixJarsDbContext db, CancellationToken ct) =>
            await db.Database.CanConnectAsync(ct) ? Results.Ok() : Results.StatusCode(StatusCodes.Status503ServiceUnavailable));

        // Cloud Run 的 startup probe（ADR 0012）：資料庫還有這個版本認得、但尚未套用的 migration 時回 503，
        // 新 revision 就拿不到流量、舊版繼續服務。migration 由使用者在本機執行，app 不會自己 migrate。
        app.MapGet("/health/ready", Ready);
    }

    /// <remarks>
    /// 不 ready 時一律記 warning：probe 的請求本身看不到原因，Cloud Run 上只能從 log 判斷是連不上資料庫還是漏跑 migration。
    /// 不用 <c>CanConnectAsync</c>，因為它會吞掉例外；改為自己開底層連線，失敗時把 Npgsql 的錯誤（不含密碼）寫進 log。
    /// 不經 <c>Database.OpenConnectionAsync</c>：它走 execution strategy，例外會被包成 InvalidOperationException。
    /// 連線由這裡開啟，EF 查 migration 歷史時沿用、不會自己關，所以在 finally 關閉。
    /// </remarks>
    private static async Task<IResult> Ready(SixJarsDbContext db, ILoggerFactory loggerFactory, CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger("SixJars.Api.Health");
        var connection = db.Database.GetDbConnection();
        try
        {
            await connection.OpenAsync(ct);
        }
        catch (DbException e)
        {
            logger.LogWarning(e, "/health/ready 未就緒：連不上資料庫（檢查 ConnectionStrings__SixJars 的 host、資料庫名稱、帳密與 SSL Mode）。");
            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        }

        try
        {
            var pending = (await db.Database.GetPendingMigrationsAsync(ct)).ToList();
            if (pending.Count == 0)
            {
                return Results.Ok();
            }

            logger.LogWarning(
                "/health/ready 未就緒：資料庫還有 {Count} 個 migration 未套用（{Migrations}），請先在本機執行 migration bundle（docs/deploy.md §4）。",
                pending.Count, string.Join(", ", pending));
            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        }
        catch (DbException e)
        {
            logger.LogWarning(e, "/health/ready 未就緒：讀不到 migration 歷史（檢查 app 角色對 __EFMigrationsHistory 的 SELECT 權限，docs/deploy.md §3.3）。");
            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        }
        finally
        {
            await connection.CloseAsync();
        }
    }
}
