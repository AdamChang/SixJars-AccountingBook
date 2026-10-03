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

    public async Task<Func<SixJarsDbContext>> CreateDatabaseAsync(CancellationToken cancellationToken)
    {
        var connectionString = new NpgsqlConnectionStringBuilder(_container.GetConnectionString())
        {
            Database = $"t_{Guid.NewGuid():N}",
        }.ConnectionString;
        var options = new DbContextOptionsBuilder<SixJarsDbContext>().UseNpgsql(connectionString).Options;
        await using (var db = new SixJarsDbContext(options))
        {
            await db.Database.MigrateAsync(cancellationToken);
        }

        return () => new SixJarsDbContext(options);
    }
}
