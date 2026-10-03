using SixJars.Infrastructure.Persistence;

namespace SixJars.Api.Endpoints;

internal static class HealthEndpoints
{
    /// <summary>Cloud Scheduler 定時 ping（spec §5）：會實際連到資料庫，讓 Supabase 不會因為閒置而暫停。</summary>
    public static void MapHealthEndpoints(this IEndpointRouteBuilder app) =>
        app.MapGet("/health", async (SixJarsDbContext db, CancellationToken ct) =>
            await db.Database.CanConnectAsync(ct) ? Results.Ok() : Results.StatusCode(StatusCodes.Status503ServiceUnavailable));
}
