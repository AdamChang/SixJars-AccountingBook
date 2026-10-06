using Microsoft.EntityFrameworkCore;
using Npgsql;
using SixJars.Infrastructure.Persistence;
using Testcontainers.PostgreSql;
using Xunit;

namespace SixJars.Tests.Shared;

/// <summary>整個測試組件共用一個 PostgreSQL 容器；每個測試各自建立獨立的資料庫。</summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17-alpine").Build();

    public async ValueTask InitializeAsync() => await _container.StartAsync(TestContext.Current.CancellationToken);

    public ValueTask DisposeAsync() => _container.DisposeAsync();

    /// <summary>建立一個已套用 migration 的獨立資料庫，回傳連線字串（API 測試用）。</summary>
    public async Task<string> CreateConnectionStringAsync(CancellationToken cancellationToken)
    {
        var connectionString = new NpgsqlConnectionStringBuilder(_container.GetConnectionString())
        {
            Database = $"t_{Guid.NewGuid():N}",
        }.ConnectionString;
        await using var db = new SixJarsDbContext(Options(connectionString));
        await db.Database.MigrateAsync(cancellationToken);
        return connectionString;
    }

    /// <summary>只套用到 <paramref name="targetMigration"/> 為止的資料庫（測試 migration 的資料回填）。</summary>
    public async Task<string> CreateConnectionStringAtAsync(string targetMigration, CancellationToken cancellationToken)
    {
        var connectionString = new NpgsqlConnectionStringBuilder(_container.GetConnectionString())
        {
            Database = $"t_{Guid.NewGuid():N}",
        }.ConnectionString;
        await using var db = new SixJarsDbContext(Options(connectionString));
        await db.Database.MigrateAsync(targetMigration, cancellationToken);
        return connectionString;
    }

    public async Task<Func<SixJarsDbContext>> CreateDatabaseAsync(CancellationToken cancellationToken)
    {
        var options = Options(await CreateConnectionStringAsync(cancellationToken));
        return () => new SixJarsDbContext(options);
    }

    /// <summary>與 app 用同一個轉換後的連線字串，才會共用連線池，讓 <see cref="ApiFactory"/> 一次清乾淨。</summary>
    private static DbContextOptions<SixJarsDbContext> Options(string connectionString) =>
        new DbContextOptionsBuilder<SixJarsDbContext>().UseNpgsql(SixJarsConnectionString.ForNpgsql(connectionString)).Options;
}
