using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SixJars.Domain.Books;
using SixJars.Domain.Common;
using SixJars.Infrastructure.Persistence;
using SixJars.Tests.Shared;
using Xunit;

namespace SixJars.Infrastructure.Tests.Persistence;

public class SettingsOrderMigrationTests(PostgresFixture postgres)
{
    private const string BeforeSettingsOrder = "20261004050139_AddDataProtectionKeys";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Migration_backfills_sort_order_by_id_within_each_group()
    {
        var connectionString = await postgres.CreateConnectionStringAtAsync(BeforeSettingsOrder, Ct);
        var bookId = Guid.CreateVersion7();
        // Id 以字面值寫死，刻意讓插入順序與 Id 順序相反，證明回填依 Id 而不是依實體順序
        const string accountLater = "00000000-0000-7000-8000-000000000002", accountEarlier = "00000000-0000-7000-8000-000000000001";
        const string income = "00000000-0000-7000-8000-000000000010";
        const string food = "00000000-0000-7000-8000-000000000011", transport = "00000000-0000-7000-8000-000000000012";
        const string dinner = "00000000-0000-7000-8000-000000000022", lunch = "00000000-0000-7000-8000-000000000021";
        await using (var connection = new NpgsqlConnection(connectionString))
        {
            await connection.OpenAsync(Ct);
            await using var command = new NpgsqlCommand($"""
                INSERT INTO "Books" ("Id", "Name", "OpeningDate") VALUES ('{bookId}', '帳本', '2025-12-30');
                INSERT INTO "Accounts" ("Id", "Name", "Type", "OpeningBalance", "CountsAsAvailableCash", "BookId") VALUES
                  ('{accountLater}', '銀行', 'Bank', 0, false, '{bookId}'),
                  ('{accountEarlier}', '現金', 'Cash', 0, true, '{bookId}');
                INSERT INTO "Categories" ("Id", "Name", "Kind", "Nature", "ParentId", "BookId") VALUES
                  ('{transport}', '交通', 'Expense', 'Floating', NULL, '{bookId}'),
                  ('{food}', '飲食', 'Expense', 'Floating', NULL, '{bookId}'),
                  ('{income}', '薪資', 'Income', NULL, NULL, '{bookId}'),
                  ('{dinner}', '晚餐', 'Expense', 'Floating', '{food}', '{bookId}'),
                  ('{lunch}', '午餐', 'Expense', 'Floating', '{food}', '{bookId}');
                """, connection);
            await command.ExecuteNonQueryAsync(Ct);
        }

        await using var db = new SixJarsDbContext(new DbContextOptionsBuilder<SixJarsDbContext>()
            .UseNpgsql(SixJarsConnectionString.ForNpgsql(connectionString)).Options);
        await db.Database.MigrateAsync(Ct);
        var book = await db.Books.AsNoTracking().SingleAsync(Ct);

        book.GetAccount(new AccountId(Guid.Parse(accountEarlier))).SortOrder.Should().Be(0);
        book.GetAccount(new AccountId(Guid.Parse(accountLater))).SortOrder.Should().Be(1);
        // 收入主分類與支出主分類各自一組（ParentId 為 NULL 的分到同一個 partition）
        book.GetCategory(new CategoryId(Guid.Parse(income))).SortOrder.Should().Be(0);
        book.GetCategory(new CategoryId(Guid.Parse(food))).SortOrder.Should().Be(0);
        book.GetCategory(new CategoryId(Guid.Parse(transport))).SortOrder.Should().Be(1);
        book.GetCategory(new CategoryId(Guid.Parse(lunch))).SortOrder.Should().Be(0);
        book.GetCategory(new CategoryId(Guid.Parse(dinner))).SortOrder.Should().Be(1);
        book.Categories.Should().OnlyContain(c => c.ArchivedAt == null);
    }

    [Fact]
    public async Task Sort_order_and_archive_round_trip()
    {
        var createContext = await postgres.CreateDatabaseAsync(Ct);
        var archivedAt = new DateTimeOffset(2026, 10, 6, 1, 0, 0, TimeSpan.Zero);
        var book = new Book("帳本", new DateOnly(2025, 12, 30));
        var a = book.AddAccount("A", AccountType.Cash);
        var b = book.AddAccount("B", AccountType.Bank);
        var fund = book.AddPlanningFund("旅遊基金");
        book.ArchiveAccount(a.Id, balance: 0m, archivedAt);
        await using (var db = createContext())
        {
            db.Books.Add(book);
            await db.SaveChangesAsync(Ct);
        }

        await using var read = createContext();
        var loaded = await read.Books.AsNoTracking().SingleAsync(Ct);
        loaded.GetAccount(a.Id).SortOrder.Should().Be(0);
        loaded.GetAccount(b.Id).SortOrder.Should().Be(1);
        loaded.GetPlanningFund(fund.Id).SortOrder.Should().Be(0);
        loaded.GetAccount(a.Id).ArchivedAt.Should().Be(archivedAt);
        loaded.GetAccount(b.Id).ArchivedAt.Should().BeNull();
    }
}
