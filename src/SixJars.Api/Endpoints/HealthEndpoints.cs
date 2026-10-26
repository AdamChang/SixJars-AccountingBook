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
        app.MapGet("/health/ready", async (SixJarsDbContext db, CancellationToken ct) =>
            await db.Database.CanConnectAsync(ct) && !(await db.Database.GetPendingMigrationsAsync(ct)).Any()
                ? Results.Ok()
                : Results.StatusCode(StatusCodes.Status503ServiceUnavailable));
    }
}
