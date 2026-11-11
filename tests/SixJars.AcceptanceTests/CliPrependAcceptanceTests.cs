using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SixJars.Cli;
using SixJars.Domain.Ledger;
using SixJars.Infrastructure.Persistence;
using SixJars.Tests.Shared;
using Xunit;

namespace SixJars.AcceptanceTests;

/// <summary>
/// 補匯舊年度的真實資料驗收（spec「測試」）：CLI 匯入本年度記帳本後，再以 <c>import-legacy --into</c> 補上前一年，
/// 銜接檢查必須通過，而且本年度每一天的帳戶與財務規劃帳戶餘額都不變。兩個檔案任一不存在時略過。
/// </summary>
public class CliPrependAcceptanceTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Previous_year_prepends_without_changing_any_later_balance()
    {
        Assert.SkipUnless(File.Exists(RepoPaths.LegacyWorkbook), $"找不到 {RepoPaths.LegacyWorkbook}，略過驗收");
        Assert.SkipUnless(File.Exists(RepoPaths.PreviousLegacyWorkbook), $"找不到 {RepoPaths.PreviousLegacyWorkbook}，略過驗收");
        var connectionString = await postgres.CreateConnectionStringAsync(Ct);

        var import = new StringWriter();
        (await RunAsync(connectionString, import,
                "import-legacy", "--file", RepoPaths.LegacyWorkbook, "--book-name", "驗收帳本", "--owner-email", "owner@example.com"))
            .Should().Be(0, import.ToString());
        Guid bookId;
        await using (var db = Open(connectionString))
        {
            bookId = (await db.Books.SingleAsync(Ct)).Id.Value;
        }

        var before = await DailyBalancesAsync(connectionString);

        var prepend = new StringWriter();
        var exitCode = await RunAsync(connectionString, prepend,
            "import-legacy", "--file", RepoPaths.PreviousLegacyWorkbook, "--into", bookId.ToString());

        // 失敗時輸出含銜接摘要：判斷是 Excel 本身對不起來還是對應邏輯的 bug，不要放寬檢查。
        exitCode.Should().Be(0, prepend.ToString());
        (await DailyBalancesAsync(connectionString, before.Keys)).Should().Equal(before);
    }

    /// <summary>補匯前的期初日隔天到最後一筆交易日，每一天每個帳戶與財務規劃帳戶的餘額。</summary>
    private static async Task<Dictionary<string, decimal>> DailyBalancesAsync(string connectionString, IEnumerable<string>? keys = null)
    {
        await using var db = Open(connectionString);
        var bookId = (await db.Books.SingleAsync(Ct)).Id;
        var ledger = await new LedgerSnapshotLoader(db).LoadAsync(bookId, Ct);
        var calculator = new BalanceCalculator(ledger);
        if (keys is not null)
        {
            return keys.ToDictionary(k => k, k => Balance(calculator, ledger, k));
        }

        var balances = new Dictionary<string, decimal>();
        var last = ledger.Transactions.Max(t => t.Date);
        for (var date = ledger.Book.OpeningDate.AddDays(1); date <= last; date = date.AddDays(1))
        {
            foreach (var account in ledger.Book.Accounts)
            {
                balances[$"A|{account.Id.Value}|{date:yyyy-MM-dd}"] = calculator.AccountBalanceAsOf(account.Id, date);
            }

            foreach (var fund in ledger.Book.PlanningFunds)
            {
                balances[$"F|{fund.Id.Value}|{date:yyyy-MM-dd}"] = calculator.FundBalanceAsOf(fund.Id, date);
            }
        }

        return balances;
    }

    private static decimal Balance(BalanceCalculator calculator, LedgerSnapshot ledger, string key)
    {
        var parts = key.Split('|');
        var date = DateOnly.Parse(parts[2]);
        var id = Guid.Parse(parts[1]);
        return parts[0] == "A"
            ? calculator.AccountBalanceAsOf(ledger.Book.Accounts.Single(a => a.Id.Value == id).Id, date)
            : calculator.FundBalanceAsOf(ledger.Book.PlanningFunds.Single(f => f.Id.Value == id).Id, date);
    }

    private static Task<int> RunAsync(string connectionString, TextWriter output, params string[] args) =>
        CliApp.RunAsync(args, new Dictionary<string, string?> { [CliApp.ConnectionStringVariable] = connectionString }, output, Ct);

    private static SixJarsDbContext Open(string connectionString) =>
        new(new DbContextOptionsBuilder<SixJarsDbContext>().UseNpgsql(connectionString).Options);
}
