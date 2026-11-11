using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SixJars.Api.Tests;
using SixJars.Application;
using SixJars.Application.Common;
using SixJars.Infrastructure.Persistence;
using SixJars.Tests.Shared;
using Xunit;

namespace SixJars.Infrastructure.Tests.LegacyExcel;

/// <summary>與 CLI 相同的組裝方式（Application + Infrastructure），每個測試一個獨立的資料庫。</summary>
internal sealed class LegacyImportHost(ServiceProvider provider, string connectionString) : IAsyncDisposable
{
    public const string Actor = "importer-sub";
    public static readonly DateTimeOffset Now = new(2026, 10, 4, 1, 2, 3, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public string ConnectionString => connectionString;

    public static async Task<LegacyImportHost> CreateAsync(PostgresFixture postgres, IInterceptor? interceptor = null)
    {
        var connectionString = await postgres.CreateConnectionStringAsync(Ct);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSixJarsApplication(mediatRLicenseKey: null);
        services.AddSixJarsInfrastructure(connectionString);
        services.AddSingleton<ICurrentUser, FixedUser>();
        services.AddSingleton<TimeProvider>(new FixedTimeProvider(Now));
        if (interceptor is not null)
        {
            services.AddSingleton(interceptor);
        }

        return new LegacyImportHost(services.BuildServiceProvider(), connectionString);
    }

    public async Task<T> SendAsync<T>(IRequest<T> request)
    {
        await using var scope = provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ISender>().Send(request, Ct);
    }

    public SixJarsDbContext Open() => Open(connectionString);

    public static SixJarsDbContext Open(string connectionString) =>
        new(new DbContextOptionsBuilder<SixJarsDbContext>().UseNpgsql(connectionString).Options);

    public async Task ShouldBeEmptyAsync()
    {
        await using var db = Open();
        (await db.Books.CountAsync(Ct)).Should().Be(0);
        (await db.Transactions.IgnoreQueryFilters().CountAsync(Ct)).Should().Be(0);
        (await db.PlannedExpenses.IgnoreQueryFilters().CountAsync(Ct)).Should().Be(0);
        (await db.BookMembers.CountAsync(Ct)).Should().Be(0);
        (await db.AuditEntries.CountAsync(Ct)).Should().Be(0);
    }

    /// <summary>
    /// 每個測試一個資料庫，閒置連線池會累積到超過 max_connections（同 ApiFactory 的說明）。
    /// app 用 <see cref="SixJarsConnectionString.ForNpgsql"/> 轉換後的字串，<see cref="Open()"/> 用原字串，兩個連線池都要清。
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        await provider.DisposeAsync();
        foreach (var pool in new[] { SixJarsConnectionString.ForNpgsql(connectionString), connectionString })
        {
            await using var connection = new NpgsqlConnection(pool);
            NpgsqlConnection.ClearPool(connection);
        }
    }

    private sealed class FixedUser : ICurrentUser
    {
        public string Subject => Actor;
        public string? Email => null;
    }
}
