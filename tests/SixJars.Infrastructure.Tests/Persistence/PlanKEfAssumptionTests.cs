using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using SixJars.Domain.Books;
using SixJars.Domain.Common;
using SixJars.Domain.Planning;
using SixJars.Infrastructure.Persistence;
using SixJars.Tests.Shared;
using Xunit;

namespace SixJars.Infrastructure.Tests.Persistence;

/// <summary>
/// P4 K plan 核准前事實查核：計畫依賴的三個 EF Core／Npgsql 行為，在實際安裝的版本上驗證。
/// 留作回歸測試：日後升級 EF 或 Npgsql 時，這些假設若改變會在這裡先失敗。
/// </summary>
public class PlanKEfAssumptionTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly BudgetMonth April = BudgetMonth.FromKey(202604);

    /// <summary>
    /// K1／K2：私有欄位 <c>int[] _months</c> 以 <c>Property&lt;int[]&gt;("_months")</c> 對應成 PostgreSQL <c>integer[]</c>，
    /// 公開的 <c>IReadOnlyList&lt;int&gt; Months</c> 被 Ignore；EF 用私有無參數建構子建立實體。
    /// 換一個新陣列之後 SaveChanges 要偵測得到變更。
    /// </summary>
    [Fact]
    public async Task Field_only_int_array_maps_to_integer_array_and_tracks_replacement()
    {
        var options = new DbContextOptionsBuilder<MonthsSpikeContext>()
            .UseNpgsql(SixJarsConnectionString.ForNpgsql(await postgres.CreateConnectionStringAsync(Ct)))
            .Options;
        await using (var create = new MonthsSpikeContext(options))
        {
            // 資料庫已有 SixJars 的表，EnsureCreated 會什麼都不做；直接建立 spike 模型的表。
            await create.GetService<IRelationalDatabaseCreator>().CreateTablesAsync(Ct);
            create.Model.FindEntityType(typeof(MonthsSpike))!.FindProperty("_months")!.GetColumnType().Should().Be("integer[]");
            create.Items.Add(MonthsSpike.Create(7, 1, 7));
            await create.SaveChangesAsync(Ct);
        }

        await using (var update = new MonthsSpikeContext(options))
        {
            var item = await update.Items.SingleAsync(Ct);
            item.Months.Should().Equal(1, 7);
            item.ReplaceMonths(3);
            await update.SaveChangesAsync(Ct);
        }

        await using var read = new MonthsSpikeContext(options);
        (await read.Items.SingleAsync(Ct)).Months.Should().Equal(3);
    }

    /// <summary>
    /// K6／K7：有 converter 的可空強型別 Id，在投影中寫 <c>.Value</c>（計畫裡的 <c>p.SourceId!.Value</c>）能被翻譯。
    /// SourceId 要到 K2 才存在，這裡用同形狀的 <c>PlannedExpense.AccountId</c>（<c>AccountId?</c>）代替。
    /// </summary>
    [Fact]
    public async Task Nullable_strongly_typed_id_value_is_translated_in_projection()
    {
        var (createContext, book, bank, insurance) = await SeedAsync();
        await using (var seed = createContext())
        {
            seed.PlannedExpenses.Add(PlannedExpense.Create(book, April, insurance, bank, -100m));
            seed.PlannedExpenses.Add(PlannedExpense.Create(book, April, insurance, null, -200m));
            await seed.SaveChangesAsync(Ct);
        }

        await using var db = createContext();
        var accountIds = await db.PlannedExpenses.IgnoreQueryFilters()
            .Where(p => p.BookId == book.Id && p.BudgetMonth == April && p.AccountId != null)
            .Select(p => p.AccountId!.Value)
            .ToListAsync(Ct);

        accountIds.Should().Equal(bank);
    }

    /// <summary>
    /// K5：<c>ExpectVersion</c> 把實體標成 Modified 之後再 <c>Remove</c>，DELETE 仍帶 xmin 條件：
    /// 版本過期擲 <see cref="DbUpdateConcurrencyException"/>，版本正確則刪除成功。
    /// </summary>
    [Fact]
    public async Task Remove_after_expect_version_checks_xmin()
    {
        var (createContext, book, bank, insurance) = await SeedAsync();
        var stalePlanned = PlannedExpense.Create(book, April, insurance, bank, -100m);
        var currentPlanned = PlannedExpense.Create(book, April, insurance, bank, -200m);
        await using (var seed = createContext())
        {
            seed.PlannedExpenses.AddRange(stalePlanned, currentPlanned);
            await seed.SaveChangesAsync(Ct);
        }

        // 前端先讀到版本，之後有人修改；後端處理刪除時才載入實體，追蹤到的 xmin 已是最新的，
        // 所以只有 ExpectVersion 帶入的舊版本能讓 DELETE 失敗（拿掉 ExpectVersion 這條測試必須變紅）。
        uint staleVersion;
        await using (var reader = createContext())
        {
            staleVersion = reader.GetVersion(await reader.PlannedExpenses.SingleAsync(p => p.Id == stalePlanned.Id, Ct));
        }

        await using (var other = createContext())
        {
            await other.PlannedExpenses.Where(p => p.Id == stalePlanned.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.Note, "別人改的"), Ct);
        }

        await using var db = createContext();
        var stale = await db.PlannedExpenses.SingleAsync(p => p.Id == stalePlanned.Id, Ct);
        db.ExpectVersion(stale, staleVersion);
        db.PlannedExpenses.Remove(stale);
        var save = () => db.SaveChangesAsync(Ct);
        await save.Should().ThrowAsync<DbUpdateConcurrencyException>();

        await using var fresh = createContext();
        var current = await fresh.PlannedExpenses.SingleAsync(p => p.Id == currentPlanned.Id, Ct);
        fresh.ExpectVersion(current, fresh.GetVersion(current));
        fresh.PlannedExpenses.Remove(current);
        await fresh.SaveChangesAsync(Ct);
        (await fresh.PlannedExpenses.IgnoreQueryFilters().AnyAsync(p => p.Id == currentPlanned.Id, Ct)).Should().BeFalse();
    }

    private async Task<(Func<SixJarsDbContext> CreateContext, Book Book, AccountId Bank, CategoryId Insurance)> SeedAsync()
    {
        var book = new Book("測試帳本", new DateOnly(2025, 12, 30));
        var bank = book.AddAccount("銀行", AccountType.Bank);
        var insurance = book.AddSubCategory(book.AddExpenseCategory("固定支出", ExpenseNature.Fixed).Id, "保險費");
        var createContext = await postgres.CreateDatabaseAsync(Ct);
        await using var db = createContext();
        db.Books.Add(book);
        await db.SaveChangesAsync(Ct);
        return (createContext, book, bank.Id, insurance.Id);
    }

    /// <summary>與 K1 的 <c>RecurringPlannedExpense</c> 同樣的對應方式，只保留查核需要的部分。</summary>
    private sealed class MonthsSpike
    {
        private int[] _months = [];

        private MonthsSpike()
        {
        }

        public Guid Id { get; private set; }
        public IReadOnlyList<int> Months => _months;

        public static MonthsSpike Create(params int[] months) =>
            new() { Id = Guid.CreateVersion7(), _months = [.. months.Distinct().Order()] };

        public void ReplaceMonths(params int[] months) => _months = [.. months.Distinct().Order()];
    }

    private sealed class MonthsSpikeContext(DbContextOptions<MonthsSpikeContext> options) : DbContext(options)
    {
        public DbSet<MonthsSpike> Items => Set<MonthsSpike>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            var spike = modelBuilder.Entity<MonthsSpike>();
            spike.ToTable("PlanKMonthsSpike");
            spike.HasKey(s => s.Id);
            spike.Property(s => s.Id).ValueGeneratedNever();
            spike.Ignore(s => s.Months);
            spike.Property<int[]>("_months").HasColumnName("Months");
        }
    }
}
