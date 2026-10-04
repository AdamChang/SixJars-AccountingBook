using SixJars.Infrastructure.Persistence;

namespace SixJars.Api.Endpoints;

internal static class HealthEndpoints
{
    /// <summary>健康檢查（spec §5）：會實際連到資料庫，連不上時回 503。不需要登入。</summary>
    public static void MapHealthEndpoints(this IEndpointRouteBuilder app) =>
        app.MapGet("/health", async (SixJarsDbContext db, CancellationToken ct) =>
            await db.Database.CanConnectAsync(ct) ? Results.Ok() : Results.StatusCode(StatusCodes.Status503ServiceUnavailable));
}
